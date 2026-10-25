using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NGarage.Core;

static class DemoTests
{
    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Demo: "+label);count++;}
        var open=RequestBudget.Defaults.ToDictionary(k=>k.Key,_=>new RequestLimit(TimeSpan.Zero,1000));
        var demo=new DemoHyundai(Encoding.ASCII.GetBytes("demo-picture"));
        using var c=new HyundaiUsClient(demo,null,new RequestBudget(new MemoryBudgetStore(),null,open));

        // The real client, unchanged, against the pretend service.
        var cars=await c.LoginAsync(DemoHyundai.Email,"demo","0000",default);
        Check(cars.Count==1&&cars[0].Vin==DemoHyundai.Vin&&cars[0].Electric&&cars[0].Generation==3&&CarModels.Detect(cars[0].Model)?.Id=="ioniq5n","One electric demo IONIQ 5 N");
        await c.SelectAsync(cars[0],default);
        var s=c.State!;
        Check(s.Locked==true&&s.ClimateOn==false&&s.Battery==78&&s.AcLimit==80&&s.DcLimit==90&&s.RangeMiles>150&&s.Health?.Battery12V==86,"Status reads like a parked, locked car");
        Check(s.StatusLocation is {Latitude: DemoHyundai.Latitude}&&HealthAlerts.From(s).Count==0,"Status includes the demo location and raises no alerts");

        async Task Send(VehicleCommand cmd,int? dc=null){await c.ExecuteAsync(cmd,dc,default);await c.CheckCommandAsync(default);await c.ReadAsync(false,default);}
        await Send(new(ActionKind.Unlock));
        Check(c.State!.Locked==false&&c.Pending?.Outcome==RemoteOutcome.Confirmed,"Unlock is confirmed and reported");
        await Send(new(ActionKind.Lock));
        Check(c.State!.Locked==true,"Lock is reported");
        await Send(new(ActionKind.Climate,76,true,new ClimateOptions(SeatMode.HeatHigh,SeatMode.CoolLow,true,true)));
        Check(c.State!.ClimateOn==true&&c.State.TemperatureF==76&&c.State.DriverSeat==8&&c.State.PassengerSeat==3&&c.State.SteeringWheelHeat==true,"Climate, seats and wheel are reported back");
        await Send(new(ActionKind.Climate,76,false));
        Check(c.State!.ClimateOn==false&&c.State.DriverSeat==0&&c.State.SteeringWheelHeat==false,"Climate off clears seats and wheel");
        await Send(new(ActionKind.ChargeLimit,90),100);
        Check(c.State!.AcLimit==90&&c.State.DcLimit==100,"Charge targets change");
        await Send(new(ActionKind.Charge,0,true));
        Check(c.State!.Charging==true&&c.State.ChargingPowerKw==7.2&&c.State.ChargeMinutesLeft>0,"Charging reports power and time left");
        await Send(new(ActionKind.Lights));
        Check(c.Pending?.Outcome==RemoteOutcome.Confirmed,"Flash lights is confirmed");

        var at=await c.LocateAsync(default);
        Check(at.Latitude==DemoHyundai.Latitude&&at.Longitude==DemoHyundai.Longitude&&at.Heading==DemoHyundai.Heading&&
            s.StatusLocation?.Heading==DemoHyundai.Heading,"Location is the public demo place, with the way the car faces");
        var view=await c.CaptureSurroundViewAsync(null,default,(_,_)=>Task.CompletedTask);
        Check(Encoding.ASCII.GetString(view.Image)=="demo-picture","Surround View returns the given picture");

        var stray=await new System.Net.Http.HttpClient(new DemoHyundai(Array.Empty<byte>())).GetAsync("https://api.telematics.hyundaiusa.com/ac/v2/unknown");
        Check((int)stray.StatusCode==404,"Unknown routes are refused, never forwarded");
        Console.WriteLine($"PASS: {count} demo checks (no network).");
    }
}
