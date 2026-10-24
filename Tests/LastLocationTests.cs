using System;
using NGarage.Core;

static class LastLocationTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Last location: "+label);count++;}
        var now=new DateTimeOffset(2026,10,23,20,0,0,TimeSpan.Zero);
        var requested=new VehicleLocation(40.25,-74.5,"2026-10-23T18:00:00Z",now.AddHours(-2));
        var inStatus=new VehicleLocation(40.111111,-74.222222,"2026-10-23T19:00:00Z",now.AddHours(-3));

        Check(LastLocation.Newest()==null&&LastLocation.Newest(null,null)==null,"No candidates means none");
        Check(LastLocation.Newest(requested,null)==requested,"A single location wins");
        Check(LastLocation.Newest(requested,inStatus)==inStatus,"Hyundai's later report time wins over when nCTRL received it");
        var compact=inStatus with {ReportedAt="20261023190000"};
        Check(LastLocation.Newest(requested,compact)==requested,"Different time formats fall back to when nCTRL received it");
        Check(LastLocation.Newest(requested with {ReportedAt=""},inStatus with {ReportedAt=""})?.Latitude==40.25,"Untimed locations compare by when received");
        Check(LastLocation.Same(inStatus,inStatus with {RetrievedAt=now})&&!LastLocation.Same(inStatus,inStatus with {ReportedAt="2026-10-23T19:05:00Z"})&&
            !LastLocation.Same(inStatus,null),"An unmoved repeat report is the same location");

        Check(LastLocation.Age(now.AddMinutes(-90),now)=="1 hour 30 minutes ago"&&LastLocation.Age(now.AddSeconds(-20),now)=="just now"&&
            LastLocation.Age(default,now)=="at an unknown time","Age reads plainly");

        string vehicle=VehicleLink.Fingerprint(VehicleLink.AccountKey("tester@example.invalid"),"TESTVIN0000000001");
        string json=LastLocation.Encode(vehicle,requested);
        Check(!json.Contains("TESTVIN")&&!json.Contains("tester"),"Stored without the VIN or email");
        Check(LastLocation.Decode(json,vehicle,now)==requested,"Round trip keeps the point and both times");
        Check(LastLocation.Decode(json,VehicleLink.Fingerprint(VehicleLink.AccountKey("other@example.invalid"),"TESTVIN0000000001"),now)==null,
            "Another account or vehicle never sees it");
        Check(LastLocation.Decode(json,vehicle,now.AddHours(-5))?.RetrievedAt==now.AddHours(-5),"A future receive time is clamped");
        Check(LastLocation.Decode("",vehicle,now)==null&&LastLocation.Decode("{nope",vehicle,now)==null&&
            LastLocation.Decode(json.Replace("40.25","95"),vehicle,now)==null&&LastLocation.Decode(json.Replace("\"version\":1","\"version\":2"),vehicle,now)==null,
            "Empty, malformed, out-of-range or unknown data reads as none");
        Console.WriteLine($"PASS: {count} last-location checks.");
    }
}
