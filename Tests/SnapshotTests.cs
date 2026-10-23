using System;
using NGarage.Core;

static class SnapshotTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Vehicle snapshot: "+label);count++;}
        string account=VehicleLink.AccountKey("tester@example.invalid");
        string vehicle=VehicleLink.Fingerprint(account,"TESTVIN0000000001");
        var state=new LiveVehicleState(true,false,72,78,true,false,184.5,80,90,"2026-10-22T10:00:00Z",
            new DateTimeOffset(2026,10,23,14,5,0,TimeSpan.Zero),8,null,true);
        var snapshot=new VehicleSnapshot(account,vehicle,"Test Ioniq 5 N","000001",3,state);
        string json=snapshot.Encode();
        var back=VehicleSnapshot.Decode(json);
        Check(back!=null&&back.State==state&&back.Name==snapshot.Name&&back.VinTail=="000001"&&back.Generation==3&&back.AccountKey==account&&back.VehicleFingerprint==vehicle,"Snapshot round-trips every status field");
        Check(!json.Contains("tester")&&!json.Contains("TESTVIN")&&!json.Contains("lat")&&!json.Contains("lon"),"Stored snapshot has no email, full VIN or location");
        var unknown=VehicleSnapshot.Decode(new VehicleSnapshot(account,vehicle,"","",2,new LiveVehicleState(null,null,null,null,null,null,null,null,null,"",state.RetrievedAt)).Encode());
        Check(unknown!=null&&unknown.State.Locked==null&&unknown.State.RangeMiles==null&&unknown.State.DriverSeat==null,"Unknown values stay unknown");
        Check(VehicleSnapshot.Decode("not json")==null&&VehicleSnapshot.Decode("")==null,"Malformed snapshot is discarded");
        Check(VehicleSnapshot.Decode(json.Replace(account,"tester@example.invalid"))==null,"Raw email is never accepted as the account key");
        Check(VehicleSnapshot.Decode(json.Replace("\"000001\"","\"TESTVIN0000000001\""))==null,"Full VIN is never accepted");
        Check(VehicleSnapshot.Decode(json.Replace("\"version\":1","\"version\":2"))==null,"Unknown format version is discarded");
        var health=new VehicleHealth(41,false,true,null,false,true,false,false,false,true,false,true,false,null,true);
        var withHealth=snapshot with {State=state with {Health=health,StatusLocation=new VehicleLocation(40.111111,-74.222222,"t")}};
        string healthJson=withHealth.Encode();var healthBack=VehicleSnapshot.Decode(healthJson);
        Check(healthBack?.State.Health==health,"Health and openings round-trip");
        Check(healthBack?.State.StatusLocation==null&&!healthJson.Contains("40.11")&&!healthJson.Contains("74.22"),"The status location is never saved");
        Check(VehicleSnapshot.Decode(json)?.State.Health==null,"Snapshots saved before health existed still open");
        var ignored=VehicleSnapshot.Decode((withHealth with {AtIgnoredPlace=true}).Encode());
        Check(ignored?.AtIgnoredPlace==true&&VehicleSnapshot.Decode(json)?.AtIgnoredPlace==false,"Only a yes/no ignored-place flag is saved; older snapshots read as no");
        Console.WriteLine($"PASS: {count} vehicle-snapshot checks.");
    }
}
