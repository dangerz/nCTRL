using System;
using NGarage.Core;

static class IndicatorTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Car-art indicators: "+label);count++;}
        Check(VehicleIndicators.Lock(true)==LockEffect.Locked&&VehicleIndicators.Lock(false)==LockEffect.Unlocked,"Reported lock state drives the padlock");
        Check(VehicleIndicators.Lock(null)==LockEffect.None,"Unknown lock state shows no padlock");
        Check(VehicleIndicators.Windshield(false,80)==ComfortTint.None&&VehicleIndicators.Windshield(null,80)==ComfortTint.None,"Climate off or unknown leaves the windshield clear");
        Check(VehicleIndicators.Windshield(true,72)==ComfortTint.Heat&&VehicleIndicators.Windshield(true,82)==ComfortTint.Heat,"Setpoint 72°F and above tints the windshield red");
        Check(VehicleIndicators.Windshield(true,71)==ComfortTint.Cool&&VehicleIndicators.Windshield(true,62)==ComfortTint.Cool,"Setpoint below 72°F tints the windshield blue");
        Check(VehicleIndicators.Windshield(true,null)==ComfortTint.None,"Climate on without a setpoint is not guessed");
        Check(VehicleIndicators.Seat(6)==ComfortTint.Heat&&VehicleIndicators.Seat(8)==ComfortTint.Heat,"Heated seat codes tint red");
        Check(VehicleIndicators.Seat(3)==ComfortTint.Cool&&VehicleIndicators.Seat(5)==ComfortTint.Cool,"Ventilated seat codes tint blue");
        Check(VehicleIndicators.Seat(0)==ComfortTint.None&&VehicleIndicators.Seat(1)==ComfortTint.None&&VehicleIndicators.Seat(2)==ComfortTint.None&&VehicleIndicators.Seat(null)==ComfortTint.None,
            "Off, unspecified on, or unreported seats show nothing");
        Console.WriteLine($"PASS: {count} car-art indicator checks.");
    }
}
