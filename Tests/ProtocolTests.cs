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

static class ProtocolTests
{
    const string Token="{\"access_token\":\"test-access\",\"refresh_token\":\"test-refresh\",\"expires_in\":3600}";
    const string Enrollment="{\"enrolledVehicleDetails\":[{\"vehicleDetails\":{\"regid\":\"reg-1\",\"vin\":\"TESTVIN0000000001\",\"nickName\":\"Test EV\",\"modelCode\":\"IONIQ5N\",\"vehicleGeneration\":\"3\",\"evStatus\":\"E\"}}]}";
    const string Status="""
    {"vehicleStatus":{"dateTime":"2026-10-22T10:00:00Z","doorLock":true,"airCtrlOn":false,
      "airTemp":{"value":"72","unit":1},"evStatus":{"batteryStatus":78,"batteryCharge":false,"batteryPlugin":2,
      "drvDistance":[{"rangeByFuel":{"totalAvailableRange":{"value":160.9344,"unit":1}}}],
      "reservChargeInfos":{"targetSOClist":[{"plugType":0,"targetSOClevel":90},{"plugType":1,"targetSOClevel":80}]}}}}
    """;
    sealed record Captured(string Path,string Method,string Body,Dictionary<string,string> Headers);
    sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Replies=new();
        public readonly List<Captured> Calls=new();
        public void Add(string body,HttpStatusCode status=HttpStatusCode.OK,string? tid=null)
        { Replies.Enqueue(()=>{var r=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};if(tid!=null)r.Headers.Add("tmsTid",tid);return r;}); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            if(r.RequestUri!.Host!="api.telematics.hyundaiusa.com"||r.RequestUri.Scheme!="https")throw new Exception("Unexpected origin");
            Calls.Add(new(r.RequestUri.AbsolutePath,r.Method.Method,r.Content==null?"":await r.Content.ReadAsStringAsync(ct),r.Headers.ToDictionary(x=>x.Key,x=>string.Join(",",x.Value),StringComparer.OrdinalIgnoreCase)));
            Calls[^1].Headers["user-agent"]=r.Headers.UserAgent.ToString();
            if(Replies.Count==0)throw new Exception("Unexpected request / retry");return Replies.Dequeue()();
        }
    }
    static int count;
    static void Check(bool b,string name){if(!b)throw new Exception("Protocol: "+name);count++;}
    static async Task Reject(Func<Task> f,string name){try{await f();}catch(BluelinkException){count++;return;}throw new Exception("Expected rejection: "+name);}
    static async Task<BluelinkException> Failure(Func<Task> action)
    {try{await action();}catch(BluelinkException e){return e;}throw new Exception("Expected sanitized failure");}
    static async Task<(HyundaiUsClient,Fake)> Session()
    {
        var f=new Fake();f.Add(Token);f.Add(Enrollment);f.Add(Status);
        var c=new HyundaiUsClient(f);
        var list=await c.LoginAsync("tester@example.invalid","test-password","1234",default);
        Check(list.Count==1&&list[0].Electric&&list[0].Generation==3,"Enrollment parsing");
        await c.SelectAsync(list[0],default);return(c,f);
    }
    private static async Task MobilePayloadsAndFailures()
    {
        const string secret="quote\" slash\\ newline\n日本語";
        var f=new Fake();f.Add(Token);f.Add(Enrollment.Replace("\"3\"","\"2\""));f.Add(Status);f.Add("");
        using(var client=new HyundaiUsClient(f)) {
            var vehicles=await client.LoginAsync("tester@example.invalid",secret,"1234",default);
            using var json=JsonDocument.Parse(f.Calls[0].Body);
            Check(json.RootElement.GetProperty("password").GetString()==secret,"JSON preserves password Unicode and escaping");
            Check(json.RootElement.EnumerateObject().Count()==2,"Initial auth sends only username/password");
            await client.SelectAsync(vehicles[0],default);
            await client.ExecuteAsync(new(ActionKind.Climate,72,true),null,default);
            using var climate=JsonDocument.Parse(f.Calls[^1].Body);
            Check(!climate.RootElement.TryGetProperty("igniOnDuration",out _)&&!climate.RootElement.TryGetProperty("seatHeaterVentInfo",out _),"Generation 2 omits generation 3 climate fields");
            Check(!climate.RootElement.GetProperty("defrost").GetBoolean()&&climate.RootElement.GetProperty("heating1").GetInt32()==0,"Climate heater defaults preserved");
        }
        foreach(bool enrollmentFailure in new[]{false,true})
        foreach(Exception failure in new Exception[]{new InvalidOperationException(secret),new PlatformNotSupportedException(secret),new TypeInitializationException(secret,new Exception(secret)),new Exception(secret)}) {
            f=new Fake();if(enrollmentFailure)f.Add(Token);f.Replies.Enqueue(()=>throw failure);
            using var client=new HyundaiUsClient(f);
            var error=await Failure(()=>client.LoginAsync("tester@example.invalid",secret,"1234",default));
            Check(error.Message.StartsWith(enrollmentFailure?"Vehicle list:":"Authentication:")&&!error.ToString().Contains(secret)&&error.InnerException==null,"Unexpected runtime error keeps stage and withholds private details");
            Check(!client.IsAuthenticated&&f.Calls.Count==(enrollmentFailure?2:1),"Runtime error clears session without retries");
            f.Add(Token);f.Add(Enrollment);
            Check((await client.LoginAsync("tester@example.invalid",secret,"1234",default)).Count==1,"Manual recovery after runtime error");
        }
        Check(SafeFailure.Describe(new InvalidOperationException(secret)).Contains("runtime-state")&&SafeFailure.Describe(new PlatformNotSupportedException(secret)).Contains("platform-unsupported"),"Runtime diagnostics identify safe error categories");
    }
    public static async Task Run()
    {
        Check(!JsonSerializer.IsReflectionEnabledByDefault,"Tests enforce mobile JSON reflection restrictions");
        await MobilePayloadsAndFailures();
        var (c,f)=await Session();using(c)
        {
            Check(f.Calls[0].Path=="/v2/ac/oauth/token"&&f.Calls[0].Method=="POST","Login endpoint");
            using var login=JsonDocument.Parse(f.Calls[0].Body);
            Check(login.RootElement.GetProperty("password").GetString()=="test-password","Login payload");
            Check(!f.Calls[0].Headers.ContainsKey("accessToken"),"No auth header on login");
            Check(f.Calls[2].Headers["registrationId"]=="reg-1"&&f.Calls[2].Headers["gen"]=="3"&&f.Calls[2].Headers["blueLinkServicePin"]=="1234","Vehicle headers");
            Check(c.State?.RangeMiles is double r&&Math.Abs(r-100)<.01,"Kilometres to miles");
            Check(c.State?.Battery==78&&c.State.PluggedIn==true&&c.State.AcLimit==80&&c.State.DcLimit==90,"EV status parsing");
            var initial=c.State;f.Add("",tid:"transaction-1");
            var receipt=await c.ExecuteAsync(new(ActionKind.Unlock),null,default);
            Check(f.Calls[^1].Path.EndsWith("/rcs/rdo/on")&&f.Calls[^1].Headers["APPCLOUD-VIN"]=="TESTVIN0000000001","Unlock route and VIN");
            using var doors=JsonDocument.Parse(f.Calls[^1].Body);
            Check(doors.RootElement.GetProperty("userName").GetString()=="tester@example.invalid"&&doors.RootElement.GetProperty("vin").GetString()=="TESTVIN0000000001","Door payload in reflection-disabled runtime");
            Check(receipt.Outcome==RemoteOutcome.Accepted&&c.State==initial,"Acceptance never changes vehicle state");
            Check(receipt.Message.Contains("HTTP 200")&&receipt.Message.Contains("completion pending")&&!receipt.Message.Contains("transaction-1"),"Command receipt displays HTTP acceptance and pending completion without tracking secrets");
            var calls=f.Calls.Count;await Reject(()=>c.ExecuteAsync(new(ActionKind.Unlock),null,default),"Duplicate command");Check(f.Calls.Count==calls,"Duplicate not submitted");
            f.Add("{\"status\":\"SUCCESS\"}");receipt=await c.CheckCommandAsync(default);
            Check(receipt.Outcome==RemoteOutcome.Confirmed&&c.State==initial,"Completion does not fabricate new state");
            Check(f.Calls[^1].Headers["tid"]=="transaction-1"&&f.Calls[^1].Headers["service_type"]=="REMOTE_POLL","Tracking headers");
        }
        (c,f)=await Session();using(c)
        {
            f.Add("{}");await c.ExecuteAsync(new(ActionKind.Lock),null,default);
            Check(f.Calls[^1].Path.EndsWith("/rcs/rdo/off"),"Lock endpoint");
            Check(c.Pending?.TransactionId==null&&c.Pending?.Outcome==RemoteOutcome.Accepted,"Empty tracking ID supported");
            Check(c.Pending!.Message.Contains("completion unverified"),"Untracked acceptance does not claim car response");
        }
        (c,f)=await Session();using(c)
        {
            f.Add("");await c.ExecuteAsync(new(ActionKind.Climate,75,true),null,default);
            using var p=JsonDocument.Parse(f.Calls[^1].Body);var root=p.RootElement;
            Check(f.Calls[^1].Path.EndsWith("/evc/fatc/start")&&root.GetProperty("airTemp").GetProperty("value").GetString()=="75","EV climate route and temperature");
            Check(root.GetProperty("igniOnDuration").GetInt32()==5&&root.GetProperty("airTemp").GetProperty("unit").GetInt32()==1,"Generation 3 duration and Fahrenheit");
        }
        foreach(var cmd in new[]{new VehicleCommand(ActionKind.Climate,72,false),new VehicleCommand(ActionKind.Charge,Enabled:true),new VehicleCommand(ActionKind.Charge,Enabled:false)})
        {
            (c,f)=await Session();using(c){f.Add("");await c.ExecuteAsync(cmd,null,default);
                string route=cmd.Kind==ActionKind.Climate?"evc/fatc/stop":cmd.Enabled?"evc/charge/start":"evc/charge/stop";
                Check(f.Calls[^1].Path.EndsWith(route)&&f.Calls[^1].Body=="","Bodyless EV command "+route);}
        }
        (c,f)=await Session();using(c)
        {
            int n=f.Calls.Count;await Reject(()=>c.ExecuteAsync(new(ActionKind.ChargeLimit,80),null,default),"Unknown DC");
            Check(f.Calls.Count==n,"Missing DC never sent");
            f.Add("");await c.ExecuteAsync(new(ActionKind.ChargeLimit,80),90,default);
            using var p=JsonDocument.Parse(f.Calls[^1].Body);var list=p.RootElement.GetProperty("targetSOClist");
            Check(list[0].GetProperty("plugType").GetInt32()==0&&list[0].GetProperty("targetSOClevel").GetInt32()==90&&list[1].GetProperty("targetSOClevel").GetInt32()==80,"AC and DC preserved separately");
        }
        (c,f)=await Session();using(c)
        {
            var state=c.State;f.Add("{\"errorCode\":400,\"errorMessage\":\"sensitive test payload\"}");
            try{await c.ExecuteAsync(new(ActionKind.Unlock),null,default);throw new Exception("Error accepted");}
            catch(BluelinkException e){Check(!e.Message.Contains("sensitive"),"Server body not exposed");}
            Check(c.State==state&&f.Replies.Count==0,"API error preserves state and no retry");
        }
        (c,f)=await Session();using(c)
        {
            f.Add("",HttpStatusCode.Unauthorized);
            await Reject(()=>c.ExecuteAsync(new(ActionKind.Lock),null,default),"Unauthorized");
            Check(!c.IsAuthenticated,"401 invalidates session");
        }
        (c,f)=await Session();using(c)
        {
            f.Add("",HttpStatusCode.TooManyRequests);await Reject(()=>c.LocateAsync(default),"Rate limit");
            int calls=f.Calls.Count;await Reject(()=>c.ExecuteAsync(new(ActionKind.Lock),null,default),"Rate limit enforced");
            Check(f.Calls.Count==calls,"No HTTP during backoff");
        }
        (c,f)=await Session();using(c)
        {
            f.Replies.Enqueue(()=>throw new HttpRequestException("fake connection loss"));
            var receipt=await c.ExecuteAsync(new(ActionKind.Lock),null,default);
            Check(receipt.Outcome==RemoteOutcome.Unknown,"Transport loss is unknown, not failure or success");
        }
        (c,f)=await Session();using(c)
        {
            f.Add("{\"coord\":{\"lat\":38.5,\"lon\":-76.5},\"time\":\"20261022120000\"}");
            var loc=await c.LocateAsync(default);Check(loc.Latitude==38.5&&loc.Longitude==-76.5,"Location parsing");
            await Reject(()=>c.LocateAsync(default),"Location cooldown");
        }
        using(var json=JsonDocument.Parse("{\"vehicleStatus\":{}}"))
        {
            var s=HyundaiUsClient.ParseState(json.RootElement);
            Check(s.Locked==null&&s.Battery==null&&s.RangeMiles==null&&s.AcLimit==null&&s.ClimateOn==null,"Missing fields remain unknown");
        }
        using(var json=JsonDocument.Parse("{\"vehicleStatus\":{\"evStatus\":{\"drvDistance\":[{\"rangeByFuel\":{\"totalAvailableRange\":{\"value\":184,\"unit\":2}}}]}}}"))
            Check(HyundaiUsClient.ParseState(json.RootElement).RangeMiles==184,"Alternate mile unit");
        using(var json=JsonDocument.Parse("{\"vehicleStatus\":{\"doorLock\":\"0\",\"evStatus\":{\"batteryStatus\":\"200\",\"batteryPlugin\":\"false\",\"batteryCharge\":\"true\"}}}"))
        {
            var s=HyundaiUsClient.ParseState(json.RootElement);Check(s.Locked==false&&s.Battery==null&&s.PluggedIn==false&&s.Charging==true,"String booleans and invalid SOC");
        }
        f=new Fake();f.Add("{\"access_token\":\"short\",\"refresh_token\":\"rotate-1\",\"expires_in\":30}");f.Add(Enrollment);f.Add(Token);f.Add(Status);
        using(c=new HyundaiUsClient(f))
        {
            var list=await c.LoginAsync("tester@example.invalid","test-password","1234",default);await c.SelectAsync(list[0],default);
            using var p=JsonDocument.Parse(f.Calls[2].Body);
            Check(p.RootElement.GetProperty("grant_type").GetString()=="refresh_token"&&p.RootElement.GetProperty("refresh_token").GetString()=="rotate-1"&&p.RootElement.GetProperty("password").GetString()=="test-password","Refresh payload includes credentials");
        }
        f=new Fake();f.Add("{\"challenge\":\"OTP\"}");using(c=new HyundaiUsClient(f))
        {
            await Reject(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default),"Unsupported challenge");Check(!c.IsAuthenticated&&f.Calls.Count==1,"Challenge stops login without retries");
        }
        // All requests below stay in Fake. No credentials or responses are printed.
        foreach(bool enrollmentFailure in new[]{false,true})
        foreach(var scenario in new[]{
            (HttpStatusCode.BadGateway,"<html>tester@example.invalid test-password 1234 test-access TESTVIN0000000001</html>",(int?)null),
            (HttpStatusCode.OK,"{\"errorCode\":502,\"errorMessage\":\"tester@example.invalid test-password 1234 test-access TESTVIN0000000001\"}",(int?)502),
            (HttpStatusCode.BadGateway,"{\"errorCode\":\"502\",\"errorMessage\":\"private account response\"}",(int?)502)})
        {
            f=new Fake();if(enrollmentFailure)f.Add(Token);f.Add(scenario.Item2,scenario.Item1);
            using(c=new HyundaiUsClient(f))
            {
                var e=await Failure(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default));
                Check(e.Message.StartsWith(enrollmentFailure?"Vehicle list:":"Authentication:"),"Failure stage");
                Check(e.HttpStatus==(int)scenario.Item1&&e.ApiErrorCode==scenario.Item3,"HTTP and API codes independently captured");
                Check(e.Message.Contains("HTTP "+(int)scenario.Item1)&&(scenario.Item3==null?!e.Message.Contains("API code"):e.Message.Contains("API code 502")),"Distinct diagnostic labels");
                Check(!new[]{"tester@","test-password","1234","test-access","TESTVIN","<html>","https://","private account"}.Any(e.ToString().Contains),"Diagnostic does not expose account data");
                Check(!c.IsAuthenticated&&c.Selected==null&&c.State==null&&f.Calls.Count==(enrollmentFailure?2:1),"Failed login clears session and never retries");
                f.Add(Token);f.Add(Enrollment);
                var list=await c.LoginAsync("tester@example.invalid","test-password","1234",default);
                Check(c.IsAuthenticated&&list.Count==1&&f.Replies.Count==0,"Manual sign-in succeeds after failure");
            }
        }
        foreach(string unsafeCode in new[]{"tester@example.invalid","1234","TESTVIN0000000001","https://example.invalid/account","test-access"})
        {
            f=new Fake();f.Add(RequestJson.Object(w=>{w.WriteString("errorCode",unsafeCode);w.WriteString("errorMessage",unsafeCode);}));
            using(c=new HyundaiUsClient(f))
            {
                var e=await Failure(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default));
                Check(e.ApiErrorCode==null&&!e.ToString().Contains(unsafeCode)&&e.Message.Contains("code withheld"),"Untrusted error code withheld");
            }
        }
        foreach(bool enrollmentFailure in new[]{false,true})
        foreach(bool timeout in new[]{false,true})
        {
            f=new Fake();if(enrollmentFailure)f.Add(Token);
            f.Replies.Enqueue(()=>throw (timeout?new TaskCanceledException("https://private-account test-password"):new HttpRequestException("https://private-account test-password")));
            using(c=new HyundaiUsClient(f))
            {
                var e=await Failure(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default));
                Check(e.Message.StartsWith(enrollmentFailure?"Vehicle list:":"Authentication:")&&!e.ToString().Contains("private-account")&&!e.ToString().Contains("test-password"),"Transport failure stage and sanitization");
                Check(!c.IsAuthenticated&&f.Calls.Count==(enrollmentFailure?2:1),"Transport failure does not retry");
            }
        }
        foreach(string field in new[]{"challenge","mfaRequired","otpRequired","captchaRequired"})
        {
            f=new Fake();f.Add(Token.TrimEnd('}')+",\""+field+"\":true}");
            using(c=new HyundaiUsClient(f))
            {
                var e=await Failure(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default));
                Check(e.Message.Contains("challenge required")&&!c.IsAuthenticated&&f.Calls.Count==1,"Challenge with token still stops login");
            }
        }
        f=new Fake();f.Add(Token);f.Add("{\"mfaRequired\":true}");using(c=new HyundaiUsClient(f))
        {
            var e=await Failure(()=>c.LoginAsync("tester@example.invalid","test-password","1234",default));
            Check(e.Message.StartsWith("Vehicle list:")&&e.Message.Contains("challenge required")&&!c.IsAuthenticated,"Enrollment challenge stops login");
        }
        f=new Fake();f.Add(Token);f.Add(Enrollment);using(c=new HyundaiUsClient(f))
        {
            await c.LoginAsync("tester+car@example.invalid","test-password","1234",default);
            Check(f.Calls[1].Path=="/ac/v2/enrollment/details/tester+car@example.invalid","Enrollment email punctuation matches working fiveN path");
            Check(f.Calls[1].Method=="GET"&&f.Calls[1].Body==""&&f.Calls[1].Headers["username"]=="tester+car@example.invalid"&&f.Calls[1].Headers["accessToken"]=="test-access","Enrollment method and fiveN account headers");
            foreach(var call in f.Calls)
            {
                Check(!new[]{"blueLinkServicePin","origin","referer","encryptFlag","accept-language","sec-fetch-mode","accept-encoding"}.Any(call.Headers.ContainsKey),"fiveN minimal sign-in profile; no service PIN before vehicle requests");
                Check(call.Headers["user-agent"]=="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"&&call.Headers["from"]=="SPA"&&call.Headers["to"]=="ISS"&&call.Headers["refresh"]=="false","fiveN user agent and routing headers");
            }
        }
        f=new Fake();f.Add(Token);f.Add(Enrollment);using(c=new HyundaiUsClient(f))
        {
            await c.LoginAsync("tester/part?#@example.invalid","test-password","1234",default);
            Check(f.Calls[1].Path=="/ac/v2/enrollment/details/tester%2Fpart%3F%23@example.invalid","Path/query delimiters in account names remain escaped");
        }
        foreach(bool commandFailure in new[]{false,true})
        {
            (c,f)=await Session();using(c)
            {
                var previous=c.State;int calls=f.Calls.Count;f.Add("private response",HttpStatusCode.BadGateway);
                await Failure(()=>commandFailure?c.ExecuteAsync(new(ActionKind.Lock),null,default):c.ReadAsync(true,default));
                Check(c.State==previous&&f.Calls.Count==calls+1,"502 preserves last known state and never retries");
                if(commandFailure)Check(c.Pending?.Outcome==RemoteOutcome.Unknown,"502 command outcome remains unknown");
            }
        }
        Console.WriteLine($"PASS: {count} offline Hyundai protocol checks (no real API requests).");
    }
}
