using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

public sealed record IgnoredPlace(string Name,double Latitude,double Longitude,int RadiusMeters=IgnoredPlaces.DefaultRadius);

/// <summary>Places where an unlocked car is not flagged, matched against the car's own reported
/// location. Coordinates are sensitive: they live only in the device Keychain, never in files,
/// logs, the saved status or the repository.</summary>
public static class IgnoredPlaces
{
    public const int DefaultRadius=150,MaxPlaces=20,MaxName=30;
    // A location farther than this from the status report is too old to hide an alert.
    public static readonly TimeSpan FreshWithin=TimeSpan.FromMinutes(10);

    public static string CleanName(string name)
    {
        name=string.Join(' ',name.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
        return name.Length==0?"Place":name.Length>MaxName?name[..MaxName]:name;
    }
    public static IgnoredPlace? Match(IReadOnlyList<IgnoredPlace> places,VehicleLocation? at)=>
        at==null?null:places.FirstOrDefault(p=>Meters(p.Latitude,p.Longitude,at.Latitude,at.Longitude)<=p.RadiusMeters);
    public static double Meters(double lat1,double lon1,double lat2,double lon2)
    {
        const double R=6371000;double Rad(double d)=>d*Math.PI/180;
        double dLat=Rad(lat2-lat1),dLon=Rad(lon2-lon1);
        double a=Math.Sin(dLat/2)*Math.Sin(dLat/2)+Math.Cos(Rad(lat1))*Math.Cos(Rad(lat2))*Math.Sin(dLon/2)*Math.Sin(dLon/2);
        return 2*R*Math.Asin(Math.Min(1,Math.Sqrt(a)));
    }

    /// <summary>The car location that belongs with this status report, or null when there is none or it is
    /// too old. A location inside the report counts unless its own timestamp is clearly older; a separately
    /// requested location counts only if received within ten minutes of the status.</summary>
    public static VehicleLocation? FreshLocation(LiveVehicleState? state,VehicleLocation? requested)
    {
        if(state==null)return null;
        if(state.StatusLocation is {} inReport) {
            var status=Stamp(state.ReportedAt);var located=Stamp(inReport.ReportedAt);
            bool older=status is var (s,sf)&&located is var (l,lf)&&sf==lf&&s-l>FreshWithin;
            if(!older)return inReport;
        }
        if(requested is {} r&&r.RetrievedAt!=default&&(state.RetrievedAt-r.RetrievedAt).Duration()<=FreshWithin)return r;
        return null;
    }
    // Hyundai uses ISO times and compact yyyyMMddHHmmss times; only stamps of the same kind are compared.
    private static (DateTimeOffset At,bool Compact)? Stamp(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(text.Length==14&&DateTime.TryParseExact(text,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var compact))
            return (new DateTimeOffset(compact.ToUniversalTime()),true);
        return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var iso)?(iso,false):null;
    }

    public static string Encode(IReadOnlyList<IgnoredPlace> places)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteStartArray("places");
            foreach(var p in places.Take(MaxPlaces)) {
                w.WriteStartObject();w.WriteString("name",CleanName(p.Name));w.WriteNumber("lat",p.Latitude);w.WriteNumber("lon",p.Longitude);
                w.WriteNumber("radius",p.RadiusMeters);w.WriteEndObject();
            }
            w.WriteEndArray();w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    // Stored data is untrusted: anything malformed is skipped.
    public static IReadOnlyList<IgnoredPlace> Decode(string json)
    {
        var places=new List<IgnoredPlace>();
        if(string.IsNullOrWhiteSpace(json)||json.Length>65536)return places;
        try {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.GetProperty("version").GetInt32()!=1)return places;
            foreach(var p in doc.RootElement.GetProperty("places").EnumerateArray()) {
                if(places.Count>=MaxPlaces)break;
                double lat=p.GetProperty("lat").GetDouble(),lon=p.GetProperty("lon").GetDouble();int radius=p.GetProperty("radius").GetInt32();
                if(Math.Abs(lat)>90||Math.Abs(lon)>180||radius is <10 or >5000)continue;
                places.Add(new(CleanName(p.GetProperty("name").GetString()??""),lat,lon,radius));
            }
        } catch(Exception) {return new List<IgnoredPlace>();}
        return places;
    }
}
