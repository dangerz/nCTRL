using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

// Kia Connect (US) protocol checks against a fake server. Field shapes follow upstream KiaUvoApiUSA.py and a real
// cmm/gvi reply seen by Tools/KiaProbe on 2026-11-08 (values changed).
static class KiaProtocolTests
{
    const string Ok="{\"status\":{\"statusCode\":0,\"errorType\":0,\"errorCode\":0}}";
    const string Otp="""{"status":{"statusCode":0,"errorType":0,"errorCode":0},"payload":{"otpKey":"otp-key-1","hasEmail":true,"email":"*****@example.invalid","hasPhone":true,"phone":"(***) ***-0000","rmTokenExpired":false}}""";
    const string Vehicles="""{"status":{"statusCode":0},"payload":{"vehicleSummary":[{"vehicleIdentifier":"veh-1","nickName":"FLAREON2","modelName":"EV6","modelYear":"2024","fuelType":4,"vehicleKey":"key-1"},{"vehicleIdentifier":"veh-0","nickName":"OLD","modelName":"EV6","fuelType":4,"vehicleKey":"key-0"}]}}""";
    const string Status="""
    {"status":{"statusCode":0},"payload":{"vehicleInfoList":[{"lastVehicleInfo":{
      "vehicleStatusRpt":{"vehicleStatus":{"syncDate":{"utc":"20261108141440"},"doorLock":false,
        "climate":{"airCtrl":false,"defrost":false,"airTemp":{"value":"72","unit":1},"heatingAccessory":{"steeringWheel":0,"rearWindow":0,"sideMirror":0}},
        "doorStatus":{"frontLeft":0,"frontRight":0,"backLeft":1,"backRight":0,"trunk":0,"hood":0},
        "windowStatus":{"windowFL":0,"windowFR":1,"windowRL":0,"windowRR":0},
        "tirePressure":{"all":0},"batteryStatus":{"stateOfCharge":88},"washerFluidStatus":false,"breakOilStatus":false,"smartKeyBatteryWarning":false,
        "evStatus":{"batteryStatus":77,"batteryCharge":false,"batteryPlugin":0,"realTimePower":0,
          "drvDistance":[{"type":2,"rangeByFuel":{"evModeRange":{"value":133.0,"unit":3},"totalAvailableRange":{"value":133.0,"unit":3}}}],
          "targetSOC":[{"plugType":0,"targetSOClevel":100},{"plugType":1,"targetSOClevel":90}]}}},
      "location":{"coord":{"lat":40.7812,"lon":-73.9665},"syncDate":{"utc":"20261108141000"}}},
      "vehicleConfig":{"vehicleDetail":{"vehicle":{"mileage":"12345"}}}}]}}
    """;
    const string Expired="{\"status\":{\"statusCode\":1,\"errorType\":1,\"errorCode\":1003,\"errorMessage\":\"Session Key is either invalid or expired\"}}";

    sealed record Captured(string Path,string Method,string Body,Dictionary<string,string> Headers);
    sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Replies=new();
        public readonly List<Captured> Calls=new();
        public void Add(string body,params (string,string)[] headers)=>Add(body,HttpStatusCode.OK,headers);
        public void Add(string body,HttpStatusCode status,params (string,string)[] headers)
        { Replies.Enqueue(()=>{var r=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};foreach(var (k,v) in headers)r.Headers.TryAddWithoutValidation(k,v);return r;}); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            if(r.RequestUri!.Host!="api.owners.kia.com"||r.RequestUri.Scheme!="https"||!r.RequestUri.AbsolutePath.StartsWith("/apigw/v1/"))throw new Exception("Unexpected origin");
            var headers=r.Headers.ToDictionary(x=>x.Key,x=>string.Join(",",x.Value),StringComparer.OrdinalIgnoreCase);
            if(r.Content!=null)foreach(var h in r.Content.Headers)headers[h.Key]=string.Join(",",h.Value);
            Calls.Add(new(r.RequestUri.AbsolutePath["/apigw/v1/".Length..],r.Method.Method,r.Content==null?"":await r.Content.ReadAsStringAsync(ct),headers));
            if(Replies.Count==0)throw new Exception("Unexpected request / retry");
            return Replies.Dequeue()();
        }
    }
    static int count;
    static void Check(bool b,string name){if(!b)throw new Exception("Kia protocol: "+name);count++;}
    static async Task<BluelinkException> Failure(Func<Task> action)
    {try{await action();}catch(BluelinkException e){return e;}throw new Exception("Kia protocol: expected a sanitized failure");}
    static RequestBudget Unlimited()=>new(new MemoryBudgetStore(),null,
        RequestBudget.Defaults.ToDictionary(x=>x.Key,x=>new RequestLimit(TimeSpan.Zero,int.MaxValue)));
    static JsonElement Json(string s){using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}

    // Signs in through the one-time code and selects FLAREON2.
    static async Task<(KiaUsClient,Fake)> Session()
    {
        var f=new Fake();
        f.Add(Otp,("xid","xid-1"));f.Add(Ok);
        f.Add(Ok,("sid","temp-sid"),("rmtoken","remember-1"));f.Add(Ok,("sid","sid-1"));f.Add(Vehicles);
        f.Add(Status);
        var c=new KiaUsClient(f,null,Unlimited()){DeviceId="TEST-DEVICE"};
        try{await c.LoginAsync("tester@example.invalid","pass\"word","",default);throw new Exception("Kia protocol: code not asked");}
        catch(OneTimeCodeRequiredException){}
        await c.SendCodeAsync(CodeChannel.Text,default);
        var list=await c.VerifyCodeAsync(" 123456 ",default);
        await c.SelectAsync(list[0],default);
        return (c,f);
    }

    public static async Task Run()
    {
        count=0;
        Check(KiaUsClient.Uuid5Dns("TEST-DEVICE")=="25d0dff4-2ddc-55fe-b890-e04a97c78f69","clientuuid matches Python uuid5(NAMESPACE_DNS)");
        Check(KiaUsClient.Iso("20261108141440")=="2026-11-08T14:14:40Z"&&KiaUsClient.Iso("odd")=="odd","Kia compact UTC times become ISO");
        await SignIn();
        await Reads();
        await Commands();
        await SessionRepair();
        await Failures();
        SavedLogin();
        Console.WriteLine($"PASS: {count} Kia protocol checks (fake server, no network).");
    }

    static async Task SignIn()
    {
        var f=new Fake();f.Add(Otp,("xid","xid-1"));
        var c=new KiaUsClient(f,null,Unlimited()){DeviceId="TEST-DEVICE"};
        CodeDestinations? where=null;
        try{await c.LoginAsync("tester@example.invalid","pass\"word","",default);}catch(OneTimeCodeRequiredException e){where=e.Destinations;}
        Check(where is {HasEmail:true,HasPhone:true}&&where.Phone=="(***) ***-0000","A code request reports Kia's masked destinations");
        var login=f.Calls[0];
        Check(login.Method=="POST"&&login.Path=="prof/authUser","Sign-in is POST prof/authUser");
        using(var body=JsonDocument.Parse(login.Body)) {
            var b=body.RootElement;
            Check(b.GetProperty("deviceKey").GetString()=="TEST-DEVICE"&&b.GetProperty("deviceType").GetInt32()==2&&b.GetProperty("tncFlag").GetInt32()==1,"First sign-in body has device and tncFlag");
            Check(b.GetProperty("userCredential").GetProperty("password").GetString()=="pass\"word","Password is escaped, not altered");
        }
        var h=login.Headers;
        Check(h["clientid"]=="SPACL716-APL"&&h["secretkey"]=="sydnat-9kykci-Kuhtep-h5nK"&&h["appversion"]=="7.22.0"&&h["from"]=="SPA"&&h["to"]=="APIGW","Static Kia headers");
        Check(h["deviceid"]=="TEST-DEVICE"&&h["clientuuid"]=="25d0dff4-2ddc-55fe-b890-e04a97c78f69"&&h.ContainsKey("date")&&h.ContainsKey("offset"),"Device headers and date");
        Check(h["content-type"].StartsWith("application/json")&&!h.ContainsKey("rmtoken")&&!h.ContainsKey("sid"),"No remember token or session on a first sign-in");

        f.Add(Ok);await c.SendCodeAsync(CodeChannel.Text,default);
        var send=f.Calls[^1];
        Check(send.Path=="cmm/sendOTP"&&send.Body=="{}"&&send.Headers["otpkey"]=="otp-key-1"&&send.Headers["notifytype"]=="SMS"&&send.Headers["xid"]=="xid-1","sendOTP carries the code request");
        f.Add(Ok);await c.SendCodeAsync(CodeChannel.Email,default);
        Check(f.Calls[^1].Headers["notifytype"]=="EMAIL","Email code");

        var bad=await Failure(()=>c.VerifyCodeAsync("12ab",default));
        Check(f.Calls.Count==3,"A malformed code sends nothing");
        f.Add(Ok,("sid","temp-sid"),("rmtoken","remember-1"));f.Add(Ok,("sid","sid-1"));f.Add(Vehicles);
        var list=await c.VerifyCodeAsync("123456",default);
        var verify=f.Calls[3];var complete=f.Calls[4];var gvl=f.Calls[5];
        Check(verify.Path=="cmm/verifyOTP"&&verify.Body=="{\"otp\":\"123456\"}"&&verify.Headers["otpkey"]=="otp-key-1"&&verify.Headers["xid"]=="xid-1","verifyOTP body and headers");
        using(var body=JsonDocument.Parse(complete.Body))
            Check(complete.Path=="prof/authUser"&&complete.Headers["sid"]=="temp-sid"&&complete.Headers["rmtoken"]=="remember-1"&&!body.RootElement.TryGetProperty("tncFlag",out _),"Completing sign-in sends the verified session and remember token, no tncFlag");
        Check(gvl.Method=="GET"&&gvl.Path=="ownr/gvl"&&gvl.Headers["sid"]=="sid-1"&&gvl.Headers["content-type"].StartsWith("application/json")&&gvl.Body=="","Vehicle list is a GET with the session and content-type");
        Check(c.RememberToken=="remember-1"&&c.IsAuthenticated&&c.SessionValid,"Remember token and session kept");
        Check(list.Count==2&&list[0].Name=="FLAREON2"&&list[0].Model=="EV6"&&list[0].Electric&&list[0].Generation==3&&list[0].Id=="veh-1","Vehicles parsed; fuelType 4 is electric");
        Check(!list[0].ToString().Contains("key-1"),"The session-only vehicle key isn't part of the saved vehicle");

        // A later sign-in with the remember token skips the code.
        var f2=new Fake();f2.Add(Ok,("sid","sid-2"));f2.Add(Vehicles);
        var c2=new KiaUsClient(f2,null,Unlimited()){DeviceId="TEST-DEVICE",RememberToken="remember-1"};
        var again=await c2.LoginAsync("tester@example.invalid","pw","",default);
        Check(again.Count==2&&f2.Calls[0].Headers["rmtoken"]=="remember-1"&&f2.Calls.Count==2,"Remember token signs in without a code");
        // An expired remember token is dropped so the next code replaces it.
        var f3=new Fake();f3.Add(Otp.Replace("\"rmTokenExpired\":false","\"rmTokenExpired\":true"),("xid","x"));
        var c3=new KiaUsClient(f3,null,Unlimited()){RememberToken="old"};
        try{await c3.LoginAsync("tester@example.invalid","pw","",default);}catch(OneTimeCodeRequiredException){}
        Check(c3.RememberToken=="","Expired remember token forgotten");
        // Wrong password.
        var f4=new Fake();f4.Add("{\"status\":{\"statusCode\":1,\"errorType\":1,\"errorCode\":1001,\"errorMessage\":\"secret server text\"}}");
        var c4=new KiaUsClient(f4,null,Unlimited());
        var wrong=await Failure(()=>c4.LoginAsync("tester@example.invalid","pw","",default));
        Check(wrong.Message.Contains("didn't accept that email and password")&&!wrong.Message.Contains("secret server text")&&!c4.IsAuthenticated,"Wrong password explained, server text withheld");
        var empty=await Failure(()=>new KiaUsClient(new Fake()).LoginAsync("","pw","",default));
        Check(empty.Message.Contains("email and password"),"Missing email refused before any request");
    }

    static async Task Reads()
    {
        var (c,f)=await Session();
        var gvi=f.Calls[^1];
        Check(gvi.Method=="POST"&&gvi.Path=="cmm/gvi"&&gvi.Headers["vinkey"]=="key-1"&&gvi.Headers["sid"]=="sid-1","Cached status is POST cmm/gvi with the vehicle key");
        using(var body=JsonDocument.Parse(gvi.Body)) {
            var b=body.RootElement;
            Check(b.GetProperty("vinKey")[0].GetString()=="key-1"&&b.GetProperty("vehicleInfoReq").GetProperty("location").GetString()=="1"&&
                b.GetProperty("vehicleInfoReq").GetProperty("drivingActivty").GetString()=="0","gvi body asks for status and location");
        }
        var s=c.State!;
        Check(s.Battery==77&&s.RangeMiles==133&&s.Locked==false&&s.ClimateOn==false&&s.TemperatureF==72,"Battery, range, lock and climate");
        Check(s.AcLimit==90&&s.DcLimit==100&&s.PluggedIn==false&&s.Charging==false,"Charge targets (plugType 1 AC, 0 DC) and plug state");
        Check(s.ReportedAt=="2026-11-08T14:14:40Z","Report time as ISO");
        Check(s.Health is {Battery12V:88,RearLeftDoor:true,FrontLeftDoor:false,FrontRightWindow:true,TireWarning:false,Tailgate:false},"Doors, windows (windowStatus fallback), 12 V and tires");
        Check(s.StatusLocation is {Latitude:40.7812,Longitude:-73.9665,ReportedAt:"2026-11-08T14:10:00Z",Heading:null},"Location from the report, without heading");
        Check(c.RawStatus?.GetProperty("doorLock").GetBoolean()==false,"Raw status kept for Details");

        f.Add("{\"status\":{\"statusCode\":0}}");f.Add(Status);
        await c.ReadAsync(true,default);
        Check(f.Calls[^2].Path=="rems/rvs"&&f.Calls[^2].Body=="{\"requestType\":0}"&&f.Calls[^1].Path=="cmm/gvi","Wake asks for a new report, then reads it");
        f.Add(Status);
        var at=await c.LocateAsync(default);
        Check(at.Latitude==40.7812&&c.Location==at&&f.Calls[^1].Path=="cmm/gvi","Location comes from a status read");

        // Kilometers, LOW, plugged as a number, a 0 target ignored, charging minutes.
        var other=Json(Status.Replace("\"value\":133.0,\"unit\":3},\"totalAvailableRange\":{\"value\":133.0,\"unit\":3}","\"value\":160.9344,\"unit\":1},\"totalAvailableRange\":{\"value\":160.9344,\"unit\":1}")
            .Replace("\"value\":\"72\"","\"value\":\"LOW\"").Replace("\"batteryPlugin\":0","\"batteryPlugin\":1").Replace("\"batteryCharge\":false","\"batteryCharge\":true")
            .Replace("{\"plugType\":1,\"targetSOClevel\":90}","{\"plugType\":1,\"targetSOClevel\":0}").Replace("\"realTimePower\":0","\"realTimePower\":10.5,\"remainChargeTime\":[{\"timeInterval\":{\"value\":95,\"unit\":1}}]"));
        var k=KiaUsClient.ParseState(HyundaiUsClient.At(other,"payload.vehicleInfoList.0"),DateTimeOffset.UnixEpoch);
        Check(Math.Abs(k.RangeMiles!.Value-100)<0.001&&k.TemperatureF==62&&k.PluggedIn==true&&k.Charging==true,"Kilometers, LOW and plugged code");
        Check(k.AcLimit==null&&k.DcLimit==100&&k.ChargingPowerKw==10.5&&k.ChargeMinutesLeft==95,"A 0 target is not a limit; power and minutes left while charging");
        var noStatus=await Failure(()=>Task.FromResult(KiaUsClient.ParseState(Json("{}"),DateTimeOffset.UnixEpoch)));
        Check(noStatus.Message.Contains("unsupported"),"A reply without status loads nothing");
    }

    static async Task Commands()
    {
        var (c,f)=await Session();
        f.Add(Ok,("Xid","tx-1"));
        var receipt=await c.ExecuteAsync(new(ActionKind.Lock),null,default);
        Check(f.Calls[^1].Method=="GET"&&f.Calls[^1].Path=="rems/door/lock"&&f.Calls[^1].Headers["vinkey"]=="key-1","Lock is GET rems/door/lock");
        Check(receipt.Outcome==RemoteOutcome.Accepted&&receipt.TransactionId=="tx-1"&&receipt.Message.StartsWith("Kia accepted"),"Accepted with Kia's Xid");
        f.Add("{\"status\":{\"statusCode\":0},\"payload\":{\"remoteStatus\":1,\"alertStatus\":0}}");
        Check((await c.CheckCommandAsync(default)).Outcome==RemoteOutcome.Accepted&&f.Calls[^1].Path=="cmm/gts"&&f.Calls[^1].Body=="{\"xid\":\"tx-1\"}","Tracking: a counter above 0 is still pending");
        f.Add("{\"status\":{\"statusCode\":0},\"payload\":{\"remoteStatus\":0,\"alertStatus\":0}}");
        Check((await c.CheckCommandAsync(default)).Outcome==RemoteOutcome.Confirmed,"Tracking: all counters 0 is done");

        var (c2,f2)=await Session();
        f2.Add(Ok,("Xid","tx-2"));
        await c2.ExecuteAsync(new(ActionKind.Climate,72,true,new(SeatMode.HeatHigh,SeatMode.CoolLow,true,true)),null,default);
        var climate=f2.Calls[^1];
        Check(climate.Method=="POST"&&climate.Path=="rems/start","Climate start is POST rems/start");
        using(var body=JsonDocument.Parse(climate.Body)) {
            var r=body.RootElement.GetProperty("remoteClimate");
            Check(r.GetProperty("airTemp").GetProperty("value").GetString()=="72"&&r.GetProperty("airTemp").GetProperty("unit").GetInt32()==1&&r.GetProperty("airCtrl").GetBoolean()&&r.GetProperty("defrost").GetBoolean(),"Temperature, air and defrost");
            var acc=r.GetProperty("heatingAccessory");
            Check(acc.GetProperty("steeringWheel").GetInt32()==1&&acc.GetProperty("rearWindow").GetInt32()==0&&acc.GetProperty("sideMirror").GetInt32()==0,"Steering wheel on; rear window and mirrors stay off");
            var seats=r.GetProperty("heatVentSeat");
            string S(string seat)=>$"{seats.GetProperty(seat).GetProperty("heatVentType").GetInt32()}{seats.GetProperty(seat).GetProperty("heatVentLevel").GetInt32()}{seats.GetProperty(seat).GetProperty("heatVentStep").GetInt32()}";
            Check(S("driverSeat")=="141"&&S("passengerSeat")=="223"&&S("rearLeftSeat")=="010"&&S("rearRightSeat")=="010","Seat codes match upstream (heat high, cool low, rear off)");
            Check(r.GetProperty("ignitionOnDuration").GetProperty("value").GetInt32()==5,"Five-minute run");
        }

        var (c3,f3)=await Session();
        f3.Add(Ok,("Xid","tx-3"));
        await c3.ExecuteAsync(new(ActionKind.Climate,70,true),null,default);
        Check(!f3.Calls[^1].Body.Contains("heatVentSeat"),"No seat block when no seat is chosen");
        int before=f3.Calls.Count;
        await Failure(()=>new KiaUsClient(new Fake()).ExecuteAsync(new(ActionKind.Lock),null,default));
        var (c4,f4)=await Session();before=f4.Calls.Count;
        await Failure(()=>c4.ExecuteAsync(new(ActionKind.Climate,90,true),null,default));
        await Failure(()=>c4.ExecuteAsync(new(ActionKind.HornLights),null,default));
        await Failure(()=>c4.ExecuteAsync(new(ActionKind.ChargeLimit,85),95,default));
        await Failure(()=>c4.SendDestinationAsync(new("Somewhere","",40,-73,""),default));
        await Failure(()=>c4.CaptureSurroundViewAsync(null,default));
        Check(f4.Calls.Count==before,"Out-of-range climate, horn, bad targets, send-to-car and Surround View send nothing");

        f4.Add(Ok,("Xid","tx-4"));
        await c4.ExecuteAsync(new(ActionKind.Climate,0,false),null,default);
        Check(f4.Calls[^1].Method=="GET"&&f4.Calls[^1].Path=="rems/stop","Climate stop is GET rems/stop");
        var (c5,f5)=await Session();
        f5.Add(Ok,("Xid","tx-5"));await c5.ExecuteAsync(new(ActionKind.Charge,0,true),null,default);
        Check(f5.Calls[^1].Path=="evc/charge"&&f5.Calls[^1].Body=="{\"chargeRatio\":100}","Charge start");
        var (c6,f6)=await Session();
        f6.Add(Ok,("Xid","tx-6"));await c6.ExecuteAsync(new(ActionKind.Charge,0,false),null,default);
        Check(f6.Calls[^1].Method=="GET"&&f6.Calls[^1].Path=="evc/cancel","Charge stop");
        var (c7,f7)=await Session();
        f7.Add(Ok,("Xid","tx-7"));await c7.ExecuteAsync(new(ActionKind.ChargeLimit,80),90,default);
        Check(f7.Calls[^1].Path=="evc/sts"&&f7.Calls[^1].Body=="{\"targetSOClist\":[{\"plugType\":0,\"targetSOClevel\":90},{\"plugType\":1,\"targetSOClevel\":80}]}","Charge targets: DC then AC");
        Check(KiaUsClient.Info is {OneTimeCode:true,NeedsPin:false,LightsAndHorn:false,SurroundView:false,SendToCar:false,FrontSeats:true},"Kia capabilities");
    }

    static async Task SessionRepair()
    {
        // A read whose session expired signs in again with the remember token, refreshes keys and reads once more.
        var (c,f)=await Session();
        f.Add(Expired);f.Add(Ok,("sid","sid-2"));f.Add(Vehicles.Replace("key-1","key-1b"));f.Add(Status);
        await c.ReadAsync(false,default);
        Check(f.Calls[^4].Path=="cmm/gvi"&&f.Calls[^3].Path=="prof/authUser"&&f.Calls[^3].Headers["rmtoken"]=="remember-1"&&f.Calls[^2].Path=="ownr/gvl","Expired read repairs the session");
        Check(f.Calls[^1].Path=="cmm/gvi"&&f.Calls[^1].Headers["sid"]=="sid-2"&&f.Calls[^1].Headers["vinkey"]=="key-1b"&&f.Calls[^1].Body.Contains("key-1b"),"The read is sent once more with the new session and key");

        // A command whose session expired is repaired but never resent.
        var (c2,f2)=await Session();
        f2.Add(Expired);f2.Add(Ok,("sid","sid-2"));f2.Add(Vehicles);
        var e=await Failure(()=>c2.ExecuteAsync(new(ActionKind.Unlock),null,default));
        Check(e.Message.Contains("nothing was sent")&&f2.Calls.Count(x=>x.Path=="rems/door/unlock")==1&&c2.Pending==null,"Expired command: renewed, not resent");

        // Without a remember token, an expired session ends.
        var (c3,f3)=await Session();c3.RememberToken="";
        f3.Add(Expired);
        var ended=await Failure(()=>c3.ReadAsync(false,default));
        Check(ended.Message.Contains("Sign in again")&&!c3.IsAuthenticated,"No remember token: sign in again");

        // Resuming a saved session reads the vehicle keys first.
        var (c4,_)=await Session();
        var saved=c4.ExportSession()!;
        Check(saved.RefreshToken=="remember-1"&&saved.AccessToken=="sid-1"&&saved.ToString()=="SharedSession [redacted]","Session export");
        var f5=new Fake();f5.Add(Vehicles);f5.Add(Status);
        var c5=new KiaUsClient(f5,null,Unlimited());c5.ResumeSession("tester@example.invalid","pw","",saved);
        await c5.ReadAsync(false,default);
        Check(f5.Calls[0].Path=="ownr/gvl"&&f5.Calls[1].Headers["vinkey"]=="key-1"&&c5.RememberToken=="remember-1","Resumed session fetches the vehicle key, then reads");
    }

    static void SavedLogin()
    {
        var kia=new SavedAccount("tester@example.invalid","pw","","remember-1","TEST-DEVICE");
        var back=SavedAccount.Decode(kia.Encode());
        Check(back.Pin==""&&back.RememberToken=="remember-1"&&back.DeviceId=="TEST-DEVICE","Saved Kia login keeps the remember token and device, no PIN");
        Check(!kia.ToString().Contains("remember-1")&&kia.ToString()=="SavedAccount [redacted]","Saved login text hides secrets");
        var old=SavedAccount.Decode("{\"version\":1,\"email\":\"a@example.invalid\",\"password\":\"pw\",\"pin\":\"1234\"}");
        Check(old.Pin=="1234"&&old.RememberToken==""&&old.DeviceId=="","A version 1 saved login still reads");
        var renewed=kia.WithRemember("remember-2","DEV-2");
        Check(renewed.RememberToken=="remember-2"&&renewed.DeviceId=="DEV-2"&&renewed.Password=="pw","New remember token keeps the login");
        bool refused=false;try{_=new SavedAccount("a@example.invalid","pw","12");}catch(VaultException){refused=true;}
        Check(refused,"A PIN must be empty or four digits");
    }

    static async Task Failures()
    {
        var (c,f)=await Session();
        f.Add("{\"status\":{\"statusCode\":1,\"errorType\":3,\"errorCode\":9200,\"errorMessage\":\"server detail sid-1\"}}");
        var e=await Failure(()=>c.ReadAsync(false,default));
        Check(e.ApiErrorCode==9200&&e.Message.Contains("Kia code 9200")&&!e.Message.Contains("server detail")&&!e.Message.Contains("sid-1"),"Kia errors keep the code, never the server text or secrets");

        var (c2,f2)=await Session();
        f2.Add("{}",HttpStatusCode.TooManyRequests);
        var limited=await Failure(()=>c2.ReadAsync(false,default));
        Check(limited.HttpStatus==429&&c2.Budget.HyundaiBlockedUntil>DateTimeOffset.UtcNow,"429 pauses requests");
        int calls=f2.Calls.Count;
        await Failure(()=>c2.ReadAsync(false,default));
        Check(f2.Calls.Count==calls,"Nothing is sent while rate limited");

        var (c3,f3)=await Session();
        f3.Replies.Enqueue(()=>throw new HttpRequestException("socket detail"));
        var lost=await c3.ExecuteAsync(new(ActionKind.Lock),null,default);
        Check(lost.Outcome==RemoteOutcome.Unknown&&f3.Calls.Count(x=>x.Path=="rems/door/lock")==1,"Lost connection: outcome unknown, no retry");
    }
}
