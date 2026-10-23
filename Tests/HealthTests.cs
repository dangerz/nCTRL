using System;
using System.Linq;
using System.Text.Json;
using NGarage.Core;

static class HealthTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Health alerts: "+label);count++;}
        var at=new DateTimeOffset(2026,10,23,14,0,0,TimeSpan.Zero);
        var closed=new VehicleHealth(87,false,false,false,false,false,false,false,false,false,false,false,false,false,false);
        LiveVehicleState State(bool? locked,VehicleHealth? h)=>new(locked,false,72,78,false,false,184,80,90,"2026-10-23T14:00:00Z",at,Health:h);

        Check(HealthAlerts.From(null).Count==0&&HealthAlerts.From(State(null,null)).Count==0,"Unknown state raises nothing");
        Check(HealthAlerts.From(State(true,closed)).Count==0,"Closed, locked and healthy raises nothing");
        var unlocked=HealthAlerts.From(State(false,closed));
        Check(unlocked.Count==1&&unlocked[0].Kind==AlertKind.Unlocked&&unlocked[0].Critical,"Unlocked doors raise a critical alert");
        Check(HealthAlerts.From(State(false,closed),atIgnoredPlace:true).Count==0,"An ignored place hides only the unlocked alert");
        var open=closed with {FrontRightDoor=true,Tailgate=true,RearLeftWindow=true,Battery12V=41,TireWarning=true,WasherFluidLow=true,KeyFobBatteryLow=true,BrakeFluidWarning=true};
        var all=HealthAlerts.From(State(false,open),atIgnoredPlace:true);
        Check(all.Select(a=>a.Text).SequenceEqual(new[]{"Front-right door open","Tailgate open","Rear-left window open","Tire pressure low",
            "12-volt battery low (41%)","Brake fluid warning","Washer fluid low","Key fob battery low"}),"Every alert, most important first, still shown at an ignored place");
        Check(all.Where(a=>!a.Critical).Select(a=>a.Kind).SequenceEqual(new[]{AlertKind.Battery12VLow,AlertKind.WasherFluid,AlertKind.KeyFob}),"Reminders are not critical");
        Check(HealthAlerts.From(State(false,open)).First().Kind==AlertKind.Unlocked,"Unlocked comes first");
        Check(!HealthAlerts.Low12V(State(true,closed with {Battery12V=50}))&&HealthAlerts.Low12V(State(true,closed with {Battery12V=49}))&&
            !HealthAlerts.Low12V(State(true,closed with {Battery12V=null})),"12-volt low means below 50%, never when unknown");
        var unknown=new VehicleHealth(null,null,null,null,null,null,null,null,null,null,null,null,null,null,null);
        Check(HealthAlerts.From(State(true,unknown)).Count==0,"Unreported items never raise alerts");

        // Parsing from Hyundai's status, including number and text booleans and a status-included location.
        using var doc=JsonDocument.Parse("""
        {"vehicleStatus":{"dateTime":"2026-10-23T14:00:00Z","doorLock":false,"battery":{"batSoc":41},
          "doorOpen":{"frontLeft":0,"frontRight":1,"backLeft":0,"backRight":0},"trunkOpen":"true","hoodOpen":false,
          "windowOpen":{"frontLeft":0,"frontRight":0,"backLeft":1,"backRight":0},
          "tirePressureLamp":{"tirePressureWarningLampFrontLeft":0,"tirePressureWarningLampRearRight":1},
          "washerFluidStatus":0,"breakOilStatus":false,"smartKeyBatteryWarning":1,
          "vehicleLocation":{"coord":{"lat":40.111111,"lon":-74.222222},"time":"20261023140000"}}}
        """);
        var parsed=HyundaiUsClient.ParseState(doc.RootElement);var ph=parsed.Health!;
        Check(ph.Battery12V==41&&ph.FrontRightDoor==true&&ph.FrontLeftDoor==false&&ph.Tailgate==true&&ph.Hood==false&&ph.RearLeftWindow==true,"Openings and 12-volt parsed");
        Check(ph.TireWarning==true&&ph.WasherFluidLow==false&&ph.BrakeFluidWarning==false&&ph.KeyFobBatteryLow==true,"Per-tire light and warnings parsed");
        Check(parsed.StatusLocation is {Latitude: 40.111111,Longitude: -74.222222,ReportedAt: "20261023140000"},"Status-included location kept for ignored-place checks");
        using var none=JsonDocument.Parse("{\"vehicleStatus\":{\"vehicleLocation\":{\"coord\":{\"lat\":0,\"lon\":0}}}}");
        var bare=HyundaiUsClient.ParseState(none.RootElement);
        Check(bare.StatusLocation==null&&bare.Health!.Battery12V==null&&bare.Health.TireWarning==null,"Missing or zero location and unreported items stay unknown");
        Console.WriteLine($"PASS: {count} car-health checks.");
    }
}
