namespace NGarage.Core;

public enum ActionKind { Lock, Unlock, Climate, Charge, ChargeLimit, Lights, HornLights }
public enum SeatMode { Off, HeatLow, HeatMedium, HeatHigh, CoolLow, CoolMedium, CoolHigh }
// Sent with a climate start. Front seats and the steering wheel only; rear seats always stay off.
public sealed record ClimateOptions(SeatMode DriverSeat=SeatMode.Off,SeatMode PassengerSeat=SeatMode.Off,bool SteeringWheel=false,bool Defrost=false)
{
    public bool HasSeats=>DriverSeat!=SeatMode.Off||PassengerSeat!=SeatMode.Off;
}
public record VehicleCommand(ActionKind Kind, int Value = 0, bool Enabled = false, ClimateOptions? Climate = null);

// Hyundai US seatHeaterVentInfo / seatHeaterVentState codes (upstream SEAT_STATUS).
public static class SeatCodes
{
    public static int Code(SeatMode mode)=>mode switch {
        SeatMode.CoolLow=>3,SeatMode.CoolMedium=>4,SeatMode.CoolHigh=>5,
        SeatMode.HeatLow=>6,SeatMode.HeatMedium=>7,SeatMode.HeatHigh=>8,_=>0};
    public static string Label(SeatMode mode)=>mode switch {
        SeatMode.HeatLow=>"Heat low",SeatMode.HeatMedium=>"Heat medium",SeatMode.HeatHigh=>"Heat high",
        SeatMode.CoolLow=>"Cool low",SeatMode.CoolMedium=>"Cool medium",SeatMode.CoolHigh=>"Cool high",_=>"Off"};
    // Reported codes: 0 and 2 are off, 1 is on without a level.
    public static string Reported(int? code)=>code switch {
        0 or 2=>"Off",1=>"On",3=>"Cool low",4=>"Cool medium",5=>"Cool high",6=>"Heat low",7=>"Heat medium",8=>"Heat high",_=>"—"};
}
