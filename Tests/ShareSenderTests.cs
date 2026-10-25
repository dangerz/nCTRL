using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NGarage.Core;

static class ShareSenderTests
{
    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Share sheet sending: "+label);count++;}
        string Request(string account,string? session,string place,string limits)
        {
            using var stream=new MemoryStream();
            using(var w=new Utf8JsonWriter(stream)) {
                w.WriteStartObject();w.WriteString("account",account);
                if(session!=null)w.WriteString("session",session);else w.WriteNull("session");
                w.WritePropertyName("place");using(var p=JsonDocument.Parse(place))p.RootElement.WriteTo(w);
                w.WriteString("limits",limits);w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        const string park="{\"name\":\"Central Park\",\"address\":\"New York, NY\",\"zip\":\"10022\",\"lat\":40.7812,\"lon\":-73.9665}";
        string account=new SavedAccount(DemoHyundai.Email,"demo","0000").Encode();
        string limits=Path.Combine(Path.GetTempPath(),"nctrl-share-limits-"+Guid.NewGuid().ToString("N")+".json");
        try {
            // The app's session, as it would save it after connecting.
            var demo=new DemoHyundai(Array.Empty<byte>());
            using(var app=new HyundaiUsClient(demo,null,new RequestBudget(new MemoryBudgetStore()))) {
                var cars=await app.LoginAsync(DemoHyundai.Email,"demo","0000",default);await app.SelectAsync(cars[0],default);
                var session=app.ExportSession()!;
                Check(SharedSession.Decode(session.Encode())==session&&!session.ToString().Contains(session.AccessToken),"The shared session round-trips and never prints its tokens");
                int before=demo.Requests;
                string sent=await ShareSender.SendAsync(Request(account,session.Encode(),park,limits),demo,default);
                using(var r=JsonDocument.Parse(sent)) {
                    Check(r.RootElement.GetProperty("ok").GetBoolean()&&r.RootElement.GetProperty("tracking").GetString()=="demo"&&
                        SharedSession.Decode(r.RootElement.GetProperty("session").GetString()!)?.Vehicle.Vin==DemoHyundai.Vin,"Sent with the app's session, returning the tracking ID and the session");
                }
                Check(demo.Requests==before+1&&demo.LastDestination.Contains("\"name\":\"Central Park\""),"Exactly one request: no new sign-in with a session");
                Check(new RequestBudget(new PathBudgetStore(limits)).Status(RequestKind.Command).UsedToday==1,"Counted in the shared request limits file");
                // Destinations skip the command cooldown: a second send right away goes through, and still counts.
                string again=await ShareSender.SendAsync(Request(account,session.Encode(),park,limits),demo,default);
                using(var r=JsonDocument.Parse(again))
                    Check(r.RootElement.GetProperty("ok").GetBoolean()&&demo.Requests==before+2&&
                        new RequestBudget(new PathBudgetStore(limits)).Status(RequestKind.Command).UsedToday==2,"A second send right away isn't held by the command cooldown, and is counted");
            }
            // No session from the app yet: signs in, and with one car on the account, sends to it.
            File.Delete(limits);
            var fresh=new DemoHyundai(Array.Empty<byte>());
            string signedIn=await ShareSender.SendAsync(Request(account,null,park,limits),fresh,default);
            using(var r=JsonDocument.Parse(signedIn))Check(r.RootElement.GetProperty("ok").GetBoolean()&&fresh.LastDestination.Contains("Central Park"),"Without a session: signs in and sends to the only car");
            // Problems come back as a message, with nothing sent.
            foreach(var (request,label) in new[]{(Request("not a login",null,park,limits),"a damaged login"),
                (Request(account,null,"{\"name\":\"Nowhere\",\"lat\":0,\"lon\":0}",limits),"a place without a location")}) {
                int sentBefore=fresh.Requests;
                using var r=JsonDocument.Parse(await ShareSender.SendAsync(request,fresh,default));
                Check(!r.RootElement.GetProperty("ok").GetBoolean()&&r.RootElement.GetProperty("error").GetString()!.Length>0&&fresh.Requests==sentBefore,"Nothing sent for "+label);
            }
            // Reading a share for the extension: the place, or what to look up.
            const string card="BEGIN:VCARD\r\nFN:Central Park\r\nitem1.URL;type=pref:http://maps.apple.com/?ll=40.7812\\,-73.9665\r\nEND:VCARD";
            using(var r=JsonDocument.Parse(await ShareSender.ReadJsonAsync("{\"version\":1,\"urls\":[],\"texts\":["+JsonString(card)+"]}",null,default)))
                Check(r.RootElement.GetProperty("place").GetProperty("name").GetString()=="Central Park"&&r.RootElement.GetProperty("source").GetString()=="Apple Maps","Reads an Apple Maps share's place");
            using(var r=JsonDocument.Parse(await ShareSender.ReadJsonAsync("{\"version\":1,\"urls\":[],\"texts\":[\"Corner Cafe\\n1 Main St, Springfield\"]}",null,default)))
                Check(!r.RootElement.TryGetProperty("place",out _)&&r.RootElement.GetProperty("lookup").GetString()=="1 Main St, Springfield"&&r.RootElement.GetProperty("name").GetString()=="Corner Cafe",
                    "An address to look up, with its name");
        } finally {if(File.Exists(limits))File.Delete(limits);}
        Console.WriteLine($"PASS: {count} Share sheet sending checks (no network).");
    }
    static string JsonString(string text)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream))w.WriteStringValue(text);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
