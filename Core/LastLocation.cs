using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>The car's last known location, shown (marked old) when a new one can't be requested.
/// Coordinates are sensitive: kept only in the device Keychain.
/// The vehicle is identified by its account-scoped fingerprint, never the VIN.</summary>
public static class LastLocation
{
    /// <summary>The most recent of the given locations. Hyundai's own timestamps decide when both have the
    /// same kind; otherwise the later one received by nCTRL wins.</summary>
    public static VehicleLocation? Newest(params VehicleLocation?[] candidates)
    {
        VehicleLocation? best=null;
        foreach(var c in candidates)if(c!=null&&(best==null||Newer(c,best)))best=c;
        return best;
    }
    private static bool Newer(VehicleLocation a,VehicleLocation b)
    {
        if(IgnoredPlaces.Stamp(a.ReportedAt) is var (at,ac)&&IgnoredPlaces.Stamp(b.ReportedAt) is var (bt,bc)&&ac==bc&&at!=bt)return at>bt;
        return a.RetrievedAt>b.RetrievedAt;
    }
    // Same reported point; a repeated status read of an unmoved car needs no new save.
    public static bool Same(VehicleLocation? a,VehicleLocation? b)=>
        a!=null&&b!=null&&a.Latitude==b.Latitude&&a.Longitude==b.Longitude&&a.ReportedAt==b.ReportedAt;

    public static string Age(DateTimeOffset retrieved,DateTimeOffset now)=>
        retrieved==default?"at an unknown time":now-retrieved<TimeSpan.FromMinutes(1)?"just now":RequestBudget.Duration(now-retrieved)+" ago";

    public static string Encode(string vehicle,VehicleLocation at)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteString("vehicle",vehicle);
            w.WriteNumber("lat",at.Latitude);w.WriteNumber("lon",at.Longitude);
            if(at.Heading is double heading)w.WriteNumber("head",heading);
            w.WriteString("reported",at.ReportedAt.Length>40?"":at.ReportedAt);
            w.WriteString("received",at.RetrievedAt.ToString("O",CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    // Stored data is untrusted. Another vehicle's location, or anything malformed, reads as none.
    public static VehicleLocation? Decode(string json,string vehicle,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>4096||vehicle.Length==0)return null;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1||r.GetProperty("vehicle").GetString()!=vehicle)return null;
            double lat=r.GetProperty("lat").GetDouble(),lon=r.GetProperty("lon").GetDouble();
            if(Math.Abs(lat)>90||Math.Abs(lon)>180||lat==0&&lon==0)return null;
            string reported=r.GetProperty("reported").GetString()??"";
            if(!DateTimeOffset.TryParse(r.GetProperty("received").GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var received))return null;
            // A clock change must not make an old location look new.
            if(received>now)received=now;
            double? heading=r.TryGetProperty("head",out var head)&&head.TryGetDouble(out double h)&&h>=0&&h<360?h:null;
            return new(lat,lon,reported.Length>40?"":reported,received,heading);
        } catch(Exception) {return null;}
    }
}
