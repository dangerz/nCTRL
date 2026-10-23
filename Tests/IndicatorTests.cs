using System;
using NGarage.Core;

static class IndicatorTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Car-art indicators: "+label);count++;}
        Check(VehicleIndicators.Lock(true)==LockEffect.Locked&&VehicleIndicators.Lock(false)==LockEffect.Unlocked,"Reported lock state drives the lock effect");
        Check(VehicleIndicators.Lock(null)==LockEffect.None,"Unknown lock state shows no lock effect");
        Check(VehicleIndicators.Climate(false,80)==ClimateEffect.None&&VehicleIndicators.Climate(null,80)==ClimateEffect.None,"Climate off or unknown shows no effect");
        Check(VehicleIndicators.Climate(true,74)==ClimateEffect.Heat&&VehicleIndicators.Climate(true,82)==ClimateEffect.Heat,"74°F and above shows heat");
        Check(VehicleIndicators.Climate(true,70)==ClimateEffect.Frost&&VehicleIndicators.Climate(true,62)==ClimateEffect.Frost,"70°F and below shows frost");
        Check(VehicleIndicators.Climate(true,71)==ClimateEffect.Airflow&&VehicleIndicators.Climate(true,73)==ClimateEffect.Airflow,"Between thresholds shows neutral airflow");
        Check(VehicleIndicators.Climate(true,null)==ClimateEffect.Airflow,"Climate on without a setpoint shows neutral airflow");
        Console.WriteLine($"PASS: {count} car-art indicator checks.");
    }
}
