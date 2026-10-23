namespace NGarage.Core;

public enum LockEffect { None, Locked, Unlocked }
public enum ClimateEffect { None, Heat, Frost, Airflow }

// Maps reported vehicle state to the car-art effects. Unknown state shows nothing rather than a guess.
public static class VehicleIndicators
{
    public const int HeatAtOrAbove=74, FrostAtOrBelow=70;
    public static LockEffect Lock(bool? locked)=>locked switch {true=>LockEffect.Locked,false=>LockEffect.Unlocked,_=>LockEffect.None};
    public static ClimateEffect Climate(bool? on,int? setpointF)=>on!=true?ClimateEffect.None:
        setpointF>=HeatAtOrAbove?ClimateEffect.Heat:setpointF<=FrostAtOrBelow?ClimateEffect.Frost:ClimateEffect.Airflow;
}
