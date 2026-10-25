using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>A place shared to nCTRL (Activity → Shared): when, from where, what kinds of items came, and the place
/// it led to or why it couldn't be read. Opened is whether the Share extension could bring nCTRL forward.</summary>
public sealed record SharedEntry(DateTimeOffset At,string Source,string Kinds,Destination? Place,string Problem,bool? Opened=null);
/// <summary>The Share extension's note about one share (share-log.json), written as the share starts and updated as it
/// goes: what kinds of items came, the link hosts, how far it got, whether it saved the share, sent it or opened nCTRL,
/// why not, and, when it sent the place to the car itself, that place.</summary>
public sealed record SharedReceipt(string Id,DateTimeOffset At,string Kinds,string Source,bool Saved,bool Opened,bool Sent=false,Destination? Place=null,
    string Stage="",string Problem="")
{
    /// <summary>Why a share that went nowhere went nowhere, for Activity → Shared.</summary>
    public string Reason=>Problem.Length>0?Problem.TrimEnd('.'):Stage switch {
        "started"=>"The Share sheet stopped before anything arrived",
        "received"=>"The Share sheet stopped while reading what was shared",
        "looking up"=>"The Share sheet stopped while looking up the address",
        "found" or "sending"=>"The Share sheet stopped while sending",
        _=>"Nothing in it looked like a place, so nothing was saved"};
}

public static class SharedHistory
{
    public const int Max=20;
    public static IReadOnlyList<SharedEntry> Add(IReadOnlyList<SharedEntry> entries,SharedEntry entry)=>
        new[]{entry}.Concat(entries).Take(Max).ToList();

    // Kept in the device Keychain; anything malformed reads as nothing.
    public static string Encode(IReadOnlyList<SharedEntry> entries)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteStartArray("shared");
            foreach(var e in entries.Take(Max)) {
                w.WriteStartObject();
                w.WriteString("at",e.At.ToString("O",CultureInfo.InvariantCulture));w.WriteString("source",e.Source);w.WriteString("kinds",e.Kinds);
                w.WriteString("problem",e.Problem);
                if(e.Opened is bool opened)w.WriteBoolean("opened",opened);
                if(e.Place is {} p) {
                    w.WriteString("name",p.Name);w.WriteString("address",p.Address);w.WriteString("zip",p.Zip);
                    w.WriteNumber("lat",p.Latitude);w.WriteNumber("lon",p.Longitude);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static IReadOnlyList<SharedEntry> Decode(string json)
    {
        var entries=new List<SharedEntry>();
        if(string.IsNullOrWhiteSpace(json)||json.Length>256*1024)return entries;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)return entries;
            foreach(var e in r.GetProperty("shared").EnumerateArray().Take(Max)) {
                string Text(string key,int max)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?Destinations.Clean(v.GetString(),max):"";
                if(!DateTimeOffset.TryParse(Text("at",40),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at))continue;
                Destination? place=null;
                if(e.TryGetProperty("lat",out var lat)&&e.TryGetProperty("lon",out var lon)&&lat.TryGetDouble(out double la)&&lon.TryGetDouble(out double lo)) {
                    var candidate=new Destination(Text("name",Destinations.MaxName),Text("address",Destinations.MaxAddress),la,lo,Text("zip",10));
                    if(Destinations.Valid(candidate))place=candidate;
                }
                bool? opened=e.TryGetProperty("opened",out var o)&&o.ValueKind is JsonValueKind.True or JsonValueKind.False?o.GetBoolean():null;
                entries.Add(new(at,Text("source",40),Text("kinds",80),place,Text("problem",200),opened));
            }
        } catch(Exception){return new List<SharedEntry>();}
        return entries;
    }
    public static IReadOnlyList<SharedReceipt> Receipts(string json)
    {
        var receipts=new List<SharedReceipt>();
        if(string.IsNullOrWhiteSpace(json)||json.Length>64*1024)return receipts;
        try {
            using var doc=JsonDocument.Parse(json);
            foreach(var r in doc.RootElement.EnumerateArray().Take(10)) {
                string Text(string key,int max=64)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?Destinations.Clean(v.GetString(),max):"";
                IEnumerable<string> List(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?
                    v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>Destinations.Clean(x.GetString(),40)):Array.Empty<string>();
                bool Flag(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.True;
                if(!DateTimeOffset.TryParse(Text("at"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at))continue;
                var place=Flag("sent")&&r.TryGetProperty("place",out var p)?ShareSender.ReadPlace(p):null;
                receipts.Add(new(Text("id"),at,string.Join(", ",List("kinds")),Destinations.Source(List("hosts").Select(h=>"https://"+h)),Flag("saved"),Flag("opened"),place!=null,place,
                    Text("stage",20),Text("problem",200)));
            }
        } catch(Exception){return new List<SharedReceipt>();}
        return receipts;
    }
}
