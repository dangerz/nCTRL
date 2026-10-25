using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class DestinationTests
{
    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Send to car: "+label);count++;}

        // Reading what was shared or typed.
        var (pair,_)=Destinations.FromText(" 38.89768, -77.036529 ");
        Check(pair is {Latitude: 38.89768,Longitude: -77.036529}&&pair.Name=="38.89768, -77.03653","Plain coordinates");
        var (apple,_)=Destinations.FromText("https://maps.apple.com/?address=1%20Main%20St,%20Springfield&ll=38.9,-77.0&q=Corner%20Cafe");
        Check(apple==new Destination("Corner Cafe","1 Main St, Springfield",38.9,-77.0),"An Apple Maps link with a name and address");
        var (applePlace,_)=Destinations.FromText("https://maps.apple.com/place?coordinate=40.7812,-73.9665&name=Central%20Park");
        Check(applePlace is {Name: "Central Park",Latitude: 40.7812},"A newer Apple Maps place link");
        var (google,_)=Destinations.FromText("https://www.google.com/maps/place/Central+Park/@40.7812,-73.9665,15z");
        Check(google is {Name: "Central Park",Latitude: 40.7812,Longitude: -73.9665},"A Google Maps place link");
        Check(Destinations.FromText("geo:40.7812,-73.9665").Place is {Latitude: 40.7812},"A geo: link");
        Check(Destinations.FromText("https://maps.apple.com/?address=1%20Main%20St").Lookup=="1 Main St"&&
            Destinations.FromText("1 Main St, Springfield").Lookup=="1 Main St, Springfield","Addresses without coordinates are looked up");
        Check(Destinations.FromText("").Lookup==""&&Destinations.FromText(new string('x',5000)).Lookup==""&&
            Destinations.FromText("95, 200").Place==null&&Destinations.FromText("0, 0").Place==null,"Nothing, too much, or impossible coordinates");
        Check(Destinations.Clean("Line\none\t\u0007two  ",40)=="Line one two"&&Destinations.Clean(new string('a',100),60).Length==60,"Names and addresses are one clean line");
        Check(Destinations.FromLookup("{\"name\":\"Corner Cafe\",\"address\":\"1 Main St, Springfield, DC 20500\",\"zip\":\"20500\",\"lat\":38.9,\"lon\":-77.0}")==
            new Destination("Corner Cafe","1 Main St, Springfield, DC 20500",38.9,-77.0,"20500")&&Destinations.FromLookup("{\"address\":\"1 Main St\",\"lat\":38.9,\"lon\":-77.0}")?.Name=="1 Main St"&&
            Destinations.FromLookup("{\"name\":\"x\",\"lat\":0,\"lon\":0}")==null&&Destinations.FromLookup("nope")==null,"The phone's address lookup result");
        Check(!Destinations.Valid(new("",  "",40,-74))&&!Destinations.Valid(new("x","",double.NaN,1))&&Destinations.Valid(new("x","",40,-74)),"A place needs a name and a real location");

        // The request: the body Hyundai's own app sends, to the map service, counted as one command, never retried.
        var service=new DemoHyundai(Array.Empty<byte>());
        var budget=new RequestBudget(new MemoryBudgetStore(),null,RequestBudget.Defaults.ToDictionary(k=>k.Key,_=>new RequestLimit(TimeSpan.Zero,1000)));
        var watcher=new Watch(service);
        using var client=new HyundaiUsClient(watcher,null,budget);
        var cars=await client.LoginAsync(DemoHyundai.Email,"demo","0000",default);await client.SelectAsync(cars[0],default);
        int commands=budget.Status(RequestKind.Command).UsedToday;
        var place=new Destination("Corner \"Cafe\"","1 Main St, Springfield",38.89768,-77.036529,"20500");
        string? tid=await client.SendDestinationAsync(place,default);
        using(var doc=JsonDocument.Parse(service.LastDestination)) {
            var r=doc.RootElement;var poi=r.GetProperty("poiInfoList")[0];
            Check(r.GetProperty("actionType").GetString()=="SAVE"&&r.GetProperty("vin").GetString()==DemoHyundai.Vin&&r.GetProperty("username").GetString()==DemoHyundai.Email,"Body: save, for this car and account");
            Check(poi.GetProperty("name").GetString()=="Corner \"Cafe\""&&poi.GetProperty("addr").GetString()=="1 Main St, Springfield"&&poi.GetProperty("zip").GetString()=="20500","Name, address and ZIP, escaped");
            Check(poi.GetProperty("coord").GetProperty("lat").GetString()=="38.897680"&&poi.GetProperty("coord").GetProperty("lon").GetString()=="-77.036529"&&
                poi.GetProperty("src").GetString()=="HERE"&&poi.GetProperty("waypointID").GetInt32()==0,"Coordinates as text, like Hyundai's app");
        }
        Check(watcher.Path=="/ac/v2/rcs/stc/sendPoi"&&watcher.Method==HttpMethod.Post&&watcher.To=="GIS"&&!watcher.Headers.Contains("Location:")&&
            !watcher.Headers.Contains("deviceId")&&!watcher.Headers.Contains("deviceMeid"),"POST to the map service, with no phone location or device ID");
        Check(tid=="demo"&&budget.Status(RequestKind.Command).UsedToday==commands+1,"Hyundai's tracking ID comes back, and it counts as one command");
        int sent=watcher.Count;bool refused=false;
        try {await client.SendDestinationAsync(new Destination("Nowhere","",double.NaN,0),default);} catch(BluelinkException){refused=true;}
        Check(refused&&watcher.Count==sent,"A place without a valid location is never sent");
        watcher.Fail=true;bool failed=false;
        try {await client.SendDestinationAsync(place,default);} catch(BluelinkException){failed=true;}
        Check(failed&&watcher.Count==sent+1,"A refused send is reported once, never retried");
        Console.WriteLine($"PASS: {count} send-to-car checks (no network).");
    }
    sealed class Watch(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public string Path="",To="",Headers="";public HttpMethod? Method;public int Count;public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath.EndsWith("/sendPoi")) {
                Count++;Path=request.RequestUri.AbsolutePath;Method=request.Method;Headers=request.Headers.ToString();
                To=request.Headers.TryGetValues("to",out var to)?to.First():"";
                if(Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"errorCode\":\"400\"}",Encoding.UTF8,"application/json")});
            }
            return base.SendAsync(request,ct);
        }
    }
}
