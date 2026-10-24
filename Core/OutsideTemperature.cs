using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

public sealed record OutsideReading(double TemperatureF,DateTimeOffset RetrievedAt,string Area);

/// <summary>The current outside temperature near the car, from Open-Meteo (no account or key). Only the
/// car's location rounded to 0.1° (about 11 km) is sent. Completely separate transport: never receives
/// Hyundai headers or account identifiers.</summary>
public sealed class OutsideTemperature : IDisposable
{
    public const string Host="api.open-meteo.com";
    public static readonly TimeSpan Fresh=TimeSpan.FromMinutes(30),AfterFailure=TimeSpan.FromMinutes(10);
    private readonly HttpClient http;
    public OutsideTemperature(HttpMessageHandler? handler=null)
    {
        http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=64*1024};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("nCTRL/1.0 (outside-temperature)");
    }
    /// <summary>The rounded area that is sent, e.g. "40.8,-74.0".</summary>
    public static string Area(VehicleLocation at)
    {
        if(!double.IsFinite(at.Latitude)||!double.IsFinite(at.Longitude)||Math.Abs(at.Latitude)>90||Math.Abs(at.Longitude)>180)
            throw new ArgumentOutOfRangeException(nameof(at));
        static string R(double v)=>(Math.Round(v,1)==0?0:Math.Round(v,1)).ToString("0.0",CultureInfo.InvariantCulture);
        return R(at.Latitude)+","+R(at.Longitude);
    }
    public static string Url(string area)
    {
        var parts=area.Split(',');
        return $"https://{Host}/v1/forecast?latitude={parts[0]}&longitude={parts[1]}&current=temperature_2m&temperature_unit=fahrenheit";
    }
    /// <summary>The reported temperature in °F, or null for anything unexpected.</summary>
    public static double? Parse(string json)
    {
        try {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty("current",out var current)&&
               current.ValueKind==JsonValueKind.Object&&current.TryGetProperty("temperature_2m",out var t)&&
               t.ValueKind==JsonValueKind.Number&&t.TryGetDouble(out double f)&&f is >=-90 and <=140)return f;
        } catch(JsonException) { }
        return null;
    }
    public async Task<OutsideReading> ReadAsync(VehicleLocation at,DateTimeOffset now,CancellationToken ct)
    {
        string area=Area(at);
        using var response=await http.GetAsync(Url(area),ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct)) is double f?new(f,now,area):
            throw new FormatException("Weather reply format unsupported.");
    }
    public void Dispose()=>http.Dispose();
}
