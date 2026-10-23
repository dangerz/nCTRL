using System;
using System.Linq;
using NGarage.Core;

static class PlaceTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Ignored places: "+label);count++;}
        // Made-up coordinates, not a real address.
        var home=new IgnoredPlace("Home",40.111111,-74.222222);
        var places=new[]{home};
        var now=new DateTimeOffset(2026,10,23,14,0,0,TimeSpan.Zero);
        VehicleLocation At(double lat,double lon,string stamp="",DateTimeOffset received=default)=>new(lat,lon,stamp,received);
        Check(IgnoredPlaces.Match(places,At(40.111111,-74.222222))==home,"Same spot matches");
        Check(IgnoredPlaces.Match(places,At(40.112200,-74.222222))==home,"About 120 m away is inside the 150 m radius");
        Check(IgnoredPlaces.Match(places,At(40.112700,-74.222222))==null,"About 180 m away is outside the radius");
        Check(IgnoredPlaces.Match(places,null)==null&&IgnoredPlaces.Match(Array.Empty<IgnoredPlace>(),At(40.111111,-74.222222))==null,"No location or no places never matches");
        Check(Math.Abs(IgnoredPlaces.Meters(40.111111,-74.222222,40.112111,-74.222222)-111.2)<1,"Distance math");

        LiveVehicleState Report(string reported,VehicleLocation? inReport)=>new(false,false,72,78,false,false,184,80,90,reported,now,StatusLocation:inReport);
        var inReport=At(40.111111,-74.222222,"2026-10-23T13:55:00Z",now);
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",inReport),null)==inReport,"A location in the same report is fresh");
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",inReport with {ReportedAt="2026-10-23T13:40:00Z"}),null)==null,"A clearly older location in the report is not trusted");
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",inReport with {ReportedAt="20261023134000"}),null)!=null,"Different timestamp formats are not compared");
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",inReport with {ReportedAt=""}),null)!=null,"An untimed location in the same report is used");
        var requested=At(40.2,-74.3,"20261023135500",now.AddMinutes(-8));
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",null),requested)==requested,"A location requested within ten minutes is fresh");
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",null),requested with {RetrievedAt=now.AddMinutes(-11)})==null,"An older requested location is not");
        Check(IgnoredPlaces.FreshLocation(Report("2026-10-23T14:00:00Z",null),null)==null&&IgnoredPlaces.FreshLocation(null,requested)==null,"No location means unknown");

        var back=IgnoredPlaces.Decode(IgnoredPlaces.Encode(new[]{home,new IgnoredPlace("  Work   lot ",40.5,-74.5)}));
        Check(back.Count==2&&back[0]==home&&back[1].Name=="Work lot"&&back[1].RadiusMeters==150,"Places round-trip with cleaned names");
        Check(IgnoredPlaces.CleanName("")=="Place"&&IgnoredPlaces.CleanName(new string('x',50)).Length==IgnoredPlaces.MaxName,"Names are never empty or too long");
        Check(IgnoredPlaces.Decode("not json").Count==0&&IgnoredPlaces.Decode("{\"version\":1,\"places\":[{\"name\":\"x\",\"lat\":999,\"lon\":0,\"radius\":150}]}").Count==0,
            "Malformed or out-of-range places are dropped");
        Check(IgnoredPlaces.Decode(IgnoredPlaces.Encode(Enumerable.Range(0,30).Select(i=>new IgnoredPlace("P"+i,40,-74)).ToList())).Count==IgnoredPlaces.MaxPlaces,"Place count is capped");
        Console.WriteLine($"PASS: {count} ignored-place checks.");
    }
}
