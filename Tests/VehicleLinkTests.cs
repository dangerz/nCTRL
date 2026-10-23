using System;
using NGarage.Core;

static class VehicleLinkTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Vehicle link: "+label);count++;}
        var first=new EnrolledVehicle("old-registration","TESTVIN0000000001","one","IONIQ5N",3,true);
        var second=first with {Vin="TESTVIN0000000002",Name="two"};
        string key=VehicleLink.AccountKey("tester@example.invalid");
        string fingerprint=VehicleLink.Fingerprint(key,second.Vin);
        Check(VehicleLink.Find(fingerprint,key,new[]{first,second})==1,"Select previously chosen vehicle, not first enrollment");
        Check(VehicleLink.Find(fingerprint,key,new[]{second with {Id="renewed-registration"},first})==0,"Registration ID and list order may change");
        Check(VehicleLink.Find(fingerprint,VehicleLink.AccountKey("other@example.invalid"),new[]{second})==-1,"Never select across accounts");
        Check(VehicleLink.Find(fingerprint,key,new[]{first})==-1,"Removed vehicle requires selection");
        Check(VehicleLink.Find("",key,new[]{first})==-1,"First sign-in requires selection");
        Check(VehicleLink.Find("malformed",key,new[]{second})==-1,"Invalid preference requires selection");
        Check(key==VehicleLink.AccountKey(" TESTER@example.invalid "),"Email normalization stable across relaunch");
        Check(fingerprint.Length==64&&!fingerprint.Contains("TESTVIN")&&!fingerprint.Contains("tester"),"Persisted preference contains no raw email or VIN");
        Check(VehicleLink.DoorAction(true)==ActionKind.Unlock,"Locked car offers unlock");
        Check(VehicleLink.DoorAction(false)==ActionKind.Lock,"Unlocked car offers lock");
        Check(VehicleLink.DoorAction(null)==null,"Unknown lock state never guesses a command");
        Console.WriteLine($"PASS: {count} remembered-vehicle and door-control checks.");
    }
}
