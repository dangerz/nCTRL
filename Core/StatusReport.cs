using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NGarage.Core;

/// <summary>The latest status as text for a bug report (Details → Copy for a bug report). Everything that identifies the
/// owner or the car comes out: names, contact details, addresses, where the car is, tokens, the PIN and IDs. The VIN keeps
/// only its first 11 characters, which say what model it is, not which car. What the car reports about itself stays,
/// so others can see what the service sent.</summary>
public static partial class StatusReport
{
    public const string Removed="(removed)";
    // Keys (lower case) whose values are removed outright, whatever they hold.
    private static readonly HashSet<string> Secret=new(StringComparer.Ordinal){
        "vin","vehicleid","regid","registrationid","enrollmentid","profileid","masterprofileid","userid","loginid","username","useremail",
        "email","emailaddress","phone","phonenumber","mobile","mobilenumber","cellphone","firstname","lastname","fullname","nickname","vehiclenickname","ownername",
        "address","street","streetaddress","postalcode","zip","zipcode","city","province",
        "lat","lon","lng","latitude","longitude","alt","altitude","coord","geocoord","location","vehiclelocation",
        "accesstoken","refreshtoken","token","sid","pauth","pin","transactionid","deviceid","remembertoken",
        "licenseplate","plate","dealercode","preferreddealer","imei","iccid","msisdn"};

    /// <summary>The report: a header line (app, version, service) and the status with personal details removed.</summary>
    public static string Build(JsonElement status,string app,string version,string service)=>
        $"{app} {version} · {service} · last status, personal details removed\n\n"+Redact(status);

    public static string Redact(JsonElement status)
    {
        var vins=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Collect(status,vins);
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream,new JsonWriterOptions{Indented=true}))Write(w,status,vins);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    /// <summary>A VIN with its serial number (the last six characters) hidden.</summary>
    public static string Vin(string vin)=>vin.Length==17?vin[..11]+"******":Removed;

    private static bool SecretKey(string key)=>
        Secret.Contains(key.ToLowerInvariant())||key=="id"||key.EndsWith("Id",StringComparison.Ordinal)||key.EndsWith("ID",StringComparison.Ordinal);
    private static void Collect(JsonElement e,HashSet<string> vins)
    {
        if(e.ValueKind==JsonValueKind.Object) {
            foreach(var p in e.EnumerateObject()) {
                if(p.Name.Equals("vin",StringComparison.OrdinalIgnoreCase)&&p.Value.ValueKind==JsonValueKind.String&&p.Value.GetString() is {Length:17} v)vins.Add(v);
                Collect(p.Value,vins);
            }
        } else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())Collect(item,vins);
    }
    private static void Write(Utf8JsonWriter w,JsonElement e,HashSet<string> vins)
    {
        switch(e.ValueKind) {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach(var p in e.EnumerateObject()) {
                    w.WritePropertyName(p.Name);
                    if(p.Name.Equals("vin",StringComparison.OrdinalIgnoreCase))w.WriteStringValue(p.Value.ValueKind==JsonValueKind.String?Vin(p.Value.GetString()??""):Removed);
                    else if(p.Value.ValueKind==JsonValueKind.Null)w.WriteNullValue();
                    else if(SecretKey(p.Name))w.WriteStringValue(Removed);
                    else Write(w,p.Value,vins);
                }
                w.WriteEndObject();break;
            case JsonValueKind.Array:
                w.WriteStartArray();foreach(var item in e.EnumerateArray())Write(w,item,vins);w.WriteEndArray();break;
            case JsonValueKind.String:
                w.WriteStringValue(Clean(e.GetString()??"",vins));break;
            default:e.WriteTo(w);break;
        }
    }
    // Strings can carry personal details under innocent keys: emails, long tokens, or the VIN inside a message.
    private static string Clean(string text,HashSet<string> vins)
    {
        if(Email().IsMatch(text)||Token().IsMatch(text))return Removed;
        foreach(var vin in vins)text=text.Replace(vin,Vin(vin),StringComparison.OrdinalIgnoreCase);
        return text;
    }
    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]private static partial Regex Email();
    // 32 or more characters of token alphabet with no spaces: tokens, keys and session ids, never a status value.
    [GeneratedRegex(@"^[A-Za-z0-9+/=_.\-]{32,}$")]private static partial Regex Token();
}
