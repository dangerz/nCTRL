using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

public sealed record MapTile(int X,int Y,int Zoom,double Left,double Top)
{
    public string Url=>$"https://tile.openstreetmap.org/{Zoom}/{X}/{Y}.png";
}
/// <summary>What the map shows: its center, zoom and size in pixels.</summary>
public sealed record MapFrame(double Latitude,double Longitude,int Zoom,int Width=412,int Height=320)
{
    /// <summary>Where a point falls in this view, in pixels from its top-left (it may be outside).</summary>
    public (double X,double Y) Pixel(double latitude,double longitude)
    {
        var (cx,cy)=VehicleMap.World(Latitude,Longitude,Zoom);var (x,y)=VehicleMap.World(latitude,longitude,Zoom);
        double world=256.0*(1<<Zoom);
        // The shorter way around, across the date line.
        if(x-cx>world/2)x-=world;else if(cx-x>world/2)x+=world;
        return (x-cx+Width/2.0,y-cy+Height/2.0);
    }
    public bool Shows(double latitude,double longitude,double margin=0)
    {
        var (x,y)=Pixel(latitude,longitude);
        return x>=margin&&x<=Width-margin&&y>=margin&&y<=Height-margin;
    }
    public double MetersPerPixel=>156543.03392*Math.Cos(Latitude*Math.PI/180)/(1<<Zoom);
}
public static class VehicleMap
{
    // Your phone's position is shown with the car when it is this close; farther away, an arrow points to it.
    public const double FitMeters=8000;
    public const int CloseZoom=15,FarthestZoom=10,FitMargin=36;
    /// <summary>The car close up, or (when the phone is within FitMeters) zoomed out just enough to show both.</summary>
    public static MapFrame Frame(VehicleLocation car,double? phoneLatitude=null,double? phoneLongitude=null,int width=412,int height=320)
    {
        Validate(car);
        var close=new MapFrame(car.Latitude,car.Longitude,CloseZoom,width,height);
        if(phoneLatitude is not double lat||phoneLongitude is not double lon||!double.IsFinite(lat)||!double.IsFinite(lon)||Math.Abs(lat)>90||Math.Abs(lon)>180||
           IgnoredPlaces.Meters(car.Latitude,car.Longitude,lat,lon)>FitMeters||close.Shows(lat,lon,FitMargin))return close;
        double midLat=(car.Latitude+lat)/2,midLon=(car.Longitude+lon)/2;
        for(int zoom=CloseZoom;zoom>FarthestZoom;zoom--) {
            var frame=new MapFrame(midLat,midLon,zoom,width,height);
            if(frame.Shows(car.Latitude,car.Longitude,FitMargin)&&frame.Shows(lat,lon,FitMargin))return frame;
        }
        return new MapFrame(midLat,midLon,FarthestZoom,width,height);
    }
    internal static (double X,double Y) World(double latitude,double longitude,int zoom)
    {
        double count=1<<zoom,lat=Math.Clamp(latitude,-85.05112878,85.05112878)*Math.PI/180;
        return ((longitude+180)/360*count*256,(1-Math.Log(Math.Tan(lat)+1/Math.Cos(lat))/Math.PI)/2*count*256);
    }
    public static IReadOnlyList<MapTile> TilesFor(MapFrame frame)=>Tiles(new VehicleLocation(frame.Latitude,frame.Longitude,""),frame.Width,frame.Height,frame.Zoom);
    public static IReadOnlyList<MapTile> Tiles(VehicleLocation location,int width=412,int height=320,int zoom=15)
    {
        Validate(location);
        if(width<1||width>512||height<1||height>512||zoom<0||zoom>19)throw new ArgumentOutOfRangeException();
        int count=1<<zoom;
        double lat=Math.Clamp(location.Latitude,-85.05112878,85.05112878)*Math.PI/180;
        double cx=(location.Longitude+180)/360*count*256;
        double cy=(1-Math.Log(Math.Tan(lat)+1/Math.Cos(lat))/Math.PI)/2*count*256;
        double left=cx-width/2.0,top=cy-height/2.0;
        var tiles=new List<MapTile>();
        for(int y=(int)Math.Floor(top/256);y*256<top+height;y++)
        for(int x=(int)Math.Floor(left/256);x*256<left+width;x++)
            if(y>=0&&y<count)tiles.Add(new((x%count+count)%count,y,zoom,x*256-left,y*256-top));
        return tiles;
    }
    public static string ExternalUrl(VehicleLocation location,string platform)
    {
        Validate(location);
        string point=location.Latitude.ToString("R",CultureInfo.InvariantCulture)+","+location.Longitude.ToString("R",CultureInfo.InvariantCulture);
        return platform switch {
            "Android"=>"geo:"+point+"?q="+Uri.EscapeDataString(point+"(Vehicle)")+"&z=16",
            "iOS"=>"maps://?ll="+point+"&q=Vehicle&z=16",
            _=>"https://www.openstreetmap.org/?mlat="+location.Latitude.ToString("R",CultureInfo.InvariantCulture)+
                "&mlon="+location.Longitude.ToString("R",CultureInfo.InvariantCulture)+"#map=16/"+point.Replace(',','/')
        };
    }
    private static void Validate(VehicleLocation l)
    {
        if(!double.IsFinite(l.Latitude)||!double.IsFinite(l.Longitude)||Math.Abs(l.Latitude)>90||Math.Abs(l.Longitude)>180)
            throw new ArgumentOutOfRangeException(nameof(l));
    }
}

// Completely separate transport: never receives Hyundai headers or account identifiers.
public sealed class MapTileCache : IDisposable
{
    private readonly HttpClient http;
    private readonly string directory;
    public MapTileCache(string directory,HttpMessageHandler? handler=null)
    {
        this.directory=directory;
        http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false})
            {Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=1024*1024};
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Brand.App+"/1.0 (vehicle-location-map)");
    }
    public async Task<byte[]> LoadAsync(MapTile tile,CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tile.Url)))+".png");
        bool exists=File.Exists(file);
        var written=exists?new DateTimeOffset(File.GetLastWriteTimeUtc(file)):DateTimeOffset.MinValue;
        if(exists&&DateTimeOffset.UtcNow-written<TimeSpan.FromDays(7))return await File.ReadAllBytesAsync(file,ct);
        using var request=new HttpRequestMessage(HttpMethod.Get,tile.Url);
        if(exists)request.Headers.IfModifiedSince=written;
        using var response=await http.SendAsync(request,ct);
        if(response.StatusCode==HttpStatusCode.NotModified&&exists) {
            File.SetLastWriteTimeUtc(file,DateTime.UtcNow);return await File.ReadAllBytesAsync(file,ct);
        }
        response.EnsureSuccessStatusCode();
        var bytes=await response.Content.ReadAsByteArrayAsync(ct);
        if(bytes.Length<8||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))
            throw new IOException("Map tile format unsupported.");
        // Atomic rename prevents cancellation leaving a corrupt reusable cache entry.
        string temporary=file+".tmp";
        try {await File.WriteAllBytesAsync(temporary,bytes,ct);File.Move(temporary,file,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
        return bytes;
    }
    public void Dispose()=>http.Dispose();
    /// <summary>Pictures are kept about a week.</summary>
    public static readonly TimeSpan MaxAge=TimeSpan.FromDays(7);
    /// <summary>Deletes pictures not refreshed within <see cref="MaxAge"/> (at launch). Returns how many went.</summary>
    public static int Prune(string directory,DateTimeOffset now)
    {
        int removed=0;
        if(!Directory.Exists(directory))return 0;
        foreach(string file in Directory.EnumerateFiles(directory))
            try {if(now-new DateTimeOffset(File.GetLastWriteTimeUtc(file))>MaxAge){File.Delete(file);removed++;}} catch(IOException) { } catch(UnauthorizedAccessException) { }
        return removed;
    }
    /// <summary>Deletes every picture (Sign out & forget).</summary>
    public static void Clear(string directory)
    {
        try {if(Directory.Exists(directory))Directory.Delete(directory,true);} catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
