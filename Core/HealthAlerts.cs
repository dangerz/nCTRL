using System.Collections.Generic;

namespace NGarage.Core;

public enum AlertKind { Unlocked, DoorOpen, TailgateOpen, HoodOpen, WindowOpen, TireLow, Battery12VLow, BrakeFluid, WasherFluid, KeyFob }
// Critical alerts are things to act on now (open or unlocked car, tires, brakes); the rest are reminders.
public sealed record HealthAlert(AlertKind Kind,string Text,bool Critical);

/// <summary>"Left open" and health alerts from the last reported status, most important first.
/// Unknown values never raise an alert.</summary>
public static class HealthAlerts
{
    public const int Battery12VLowBelow=50;
    public static bool Low12V(LiveVehicleState? state)=>state?.Health?.Battery12V is int soc&&soc<Battery12VLowBelow;

    /// <summary>The Car screen line when no alert is raised. An unlocked car at an ignored place is
    /// still unlocked: say so rather than implying it is locked.</summary>
    public static string QuietStatus(LiveVehicleState? state,string? ignoredPlaceName)=>state?.Locked switch {
        null=>"Door status unknown",
        false=>"Unlocked · ignored "+(ignoredPlaceName is {Length: >0} name?"at "+name:"here"),
        _=>state.Health==null?"Doors locked":"All closed and locked"};
    // atIgnoredPlace hides only the unlocked alert; everything else still shows there.
    public static IReadOnlyList<HealthAlert> From(LiveVehicleState? state,bool atIgnoredPlace=false)
    {
        var alerts=new List<HealthAlert>();
        if(state==null)return alerts;
        var h=state.Health;
        void Add(bool? reported,AlertKind kind,string text,bool critical=true){if(reported==true)alerts.Add(new(kind,text,critical));}
        if(state.Locked==false&&!atIgnoredPlace)alerts.Add(new(AlertKind.Unlocked,"Doors unlocked",true));
        if(h==null)return alerts;
        Add(h.FrontLeftDoor,AlertKind.DoorOpen,"Front-left door open");Add(h.FrontRightDoor,AlertKind.DoorOpen,"Front-right door open");
        Add(h.RearLeftDoor,AlertKind.DoorOpen,"Rear-left door open");Add(h.RearRightDoor,AlertKind.DoorOpen,"Rear-right door open");
        Add(h.Tailgate,AlertKind.TailgateOpen,"Tailgate open");Add(h.Hood,AlertKind.HoodOpen,"Hood open");
        Add(h.FrontLeftWindow,AlertKind.WindowOpen,"Front-left window open");Add(h.FrontRightWindow,AlertKind.WindowOpen,"Front-right window open");
        Add(h.RearLeftWindow,AlertKind.WindowOpen,"Rear-left window open");Add(h.RearRightWindow,AlertKind.WindowOpen,"Rear-right window open");
        Add(h.TireWarning,AlertKind.TireLow,"Tire pressure low");
        if(Low12V(state))alerts.Add(new(AlertKind.Battery12VLow,$"12-volt battery low ({h.Battery12V}%)",false));
        Add(h.BrakeFluidWarning,AlertKind.BrakeFluid,"Brake fluid warning");
        Add(h.WasherFluidLow,AlertKind.WasherFluid,"Washer fluid low",false);
        Add(h.KeyFobBatteryLow,AlertKind.KeyFob,"Key fob battery low",false);
        return alerts;
    }
}
