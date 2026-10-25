using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NGarage.Core;

/// <summary>A place to send to the car's navigation.</summary>
public sealed record Destination(string Name,string Address,double Latitude,double Longitude,string Zip="");

public static class Destinations
{
    public const int MaxName=60,MaxAddress=200,MaxText=2000;
    private static readonly Regex Pair=new(@"(-?\d{1,2}(?:\.\d+)?)\s*[, ]\s*(-?\d{1,3}(?:\.\d+)?)",RegexOptions.CultureInvariant);
    private static readonly Regex GoogleAt=new(@"@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)",RegexOptions.CultureInvariant);

    public static bool Valid(Destination place)=>double.IsFinite(place.Latitude)&&double.IsFinite(place.Longitude)&&
        Math.Abs(place.Latitude)<=90&&Math.Abs(place.Longitude)<=180&&!(place.Latitude==0&&place.Longitude==0)&&
        place.Name.Length is >0 and <=MaxName&&place.Address.Length<=MaxAddress;
    /// <summary>One line of plain text: control characters removed, spaces collapsed, length capped.</summary>
    public static string Clean(string? text,int max)
    {
        var plain=new string((text??"").Select(c=>char.IsControl(c)?' ':c).ToArray());
        plain=Regex.Replace(plain,@"\s+"," ").Trim();
        return plain.Length>max?plain[..max].TrimEnd():plain;
    }
    /// <summary>Reads coordinates ("38.8977, -77.0365"), a geo: link or an Apple or Google Maps link. Returns the
    /// place when it has coordinates; otherwise the text to look up as an address (empty when there's nothing).</summary>
    public static (Destination? Place,string Lookup) FromText(string? text)
    {
        string raw=(text??"").Trim();
        if(raw.Length==0||raw.Length>MaxText)return (null,"");
        if(Uri.TryCreate(raw,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "geo" or "maps")return FromLink(uri,raw);
        if(Coordinates(raw,true) is var (lat,lon))return (Place(Format(lat,lon),Format(lat,lon),lat,lon),"");
        return (null,Clean(raw,MaxAddress));
    }
    private static (Destination?,string) FromLink(Uri uri,string raw)
    {
        if(uri.Scheme=="geo")return Coordinates(uri.AbsolutePath,false) is var (gl,go)?(Place("Shared location","",gl,go),""):(null,"");
        var query=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)
            .Select(p=>p.Split('=',2)).Where(p=>p.Length==2)
            .GroupBy(p=>p[0].ToLowerInvariant()).ToDictionary(g=>g.Key,g=>Uri.UnescapeDataString(g.First()[1].Replace('+',' ')));
        string Value(params string[] keys)=>keys.Select(k=>query.TryGetValue(k,out var v)?v:"").FirstOrDefault(v=>v.Length>0)??"";
        string name=Clean(Value("name","q"),MaxName),address=Clean(Value("address","daddr"),MaxAddress);
        (double,double)? at=null;
        foreach(string key in new[]{"coordinate","ll","daddr","q","sll","query"})
            if(Value(key) is {Length: >0} v&&Coordinates(v,true) is {} found){at=found;break;}
        if(at==null&&GoogleAt.Match(raw) is {Success: true} m)at=(double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture));
        if(uri.Host.Contains("google",StringComparison.OrdinalIgnoreCase)&&uri.AbsolutePath.Contains("/place/")) {
            string place=Clean(Uri.UnescapeDataString(uri.AbsolutePath.Split("/place/")[1].Split('/')[0].Replace('+',' ')),MaxAddress);
            if(name.Length==0)name=Clean(place,MaxName);if(address.Length==0)address=place;
        }
        if(at is var (lat,lon)) {
            if(Coordinates(name,true)!=null)name="";
            if(Coordinates(address,true)!=null)address="";
            return (Place(name.Length>0?name:address.Length>0?Clean(address,MaxName):Format(lat,lon),address,lat,lon),"");
        }
        // No coordinates in the link: look up its address or name instead.
        return (null,address.Length>0?address:name);
    }
    private static Destination? Place(string name,string address,double lat,double lon)
    {
        var place=new Destination(Clean(name,MaxName),Clean(address,MaxAddress),lat,lon);
        return Valid(place)?place:null;
    }
    // Two numbers that are a valid latitude and longitude. whole: the text must be just the pair.
    private static (double,double)? Coordinates(string text,bool whole)
    {
        var m=Pair.Match(text);
        if(!m.Success||whole&&m.Value.Length!=text.Trim().Length)return null;
        double lat=double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture),lon=double.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture);
        return Math.Abs(lat)<=90&&Math.Abs(lon)<=180&&!(lat==0&&lon==0)?(lat,lon):null;
    }
    /// <summary>A place shared to nCTRL from another app (share.json from the Share extension: its links and
    /// texts). Apple Maps includes a contact card with the place's name, address and a link with coordinates;
    /// Google Maps shares "Name / Address / short link" text, which is looked up by its address (the short link
    /// is never opened). Returns the place when it has coordinates, otherwise what to look up and the shared name.</summary>
    public static (Destination? Place,string Lookup,string Name) FromShare(string json)
    {
        var links=new System.Collections.Generic.List<string>();var texts=new System.Collections.Generic.List<string>();
        try {
            if(json.Length>256*1024)return (null,"","");
            using var doc=System.Text.Json.JsonDocument.Parse(json);var r=doc.RootElement;
            foreach(var (key,into) in new[]{("urls",links),("texts",texts)})
                if(r.TryGetProperty(key,out var list)&&list.ValueKind==System.Text.Json.JsonValueKind.Array)
                    foreach(var item in list.EnumerateArray().Take(5))
                        if(item.ValueKind==System.Text.Json.JsonValueKind.String&&item.GetString() is {Length: >0 and <=MaxText*2} value)into.Add(value);
        } catch(Exception){return (null,"","");}
        string name="",address="";var plain=new System.Collections.Generic.List<string>();
        foreach(var text in texts) {
            if(!text.Contains("BEGIN:VCARD",StringComparison.OrdinalIgnoreCase)){plain.Add(text);continue;}
            // Folded lines continue with a space; values escape commas, semicolons and colons.
            foreach(var line in Regex.Replace(text,@"\r?\n[ \t]","").Split('\n')) {
                int colon=line.IndexOf(':');if(colon<1)continue;
                string field=line[..colon].ToUpperInvariant(),value=line[(colon+1)..].Trim();
                string Unescape(string v)=>v.Replace("\\,",",").Replace("\\;",";").Replace("\\:",":").Replace("\\n"," ");
                if(field=="FN"||field.EndsWith(".FN"))name=Clean(Unescape(value),MaxName);
                else if(field.Split(';')[0].EndsWith("ADR")&&address.Length==0) {
                    var parts=value.Split(';').Select(p=>Clean(Unescape(p),MaxAddress)).ToArray();
                    string Part(int i)=>i<parts.Length?parts[i]:"";
                    address=Clean(string.Join(", ",new[]{Part(2),Part(3),(Part(4)+" "+Part(5)).Trim()}.Where(p=>p.Length>0)),MaxAddress);
                }
                else if(field.Split(';')[0].EndsWith("URL"))links.Add(Unescape(value));
            }
        }
        foreach(var link in links)
            if(FromText(link).Place is {} place) {
                bool unnamed=place.Name==Format(place.Latitude,place.Longitude)||place.Name=="Shared location";
                return (place with {Name=unnamed&&name.Length>0?name:place.Name,Address=place.Address.Length>0?place.Address:address},"","");
            }
        foreach(var text in plain) {
            if(FromText(text).Place is {} typed)return (typed,"","");
            var lines=text.Split('\n').Select(l=>Clean(l,MaxAddress)).Where(l=>l.Length>0&&!Uri.TryCreate(l,UriKind.Absolute,out _)).ToList();
            if(lines.Count==0)continue;
            if(name.Length==0)name=Clean(lines[0],MaxName);
            if(address.Length==0&&lines.Count>1)address=Clean(string.Join(", ",lines.Skip(1)),MaxAddress);
        }
        return (null,address.Length>0?address:name,name);
    }
    /// <summary>A place from the phone's address lookup (JSON with name, address, zip, lat, lon); null if unusable.</summary>
    public static Destination? FromLookup(string json)
    {
        if(json.Length is 0 or >MaxText)return null;
        try {
            using var doc=System.Text.Json.JsonDocument.Parse(json);var r=doc.RootElement;
            string Text(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.String?v.GetString()??"":"";
            var place=new Destination(Clean(Text("name"),MaxName),Clean(Text("address"),MaxAddress),r.GetProperty("lat").GetDouble(),r.GetProperty("lon").GetDouble(),Clean(Text("zip"),10));
            if(place.Name.Length==0)place=place with {Name=Clean(place.Address,MaxName)};
            return Valid(place)?place:null;
        } catch(Exception){return null;}
    }
    public static string Format(double lat,double lon)=>lat.ToString("0.00000",CultureInfo.InvariantCulture)+", "+lon.ToString("0.00000",CultureInfo.InvariantCulture);
}
