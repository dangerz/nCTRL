using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>Hyundai's session (tokens and when the access token expires) and the selected vehicle, shared between the
/// app and the Share extension so the extension can send without signing in again. Secret: kept only in a device-only
/// Keychain item in the app's App Group, deleted by Sign out & forget.</summary>
/// <param name="Owner">Which login the session belongs to (<see cref="OwnerOf"/>): a one-way hash of the service and the
/// account, never the email itself. "" for sessions saved before 2.16; the Share extension uses a session only when its
/// owner matches the saved login.</param>
public sealed record SharedSession(string AccessToken,string RefreshToken,DateTimeOffset Expires,EnrolledVehicle Vehicle,string Owner="")
{
    public override string ToString()=>"SharedSession [redacted]";
    /// <summary>The owner tag for a service ("hyundai", "kia"…) and an account key (<see cref="VehicleLink.AccountKey"/>).</summary>
    public static string OwnerOf(string service,string accountKey)=>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("nctrl-session\n"+service+"\n"+accountKey)));
    public bool BelongsTo(string service,string accountKey)=>Owner.Length==64&&accountKey.Length>0&&Owner==OwnerOf(service,accountKey);
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",2);w.WriteString("owner",Owner);
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
            int version=r.GetProperty("version").GetInt32();
            if(version is not (1 or 2))return null;
            var v=r.GetProperty("vehicle");
            string Text(JsonElement e,string key)=>e.GetProperty(key).GetString()??"";
            var vehicle=new EnrolledVehicle(Text(v,"id"),Text(v,"vin"),Text(v,"name"),Text(v,"model"),v.GetProperty("generation").GetInt32(),v.GetProperty("electric").GetBoolean());
            if(vehicle.Id.Length==0||vehicle.Vin.Length!=17||Text(r,"access").Length==0)return null;
            if(!DateTimeOffset.TryParse(Text(r,"expires"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var expires))return null;
            string owner=version==2&&r.TryGetProperty("owner",out var o)&&o.ValueKind==JsonValueKind.String?o.GetString()??"":"";
            return new(Text(r,"access"),Text(r,"refresh"),expires,vehicle,owner.Length==64?owner:"");
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
