using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NGarage.Core;

public sealed record TireReading(string Position,double? Psi,bool? Warning);
public sealed record DetailRow(string Label,string Value);

/// <summary>Turns Hyundai's raw vehicleStatus into display rows: tires first, then every reported
/// field with a readable name where known and the raw field path otherwise. Location, coordinates
/// and identifiers are never shown.</summary>
public static class StatusDetails
{
    public const int MaxRows=400;
    private static readonly (string Position,string Key)[] Wheels={("Front left","FrontLeft"),("Front right","FrontRight"),("Rear left","RearLeft"),("Rear right","RearRight")};
    private static readonly HashSet<string> Hidden=new(StringComparer.OrdinalIgnoreCase){
        "vehicleLocation","coord","lat","lon","latitude","longitude","alt","heading","head","speed","vin","regid","registrationId","username","accessToken","pin"};

    public static IReadOnlyList<TireReading> Tires(JsonElement status)=>Wheels.Select(w=>new TireReading(w.Position,
        Number(HyundaiUsClient.At(status,"tirePressure.tirePressure"+w.Key)),Flag(HyundaiUsClient.At(status,"tirePressureLamp.tirePressureWarningLamp"+w.Key)))).ToList();
    public static bool? AnyTireWarning(JsonElement status)=>Flag(HyundaiUsClient.At(status,"tirePressureLamp.tirePressureWarningLampAll"));

    private static readonly List<(string Path,string Label,Func<JsonElement,string> Format)> Known=new() {
        ("dateTime","Reported at",Raw),("doorLock","Doors locked",YesNo),
        ("doorOpen.frontLeft","Front-left door",OpenClosed),("doorOpen.frontRight","Front-right door",OpenClosed),
        ("doorOpen.backLeft","Rear-left door",OpenClosed),("doorOpen.backRight","Rear-right door",OpenClosed),
        ("trunkOpen","Tailgate",OpenClosed),("hoodOpen","Hood",OpenClosed),
        ("windowOpen.frontLeft","Front-left window",OpenClosed),("windowOpen.frontRight","Front-right window",OpenClosed),
        ("windowOpen.backLeft","Rear-left window",OpenClosed),("windowOpen.backRight","Rear-right window",OpenClosed),
        ("evStatus.batteryStatus","Battery",Percent),("battery.batSoc","12-volt battery",Percent),
        ("evStatus.batteryCharge","Charging",YesNo),("evStatus.batteryPlugin","Charge cable",Plug),("evStatus.realTimePower","Charging power (kW)",Raw),
        ("evStatus.drvDistance.0.rangeByFuel.totalAvailableRange.value","Range",Raw),("evStatus.drvDistance.0.rangeByFuel.evModeRange.value","Electric range",Raw),
        ("evStatus.remainTime2.atc.value","Charge time left (min)",Raw),("evStatus.remainTime2.etc1.value","DC fast charge time (min)",Raw),
        ("evStatus.remainTime2.etc2.value","Portable charger time (min)",Raw),("evStatus.remainTime2.etc3.value","Level 2 charge time (min)",Raw),
        ("airCtrlOn","Climate",OnOff),("airTemp.value","Climate setpoint",Raw),("defrost","Defrost",OnOff),
        ("steerWheelHeat","Steering wheel heat",OnOff),("sideBackWindowHeat","Rear window heat",OnOff),("sideMirrorHeat","Mirror heat",OnOff),
        ("seatHeaterVentState.flSeatHeatState","Driver seat",Seat),("seatHeaterVentState.frSeatHeatState","Passenger seat",Seat),
        ("seatHeaterVentState.rlSeatHeatState","Rear-left seat",Seat),("seatHeaterVentState.rrSeatHeatState","Rear-right seat",Seat),
        ("tirePressureLamp.tirePressureWarningLampAll","Tire pressure warning",Warning),
        ("tirePressure.tirePressureFrontLeft","Front-left tire",Raw),("tirePressure.tirePressureFrontRight","Front-right tire",Raw),
        ("tirePressure.tirePressureRearLeft","Rear-left tire",Raw),("tirePressure.tirePressureRearRight","Rear-right tire",Raw),
        ("tirePressureLamp.tirePressureWarningLampFrontLeft","Front-left tire warning",Warning),("tirePressureLamp.tirePressureWarningLampFrontRight","Front-right tire warning",Warning),
        ("tirePressureLamp.tirePressureWarningLampRearLeft","Rear-left tire warning",Warning),("tirePressureLamp.tirePressureWarningLampRearRight","Rear-right tire warning",Warning),
        ("washerFluidStatus","Washer fluid low",YesNo),("breakOilStatus","Brake fluid warning",YesNo),("smartKeyBatteryWarning","Key fob battery low",YesNo),
        ("odometer","Odometer",Raw),("engine","Vehicle on",YesNo),
        // Kia Connect (US) cmm/gvi vehicleStatus.
        ("syncDate.utc","Last report",KiaTime),
        ("climate.airCtrl","Climate",OnOff),("climate.airTemp.value","Climate set to",Raw),("climate.defrost","Front defrost",OnOff),
        ("climate.heatingAccessory.steeringWheel","Steering wheel heat",OnOff),("climate.heatingAccessory.steeringWheelStep","Steering wheel heat level",Raw),
        ("climate.heatingAccessory.rearWindow","Rear window heat",OnOff),("climate.heatingAccessory.sideMirror","Mirror heat",OnOff),
        ("climate.heatVentSeat.driverSeat.heatVentType","Driver seat",VentType),("climate.heatVentSeat.passengerSeat.heatVentType","Passenger seat",VentType),
        ("climate.heatVentSeat.rearLeftSeat.heatVentType","Rear-left seat",VentType),("climate.heatVentSeat.rearRightSeat.heatVentType","Rear-right seat",VentType),
        ("doorStatus.frontLeft","Front-left door",OpenClosed),("doorStatus.frontRight","Front-right door",OpenClosed),
        ("doorStatus.backLeft","Rear-left door",OpenClosed),("doorStatus.backRight","Rear-right door",OpenClosed),
        ("doorStatus.trunk","Tailgate",OpenClosed),("doorStatus.hood","Hood",OpenClosed),("sunroofOpen","Sunroof",OpenClosed),
        ("windowStatus.windowFL","Front-left window",OpenClosed),("windowStatus.windowFR","Front-right window",OpenClosed),
        ("windowStatus.windowRL","Rear-left window",OpenClosed),("windowStatus.windowRR","Rear-right window",OpenClosed),
        ("evStatus.windowStatus.windowFL","Front-left window",OpenClosed),("evStatus.windowStatus.windowFR","Front-right window",OpenClosed),
        ("evStatus.windowStatus.windowRL","Rear-left window",OpenClosed),("evStatus.windowStatus.windowRR","Rear-right window",OpenClosed),
        ("tirePressure.all","Tire pressure",Warning),("batteryStatus.stateOfCharge","12-volt battery",Percent),
        ("evStatus.batteryPrecondition","Battery preconditioning",OnOff),
        ("evStatus.remainChargeTime.0.timeInterval.value","Charge time left (min)",Raw),("evStatus.remainChargeTime.0.etc1.value","DC fast charge time (min)",Raw),
        ("evStatus.remainChargeTime.0.etc2.value","Portable charger time (min)",Raw),("evStatus.remainChargeTime.0.etc3.value","Level 2 charge time (min)",Raw),
        ("evStatus.drvDistance.0.rangeByFuel.gasModeRange.value","Fuel range",Raw),("distanceToEmpty.value","Range to empty",Raw),
        ("lowFuelLight","Low fuel light",OnOff),("ign3","Accessory power",OnOff),("sleepMode","Sleep mode",OnOff),
        ("valetParkingMode","Valet mode",OnOff),("remoteControlAvailable","Remote control available",YesNo)
    };
    // Plumbing the readable view leaves out: units, codes and the range estimate at each charge target.
    private static bool Plumbing(string path)=>path.EndsWith(".unit",StringComparison.OrdinalIgnoreCase)||
        path.EndsWith(".type",StringComparison.OrdinalIgnoreCase)||path.Contains(".dte.",StringComparison.OrdinalIgnoreCase)||
        path.Contains(".heatVentLevel",StringComparison.OrdinalIgnoreCase)||path.Contains(".heatVentStep",StringComparison.OrdinalIgnoreCase);

    /// <summary>The field paths nCTRL knows (case-insensitive), for the bug report's allow-list.</summary>
    public static readonly IReadOnlySet<string> KnownPaths=new HashSet<string>(Known.Select(k=>k.Path),StringComparer.OrdinalIgnoreCase);
    /// <summary>Every reported field. Readable: known fields with plain names, values and units first, then the rest
    /// by raw name (unit and code fields left out). Not readable: every field by its raw name and raw value.</summary>
    public static IReadOnlyList<DetailRow> Rows(JsonElement status,UnitSystem units=UnitSystem.Imperial,bool readable=true)
    {
        var leaves=new List<(string Path,JsonElement Value)>();
        Walk(status,"",leaves);
        if(!readable)return leaves.Take(MaxRows).Select(l=>new DetailRow(l.Path,Clip(l.Value.ValueKind==JsonValueKind.String?l.Value.GetString()??"":l.Value.GetRawText()))).ToList();
        var byPath=leaves.GroupBy(l=>l.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First().Value,StringComparer.OrdinalIgnoreCase);
        var rows=new List<DetailRow>();var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (path,label,format) in Known)
            // A label already shown (Hyundai and Kia name some fields differently) isn't repeated.
            if(byPath.TryGetValue(path,out var value)){used.Add(path);if(labels.Add(label))rows.Add(new(label,Clip(Measure(path,value,byPath,units)??format(value))));}
        // Kia's charge targets: a list of plug types and levels.
        for(int i=0;i<4;i++)
            if(byPath.TryGetValue($"evStatus.targetSOC.{i}.targetSOClevel",out var level)&&byPath.TryGetValue($"evStatus.targetSOC.{i}.plugType",out var plug)) {
                rows.Add(new(Number(plug)==1?"AC charge target":Number(plug)==0?"DC charge target":"Charge target",Percent(level)));
                used.Add($"evStatus.targetSOC.{i}.targetSOClevel");used.Add($"evStatus.targetSOC.{i}.plugType");
            }
        foreach(var (path,value) in leaves)
            if(rows.Count<MaxRows&&!Plumbing(path)&&used.Add(path))rows.Add(new(path,Clip(Raw(value))));
        return rows.Take(MaxRows).ToList();
    }
    // Distances, pressures and temperatures in the chosen units. Hyundai USA reports miles (a range with
    // unit 1 is in km), psi and °F (airTemp unit 0 is °C).
    private static string? Measure(string path,JsonElement value,Dictionary<string,JsonElement> byPath,UnitSystem units)
    {
        if(Number(value) is not double n)return null;
        bool Unit(string key,double code)=>byPath.TryGetValue(key,out var u)&&Number(u)==code;
        if(path.StartsWith("tirePressure.",StringComparison.OrdinalIgnoreCase))return Units.Pressure(n,units);
        if(path=="odometer")return Units.Distance(n,units);
        if(path=="airTemp.value")return Units.Temperature(Unit("airTemp.unit",0)?Units.Fahrenheit(n):n,units);
        if(path=="climate.airTemp.value")return Units.Temperature(Unit("climate.airTemp.unit",0)?Units.Fahrenheit(n):n,units);
        if(path.EndsWith("Range.value",StringComparison.OrdinalIgnoreCase))return Units.Distance(Unit(path[..^5]+"unit",1)?n/Units.KmPerMile:n,units);
        return null;
    }
    private static void Walk(JsonElement e,string path,List<(string,JsonElement)> leaves)
    {
        if(leaves.Count>=MaxRows*2)return;
        if(e.ValueKind==JsonValueKind.Object) {
            foreach(var p in e.EnumerateObject())if(!Hidden.Contains(p.Name))Walk(p.Value,path.Length==0?p.Name:path+"."+p.Name,leaves);
        } else if(e.ValueKind==JsonValueKind.Array) {
            int i=0;foreach(var item in e.EnumerateArray())Walk(item,path+"."+i++,leaves);
        } else if(path.Length>0)leaves.Add((path,e));
    }
    private static string Clip(string text)=>text.Length>60?text[..57]+"…":text;
    private static bool? Flag(JsonElement e)=>e.ValueKind switch {
        JsonValueKind.True=>true,JsonValueKind.False=>false,
        JsonValueKind.Number=>e.TryGetDouble(out double n)?n!=0:null,
        JsonValueKind.String=>e.GetString() switch {"1" or "true" or "True"=>true,"0" or "false" or "False"=>false,_=>null},_=>null};
    private static double? Number(JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetDouble(out double n)&&double.IsFinite(n)?n:
        e.ValueKind==JsonValueKind.String&&double.TryParse(e.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&double.IsFinite(n)?n:null;
    private static string Raw(JsonElement e)=>e.ValueKind switch {
        JsonValueKind.True=>"Yes",JsonValueKind.False=>"No",JsonValueKind.Null or JsonValueKind.Undefined=>"—",
        JsonValueKind.String=>e.GetString() is {Length: >0} s?s:"—",_=>e.GetRawText()};
    private static string YesNo(JsonElement e)=>Flag(e) switch {true=>"Yes",false=>"No",_=>Raw(e)};
    private static string OnOff(JsonElement e)=>Flag(e) switch {true=>"On",false=>"Off",_=>Raw(e)};
    private static string OpenClosed(JsonElement e)=>Flag(e) switch {true=>"Open",false=>"Closed",_=>Raw(e)};
    private static string Warning(JsonElement e)=>Flag(e) switch {true=>"Warning",false=>"OK",_=>Raw(e)};
    private static string Percent(JsonElement e)=>Number(e) is double n?n.ToString("0",CultureInfo.InvariantCulture)+"%":Raw(e);
    private static string Seat(JsonElement e)=>Number(e) is double n&&n==Math.Floor(n)?SeatCodes.Reported((int)n):Raw(e);
    // Kia seat mode: 0 off, 1 heating, 2 ventilating.
    private static string VentType(JsonElement e)=>Number(e) switch {0=>"Off",1=>"Heating",2=>"Ventilating",_=>Raw(e)};
    // Kia's compact UTC time, shown in local time.
    private static string KiaTime(JsonElement e)=>e.ValueKind==JsonValueKind.String&&e.GetString() is {Length:14} t&&
        DateTime.TryParseExact(t,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)?
        at.ToLocalTime().ToString("MMM d, h:mm tt",CultureInfo.InvariantCulture):Raw(e);
    private static string Plug(JsonElement e)=>Number(e) switch {0=>"Unplugged",double n=>"Plugged in (type "+n.ToString(CultureInfo.InvariantCulture)+")",_=>Raw(e)};
}
