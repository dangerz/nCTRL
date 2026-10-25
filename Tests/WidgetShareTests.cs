using System;
using System.Text.Json;
using NGarage.Core;

static class WidgetShareTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Widget and sharing: "+label);count++;}
        var now=new DateTimeOffset(2026,10,24,20,0,0,TimeSpan.Zero);var utc=TimeZoneInfo.Utc;
        var location=new VehicleLocation(40.123456,-74.654321,"");
        LiveVehicleState State(bool? charging=false,int? minutes=null)=>new(true,false,72,78,true,charging,184,80,90,"2026-10-24T19:30:00Z",now.AddMinutes(-5),
            ChargeMinutesLeft:minutes,StatusLocation:location);

        // The widget's file: formatted for the widget, nothing identifying.
        var status=WidgetStatus.From(State(),"IONIQ 5 N",UnitSystem.Imperial,ColdRangeSettings.Default,28,AppTheme.Default,now,utc);
        Check(status.Battery==78&&status.Range=="184 mi"&&status.Cold=="~129 mi at 28°F"&&status.Locked==true&&status.Charge==""&&
            status.Updated==now.AddMinutes(-5)&&status.Accent=="8ecbfa","Battery, range, cold estimate, locks and colors");
        Check(WidgetStatus.From(State(),"x",UnitSystem.Metric,ColdRangeSettings.Default,80,AppTheme.Derive("bb8efa"),now,utc) is {Range: "296 km",Cold: ""} purple&&
            purple.Accent==AppTheme.Derive("bb8efa")[ThemeRole.Accent],"Units and app color follow the settings; no cold line when warm");
        Check(WidgetStatus.From(State(true,70),"x",UnitSystem.Imperial,ColdRangeSettings.Default,null,AppTheme.Default,now,utc).Charge=="Full around 8:40 PM"&&
            WidgetStatus.From(State(true,20),"x",UnitSystem.Imperial,ColdRangeSettings.Default,null,AppTheme.Default,now,utc).Charge=="","Charging finish time while it's ahead");
        string json=status.Encode();
        using(var doc=JsonDocument.Parse(json)) {
            var r=doc.RootElement;
            Check(r.GetProperty("version").GetInt32()==1&&r.GetProperty("updated").GetString()=="2026-10-24T19:55:00Z"&&r.GetProperty("battery").GetInt32()==78,"The format the widget reads");
        }
        Check(!json.Contains("40.12")&&!json.Contains("74.65")&&!json.Contains("VIN")&&!json.Contains("@"),"No location, VIN or account in the widget's file");

        // Places shared from other apps.
        string Share(string[] urls,string[] texts)
        {
            using var stream=new System.IO.MemoryStream();
            using(var w=new Utf8JsonWriter(stream)) {
                w.WriteStartObject();w.WriteNumber("version",1);
                w.WriteStartArray("urls");foreach(var u in urls)w.WriteStringValue(u);w.WriteEndArray();
                w.WriteStartArray("texts");foreach(var t in texts)w.WriteStringValue(t);w.WriteEndArray();
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        const string card="BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Central Park\r\nitem1.ADR;type=WORK:;;5th Ave\\, at 59th St;New York;NY;10022;United States\r\nitem2.URL;type=pref:http://maps.apple.com/?ll=40.7812\\,-73.9665&q=Central%20Park\r\nEND:VCARD";
        var apple=Destinations.FromShare(Share(new[]{"https://maps.apple/p/abc123"},new[]{card}));
        Check(apple.Place==new Destination("Central Park","5th Ave, at 59th St, New York, NY 10022",40.7812,-73.9665),"Apple Maps: the contact card's place, name and address");
        var google=Destinations.FromShare(Share(new[]{"https://maps.app.goo.gl/xyz"},new[]{"Corner Cafe\n1 Main St, Springfield, DC 20500\nhttps://maps.app.goo.gl/xyz"}));
        Check(google.Place==null&&google.Lookup=="1 Main St, Springfield, DC 20500"&&google.Name=="Corner Cafe"&&google.ShortLink=="https://maps.app.goo.gl/xyz"&&
            google.Source=="Google Maps"&&google.Kinds=="link, text","Google Maps: its address and name, and its short link to open");
        Check(apple.Source=="Apple Maps"&&apple.Kinds=="link, contact card","Apple Maps is recognized by its contact card");
        Check(Destinations.FromShare(Share(new[]{"https://maps.apple.com/?ll=38.8977,-77.0365&q=The%20Ellipse"},new string[0])).Place is {Name: "The Ellipse"},"A plain maps link");
        Check(Destinations.FromShare(Share(new string[0],new[]{"38.8977, -77.0365"})).Place is {Latitude: 38.8977},"Shared coordinates");
        Check(Destinations.FromShare(Share(new[]{"https://example.com/page"},new string[0])) is {Place: null,Lookup: "",ShortLink: "",Source: "example.com"},"A link with no place");
        Check(Destinations.FromShare("not json") is {Place: null,Lookup: "",Source: ""}&&Destinations.FromShare("{}") is {Place: null,Lookup: ""},"Damaged or empty shares are ignored");
        Console.WriteLine($"PASS: {count} widget and sharing checks.");
    }
}
