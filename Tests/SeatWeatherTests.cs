using System;
using NGarage.Core;

static class SeatWeatherTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Seat weather check: "+label);count++;}
        var warm=new ClimateOptions(SeatMode.HeatHigh,SeatMode.HeatLow,true,true);
        var cool=new ClimateOptions(SeatMode.CoolHigh,SeatMode.Off,false,false);
        var plain=new ClimateOptions(SeatMode.Off,SeatMode.Off,false,true);
        Check(SeatWeather.Check(warm,68)==SeatWeather.Clash.HeatOnWarmDay&&SeatWeather.Check(warm,67.9)==SeatWeather.Clash.None,"Heated seats ask from 68°F (20°C) up");
        Check(SeatWeather.Check(new ClimateOptions(SteeringWheel:true),80)==SeatWeather.Clash.HeatOnWarmDay,"The heated wheel alone asks on a warm day");
        Check(SeatWeather.Check(cool,54)==SeatWeather.Clash.CoolOnColdDay&&SeatWeather.Check(cool,54.1)==SeatWeather.Clash.None,"Cooled seats ask from 54°F (12°C) down");
        Check(SeatWeather.Check(warm,30)==SeatWeather.Clash.None&&SeatWeather.Check(cool,90)==SeatWeather.Clash.None,"Heat on a cold day and cooling on a hot one don't ask");
        Check(SeatWeather.Check(plain,95)==SeatWeather.Clash.None&&SeatWeather.Check(plain,10)==SeatWeather.Clash.None&&SeatWeather.Check(null,95)==SeatWeather.Clash.None,"No seats or wheel: never asks");
        Check(SeatWeather.Check(warm,null)==SeatWeather.Clash.None,"No temperature: never asks");
        var mixed=new ClimateOptions(SeatMode.HeatMedium,SeatMode.CoolLow,true,true);
        Check(SeatWeather.Without(mixed,SeatWeather.Clash.HeatOnWarmDay)==new ClimateOptions(SeatMode.Off,SeatMode.CoolLow,false,true),"Without seats on a warm day turns off only heating, keeping cooling and defrost");
        Check(SeatWeather.Without(mixed,SeatWeather.Clash.CoolOnColdDay)==new ClimateOptions(SeatMode.HeatMedium,SeatMode.Off,true,true),"Without seats on a cold day turns off only cooling");
        var now=DateTimeOffset.UtcNow;
        Check(SeatWeather.Usable(new OutsideReading(75,now.AddMinutes(-30),"a"),now)==75,"A half-hour-old reading counts");
        Check(SeatWeather.Usable(new OutsideReading(75,now.AddHours(-3),"a"),now)==null&&SeatWeather.Usable(null,now)==null&&
            SeatWeather.Usable(new OutsideReading(double.NaN,now,"a"),now)==null&&SeatWeather.Usable(new OutsideReading(75,now.AddHours(1),"a"),now)==null,
            "Old, missing, broken or future readings don't count");
        Check(SeatWeather.Question(SeatWeather.Clash.HeatOnWarmDay,warm,"24°C")=="It's 24°C near the car. Start climate with heated seats and the heated steering wheel anyway?"&&
            SeatWeather.Question(SeatWeather.Clash.HeatOnWarmDay,new ClimateOptions(SteeringWheel:true),"75°F").Contains("with the heated steering wheel anyway")&&
            SeatWeather.Question(SeatWeather.Clash.CoolOnColdDay,cool,"40°F").Contains("cooled seats")&&SeatWeather.Title(SeatWeather.Clash.CoolOnColdDay)=="COOLED SEATS?",
            "The question names what's on and the temperature in the user's units");
        Console.WriteLine($"PASS: {count} warm and cold seat checks.");
    }
}
