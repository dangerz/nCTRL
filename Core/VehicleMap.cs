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
public static class VehicleMap
{
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
        http.DefaultRequestHeaders.UserAgent.ParseAdd("nCTRL/1.0 (vehicle-location-map)");
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
}
