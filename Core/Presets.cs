using System;
using System.Globalization;

namespace NGarage.Core;

public sealed record ChargePreset(string Name,int Ac,int Dc)
{
    public static readonly ChargePreset Daily=new("Daily 80%",80,80),RoadTrip=new("Road trip 100%",100,100);
}

// A one-tap climate start: temperature, front seats, steering wheel and front defrost.
public sealed record ClimatePreset(string Name,int TemperatureF,SeatMode DriverSeat,SeatMode PassengerSeat,bool SteeringWheel,bool Defrost)
{
    public static readonly ClimatePreset WarmUp=new("Warm up",76,SeatMode.HeatHigh,SeatMode.HeatHigh,true,true),
        CoolDown=new("Cool down",66,SeatMode.CoolHigh,SeatMode.CoolHigh,false,false);
    public ClimateOptions Options(bool seatsSupported)=>
        new(seatsSupported?DriverSeat:SeatMode.Off,seatsSupported?PassengerSeat:SeatMode.Off,SteeringWheel,Defrost);
    // Stored settings are untrusted: out-of-range values fall back to the preset's defaults.
    public ClimatePreset With(int temperatureF,int driverSeat,int passengerSeat,bool wheel,bool defrost)=>this with {
        TemperatureF=temperatureF is >=62 and <=82?temperatureF:TemperatureF,
        DriverSeat=Enum.IsDefined(typeof(SeatMode),driverSeat)?(SeatMode)driverSeat:DriverSeat,
        PassengerSeat=Enum.IsDefined(typeof(SeatMode),passengerSeat)?(SeatMode)passengerSeat:PassengerSeat,
        SteeringWheel=wheel,Defrost=defrost};
}

public static class ChargeEstimate
{
    /// <summary>When Hyundai expects the current charge to finish: minutes left counted from the report's
    /// own time when it has a full date and time, otherwise from when nCTRL received it. Null unless charging.</summary>
    public static DateTimeOffset? FullAt(LiveVehicleState? state)
    {
        if(state?.Charging!=true||state.ChargeMinutesLeft is not int minutes)return null;
        var from=DateTimeOffset.TryParse(state.ReportedAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var reported)&&
            state.ReportedAt.Contains('T')?reported:state.RetrievedAt;
        return from.AddMinutes(minutes);
    }
}
