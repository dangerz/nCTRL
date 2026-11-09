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

// Hyundai Bluelink Canada / Kia Connect Canada protocol checks against a fake server. Field shapes follow upstream
// KiaUvoApiCA.py and BetterBlueKit's Canada parsing; every account value here is made up.
static class CanadaProtocolTests
{
    const string Email="driver@example.invalid",Password="pa\"ss-word",Pin="1234";
    const string Ok="""{"responseHeader":{"responseCode":0,"responseDesc":"Success"},"result":{}}""";
    const string Login="""{"responseHeader":{"responseCode":0},"result":{"token":{"accessToken":"access-1","refreshToken":"refresh-1","expireIn":3600}}}""";
    const string Login2="""{"responseHeader":{"responseCode":0},"result":{"token":{"accessToken":"access-2","refreshToken":"refresh-2","expireIn":3600}}}""";
    const string NeedCode="""{"responseHeader":{"responseCode":1},"error":{"errorCode":"7110","errorDesc":"OTP Required"}}""";
    const string Methods="""{"responseHeader":{"responseCode":0},"result":{"userInfoUuid":"uuid-1","emailList":["d****r@example.invalid"],"userPhone":"***-***-0000"}}""";
    const string Sent="""{"responseHeader":{"responseCode":0},"result":{"otpKey":"otp-key-1"}}""";
    const string Validated="""{"responseHeader":{"responseCode":0},"result":{"verifiedOtp":true,"otpValidationKey":"validation-1"}}""";
    const string Vehicles="""{"responseHeader":{"responseCode":0},"result":{"vehicles":[{"vehicleId":"veh-1","nickName":"NORTH","modelName":"IONIQ 5","modelYear":"2024","vin":"KMTEST00000000001","fuelKindCode":"E","dtcCount":0}]}}""";
    const string KiaVehicles="""{"responseHeader":{"responseCode":0},"result":{"vehicles":[{"vehicleId":"veh-9","nickName":"FAMILY","modelName":"EV9","modelYear":"2025","vin":"KNTEST00000000009","fuelKindCode":"E","dtcCount":0}]}}""";
    const string Status="""
    {"responseHeader":{"responseCode":0},"result":{"status":{
      "lastStatusDate":"20261108141440","doorLock":true,"airCtrlOn":false,"defrost":false,
      "airTemp":{"value":"10H","unit":0,"hvacTempType":1},"steerWheelHeat":0,
      "doorOpen":{"frontLeft":0,"frontRight":1,"backLeft":0,"backRight":0},"trunkOpen":false,"hoodOpen":true,
      "windowOpen":{"frontLeft":0,"frontRight":0,"backLeft":1,"backRight":0},
      "tirePressureLamp":{"tirePressureLampAll":1,"tirePressureLampFL":1,"tirePressureLampFR":0,"tirePressureLampRL":0,"tirePressureLampRR":0},
      "battery":{"batSoc":81},"washerFluidStatus":false,"breakOilStatus":false,
      "seatHeaterVentState":{"flSeatHeatState":8,"frSeatHeatState":3,"rlSeatHeatState":2,"rrSeatHeatState":2},
      "evStatus":{"batteryStatus":64,"batteryCharge":true,"batteryPlugin":1,"remainTime2":{"atc":{"value":95,"unit":1}},
        "drvDistance":[{"rangeByFuel":{"evModeRange":{"value":400.0,"unit":1},"totalAvailableRange":{"value":400.0,"unit":1}},"type":2}]}}}}
    """;
    const string Soc="""{"responseHeader":{"responseCode":0},"result":[{"plugType":0,"level":90},{"plugType":1,"level":80}]}""";
    const string PinOk="""{"responseHeader":{"responseCode":0},"result":{"pAuth":"pauth-1"}}""";
    static string Error(string code)=>$$$"""{"responseHeader":{"responseCode":1},"error":{"errorCode":"{{{code}}}","errorDesc":"secret server text"}}""";

    sealed record Captured(string Host,string Path,string Method,string Body,Dictionary<string,string> Headers);
    sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Replies=new();
        public readonly List<Captured> Calls=new();
        public void Add(string body,params (string,string)[] headers)=>Add(body,HttpStatusCode.OK,headers);
        public void Add(string body,HttpStatusCode status,params (string,string)[] headers)
        { Replies.Enqueue(()=>{var r=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};foreach(var (k,v) in headers)r.Headers.TryAddWithoutValidation(k,v);return r;}); }
        public int Count(string path)=>Calls.Count(c=>c.Path==path);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            if(r.RequestUri!.Host is not ("mybluelink.ca" or "kiaconnect.ca")||r.RequestUri.Scheme!="https"||!r.RequestUri.AbsolutePath.StartsWith("/tods/api/"))throw new Exception("Unexpected origin");
            var headers=r.Headers.ToDictionary(x=>x.Key,x=>string.Join(",",x.Value),StringComparer.OrdinalIgnoreCase);
            if(r.Content!=null)foreach(var h in r.Content.Headers)headers[h.Key]=string.Join(",",h.Value);
            Calls.Add(new(r.RequestUri.Host,r.RequestUri.AbsolutePath["/tods/api/".Length..],r.Method.Method,r.Content==null?"":await r.Content.ReadAsStringAsync(ct),headers));
            if(Replies.Count==0)throw new Exception("Unexpected request / retry");
            return Replies.Dequeue()();
        }
    }
    sealed class Clock : TimeProvider
    {
        DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan by)=>now+=by;
    }
    static int count;
    static readonly List<string> messages=new();
    static void Check(bool b,string name){if(!b)throw new Exception("Canada protocol: "+name);count++;}
    static async Task<BluelinkException> Failure(Func<Task> action)
    {try{await action();}catch(BluelinkException e){messages.Add(e.Message);return e;}throw new Exception("Canada protocol: expected a sanitized failure");}
    static RequestBudget Unlimited()=>new(new MemoryBudgetStore(),null,
        RequestBudget.Defaults.ToDictionary(x=>x.Key,x=>new RequestLimit(TimeSpan.Zero,int.MaxValue)));
    // A budget whose only limit is on one kind, and that kind was just used: the next one is refused.
    static RequestBudget Spent(RequestKind kind)
    {
        var b=new RequestBudget(new MemoryBudgetStore(),null,RequestBudget.Defaults.ToDictionary(x=>x.Key,x=>new RequestLimit(x.Key==kind?TimeSpan.FromHours(1):TimeSpan.Zero,int.MaxValue)));
        b.Acquire(kind);return b;
    }
    static JsonElement Json(string s){using var d=JsonDocument.Parse(s);return d.RootElement.Clone();}

    // Signs in (no code) and selects the only car.
    static async Task<(CanadaClient,Fake)> Session(CanadaBrand brand=CanadaBrand.Hyundai,TimeProvider? clock=null,RequestBudget? budget=null)
    {
        var f=new Fake();
        f.Add(Login);f.Add(brand==CanadaBrand.Kia?KiaVehicles:Vehicles);f.Add(Status);f.Add(Soc);
        var c=new CanadaClient(brand,f,clock,budget??Unlimited()){DeviceId="TEST-DEVICE"};
        var list=await c.LoginAsync(Email,Password,Pin,default);
        await c.SelectAsync(list[0],default);
        return (c,f);
    }

    public static async Task Run()
    {
        count=0;messages.Clear();
        Units();
        await SignIn();
        await OneTimeCode();
        await Reads();
        await Commands();
        await CommandStatus();
        await SessionRepair();
        await Unsupported();
        await Budgets();
        await Failures();
        foreach(var m in messages)
            Check(!m.Contains(Password)&&!m.Contains(Pin)&&!m.Contains("access-")&&!m.Contains("refresh-")&&!m.Contains("pauth-")&&
                !m.Contains("secret server text")&&!m.Contains("otp-key")&&!m.Contains("validation-"),"No error carries the password, PIN, a token or server text");
        Console.WriteLine($"PASS: {count} Canada protocol checks (fake server, no network).");
    }

    static void Units()
    {
        Check(CanadaClient.HexTemperature(72,2024)=="10H"&&CanadaClient.HexTemperature(72,2019)=="0CH","72°F is 22.0°C: 10H from MY2020, 0CH before");
        Check(CanadaClient.HexTemperature(62,2024)=="05H"&&CanadaClient.HexTemperature(82,2024)=="1CH","62°F and 82°F land on 16.5°C and 28.0°C");
        Check(CanadaClient.HexTemperature(71,2024)=="0FH","71°F is 21.5°C, as the app shows it");
        bool round=true;
        foreach(int year in new[]{2019,2024})for(int f=62;f<=82;f++)
            round&=CanadaClient.TemperatureF(Json($$$"""{"airTemp":{"value":"{{{CanadaClient.HexTemperature(f,year)}}}","unit":0}}"""),year)==f;
        Check(round,"Every setpoint reads back as the same °F, both scales");
        Check(CanadaClient.TemperatureF(Json("""{"airTemp":{"value":"0EH","unit":0}}"""),2019)==73&&CanadaClient.TemperatureF(Json("""{"airTemp":{"value":"LOW","unit":0}}"""),2024)==62,"Old scale (0EH is 23.0°C) and LOW");
        Check(CanadaClient.TemperatureF(Json("""{"airTemp":{"value":"OFF","unit":0}}"""),2024)==null&&CanadaClient.TemperatureF(Json("""{"airTemp":{"value":"10H","unit":1}}"""),2024)==null,"OFF and a hex value in °F are not guessed");
        var at=new DateTimeOffset(2026,11,8,14,20,0,TimeSpan.Zero);
        Check(CanadaClient.Reported("20261108141440",at)=="2026-11-08T14:14:40Z"&&CanadaClient.Reported("20261108141440",at.AddHours(5))=="20261108141440",
            "A compact time close to now is UTC; anything else stays as sent");
    }

    static async Task SignIn()
    {
        var (c,f)=await Session();
        var login=f.Calls[0];var list=f.Calls[1];
        Check(login.Host=="mybluelink.ca"&&login.Method=="POST"&&login.Path=="v2/login","Hyundai sign-in is POST mybluelink.ca/tods/api/v2/login");
        using(var body=JsonDocument.Parse(login.Body))
            Check(body.RootElement.GetProperty("loginId").GetString()==Email&&body.RootElement.GetProperty("password").GetString()==Password,"Sign-in body: loginId and password, escaped not altered");
        var h=login.Headers;
        Check(h["Deviceid"]=="TEST-DEVICE"&&h["from"]=="SPA"&&h["client_id"]=="HATAHSPACA0232141ED9722C67715A0B"&&h["client_secret"]=="CLISCR01AHSPA"&&
            h["language"]=="0"&&h["User-Agent"]=="okhttp/4.12.0"&&h.ContainsKey("offset")&&!h.ContainsKey("accessToken"),"Hyundai app headers with the device id, no token");
        Check(h["content-type"].StartsWith("application/json")&&!h.ContainsKey("Origin"),"JSON content type, no portal origin for Hyundai");
        Check(list.Path=="vhcllst"&&list.Method=="POST"&&list.Headers["accessToken"]=="access-1"&&list.Body==""&&!list.Headers.ContainsKey("Deviceid"),"Vehicle list is a bodiless POST with the token");
        Check(f.Calls.All(x=>x.Headers.Values.All(v=>!v.Contains(Password)))&&f.Calls.Skip(1).All(x=>!x.Body.Contains(Password)),"The password goes only in the sign-in body");
        var car=c.Selected!;
        Check(car.Id=="veh-1"&&car.Vin=="KMTEST00000000001"&&car.Name=="NORTH"&&car.Model=="IONIQ 5"&&car.Electric&&car.Generation==3,"One car parsed; fuelKindCode E is electric");
        Check(c.IsAuthenticated&&c.SessionValid&&c.RememberToken=="refresh-1"&&c.DeviceId=="TEST-DEVICE","Session, remember token and device id kept");
        Check(c.Service==CanadaClient.HyundaiInfo&&c.CarBrand==CanadaBrand.Hyundai,"Hyundai service");

        // Gas car: not electric.
        var f2=new Fake();f2.Add(Login);f2.Add(Vehicles.Replace("\"E\"","\"G\""));
        var gas=await new CanadaClient(CanadaBrand.Hyundai,f2,null,Unlimited()).LoginAsync(Email,Password,Pin,default);
        Check(!gas[0].Electric,"fuelKindCode G is not electric");

        var saved=c.ExportSession()!;
        Check(saved.AccessToken=="access-1"&&saved.RefreshToken=="refresh-1"&&saved.Vehicle.Id=="veh-1","Session export");
        var f3=new Fake();f3.Add(Vehicles);f3.Add(Status);f3.Add(Soc);
        var resumed=new CanadaClient(CanadaBrand.Hyundai,f3,null,Unlimited());resumed.ResumeSession(Email,Password,Pin,saved);
        var s=await resumed.ReadAsync(false,default);
        Check(f3.Calls[0].Path=="vhcllst"&&f3.Calls[1].Path=="lstvhclsts"&&f3.Calls[1].Headers["accessToken"]=="access-1"&&s.TemperatureF==72&&resumed.RememberToken=="refresh-1",
            "A resumed session learns the model year first, then reads");
    }

    static async Task OneTimeCode()
    {
        var f=new Fake();f.Add(NeedCode);f.Add(Methods);
        var c=new CanadaClient(CanadaBrand.Kia,f,null,Unlimited()){DeviceId="TEST-DEVICE"};
        CodeDestinations? where=null;
        try{await c.LoginAsync(Email,Password,Pin,default);}catch(OneTimeCodeRequiredException e){where=e.Destinations;}
        Check(where is {HasEmail:true,HasPhone:true}&&where.Email=="d****r@example.invalid"&&where.Phone=="***-***-0000","A code request reports Canada's destinations");
        Check(f.Calls[0].Host=="kiaconnect.ca"&&f.Calls[0].Path=="v2/login"&&f.Calls[1].Path=="mfa/selverifmeth","Kia: v2/login on kiaconnect.ca, then selverifmeth");
        var kh=f.Calls[0].Headers;
        Check(kh["from"]=="CWP"&&kh["Origin"]=="https://kiaconnect.ca"&&kh["Referer"]=="https://kiaconnect.ca/login"&&kh["offset"]=="-5"&&kh["User-Agent"].StartsWith("Mozilla/5.0")&&kh["Deviceid"]=="TEST-DEVICE","Kia web-portal headers");
        Check(f.Calls[1].Body=="{\"mfaApiCode\":\"0107\",\"userAccount\":\"driver@example.invalid\"}"&&f.Calls[1].Headers["Deviceid"]=="TEST-DEVICE","selverifmeth body and device id");
        Check(!c.IsAuthenticated,"No session yet");

        await Failure(()=>c.VerifyCodeAsync("123456",default));
        Check(f.Calls.Count==2,"Verifying before a code was sent sends nothing");
        f.Add(Sent);await c.SendCodeAsync(CodeChannel.Text,default);
        Check(f.Calls[^1].Path=="mfa/sendotp"&&f.Calls[^1].Body=="{\"otpMethod\":\"S\",\"mfaApiCode\":\"0107\",\"userAccount\":\"d****r@example.invalid\",\"userPhone\":\"***-***-0000\",\"userInfoUuid\":\"uuid-1\"}","Text code request");
        f.Add(Sent);await c.SendCodeAsync(CodeChannel.Email,default);
        Check(f.Calls[^1].Body.Contains("\"otpMethod\":\"E\"")&&f.Calls[^1].Body.Contains("\"userPhone\":\"\""),"Email code request");
        await Failure(()=>c.VerifyCodeAsync("12ab",default));
        Check(f.Calls.Count==4,"A malformed code sends nothing");

        f.Add(Error("7999"));
        var wrong=await Failure(()=>c.VerifyCodeAsync("654321",default));
        Check(wrong.Message.Contains("didn't accept that code")&&f.Calls[^1].Path=="mfa/validateotp","A wrong code is explained");

        f.Add(Validated);f.Add(Login2);f.Add(KiaVehicles);
        var list=await c.VerifyCodeAsync(" 123456 ",default);
        var validate=f.Calls[^3];var token=f.Calls[^2];var vehicles=f.Calls[^1];
        Check(validate.Body=="{\"otpNo\":\"123456\",\"userAccount\":\"driver@example.invalid\",\"otpKey\":\"otp-key-1\",\"mfaApiCode\":\"0107\"}","validateotp uses the email as typed");
        Check(token.Path=="mfa/genmfatkn"&&token.Body=="{\"userAccount\":\"driver@example.invalid\",\"otpEmail\":\"d****r@example.invalid\",\"mfaApiCode\":\"0107\",\"otpValidationKey\":\"validation-1\",\"mfaYn\":\"Y\"}","genmfatkn body");
        Check(vehicles.Path=="vhcllst"&&vehicles.Headers["accessToken"]=="access-2"&&list.Count==1&&list[0].Model=="EV9","Verified: session and vehicles");
        Check(c.RememberToken=="refresh-2"&&c.DeviceId=="TEST-DEVICE"&&c.IsAuthenticated,"Remember token and device id set");
        await Failure(()=>c.SendCodeAsync(CodeChannel.Text,default));
        Check(f.Calls.Count==8,"The code request is finished");

        // The next sign-in with the same device id and remember token: no code.
        var f2=new Fake();f2.Add(Login);f2.Add(KiaVehicles);
        var c2=new CanadaClient(CanadaBrand.Kia,f2,null,Unlimited()){DeviceId="TEST-DEVICE",RememberToken="refresh-2"};
        var again=await c2.LoginAsync(Email,Password,Pin,default);
        Check(again.Count==1&&f2.Calls.Count==2&&f2.Calls[0].Headers["Deviceid"]=="TEST-DEVICE","A remembered device signs in without a code");

        // No phone: only email is offered, and a text asks nothing.
        var f3=new Fake();f3.Add(NeedCode);f3.Add(Methods.Replace("\"***-***-0000\"","\"\"").Replace("[\"d****r@example.invalid\"]","[]"));
        var c3=new CanadaClient(CanadaBrand.Hyundai,f3,null,Unlimited());
        try{await c3.LoginAsync(Email,Password,Pin,default);}catch(OneTimeCodeRequiredException e){where=e.Destinations;}
        Check(where is {HasPhone:false,Email:Email},"Without a listed email, the email as typed; no phone");
        await Failure(()=>c3.SendCodeAsync(CodeChannel.Text,default));
        Check(f3.Calls.Count==2,"No text without a phone");
    }

    static async Task Reads()
    {
        var (c,f)=await Session();
        var read=f.Calls[2];var soc=f.Calls[3];
        Check(read.Path=="lstvhclsts"&&read.Method=="POST"&&read.Headers["accessToken"]=="access-1"&&read.Headers["vehicleId"]=="veh-1"&&read.Body=="","Cached status is lstvhclsts with token and vehicle id");
        Check(soc.Path=="evc/selsoc"&&soc.Headers["vehicleId"]=="veh-1","Charge targets from evc/selsoc");
        var s=c.State!;
        Check(s.Battery==64&&Math.Abs(s.RangeMiles!.Value-400/1.609344)<0.001,"Battery and range (400 km in miles)");
        Check(s.Locked==true&&s.ClimateOn==false&&s.TemperatureF==72&&s.Defrost==false&&s.SteeringWheelHeat==false,"Lock and climate (10H on MY2024 is 22.0°C, 72°F)");
        Check(s.PluggedIn==true&&s.Charging==true&&s.ChargeMinutesLeft==95&&s.AcLimit==80&&s.DcLimit==90,"Charging, minutes left and targets (plugType 1 AC, 0 DC)");
        Check(s.Health is {Battery12V:81,FrontRightDoor:true,FrontLeftDoor:false,Hood:true,Tailgate:false,RearLeftWindow:true,TireWarning:true,WasherFluidLow:false},"12 V, doors, hood, trunk, windows and tires");
        Check(s.DriverSeat==8&&s.PassengerSeat==3&&s.StatusLocation==null&&s.ReportedAt=="20261108141440","Seats; no location in the status; report time kept");
        Check(c.RawStatus?.GetProperty("doorLock").GetBoolean()==true,"Raw status kept for Details");

        f.Add(Status);f.Add(Soc);
        await c.ReadAsync(true,default);
        Check(f.Calls[^2].Path=="rltmvhclsts"&&f.Calls[^2].Headers["vehicleId"]=="veh-1"&&f.Calls[^1].Path=="evc/selsoc","Live read is rltmvhclsts");

        // A failing charge-target read keeps the status.
        f.Add(Status);f.Add(Error("9999"));
        var kept=await c.ReadAsync(false,default);
        Check(kept.Battery==64&&kept.AcLimit==null,"Charge targets unavailable: status still loads");

        f.Add(PinOk);f.Add("""{"responseHeader":{"responseCode":0},"result":{"coord":{"lat":45.5,"lon":-75.5,"alt":0},"head":90,"time":"20261108141000"}}""");
        var at=await c.LocateAsync(default);
        Check(f.Calls[^2].Path=="vrfypin"&&f.Calls[^1].Path=="fndmcr"&&f.Calls[^1].Headers["pAuth"]=="pauth-1"&&f.Calls[^1].Headers["from"]=="SPA"&&f.Calls[^1].Body=="{\"pin\":\"1234\"}","Location: PIN check, then fndmcr");
        Check(at.Latitude==45.5&&at.Longitude==-75.5&&at.Heading==90&&c.Location==at,"Location parsed");

        var gas=CanadaClient.ParseState(Json("""{"doorLock":false,"dte":{"value":160.9344,"unit":1},"airTemp":{"value":"OFF","unit":0}}"""),2024,DateTimeOffset.UnixEpoch);
        Check(Math.Abs(gas.RangeMiles!.Value-100)<0.001&&gas.Battery==null&&gas.TemperatureF==null,"Gas range from dte in km");
        var noStatus=await Failure(()=>Task.FromResult(CanadaClient.ParseState(Json("[]"),2024,DateTimeOffset.UnixEpoch)));
        Check(noStatus.Message.Contains("unsupported"),"A reply without status loads nothing");
    }

    static async Task Commands()
    {
        var (c,f)=await Session();
        f.Add(PinOk);f.Add(Ok,("transactionId","tx-1"));
        var receipt=await c.ExecuteAsync(new(ActionKind.Lock),null,default);
        var pin=f.Calls[^2];var lockCall=f.Calls[^1];
        Check(pin.Path=="vrfypin"&&pin.Body=="{\"pin\":\"1234\"}"&&pin.Headers["accessToken"]=="access-1"&&pin.Headers["vehicleId"]=="veh-1","PIN check before a command");
        Check(lockCall.Path=="drlck"&&lockCall.Headers["pAuth"]=="pauth-1"&&lockCall.Headers["vehicleId"]=="veh-1"&&lockCall.Body=="{\"pin\":\"1234\"}"&&f.Count("drlck")==1,"Lock: drlck with pAuth, once");
        Check(receipt.Outcome==RemoteOutcome.Accepted&&receipt.TransactionId=="tx-1"&&receipt.Message.StartsWith("Hyundai accepted"),"Accepted with Canada's transactionId");

        var (c2,f2)=await Session();
        f2.Add(PinOk);f2.Add(Ok,("transactionId","tx-2"));await c2.ExecuteAsync(new(ActionKind.Unlock),null,default);
        Check(f2.Calls[^1].Path=="drulck"&&f2.Count("drulck")==1,"Unlock: drulck");

        var (c3,f3)=await Session();
        f3.Add(PinOk);f3.Add(Ok,("transactionId","tx-3"));
        await c3.ExecuteAsync(new(ActionKind.Climate,72,true,new(SeatMode.HeatHigh,SeatMode.CoolLow,true,true)),null,default);
        var climate=f3.Calls[^1];
        Check(climate.Path=="evc/rfon"&&climate.Headers["pAuth"]=="pauth-1"&&f3.Count("evc/rfon")==1,"Climate start: evc/rfon, once");
        using(var body=JsonDocument.Parse(climate.Body)) {
            var b=body.RootElement;var h=b.GetProperty("hvacInfo");
            Check(b.GetProperty("pin").GetString()==Pin&&h.GetProperty("airCtrl").GetInt32()==1&&h.GetProperty("defrost").GetBoolean()&&h.GetProperty("heating1").GetInt32()==3,"PIN, air, defrost and steering wheel");
            var t=h.GetProperty("airTemp");
            Check(t.GetProperty("value").GetString()=="10H"&&t.GetProperty("unit").GetInt32()==0&&t.GetProperty("hvacTempType").GetInt32()==1,"72°F sent as 22.0°C (10H on MY2024)");
            var seats=h.GetProperty("seatHeaterVentCMD");
            Check(seats.GetProperty("drvSeatOptCmd").GetInt32()==8&&seats.GetProperty("astSeatOptCmd").GetInt32()==3&&seats.GetProperty("rlSeatOptCmd").GetInt32()==0&&
                seats.GetProperty("rrSeatOptCmd").GetInt32()==0&&h.GetProperty("igniOnDuration").GetInt32()==5,"Seat codes (heat high 8, cool low 3, rear off) and five minutes");
        }

        var (c4,f4)=await Session();
        f4.Add(PinOk);f4.Add(Ok,("transactionId","tx-4"));await c4.ExecuteAsync(new(ActionKind.Climate,0,false),null,default);
        Check(f4.Calls[^1].Path=="evc/rfoff"&&f4.Calls[^1].Body=="{\"pin\":\"1234\"}","Climate stop: evc/rfoff");
        var (c5,f5)=await Session();
        f5.Add(PinOk);f5.Add(Ok,("transactionId","tx-5"));await c5.ExecuteAsync(new(ActionKind.Charge,0,true),null,default);
        Check(f5.Calls[^1].Path=="evc/rcstrt"&&f5.Calls[^1].Body=="{\"pin\":\"1234\"}","Charge start: evc/rcstrt");
        var (c6,f6)=await Session();
        f6.Add(PinOk);f6.Add(Ok,("transactionId","tx-6"));await c6.ExecuteAsync(new(ActionKind.Charge,0,false),null,default);
        Check(f6.Calls[^1].Path=="evc/rcstp","Charge stop: evc/rcstp");
        var (c7,f7)=await Session();
        f7.Add(PinOk);f7.Add(Ok,("transactionId","tx-7"));await c7.ExecuteAsync(new(ActionKind.ChargeLimit,80),90,default);
        Check(f7.Calls[^1].Path=="evc/setsoc"&&f7.Calls[^1].Body=="{\"tsoc\":[{\"plugType\":0,\"level\":90},{\"plugType\":1,\"level\":80}],\"pin\":\"1234\"}"&&
            f7.Calls[^1].Headers["from"]=="SPA"&&!f7.Calls[^1].Headers.ContainsKey("priority"),"Charge targets: evc/setsoc, DC then AC, app channel");

        // Kia: portal headers, setsoc extras, and EV9 climate under remoteControl.
        var (k,kf)=await Session(CanadaBrand.Kia);
        Check(kf.Calls.All(x=>x.Host=="kiaconnect.ca")&&k.Service==CanadaClient.KiaInfo,"Every Kia request goes to kiaconnect.ca");
        kf.Add(PinOk);kf.Add(Ok,("transactionId","tx-8"));await k.ExecuteAsync(new(ActionKind.ChargeLimit,80),100,default);
        var set=kf.Calls[^1].Headers;
        Check(set["from"]=="SPA"&&set["offset"]=="-8"&&set["priority"]=="u=1, i"&&set["Referer"]=="https://kiaconnect.ca/remote/","Kia setsoc headers");
        var (k2,kf2)=await Session(CanadaBrand.Kia);
        kf2.Add(PinOk);kf2.Add(Ok,("transactionId","tx-9"));await k2.ExecuteAsync(new(ActionKind.Climate,72,true),null,default);
        using(var body=JsonDocument.Parse(kf2.Calls[^1].Body))
            Check(body.RootElement.TryGetProperty("remoteControl",out var rc)&&!body.RootElement.TryGetProperty("hvacInfo",out _)&&rc.GetProperty("airTemp").GetProperty("value").GetString()=="10H"&&
                kf2.Calls[^1].Headers["from"]=="CWP","EV9 climate under remoteControl");

        // A refused command: outcome unknown, not resent.
        var (c8,f8)=await Session();
        f8.Add(PinOk);f8.Add(Error("6533"));
        var refused=await Failure(()=>c8.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(refused.ApiErrorCode==6533&&c8.Pending?.Outcome==RemoteOutcome.Unknown&&f8.Count("drlck")==1,"Refused: unknown, once");
        // Lost connection: unknown, not resent.
        var (c9,f9)=await Session();
        f9.Add(PinOk);f9.Replies.Enqueue(()=>throw new HttpRequestException("socket detail"));
        var lost=await c9.ExecuteAsync(new(ActionKind.Unlock),null,default);
        Check(lost.Outcome==RemoteOutcome.Unknown&&f9.Count("drulck")==1,"Lost connection: unknown, no retry");
        // A wrong PIN: explained, nothing sent to the car.
        var (c10,f10)=await Session();
        f10.Add(Error("7312"));
        var wrongPin=await Failure(()=>c10.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(wrongPin.Message.Contains("didn't accept the four-digit PIN")&&wrongPin.Message.Contains("Nothing was sent")&&f10.Count("drlck")==0&&c10.Pending==null,"Wrong PIN: explained, no command");
        f10.Add(Error("7316"));
        var locked=await Failure(()=>c10.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(locked.Message.Contains("locked")&&f10.Count("drlck")==0,"Locked PIN explained");
    }

    static async Task CommandStatus()
    {
        var (c,f)=await Session();
        f.Add(PinOk);f.Add(Ok,("transactionId","tx-1"));
        await c.ExecuteAsync(new(ActionKind.Lock),null,default);
        f.Add(PinOk);f.Add("""{"responseHeader":{"responseCode":0},"result":{"transaction":{"apiResult":"P","apiStatusCode":"null"}}}""");
        var pending=await c.CheckCommandAsync(default);
        Check(f.Calls[^1].Path=="rmtsts"&&f.Calls[^1].Headers["transactionId"]=="tx-1"&&f.Calls[^1].Headers["pAuth"]=="pauth-1"&&f.Calls[^2].Path=="vrfypin","rmtsts with transactionId and a fresh pAuth");
        Check(pending.Outcome==RemoteOutcome.Accepted,"P is pending");
        f.Add(PinOk);f.Add("""{"responseHeader":{"responseCode":0},"result":{"transaction":{"apiResult":"C"}}}""");
        Check((await c.CheckCommandAsync(default)).Outcome==RemoteOutcome.Confirmed&&f.Count("drlck")==1,"C is confirmed; nothing resent");
        var (c2,f2)=await Session();
        f2.Add(PinOk);f2.Add(Ok,("transactionId","tx-2"));await c2.ExecuteAsync(new(ActionKind.Unlock),null,default);
        f2.Add(PinOk);f2.Add("""{"responseHeader":{"responseCode":0},"result":{"transaction":{"apiResult":"F"}}}""");
        Check((await c2.CheckCommandAsync(default)).Outcome==RemoteOutcome.Failed,"Another result is a failure (upstream)");
        // Without a transactionId there's nothing to ask.
        var (c3,f3)=await Session();
        f3.Add(PinOk);f3.Add(Ok);
        var untracked=await c3.ExecuteAsync(new(ActionKind.Lock),null,default);
        int calls=f3.Calls.Count;
        Check(untracked.TransactionId==null&&(await c3.CheckCommandAsync(default)).Message.Contains("No tracking ID")&&f3.Calls.Count==calls,"No tracking ID: nothing asked");
    }

    static async Task SessionRepair()
    {
        // A read whose token Canada deleted (7602) signs in again with the same device id and reads once more.
        var (c,f)=await Session();
        f.Add(Error("7602"));f.Add(Login2);f.Add(Status);f.Add(Soc);
        await c.ReadAsync(false,default);
        Check(f.Calls[^4].Path=="lstvhclsts"&&f.Calls[^3].Path=="v2/login"&&f.Calls[^3].Headers["Deviceid"]=="TEST-DEVICE"&&f.Calls[^3].Body.Contains("loginId"),"Deleted token: signs in again");
        Check(f.Calls[^2].Path=="lstvhclsts"&&f.Calls[^2].Headers["accessToken"]=="access-2"&&c.RememberToken=="refresh-2","The read is sent once more with the new token");
        // 7606: the phone switched networks. Same repair.
        f.Add(Error("7606"));f.Add(Login);f.Add(Status);f.Add(Soc);
        await c.ReadAsync(false,default);
        Check(f.Calls[^3].Path=="v2/login"&&f.Calls[^2].Headers["accessToken"]=="access-1","Network switch (7606): signs in again for reads");
        // Repaired once only.
        f.Add(Error("7403"));f.Add(Login2);f.Add(Error("7403"));
        var twice=await Failure(()=>c.ReadAsync(false,default));
        Check(f.Count("v2/login")==4&&twice.Message.Contains("Sign in again"),"A second session error is not repaired again");

        // An expired token is replaced before the request.
        var clock=new Clock();
        var (c2,f2)=await Session(clock:clock);
        clock.Advance(TimeSpan.FromMinutes(59.5));
        Check(!c2.SessionValid,"Session ends a minute before Canada's expiry");
        f2.Add(Login2);f2.Add(Status);f2.Add(Soc);
        await c2.ReadAsync(false,default);
        Check(f2.Calls[^3].Path=="v2/login"&&f2.Calls[^2].Headers["accessToken"]=="access-2","Expired: signs in again first");

        // A command whose token expired is repaired but never resent.
        var (c3,f3)=await Session();
        f3.Add(PinOk);f3.Add(Error("7403"));f3.Add(Login2);
        var e=await Failure(()=>c3.ExecuteAsync(new(ActionKind.Unlock),null,default));
        Check(e.Message.Contains("nothing was sent")&&f3.Count("drulck")==1&&c3.Pending==null&&f3.Calls[^1].Path=="v2/login","Expired command: renewed, not resent");
        // Expired at the PIN check: renewed, the command was never sent.
        var (c4,f4)=await Session();
        f4.Add(Error("7602"));f4.Add(Login2);
        var atPin=await Failure(()=>c4.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(atPin.Message.Contains("nothing was sent")&&f4.Count("drlck")==0&&f4.Count("vrfypin")==1,"Expired at the PIN check: renewed, no command");
        // 7406 after a command: may be a session, not proof nothing ran. Unknown, renewed on the next request.
        var (c5,f5)=await Session();
        f5.Add(PinOk);f5.Add(Error("7406"));
        var maybe=await Failure(()=>c5.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(c5.Pending?.Outcome==RemoteOutcome.Unknown&&f5.Count("drlck")==1&&f5.Count("v2/login")==1&&maybe.Message.Contains("nothing was resent"),"7406 command: unknown, not resent");
        f5.Add(Login2);f5.Add(Status);f5.Add(Soc);
        await c5.ReadAsync(false,default);
        Check(f5.Calls[^3].Path=="v2/login"&&f5.Count("drlck")==1,"Next read renews the session first");

        // Signing in again would need a code: stop, ask to sign in.
        var (c6,f6)=await Session();
        f6.Add(Error("7403"));f6.Add(NeedCode);f6.Add(Methods);
        var code=await Failure(()=>c6.ReadAsync(false,default));
        Check(code.Message.Contains("new one-time code")&&!c6.IsAuthenticated,"Repair needing a code: sign in again");
    }

    static async Task Unsupported()
    {
        var (c,f)=await Session();int before=f.Calls.Count;
        await Failure(()=>c.ExecuteAsync(new(ActionKind.HornLights),null,default));
        await Failure(()=>c.ExecuteAsync(new(ActionKind.Lights),null,default));
        await Failure(()=>c.ExecuteAsync(new(ActionKind.Climate,90,true),null,default));
        await Failure(()=>c.ExecuteAsync(new(ActionKind.ChargeLimit,85),95,default));
        await Failure(()=>c.ExecuteAsync(new(ActionKind.ChargeLimit,80),null,default));
        var send=await Failure(()=>c.SendDestinationAsync(new("Somewhere","",45,-75,""),default));
        var svm=await Failure(()=>c.CaptureSurroundViewAsync(null,default));
        Check(f.Calls.Count==before&&send.Message.Contains("Nothing was sent")&&svm.Message.Contains("Nothing was sent"),"Horn, lights, bad climate, bad targets, send-to-car and Surround View send nothing");
        var f2=new Fake();f2.Add(Login);f2.Add(Vehicles.Replace("\"E\"","\"G\""));f2.Add(Status);
        var gas=new CanadaClient(CanadaBrand.Hyundai,f2,null,Unlimited());
        await gas.SelectAsync((await gas.LoginAsync(Email,Password,Pin,default))[0],default);
        before=f2.Calls.Count;
        await Failure(()=>gas.ExecuteAsync(new(ActionKind.Climate,72,true),null,default));
        await Failure(()=>gas.ExecuteAsync(new(ActionKind.Charge,0,true),null,default));
        Check(f2.Calls.Count==before&&f2.Count("evc/selsoc")==0,"Gas car: no EV climate or charging, no charge-target read");
        await Failure(()=>new CanadaClient(CanadaBrand.Kia,new Fake()).ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(CanadaClient.HyundaiInfo is {ServiceName:"Bluelink Canada",Maker:"Hyundai",NeedsPin:true,OneTimeCode:true,SendToCar:false,SurroundView:false,LightsAndHorn:false,RequestLocation:true}&&
            CanadaClient.KiaInfo is {ServiceName:"Kia Connect Canada",Maker:"Kia",NeedsPin:true,OneTimeCode:true,FrontSeats:true},"Canada capabilities");
    }

    static async Task Budgets()
    {
        // Normal limits: a second read within the cooldown is refused before anything is sent.
        var (c,f)=await Session(budget:new RequestBudget(new MemoryBudgetStore()));
        int before=f.Calls.Count;
        bool refused=false;try{await c.ReadAsync(false,default);}catch(RequestLimitException){refused=true;}
        Check(refused&&f.Calls.Count==before,"A read inside the cooldown sends nothing");
        // No commands left: nothing at all is sent, not even the PIN check.
        var noCommands=Spent(RequestKind.Command);
        var (c2,f2)=await Session(budget:noCommands);before=f2.Calls.Count;
        refused=false;try{await c2.ExecuteAsync(new(ActionKind.Unlock),null,default);}catch(RequestLimitException){refused=true;}
        Check(refused&&f2.Calls.Count==before,"A refused command sends nothing");
        var noSignIn=Spent(RequestKind.SignIn);
        var f3=new Fake();
        await Failure(()=>new CanadaClient(CanadaBrand.Hyundai,f3,null,noSignIn).LoginAsync(Email,Password,Pin,default));
        Check(f3.Calls.Count==0,"A refused sign-in sends nothing");
        var noLocation=Spent(RequestKind.Location);
        var (c4,f4)=await Session(budget:noLocation);before=f4.Calls.Count;
        await Failure(()=>c4.LocateAsync(default));
        Check(f4.Calls.Count==before&&!c4.CanRequestLocation,"A refused location sends nothing");
        // 429 pauses everything.
        var (c5,f5)=await Session();
        f5.Add("{}",HttpStatusCode.TooManyRequests);
        var limited=await Failure(()=>c5.ReadAsync(false,default));
        Check(limited.HttpStatus==429&&c5.Budget.HyundaiBlockedUntil>DateTimeOffset.UtcNow,"429 pauses requests");
        before=f5.Calls.Count;
        await Failure(()=>c5.ExecuteAsync(new(ActionKind.Lock),null,default));
        Check(f5.Calls.Count==before,"Nothing is sent while rate limited");
    }

    static async Task Failures()
    {
        var f=new Fake();f.Add(Error("7404"));
        var c=new CanadaClient(CanadaBrand.Hyundai,f,null,Unlimited());
        var wrong=await Failure(()=>c.LoginAsync(Email,Password,Pin,default));
        Check(wrong.Message.Contains("didn't accept that email and password")&&wrong.ApiErrorCode==7404&&!c.IsAuthenticated,"Wrong password explained, server text withheld");
        var f2=new Fake();f2.Add(Error("7402"));
        var locked=await Failure(()=>new CanadaClient(CanadaBrand.Kia,f2,null,Unlimited()).LoginAsync(Email,Password,Pin,default));
        Check(locked.Message.Contains("locked")&&locked.Message.Contains("kiaconnect.ca"),"Locked account explained");
        var f3=new Fake();
        await Failure(()=>new CanadaClient(CanadaBrand.Hyundai,f3,null,Unlimited()).LoginAsync(Email,Password,"12",default));
        await Failure(()=>new CanadaClient(CanadaBrand.Hyundai,f3,null,Unlimited()).LoginAsync("",Password,Pin,default));
        Check(f3.Calls.Count==0,"A missing email or a bad PIN is refused before any request");
        var f4=new Fake();f4.Add("<html>blocked</html>",HttpStatusCode.Forbidden);
        var blocked=await Failure(()=>new CanadaClient(CanadaBrand.Hyundai,f4,null,Unlimited()).LoginAsync(Email,Password,Pin,default));
        Check(blocked.HttpStatus==403&&!blocked.Message.Contains("blocked"),"An HTML block page is reported by status only");

        var (c5,f5)=await Session();
        f5.Add(Error("9200"));
        var e=await Failure(()=>c5.ReadAsync(false,default));
        Check(e.ApiErrorCode==9200&&e.Message.Contains("Hyundai code 9200"),"Canada errors keep the code, never the server text");
        f5.Add("not json");
        var odd=await Failure(()=>c5.ReadAsync(false,default));
        Check(odd.Message.Contains("Unsupported response"),"Malformed replies are refused");
    }
}
