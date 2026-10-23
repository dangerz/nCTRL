using System;
using System.Text.Json;
using NGarage.Core;

static class PresetTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Presets and charging: "+label);count++;}
        Check(ChargePreset.Daily is {Ac: 80,Dc: 80}&&ChargePreset.RoadTrip is {Ac: 100,Dc: 100},"Charge presets are Daily 80/80 and Road trip 100/100");
        var warm=ClimatePreset.WarmUp;var cool=ClimatePreset.CoolDown;
        Check(warm is {TemperatureF: 76,DriverSeat: SeatMode.HeatHigh,PassengerSeat: SeatMode.HeatHigh,SteeringWheel: true,Defrost: true},"Warm up defaults");
        Check(cool is {TemperatureF: 66,DriverSeat: SeatMode.CoolHigh,PassengerSeat: SeatMode.CoolHigh,SteeringWheel: false,Defrost: false},"Cool down defaults");
        Check(warm.Options(false) is {DriverSeat: SeatMode.Off,PassengerSeat: SeatMode.Off,SteeringWheel: true,Defrost: true},"Without seat support only the wheel and defrost are sent");
        Check(warm.With(70,(int)SeatMode.HeatLow,(int)SeatMode.Off,false,true) is {TemperatureF: 70,DriverSeat: SeatMode.HeatLow,PassengerSeat: SeatMode.Off,SteeringWheel: false},"Saved settings replace the defaults");
        Check(warm.With(99,42,-1,true,true) is {TemperatureF: 76,DriverSeat: SeatMode.HeatHigh,PassengerSeat: SeatMode.HeatHigh},"Out-of-range saved settings fall back");

        var received=new DateTimeOffset(2026,10,23,22,10,0,TimeSpan.Zero);
        LiveVehicleState State(bool? charging,int? minutes,string reported)=>new(true,false,72,60,true,charging,150,100,100,reported,received,ChargingPowerKw:7.2,ChargeMinutesLeft:minutes);
        Check(ChargeEstimate.FullAt(State(true,95,"2026-10-23T22:00:00Z"))==new DateTimeOffset(2026,10,23,23,35,0,TimeSpan.Zero),"Full time counts from the report's own time");
        Check(ChargeEstimate.FullAt(State(true,95,"20261023220000"))==received.AddMinutes(95),"A compact report time falls back to when it was received");
        Check(ChargeEstimate.FullAt(State(false,95,"2026-10-23T22:00:00Z"))==null&&ChargeEstimate.FullAt(State(null,95,""))==null,"No estimate unless charging");
        Check(ChargeEstimate.FullAt(State(true,null,"2026-10-23T22:00:00Z"))==null&&ChargeEstimate.FullAt(null)==null,"No estimate without minutes left");

        using var doc=JsonDocument.Parse("{\"vehicleStatus\":{\"evStatus\":{\"batteryCharge\":true,\"realTimePower\":7.2,\"remainTime2\":{\"atc\":{\"value\":95,\"unit\":1}}}}}");
        var s=HyundaiUsClient.ParseState(doc.RootElement);
        Check(s.Charging==true&&s.ChargingPowerKw==7.2&&s.ChargeMinutesLeft==95,"Charging power and minutes left parsed");
        using var other=JsonDocument.Parse("{\"vehicleStatus\":{\"evStatus\":{\"remainTime2\":{\"atc\":{\"value\":2,\"unit\":2}}}}}");
        Check(HyundaiUsClient.ParseState(other.RootElement).ChargeMinutesLeft==null,"Unknown time units are not interpreted");
        Console.WriteLine($"PASS: {count} preset and charging checks.");
    }
}
