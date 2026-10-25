using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>Hyundai's session (tokens and when the access token expires) and the selected vehicle, shared between the
/// app and the Share extension so the extension can send without signing in again. Secret: kept only in a device-only
/// Keychain item in the app's App Group, deleted by Sign out & forget.</summary>
public sealed record SharedSession(string AccessToken,string RefreshToken,DateTimeOffset Expires,EnrolledVehicle Vehicle)
{
    public override string ToString()=>"SharedSession [redacted]";
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);
            w.WriteString("access",AccessToken);w.WriteString("refresh",RefreshToken);
            w.WriteString("expires",Expires.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));
            w.WriteStartObject("vehicle");
            w.WriteString("id",Vehicle.Id);w.WriteString("vin",Vehicle.Vin);w.WriteString("name",Vehicle.Name);w.WriteString("model",Vehicle.Model);
            w.WriteNumber("generation",Vehicle.Generation);w.WriteBoolean("electric",Vehicle.Electric);
            w.WriteEndObject();w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static SharedSession? Decode(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>16*1024)return null;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)return null;
            var v=r.GetProperty("vehicle");
            string Text(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
            var vehicle=new EnrolledVehicle(Text(v,"id"),Text(v,"vin"),Text(v,"name"),Text(v,"model"),v.GetProperty("generation").GetInt32(),v.GetProperty("electric").GetBoolean());
            if(vehicle.Id.Length==0||vehicle.Vin.Length!=17||Text(r,"access").Length==0)return null;
            if(!DateTimeOffset.TryParse(Text(r,"expires"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expires))return null;
            return new(Text(r,"access"),Text(r,"refresh"),expires,vehicle);
        } catch(Exception){return null;}
    }
}

/// <summary>Request limits kept in a plain file (the App Group's, shared by the app and the Share extension).</summary>
public sealed class PathBudgetStore(string path) : IRequestBudgetStore
{
    public string Load()=>File.Exists(path)?File.ReadAllText(path):"";
    public void Save(string data)
    {
        string temporary=path+".tmp";
        File.WriteAllText(temporary,data);File.Move(temporary,path,true);
    }
}
