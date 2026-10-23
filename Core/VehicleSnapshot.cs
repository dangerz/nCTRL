using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>The last reported vehicle status, kept on the device so the Car screen opens instantly
/// (stale) before any request. Holds no location, credentials, email or full VIN: the account and
/// vehicle are identified by the same SHA-256 hashes as the remembered-vehicle preference.</summary>
public sealed record VehicleSnapshot(string AccountKey,string VehicleFingerprint,string Name,string VinTail,int Generation,LiveVehicleState State)
{
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);
            w.WriteString("account",AccountKey);w.WriteString("vehicle",VehicleFingerprint);
            w.WriteString("name",Name);w.WriteString("vinTail",VinTail);w.WriteNumber("generation",Generation);
            var s=State;
            Bool(w,"locked",s.Locked);Bool(w,"climateOn",s.ClimateOn);Int(w,"temperatureF",s.TemperatureF);
            Int(w,"battery",s.Battery);Bool(w,"pluggedIn",s.PluggedIn);Bool(w,"charging",s.Charging);
            if(s.RangeMiles is double range)w.WriteNumber("rangeMiles",range);else w.WriteNull("rangeMiles");
            Int(w,"acLimit",s.AcLimit);Int(w,"dcLimit",s.DcLimit);
            w.WriteString("reportedAt",s.ReportedAt);w.WriteString("retrievedAt",s.RetrievedAt.ToString("O",CultureInfo.InvariantCulture));
            Int(w,"driverSeat",s.DriverSeat);Int(w,"passengerSeat",s.PassengerSeat);Bool(w,"steeringWheelHeat",s.SteeringWheelHeat);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    // Stored data is untrusted: anything malformed is discarded and the app simply has nothing to show.
    public static VehicleSnapshot? Decode(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>16384)return null;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)return null;
            string account=r.GetProperty("account").GetString()??"",vehicle=r.GetProperty("vehicle").GetString()??"";
            string name=r.GetProperty("name").GetString()??"",vinTail=r.GetProperty("vinTail").GetString()??"";
            if(!IsHash(account)||!IsHash(vehicle)||name.Length>64||vinTail.Length>6)return null;
            foreach(char c in vinTail)if(!char.IsAsciiLetterOrDigit(c))return null;
            if(!DateTimeOffset.TryParse(r.GetProperty("retrievedAt").GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var retrieved))return null;
            string reported=r.GetProperty("reportedAt").GetString()??"";
            var state=new LiveVehicleState(Bool(r,"locked"),Bool(r,"climateOn"),Int(r,"temperatureF"),Int(r,"battery"),Bool(r,"pluggedIn"),
                Bool(r,"charging"),r.GetProperty("rangeMiles").ValueKind==JsonValueKind.Number?r.GetProperty("rangeMiles").GetDouble():null,
                Int(r,"acLimit"),Int(r,"dcLimit"),reported.Length>48?"":reported,retrieved,Int(r,"driverSeat"),Int(r,"passengerSeat"),Bool(r,"steeringWheelHeat"));
            return new(account,vehicle,name,vinTail,r.GetProperty("generation").GetInt32(),state);
        } catch(Exception) {return null;}
    }
    private static void Bool(Utf8JsonWriter w,string name,bool? value){if(value is bool b)w.WriteBoolean(name,b);else w.WriteNull(name);}
    private static void Int(Utf8JsonWriter w,string name,int? value){if(value is int n)w.WriteNumber(name,n);else w.WriteNull(name);}
    private static bool? Bool(JsonElement e,string name)=>e.GetProperty(name).ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null};
    private static int? Int(JsonElement e,string name)=>e.GetProperty(name).ValueKind==JsonValueKind.Number&&e.GetProperty(name).TryGetInt32(out int n)?n:null;
    private static bool IsHash(string key)
    {
        if(key.Length!=64)return false;
        foreach(char c in key)if(!char.IsAsciiHexDigitUpper(c)&&!char.IsAsciiDigit(c))return false;
        return true;
    }
}
