using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class MapTests
{
    static int count;
    static void Check(bool condition,string name){if(!condition)throw new Exception("Map: "+name);count++;}
    sealed class Tiles : HttpMessageHandler
    {
        public int Calls;
        public bool NotModified,Invalid;
        public bool Conditional;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Calls++;Conditional=r.Headers.IfModifiedSince!=null;
            Check(r.Method==HttpMethod.Get&&r.RequestUri?.Host=="tile.openstreetmap.org"&&r.RequestUri.Scheme=="https","HTTPS map-only GET");
            Check(r.Headers.UserAgent.ToString().StartsWith("nCTRL/")&&!r.Headers.Any(x=>x.Key is "username" or "vin" or "accessToken" or "blueLinkServicePin"),"Map identification, no account headers");
            return Task.FromResult(new HttpResponseMessage(NotModified?HttpStatusCode.NotModified:HttpStatusCode.OK) {
                Content=new ByteArrayContent(Invalid?new byte[]{0}:new byte[]{137,80,78,71,13,10,26,10,0})});
        }
    }
    public static async Task Run()
    {
        foreach(var location in new[]{new VehicleLocation(0,0,""),new(40.7,-74,""),new(90,180,""),new(-90,-180,"")}) {
            var tiles=VehicleMap.Tiles(location);
            Check(tiles.Count>0&&tiles.Count<=9,"Only visible bounded tile set");
            Check(tiles.All(t=>t.X>=0&&t.X<32768&&t.Y>=0&&t.Y<32768&&double.IsFinite(t.Left)&&double.IsFinite(t.Top)),"Dateline wrapping and polar clamp");
        }
        var center=VehicleMap.Tiles(new(0,0,""));
        Check(center.Any(t=>t.X==16384&&t.Y==16384&&t.Left==206&&t.Top==160),"Mercator center falls at vehicle marker");
        var culture=CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            Check(VehicleMap.ExternalUrl(new(40.25,-74.5,""),"Android").StartsWith("geo:40.25,-74.5?q="),"Android resolves default app with invariant coordinates");
            Check(VehicleMap.ExternalUrl(new(40.25,-74.5,""),"iOS").Contains("40.25,-74.5"),"iOS invariant coordinates");
        } finally {CultureInfo.CurrentCulture=culture;}
        try {VehicleMap.Tiles(new(double.NaN,0,""));throw new Exception("Invalid coordinate accepted");}
        catch(ArgumentOutOfRangeException){count++;}
        string directory=Path.Combine(Environment.CurrentDirectory,"nctrl-map-test-"+Guid.NewGuid().ToString("N"));
        try {
            var fake=new Tiles();using var cache=new MapTileCache(directory,fake);
            var tile=center[0];await cache.LoadAsync(tile,default);await cache.LoadAsync(tile,default);
            Check(fake.Calls==1,"Repeat map view uses disk cache");
            string file=Directory.GetFiles(directory,"*.png").Single();
            File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-8));fake.NotModified=true;
            await cache.LoadAsync(tile,default);
            Check(fake.Calls==2&&fake.Conditional,"Expired cache uses conditional GET");
            await cache.LoadAsync(tile,default);Check(fake.Calls==2,"304 refreshes cache lifetime");
            fake.NotModified=false;fake.Invalid=true;
            try{await cache.LoadAsync(center[1],default);throw new Exception("Invalid tile cached");}
            catch(IOException){count++;}
            Check(Directory.GetFiles(directory,"*.png").Length==1,"Malformed download never saved");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            try{await cache.LoadAsync(tile,cancellation.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){count++;}
        } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
        Console.WriteLine($"PASS: {count} offline map checks (no network).");
    }
}
