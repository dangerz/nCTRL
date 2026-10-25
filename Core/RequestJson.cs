using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

// Explicit JSON writing works in trimmed / NativeAOT builds without runtime reflection.
// Payloads contain secrets; never log them or include them in exception messages.
internal static class RequestJson
{
    internal static string Object(Action<Utf8JsonWriter> fields)
    {
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) {
            writer.WriteStartObject();fields(writer);writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    internal static string Token(string username,string password,string refresh)=>Object(w=>{
        w.WriteString("username",username);w.WriteString("password",password);
        if(refresh.Length>0){w.WriteString("grant_type","refresh_token");w.WriteString("refresh_token",refresh);}
    });
    // Send to car: the body Hyundai's own app sends to rcs/stc/sendPoi, one place, saved to the car's navigation.
    internal static string Destination(string username,string vin,Destination place)=>Object(w=>{
        static string N(double v)=>v.ToString("0.000000",CultureInfo.InvariantCulture);
        w.WriteString("username",username);w.WriteString("vin",vin);w.WriteString("actionType","SAVE");
        w.WriteStartArray("poiInfoList");w.WriteStartObject();
        w.WriteString("googleRefId","");w.WriteString("phone","");w.WriteString("zip",place.Zip);w.WriteString("isFavorite","0");
        w.WriteString("note","");w.WriteString("src","HERE");w.WriteNumber("waypointID",0);w.WriteString("poiId","0");
        w.WriteString("addr",place.Address);
        w.WriteStartObject("coord");w.WriteString("lat",N(place.Latitude));w.WriteString("alt","0");w.WriteString("lon",N(place.Longitude));w.WriteString("type","0");w.WriteEndObject();
        w.WriteString("lang","0");w.WriteString("name",place.Name);w.WriteString("placeid","");
        w.WriteEndObject();w.WriteEndArray();
    });
    internal static string Doors(string username,string vin,string pin)=>Object(w=>{
        w.WriteString("userName",username);w.WriteString("vin",vin);w.WriteString("pin",pin);
    });
    // heating1: 0 off, 3 steering wheel only (upstream HEAT_STATUS). Front defrost is optional; rear-window heat stays off.
    // Flash lights and horn & lights: upstream sends only the account and VIN (no service PIN in the body).
    internal static string HornLights(string username,string vin)=>Object(w=>{
        w.WriteString("userName",username);w.WriteString("vin",vin);
    });
    internal static string Climate(int temperature,int generation,ClimateOptions? options=null)=>Object(w=>{
        options??=new();
        w.WriteNumber("airCtrl",1);w.WriteStartObject("airTemp");
        w.WriteString("value",temperature.ToString(CultureInfo.InvariantCulture));w.WriteNumber("unit",1);w.WriteEndObject();
        w.WriteBoolean("defrost",options.Defrost);w.WriteNumber("heating1",options.SteeringWheel?3:0);
        if(generation==3){
            w.WriteNumber("igniOnDuration",5);w.WriteStartObject("seatHeaterVentInfo");
            w.WriteNumber("drvSeatHeatState",SeatCodes.Code(options.DriverSeat));w.WriteNumber("astSeatHeatState",SeatCodes.Code(options.PassengerSeat));
            w.WriteNumber("rlSeatHeatState",0);w.WriteNumber("rrSeatHeatState",0);
            w.WriteEndObject();
        }
    });
    internal static string ChargeLimits(int ac,int dc)=>Object(w=>{
        w.WriteStartArray("targetSOClist");
        w.WriteStartObject();w.WriteNumber("plugType",0);w.WriteNumber("targetSOClevel",dc);w.WriteEndObject();
        w.WriteStartObject();w.WriteNumber("plugType",1);w.WriteNumber("targetSOClevel",ac);w.WriteEndObject();
        w.WriteEndArray();
    });
}
