using System;

namespace NGarage.Core;

/// <summary>Heated seats on a warm day, or cooled seats on a cold one, are usually a leftover setting. Starting climate
/// like that asks first, with the option to start without them. Uses the outside temperature nCTRL already reads for the
/// cold-weather range (Open-Meteo); with no recent reading it never asks.</summary>
public static class SeatWeather
{
    public enum Clash { None, HeatOnWarmDay, CoolOnColdDay }
    /// <summary>Asks about heat at this temperature or warmer (20 °C), and about cooling at this or colder (12 °C).</summary>
    public const double WarmF=68,ColdF=54;
    /// <summary>An older reading doesn't count: the weather may have changed.</summary>
    public static readonly TimeSpan MaxAge=TimeSpan.FromHours(2);

    public static bool Heats(SeatMode mode)=>mode is SeatMode.HeatLow or SeatMode.HeatMedium or SeatMode.HeatHigh;
    public static bool Cools(SeatMode mode)=>mode is SeatMode.CoolLow or SeatMode.CoolMedium or SeatMode.CoolHigh;

    /// <summary>The reading's temperature if it is recent enough to go by, else null.</summary>
    public static double? Usable(OutsideReading? reading,DateTimeOffset now)=>
        reading is {} r&&now-r.RetrievedAt<=MaxAge&&now>=r.RetrievedAt-TimeSpan.FromMinutes(5)&&double.IsFinite(r.TemperatureF)?r.TemperatureF:null;

    public static Clash Check(ClimateOptions? options,double? outsideF)
    {
        if(options==null||outsideF is not double t)return Clash.None;
        bool heat=Heats(options.DriverSeat)||Heats(options.PassengerSeat)||options.SteeringWheel;
        bool cool=Cools(options.DriverSeat)||Cools(options.PassengerSeat);
        if(heat&&t>=WarmF)return Clash.HeatOnWarmDay;
        if(cool&&t<=ColdF)return Clash.CoolOnColdDay;
        return Clash.None;
    }
    /// <summary>The same settings with the clashing seats (and, on a warm day, the heated wheel) turned off.</summary>
    public static ClimateOptions Without(ClimateOptions options,Clash clash)=>clash switch {
        Clash.HeatOnWarmDay=>options with {DriverSeat=Heats(options.DriverSeat)?SeatMode.Off:options.DriverSeat,
            PassengerSeat=Heats(options.PassengerSeat)?SeatMode.Off:options.PassengerSeat,SteeringWheel=false},
        Clash.CoolOnColdDay=>options with {DriverSeat=Cools(options.DriverSeat)?SeatMode.Off:options.DriverSeat,
            PassengerSeat=Cools(options.PassengerSeat)?SeatMode.Off:options.PassengerSeat},
        _=>options};
    public static string Title(Clash clash)=>clash==Clash.CoolOnColdDay?"COOLED SEATS?":"HEATED SEATS?";
    /// <param name="temperature">The outside temperature as shown to the user, e.g. "75°F" or "24°C".</param>
    public static string Question(Clash clash,ClimateOptions options,string temperature)
    {
        if(clash==Clash.CoolOnColdDay)return $"It's {temperature} near the car. Start climate with cooled seats anyway?";
        bool seats=Heats(options.DriverSeat)||Heats(options.PassengerSeat);
        string what=seats&&options.SteeringWheel?"heated seats and the heated steering wheel":seats?"heated seats":"the heated steering wheel";
        return $"It's {temperature} near the car. Start climate with {what} anyway?";
    }
}
