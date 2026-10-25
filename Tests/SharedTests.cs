using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class SharedTests
{
    sealed class Redirects : HttpMessageHandler
    {
        public readonly System.Collections.Generic.List<string> Asked=new();public bool Cookies;
        public Func<Uri,HttpResponseMessage> Answer=_=>new(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Asked.Add(request.RequestUri!.ToString());Cookies|=request.Headers.Contains("Cookie")||request.Headers.Contains("Authorization");
            return Task.FromResult(Answer(request.RequestUri));
        }
        public static HttpResponseMessage To(string location){var r=new HttpResponseMessage(HttpStatusCode.Found);r.Headers.Location=new Uri(location);return r;}
    }
    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Shared places: "+label);count++;}

        // Reading Google Maps' full addresses: the place's own pin over where the map was centered.
        const string place="https://www.google.com/maps/place/Central+Park/@40.7828647,-73.9653551,15z/data=!3m1!4b1!4m6!3m5!1s0x0:0x0!8m2!3d40.7812199!4d-73.9665138!16s";
        Check(Destinations.FromText(place).Place is {Name: "Central Park",Latitude: 40.7812199,Longitude: -73.9665138},"A Google place's pin and name");
        Check(Destinations.FromText("https://www.google.com/maps/@40.78,-73.96,15z").Place is {Latitude: 40.78},"Just a map view: its center");
        Check(Destinations.FromText("https://maps.google.com/?q=40.7812,-73.9665").Place is {Latitude: 40.7812},"Coordinates in q");
        Check(Destinations.FromText("https://maps.google.com/maps?q=Corner+Cafe,+1+Main+St").Lookup=="Corner Cafe, 1 Main St","An address in q is looked up");
        Check(Destinations.FromText("https://consent.google.com/ml?continue="+Uri.EscapeDataString(place)).Place is {Name: "Central Park"},"Google's cookie notice wrapping the link");

        // Short links: opened only to read where they lead.
        Check(ShortLinks.IsGoogleShort("https://maps.app.goo.gl/abc")&&ShortLinks.IsGoogleShort("https://goo.gl/maps/abc")&&!ShortLinks.IsGoogleShort("https://goo.gl/other")&&
            !ShortLinks.IsGoogleShort("http://maps.app.goo.gl/abc")&&!ShortLinks.IsGoogleShort("https://example.com/x"),"Which links are Google Maps short links");
        var google=new Redirects {Answer=uri=>Redirects.To(place)};
        Check(await ShortLinks.ResolveAsync("https://maps.app.goo.gl/abc",google,default)==place&&google.Asked.Count==1&&!google.Cookies,"One request, no cookies or account details");
        var chain=new Redirects {Answer=uri=>uri.Host=="maps.app.goo.gl"?Redirects.To("https://goo.gl/maps/next"):Redirects.To(place)};
        Check(await ShortLinks.ResolveAsync("https://maps.app.goo.gl/abc",chain,default)==place&&chain.Asked.Count==2,"A chain of short links");
        var loop=new Redirects {Answer=uri=>Redirects.To("https://maps.app.goo.gl/again")};
        Check(await ShortLinks.ResolveAsync("https://maps.app.goo.gl/abc",loop,default)==null&&loop.Asked.Count==ShortLinks.MaxHops,"At most 3 hops");
        var elsewhere=new Redirects {Answer=uri=>Redirects.To("https://example.com/maps/@40.7,-73.9")};
        Check(await ShortLinks.ResolveAsync("https://maps.app.goo.gl/abc",elsewhere,default)==null,"Never follows a link away from Google");
        var nowhere=new Redirects();
        Check(await ShortLinks.ResolveAsync("https://maps.app.goo.gl/abc",nowhere,default)==null&&await ShortLinks.ResolveAsync("https://example.com/a",nowhere,default)==null&&
            nowhere.Asked.Count==1,"A link that leads nowhere, and links that aren't short links, give nothing");

        // The history: newest first, 20 at most, only the place itself.
        var now=new DateTimeOffset(2026,10,24,20,0,0,TimeSpan.Zero);
        var history=Enumerable.Range(0,25).Aggregate((System.Collections.Generic.IReadOnlyList<SharedEntry>)Array.Empty<SharedEntry>(),
            (list,i)=>SharedHistory.Add(list,new SharedEntry(now.AddMinutes(i),"Google Maps","link",i%2==0?new Destination("Place "+i,"Address",40,-74):null,i%2==0?"":"Couldn't read it",i%3==0)));
        Check(history.Count==SharedHistory.Max&&history[0].Place?.Name=="Place 24"&&history[^1].At==now.AddMinutes(5),"Newest first, at most 20");
        var reread=SharedHistory.Decode(SharedHistory.Encode(history));
        Check(reread.Count==20&&reread[0]==history[0]&&reread[1].Place==null&&reread[1].Problem=="Couldn't read it"&&reread[0].Opened==true,"Saved and read back");
        Check(SharedHistory.Decode("nope").Count==0&&SharedHistory.Decode("{\"version\":2,\"shared\":[]}").Count==0,"Damaged history reads as empty");
        var receipts=SharedHistory.Receipts("[{\"id\":\"a\",\"at\":\"2026-10-24T20:00:00Z\",\"kinds\":[\"link\",\"text\"],\"hosts\":[\"maps.app.goo.gl\"],\"saved\":true,\"opened\":false},{\"at\":\"bad\"}]");
        Check(receipts.Count==1&&receipts[0]==new SharedReceipt("a",now,"link, text","Google Maps",true,false),"The Share extension's receipts");
        var sentHere=SharedHistory.Receipts("[{\"id\":\"s\",\"at\":\"2026-10-24T20:00:00Z\",\"kinds\":[\"link\"],\"hosts\":[\"maps.apple.com\"],\"saved\":true,\"opened\":false,\"sent\":true,"+
            "\"place\":{\"name\":\"Central Park\",\"address\":\"New York, NY\",\"zip\":\"\",\"lat\":40.7812,\"lon\":-73.9665}}]");
        Check(sentHere.Count==1&&sentHere[0].Sent&&sentHere[0].Place?.Name=="Central Park"&&sentHere[0].Source=="Apple Maps","A place the Share sheet sent itself comes back with its receipt");
        Check(Destinations.FromShare("{\"version\":1,\"urls\":[\"https://maps.apple.com/?ll=40.78,-73.96&q=Park\"],\"texts\":[],\"unsent\":\"Hyundai is rate limiting requests.\"}").Unsent==
            "Hyundai is rate limiting requests.","A share the Share sheet couldn't send says why");
        Console.WriteLine($"PASS: {count} shared-place checks (no network).");
    }
}
