using System;
using System.Globalization;

namespace NGarage.Core;

public enum UnitSystem { Imperial, Metric }

/// <summary>Display formatting only. Hyundai USA reports and takes miles, °F and psi; nothing sent to
/// the car changes with the unit setting.</summary>
public static class Units
{
    public const double KmPerMile=1.609344,KpaPerPsi=6.894757,FeetPerMeter=3.28084;
    public static double Celsius(double f)=>(f-32)*5/9;
    public static double Fahrenheit(double c)=>c*9/5+32;
    private static string N(double value,string format="0")=>(value==0?0:value).ToString(format,CultureInfo.InvariantCulture);

    public static string Distance(double miles,UnitSystem units)=>units==UnitSystem.Metric?N(miles*KmPerMile)+" km":N(miles)+" mi";
    public static string Temperature(double f,UnitSystem units)=>units==UnitSystem.Metric?N(Math.Round(Celsius(f)))+"°C":N(Math.Round(f))+"°F";
    /// <summary>A climate setpoint. In °C it is rounded to the nearest half degree, as car displays do.</summary>
    public static string Setpoint(int f,UnitSystem units)=>units==UnitSystem.Metric?N(Math.Round(Celsius(f)*2)/2,"0.#")+"°C":f+"°F";
    public static string Pressure(double psi,UnitSystem units)=>units==UnitSystem.Metric?N(psi*KpaPerPsi)+" kPa":N(psi)+" psi";
    /// <summary>A place radius, in meters or in feet rounded to the nearest 10.</summary>
    public static string Radius(int meters,UnitSystem units)=>units==UnitSystem.Metric?meters+" m":N(Math.Round(meters*FeetPerMeter/10)*10)+" ft";
    /// <summary>A temperature setting moved one step: 5°F, or 2°C landing on whole degrees.</summary>
    public static double Step(double f,int direction,UnitSystem units)=>units==UnitSystem.Metric?Fahrenheit(Math.Round(Celsius(f))+2*direction):Math.Round(f)+5*direction;
    public static string Name(UnitSystem units)=>units==UnitSystem.Metric?"metric":"imperial";
    public static UnitSystem Parse(string? name)=>name=="metric"?UnitSystem.Metric:UnitSystem.Imperial;
}
