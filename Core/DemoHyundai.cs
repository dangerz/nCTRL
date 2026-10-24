using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>A pretend Hyundai service for the demo: the real client talks to it instead of the network, so
/// every screen works without an account and nothing leaves the phone. The car is fictional and answers
/// commands the way Hyundai would, changing its reported state.</summary>
public sealed class DemoHyundai : HttpMessageHandler
{
    public const string Email="demo@example.invalid",Vin="DEMOVIN0000000001";
    // A public place (Central Park, New York), not anyone's car.
    public const double Latitude=40.7812,Longitude=-73.9665;
    private readonly object sync=new();
    private readonly TimeProvider clock;
    private readonly byte[] picture;
    private bool locked=true,climate,charging,defrost,wheel;
    private int temperature=72,driverSeat,passengerSeat,soc=78,ac=80,dc=90;
    private DateTimeOffset? capturedAt;
    public int Requests { get; private set; }

    /// <param name="picture">What the Surround View "camera" returns: a PNG or JPEG.</param>
    public DemoHyundai(byte[] picture,TimeProvider? clock=null){this.picture=picture;this.clock=clock??TimeProvider.System;}

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string path=request.RequestUri?.AbsolutePath??"";
        string body=request.Content==null?"":await request.Content.ReadAsStringAsync(ct);
        lock(sync) {
            Requests++;
            string? tid=null;string json;
            switch(path) {
                case "/v2/ac/oauth/token":json=Json(w=>{w.WriteString("access_token","demo");w.WriteString("refresh_token","demo");w.WriteNumber("expires_in",86400);});break;
                case "/ac/v2/rcs/rvs/vehicleStatus":json=Status();break;
                case "/ac/v2/rcs/rdo/off":locked=true;json="";tid="demo";break;
                case "/ac/v2/rcs/rdo/on":locked=false;json="";tid="demo";break;
                case "/ac/v2/rcs/rhl/light" or "/ac/v2/rcs/rhl/hnl":json="";tid="demo";break;
                case "/ac/v2/evc/fatc/start":Climate(body);json="";tid="demo";break;
                case "/ac/v2/evc/fatc/stop":climate=false;defrost=false;wheel=false;driverSeat=passengerSeat=0;json="";tid="demo";break;
                case "/ac/v2/evc/charge/start":charging=soc<ac;json="";tid="demo";break;
                case "/ac/v2/evc/charge/stop":charging=false;json="";tid="demo";break;
                case "/ac/v2/evc/charge/targetsoc/set":Targets(body);json="";tid="demo";break;
                case "/ac/v2/rmt/getRunningStatus":json=Json(w=>w.WriteString("status","SUCCESS"));break;
                case "/ac/v2/rcs/rfc/findMyCar":json=Json(w=>{Coord(w);w.WriteString("time",Now());});break;
                case "/ac/v2/svm/findMyCarSVM":capturedAt=clock.GetUtcNow();json="";break;
                case "/ac/v2/svm/getSVMDetails":json=Picture();break;
                default:
                    if(path.StartsWith("/ac/v2/enrollment/details/",StringComparison.Ordinal)){json=Enrollment();break;}
                    return new HttpResponseMessage(HttpStatusCode.NotFound){Content=new StringContent("{\"errorCode\":404}",Encoding.UTF8,"application/json")};
            }
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
            if(tid!=null)response.Headers.Add("tmsTid",tid);
            return response;
        }
    }

    private string Now()=>clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture);
    private static void Coord(Utf8JsonWriter w){w.WriteStartObject("coord");w.WriteNumber("lat",Latitude);w.WriteNumber("lon",Longitude);w.WriteEndObject();}
    private static string Enrollment()=>Json(w=>{
        w.WriteStartArray("enrolledVehicleDetails");w.WriteStartObject();w.WriteStartObject("vehicleDetails");
        w.WriteString("regid","demo");w.WriteString("vin",Vin);w.WriteString("nickName","Demo car");w.WriteString("modelCode","IONIQ 5 N");
        w.WriteNumber("vehicleGeneration",3);w.WriteString("evStatus","E");
        w.WriteEndObject();w.WriteEndObject();w.WriteEndArray();
    });
    private string Status()=>Json(w=>{
        w.WriteStartObject("vehicleStatus");
        w.WriteString("dateTime",Now());w.WriteBoolean("doorLock",locked);w.WriteBoolean("airCtrlOn",climate);
        w.WriteStartObject("airTemp");w.WriteString("value",temperature.ToString(CultureInfo.InvariantCulture));w.WriteNumber("unit",1);w.WriteEndObject();
        w.WriteBoolean("defrost",climate&&defrost);w.WriteBoolean("steerWheelHeat",climate&&wheel);
        w.WriteStartObject("seatHeaterVentState");w.WriteNumber("flSeatHeatState",climate?driverSeat:0);w.WriteNumber("frSeatHeatState",climate?passengerSeat:0);w.WriteEndObject();
        w.WriteStartObject("battery");w.WriteNumber("batSoc",86);w.WriteEndObject();
        foreach(var group in new[]{"doorOpen","windowOpen"}) {
            w.WriteStartObject(group);foreach(var side in new[]{"frontLeft","frontRight","backLeft","backRight"})w.WriteNumber(side,0);w.WriteEndObject();
        }
        w.WriteBoolean("trunkOpen",false);w.WriteBoolean("hoodOpen",false);
        w.WriteStartObject("tirePressure");
        foreach(var (tire,psi) in new[]{("FrontLeft",36),("FrontRight",36),("RearLeft",35),("RearRight",35)})w.WriteNumber("tirePressure"+tire,psi);
        w.WriteEndObject();
        w.WriteStartObject("tirePressureLamp");w.WriteNumber("tirePressureWarningLampAll",0);w.WriteEndObject();
        w.WriteBoolean("washerFluidStatus",false);w.WriteBoolean("breakOilStatus",false);w.WriteBoolean("smartKeyBatteryWarning",false);
        w.WriteStartObject("vehicleLocation");Coord(w);w.WriteString("time",Now());w.WriteEndObject();
        w.WriteStartObject("evStatus");
        w.WriteNumber("batteryStatus",soc);w.WriteNumber("batteryPlugin",1);w.WriteBoolean("batteryCharge",charging);
        if(charging) {
            w.WriteNumber("realTimePower",7.2);
            w.WriteStartObject("remainTime2");w.WriteStartObject("atc");w.WriteNumber("value",Math.Max(5,(ac-soc)*8));w.WriteNumber("unit",1);w.WriteEndObject();w.WriteEndObject();
        }
        w.WriteStartArray("drvDistance");w.WriteStartObject();w.WriteStartObject("rangeByFuel");w.WriteStartObject("totalAvailableRange");
        w.WriteNumber("value",(int)Math.Round(soc*2.36));w.WriteNumber("unit",3);
        w.WriteEndObject();w.WriteEndObject();w.WriteEndObject();w.WriteEndArray();
        w.WriteStartObject("reservChargeInfos");w.WriteStartArray("targetSOClist");
        foreach(var (plug,level) in new[]{(0,dc),(1,ac)}){w.WriteStartObject();w.WriteNumber("plugType",plug);w.WriteNumber("targetSOClevel",level);w.WriteEndObject();}
        w.WriteEndArray();w.WriteEndObject();
        w.WriteEndObject();
        w.WriteEndObject();
    });
    private string Picture()=>Json(w=>{
        w.WriteStartArray("svmDetails");w.WriteStartObject();w.WriteStartObject("svmDetail");
        w.WriteString("svmImage",Convert.ToBase64String(picture));
        w.WriteStartObject("gpsDetail");w.WriteString("time",(capturedAt??clock.GetUtcNow().AddDays(-1)).ToString("yyyy-MM-ddTHH:mm:ssZ",CultureInfo.InvariantCulture));
        Coord(w);w.WriteNumber("head",90);w.WriteEndObject();
        w.WriteBoolean("trunkOpen",false);
        w.WriteEndObject();w.WriteEndObject();w.WriteEndArray();
    });
    private void Climate(string body)
    {
        climate=true;
        try {
            using var doc=JsonDocument.Parse(body);var r=doc.RootElement;
            if(int.TryParse(HyundaiUsClient.Text(r,"airTemp.value"),out int t)&&t is >=62 and <=82)temperature=t;
            defrost=r.TryGetProperty("defrost",out var d)&&d.ValueKind==JsonValueKind.True;
            wheel=r.TryGetProperty("heating1",out var h)&&h.TryGetInt32(out int heat)&&heat>0;
            driverSeat=Seat(HyundaiUsClient.At(r,"seatHeaterVentInfo.drvSeatHeatState"));
            passengerSeat=Seat(HyundaiUsClient.At(r,"seatHeaterVentInfo.astSeatHeatState"));
        } catch(JsonException) { }
        static int Seat(JsonElement e)=>e.ValueKind==JsonValueKind.Number&&e.TryGetInt32(out int code)&&code is >=0 and <=8?code:0;
    }
    private void Targets(string body)
    {
        try {
            using var doc=JsonDocument.Parse(body);
            foreach(var t in doc.RootElement.GetProperty("targetSOClist").EnumerateArray()) {
                int level=t.GetProperty("targetSOClevel").GetInt32();
                if(level is <50 or >100)continue;
                if(t.GetProperty("plugType").GetInt32()==1)ac=level;else dc=level;
            }
            if(charging&&soc>=ac)charging=false;
        } catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }
    private static string Json(Action<Utf8JsonWriter> fields)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();fields(w);w.WriteEndObject();}
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
