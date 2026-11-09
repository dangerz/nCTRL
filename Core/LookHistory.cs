using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>One Surround View picture kept on the phone: its file (in the app's own storage, never the App Group or
/// iCloud) and when the car took it. No location is kept with it.</summary>
public sealed record LookEntry(string File,string CapturedAt,DateTimeOffset SavedAt);

/// <summary>The last <see cref="Max"/> Surround View pictures, newest first. Hyundai keeps only the latest; this keeps a
/// few on the phone until you delete them or sign out. The list is the index file; pictures are separate files.</summary>
public static class LookHistory
{
    public const int Max=10;
    public const string IndexFile="look-history.json";
    /// <summary>Picture file names are ours: "look-" + 32 hex characters + ".jpg". Anything else in the index is ignored.</summary>
    public static bool ValidFile(string name)=>name.Length==41&&name.StartsWith("look-",StringComparison.Ordinal)&&name.EndsWith(".jpg",StringComparison.Ordinal)&&
        name[5..37].All(Uri.IsHexDigit);
    public static string NewFile()=>"look-"+Guid.NewGuid().ToString("N")+".jpg";

    /// <summary>Adds a picture at the front; returns the new list and the files that fell off the end (to delete).</summary>
    public static (IReadOnlyList<LookEntry> List,IReadOnlyList<string> Removed) Add(IReadOnlyList<LookEntry> list,LookEntry entry)
    {
        var all=new List<LookEntry>{entry};all.AddRange(list.Where(e=>e.File!=entry.File));
        return (all.Take(Max).ToList(),all.Skip(Max).Select(e=>e.File).ToList());
    }
    public static IReadOnlyList<LookEntry> Remove(IReadOnlyList<LookEntry> list,string file)=>list.Where(e=>e.File!=file).ToList();

    public static string Encode(IReadOnlyList<LookEntry> list)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteStartArray("pictures");
            foreach(var e in list.Take(Max)) {
                w.WriteStartObject();w.WriteString("file",e.File);w.WriteString("captured",e.CapturedAt);
                w.WriteString("saved",e.SavedAt.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));w.WriteEndObject();
            }
            w.WriteEndArray();w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    /// <summary>A damaged index reads as empty; entries with names that aren't ours are dropped.</summary>
    public static IReadOnlyList<LookEntry> Decode(string json)
    {
        var list=new List<LookEntry>();
        if(string.IsNullOrWhiteSpace(json)||json.Length>64*1024)return list;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)return list;
            foreach(var p in r.GetProperty("pictures").EnumerateArray()) {
                string file=p.GetProperty("file").GetString()??"",captured=p.TryGetProperty("captured",out var c)?c.GetString()??"":"";
                if(!ValidFile(file)||list.Any(e=>e.File==file))continue;
                var saved=DateTimeOffset.TryParse(p.TryGetProperty("saved",out var s)?s.GetString():"",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)?at:default;
                list.Add(new(file,captured.Length>64?captured[..64]:captured,saved));
                if(list.Count==Max)break;
            }
        } catch(Exception){return new List<LookEntry>();}
        return list;
    }
}
