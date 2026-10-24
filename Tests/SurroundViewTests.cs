using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class SurroundViewTests
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=new(2026,10,23,20,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
        public Task Advance(TimeSpan d,CancellationToken ct){ct.ThrowIfCancellationRequested();Now+=d;return Task.CompletedTask;}
    }
    // Fake Hyundai: details stay at the old capture until the given number of checks after the request.
    sealed class Hyundai : HttpMessageHandler
    {
        public readonly List<(string Path,string Method,string Body,Dictionary<string,string> Headers)> Calls=new();
        public int NewAfterChecks=2;public bool Busy,Refuse;int checksSinceRequest=-1;
        public static readonly string Image=Convert.ToBase64String(Encoding.ASCII.GetBytes("fake-jpeg-bytes"));
        static string Details(string time)=>"{\"svmDetails\":[{\"svmDetail\":{\"svmImage\":\""+Image+"\",\"gpsDetail\":{\"time\":\""+time+
            "\",\"coord\":{\"lat\":40.111111,\"lon\":-74.222222},\"head\":90,\"speed\":{\"value\":0,\"unit\":1}},\"trunkOpen\":false}}]}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            string path=r.RequestUri!.AbsolutePath;
            Calls.Add((path,r.Method.Method,r.Content==null?"":await r.Content.ReadAsStringAsync(ct),r.Headers.ToDictionary(h=>h.Key,h=>string.Join(",",h.Value))));
            string body;var status=HttpStatusCode.OK;
            if(path.EndsWith("/oauth/token"))body="{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":86400}";
            else if(path.Contains("/enrollment/"))body="{\"enrolledVehicleDetails\":[{\"vehicleDetails\":{\"regid\":\"reg-1\",\"vin\":\"TESTVIN0000000001\",\"nickName\":\"Test\",\"modelCode\":\"IONIQ5N\",\"vehicleGeneration\":\"3\",\"evStatus\":\"E\"}}]}";
            else if(path.EndsWith("/vehicleStatus"))body="{\"vehicleStatus\":{\"dateTime\":\"2026-10-23T19:59:00Z\",\"doorLock\":true}}";
            else if(path.EndsWith("/svm/findMyCarSVM")) {
                if(Busy){status=HttpStatusCode.BadGateway;body="{\"errorCode\":502,\"errorSubCode\":\"HT_533\",\"errorMessage\":\"private\"}";}
                else if(Refuse){status=HttpStatusCode.BadRequest;body="{\"errorCode\":400,\"errorMessage\":\"private\"}";}
                else {body="";checksSinceRequest=0;}
            }
            else if(path.EndsWith("/svm/getSVMDetails")) {
                bool fresh=checksSinceRequest>=0&&++checksSinceRequest>NewAfterChecks;
                body=Details(fresh?"2026-10-23T20:01:00Z":"2026-10-20T08:00:00Z");
            }
            else throw new Exception("Unexpected route "+path);
            return new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        }
    }
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("Surround View: "+name);count++;}
    static async Task<(HyundaiUsClient,Hyundai,Clock,RequestBudget)> Session()
    {
        var clock=new Clock();var hyundai=new Hyundai();var budget=new RequestBudget(new MemoryBudgetStore(),clock);
        var c=new HyundaiUsClient(hyundai,clock,budget);
        var list=await c.LoginAsync("tester@example.invalid","test-password","1234",default);await c.SelectAsync(list[0],default);
        return (c,hyundai,clock,budget);
    }
    public static async Task Run()
    {
        var (c,f,clock,budget)=await Session();
        using(c)
        {
            int checks=0;int before=f.Calls.Count;
            var view=await c.CaptureSurroundViewAsync(n=>checks=n,default,clock.Advance);
            var calls=f.Calls.Skip(before).ToList();
            Check(calls[0].Path=="/ac/v2/svm/getSVMDetails"&&calls[1].Path=="/ac/v2/svm/findMyCarSVM"&&calls[1].Method=="POST","Baseline read, then one capture request");
            using var body=JsonDocument.Parse(calls[1].Body);var root=body.RootElement;
            Check(root.EnumerateObject().Count()==4&&root.GetProperty("vin").GetString()=="TESTVIN0000000001"&&root.GetProperty("username").GetString()=="tester@example.invalid"&&
                root.GetProperty("gen").GetString()=="3"&&root.GetProperty("blueLinkServicePin").GetString()=="1234","Capture body matches upstream");
            Check(calls[1].Headers["registrationId"]=="reg-1"&&calls[1].Headers["vin"]=="TESTVIN0000000001","Standard vehicle headers");
            Check(calls.Count(x=>x.Method=="POST")==1&&checks==3&&calls.Count==5,"Checks every 15 s until a newer picture, never re-requesting");
            Check(Encoding.ASCII.GetString(view.Image)=="fake-jpeg-bytes"&&view.CapturedAt=="2026-10-23T20:01:00Z"&&view.Heading==90&&view.TrunkOpen==false,"Picture and details parsed");
            Check(view.Location is {Latitude: 40.111111}&&c.SurroundView==view,"Kept in memory on the client");
            Check(budget.Status(RequestKind.Camera).UsedToday==1&&budget.Status(RequestKind.Read).UsedToday>=4,"Counts one camera capture and its checks as reads");
            await RefusedLimit(()=>c.CaptureSurroundViewAsync(null,default,clock.Advance));
        }
        (c,f,clock,_)=await Session();
        using(c)
        {
            f.NewAfterChecks=99;int before=f.Calls.Count;
            try{await c.CaptureSurroundViewAsync(null,default,clock.Advance);throw new Exception("Surround View: timeout not reported");}
            catch(BluelinkException e) when(e is not RequestLimitException){Check(e.Message.Contains("within two minutes"),"Times out after about two minutes");}
            Check(f.Calls.Skip(before).Count(x=>x.Path.EndsWith("/getSVMDetails"))==1+HyundaiUsClient.SurroundViewChecks&&f.Calls.Skip(before).Count(x=>x.Method=="POST")==1,
                "Seven checks after the request, no second request");
        }
        (c,f,clock,_)=await Session();
        using(c)
        {
            f.Busy=true;
            try{await c.CaptureSurroundViewAsync(null,default,clock.Advance);throw new Exception("Surround View: busy not reported");}
            catch(BluelinkException e){Check(e.ApiSubCode=="HT_533"&&e.Message.Contains("already in progress")&&!e.Message.Contains("private"),"HT_533 means a capture is already running");}
        }
        (c,f,clock,_)=await Session();
        using(c)
        {
            f.Refuse=true;
            try{await c.CaptureSurroundViewAsync(null,default,clock.Advance);throw new Exception("Surround View: refusal not reported");}
            catch(BluelinkException e){Check(e.HttpStatus==400&&e.Message.StartsWith("Surround View: HTTP 400")&&!e.Message.Contains("private"),"A refusal is reported without server text");}
        }
        var old=new SurroundView(new byte[]{1},"2026-10-20T08:00:00Z",null,null,null,default);
        Check(HyundaiUsClient.IsNewerCapture(old,old with {CapturedAt="2026-10-23T20:01:00Z"})&&!HyundaiUsClient.IsNewerCapture(old,old),"Newer means a later capture time");
        Check(HyundaiUsClient.IsNewerCapture(old,old with {CapturedAt="20261023200100"})&&!HyundaiUsClient.IsNewerCapture(old,old with {Image=Array.Empty<byte>(),CapturedAt="2026-10-23T20:01:00Z"}),
            "Different time formats compare by value; a capture without a picture never counts");
        Check(HyundaiUsClient.IsNewerCapture(null,old)&&!HyundaiUsClient.IsNewerCapture(null,old with {CapturedAt=""}),"With no earlier capture, any timed picture counts");
        using(var none=JsonDocument.Parse("{}"))Check(HyundaiUsClient.ParseSurroundView(none.RootElement,default)==null,"No details means no picture");
        using(var bad=JsonDocument.Parse("{\"svmDetails\":[{\"svmDetail\":{\"svmImage\":\"%%%\"}}]}"))
            Check(HyundaiUsClient.ParseSurroundView(bad.RootElement,default)?.Image.Length==0,"Malformed picture data is discarded");
        Console.WriteLine($"PASS: {count} Surround View checks.");
    }
    static async Task RefusedLimit(Func<Task> action)
    {
        try{await action();}catch(RequestLimitException e){Check(e.Kind==RequestKind.Camera&&e.Overridable&&e.Message.Contains("5 minutes"),"A second capture within 5 minutes is refused before sending");return;}
        throw new Exception("Surround View: cooldown not enforced");
    }
}
