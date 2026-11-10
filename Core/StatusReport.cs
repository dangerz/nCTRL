using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NGarage.Core;

/// <summary>The latest status as text for a bug report (Details → Copy for a bug report). It fails closed: numbers and
/// yes/no values stay, but text stays only in fields nCTRL knows (<see cref="StatusDetails.KnownPaths"/>), dates and times,
/// and short upper-case codes (like HT_533); any other text is replaced with its length. Fields that name the owner or the
/// car (names, contact details, addresses, location, tokens, the PIN and IDs) are removed whatever they hold. The VIN
/// keeps only its first 11 characters, which say what model it is, not which car.</summary>
public static partial class StatusReport
{
    public const string Removed="(removed)";
    // Keys (lower case) whose values are removed outright, whatever they hold.
    private static readonly HashSet<string> Secret=new(StringComparer.Ordinal){
        "vin","regid","sid","pauth","pin","imei","iccid","msisdn","zip","zipcode","city","province","state","country","county",
        "lat","lon","lng","long","latitude","longitude","alt","altitude","coord","geocoord","location","vehiclelocation","head","heading",
        "transactionid","plate","dealercode","preferreddealer"};
    // Parts of keys (lower case) that mark a personal field: "vehicleNickName", "ownerInfo", "emailAddress", "mobilePhone"…
    private static readonly string[] SecretParts={"name","mail","phone","mobile","address","street","postal","token","password","secret",
        "plate","birth","owner","contact","credential","license"};
    // Keys ending in "id" that are ordinary words, not identifiers.
    private static readonly HashSet<string> NotIds=new(StringComparer.Ordinal){"valid","isvalid","invalid","hybrid","fluid","paid","prepaid","void","avoid","solid","humid","rapid"};

    /// <summary>The report: a header line (app, version, service) and the status with personal details removed.</summary>
    public static string Build(JsonElement status,string app,string version,string service)=>
        $"{app} {version} · {service} · last status, personal details removed\n\n"+Redact(status);

    public static string Redact(JsonElement status)
    {
        var vins=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Collect(status,vins);
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream,new JsonWriterOptions{Indented=true}))Write(w,status,"",vins);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    /// <summary>A VIN with its serial number (the last six characters) hidden.</summary>
    public static string Vin(string vin)=>vin.Length==17?vin[..11]+"******":Removed;

    public static bool SecretKey(string key)
    {
        string k=key.ToLowerInvariant();
        return Secret.Contains(k)||SecretParts.Any(p=>k.Contains(p,StringComparison.Ordinal))||k.EndsWith("id",StringComparison.Ordinal)&&!NotIds.Contains(k);
    }
    private static void Collect(JsonElement e,HashSet<string> vins)
    {
        if(e.ValueKind==JsonValueKind.Object) {
            foreach(var p in e.EnumerateObject()) {
                if(p.Name.Equals("vin",StringComparison.OrdinalIgnoreCase)&&p.Value.ValueKind==JsonValueKind.String&&p.Value.GetString() is {Length:17} v)vins.Add(v);
                Collect(p.Value,vins);
            }
        } else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())Collect(item,vins);
    }
    private static void Write(Utf8JsonWriter w,JsonElement e,string path,HashSet<string> vins)
    {
        switch(e.ValueKind) {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach(var p in e.EnumerateObject()) {
                    string at=path.Length==0?p.Name:path+"."+p.Name;
                    w.WritePropertyName(p.Name);
                    if(p.Name.Equals("vin",StringComparison.OrdinalIgnoreCase))w.WriteStringValue(p.Value.ValueKind==JsonValueKind.String?Vin(p.Value.GetString()??""):Removed);
                    else if(p.Value.ValueKind==JsonValueKind.Null)w.WriteNullValue();
                    else if(SecretKey(p.Name))w.WriteStringValue(Removed);
                    else Write(w,p.Value,at,vins);
                }
                w.WriteEndObject();break;
            case JsonValueKind.Array:
                w.WriteStartArray();int i=0;foreach(var item in e.EnumerateArray())Write(w,item,path+"."+i++,vins);w.WriteEndArray();break;
            case JsonValueKind.String:
                w.WriteStringValue(Clean(e.GetString()??"",path,vins));break;
            default:e.WriteTo(w);break;
        }
    }
    // Text stays only where it's known to be safe; anything else could be a name, an address or a message about the owner.
    private static string Clean(string text,string path,HashSet<string> vins)
    {
        if(Email().IsMatch(text)||Token().IsMatch(text))return Removed;
        if(StatusDetails.KnownPaths.Contains(path)||DateTime().IsMatch(text)||Code().IsMatch(text)) {
            foreach(var vin in vins)text=text.Replace(vin,Vin(vin),StringComparison.OrdinalIgnoreCase);
            return text;
        }
        return $"(text, {text.Length.ToString(CultureInfo.InvariantCulture)} characters)";
    }
    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+")]private static partial Regex Email();
    // 32 or more characters of token alphabet with no spaces: tokens, keys and session ids, never a status value.
    [GeneratedRegex(@"^[A-Za-z0-9+/=_.\-]{32,}$")]private static partial Regex Token();
    // Dates and times: 2026-11-09T12:00:00Z, 2026-11-09 12:00:00.000, 20261109120000, 20261109.
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?)?(Z|[+-]\d{2}:?\d{2})?|\d{8}(\d{6})?)$")]private static partial Regex DateTime();
    // Short upper-case codes and numbers sent as text: "HT_533", "ON", "1", "-12.5".
    [GeneratedRegex(@"^([A-Z][A-Z0-9_]{0,15}|-?\d{1,6}(\.\d{1,3})?)$")]private static partial Regex Code();
}
