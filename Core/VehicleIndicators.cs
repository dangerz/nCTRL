namespace NGarage.Core;

public enum LockEffect { None, Locked, Unlocked }
public enum ComfortTint { None, Heat, Cool }

// Maps reported vehicle state to the car-art tints. Unknown state shows nothing rather than a guess.
public static class VehicleIndicators
{
    // Hyundai reports climate on/off and the setpoint, not whether it is heating or cooling.
    public const int HeatAtOrAbove=72;
    public static LockEffect Lock(bool? locked)=>locked switch {true=>LockEffect.Locked,false=>LockEffect.Unlocked,_=>LockEffect.None};
    public static ComfortTint Windshield(bool? climateOn,int? setpointF)=>climateOn!=true||setpointF is null?ComfortTint.None:
        setpointF>=HeatAtOrAbove?ComfortTint.Heat:ComfortTint.Cool;
    // Reported seat codes: 6–8 heat, 3–5 ventilation; off, plain "on" or unknown shows nothing.
    public static ComfortTint Seat(int? code)=>code is >=6 and <=8?ComfortTint.Heat:code is >=3 and <=5?ComfortTint.Cool:ComfortTint.None;
}
