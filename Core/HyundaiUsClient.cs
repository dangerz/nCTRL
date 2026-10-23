// Protocol adapted from Hyundai-Kia-Connect/hyundai_kia_connect_api (MIT).
// See THIRD_PARTY_NOTICES.md. No Python dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

public sealed record EnrolledVehicle(string Id, string Vin, string Name, string Model, int Generation, bool Electric);
public sealed record LiveVehicleState(bool? Locked, bool? ClimateOn, int? TemperatureF,
    int? Battery, bool? PluggedIn, bool? Charging, double? RangeMiles, int? AcLimit, int? DcLimit,
    string ReportedAt, DateTimeOffset RetrievedAt);
public sealed record VehicleLocation(double Latitude, double Longitude, string ReportedAt);
public enum RemoteOutcome { Accepted, Confirmed, Failed, Unknown }
public sealed record RemoteReceipt(RemoteOutcome Outcome, string Message, string? TransactionId);
public sealed class BluelinkException : Exception
{
    public int? HttpStatus { get; }
    public int? ApiErrorCode { get; }
    public BluelinkException(string message,int? httpStatus=null,int? apiErrorCode=null) : base(message)
    { HttpStatus=httpStatus;ApiErrorCode=apiErrorCode; }
}

/// <summary>Unofficial Hyundai USA adapter. Secrets are memory-only. No command retries.</summary>
public sealed class HyundaiUsClient : IDisposable
{
    private const string Origin = "https://api.telematics.hyundaiusa.com";
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string username="", password="", pin="", access="", refresh="";
    private DateTimeOffset expires, blockedUntil, nextCommand, nextRead, nextWake, nextLocation;
    public bool IsAuthenticated => access.Length>0;
    public EnrolledVehicle? Selected { get; private set; }
    public LiveVehicleState? State { get; private set; }
    public RemoteReceipt? Pending { get; private set; }
    public VehicleLocation? Location { get; private set; }
    public bool CanRequestLocation=>clock.GetUtcNow()>=nextLocation;
    private DateTimeOffset submittedAt;

    public HyundaiUsClient() : this(new HttpClientHandler {
        // Match fiveN's default HttpClient compression behavior. Never forward secrets on redirects.
        AllowAutoRedirect=false, AutomaticDecompression=DecompressionMethods.None
    }) { }
    // Injection is for deterministic offline protocol tests; host remains fixed.
    public HyundaiUsClient(HttpMessageHandler handler,TimeProvider? clock=null)
    { this.clock=clock??TimeProvider.System;http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(45),MaxResponseContentBufferSize=2*1024*1024}; }

    public async Task<IReadOnlyList<EnrolledVehicle>> LoginAsync(string email,string secret,string servicePin,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        string stage="Authentication";
        try
        {
            Clear();
            if(string.IsNullOrWhiteSpace(email)||string.IsNullOrEmpty(secret)||servicePin.Length!=4||!servicePin.All(char.IsAsciiDigit))
                throw new BluelinkException("Enter your US Bluelink email, password and four-digit service PIN.");
            username=email.Trim();password=secret;pin=servicePin;
            await TokenAsync(false,ct);
            stage="Vehicle list";
            // fiveN/upstream put ordinary email punctuation directly in this path segment.
            // Keep @/+ literal (both are valid path characters), but escape path/query delimiters.
            string accountPath=Uri.EscapeDataString(username).Replace("%40","@").Replace("%2B","+");
            var reply=await RequestAsync(HttpMethod.Get,"/ac/v2/enrollment/details/"+accountPath,null,true,false,ct,stage:stage);
            var entries=At(reply.Body,"enrolledVehicleDetails");
            if(entries.ValueKind!=JsonValueKind.Array)throw new BluelinkException("Vehicle list: Unsupported enrollment response. Sign-in was not completed; account challenges are not supported.");
            var result=new List<EnrolledVehicle>();
            foreach(var item in entries.EnumerateArray())
            {
                var v=At(item,"vehicleDetails");
                if(Text(v,"enrollmentStatus")=="CANCELLED")continue;
                var id=Text(v,"regid");var vin=Text(v,"vin");
                if(id.Length==0||vin.Length!=17)continue;
                result.Add(new(id,vin,Text(v,"nickName"),Text(v,"modelCode"),Integer(v,"vehicleGeneration")??2,Text(v,"evStatus")=="E"));
            }
            if(result.Count==0)throw new BluelinkException("Vehicle list: No active enrolled vehicles were returned for this account.");
            return result;
        }
        catch(HttpRequestException) { Clear();throw new BluelinkException(stage+": Connection failed. No automatic retry was sent."); }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        { Clear();throw new BluelinkException(stage+": Request timed out. No automatic retry was sent."); }
        catch(BluelinkException) { Clear();throw; }
        catch(OperationCanceledException) { Clear();throw; }
        catch(Exception ex) { Clear();throw new BluelinkException(stage+": "+SafeFailure.Describe(ex)); }
        finally { gate.Release(); }
    }
    public async Task SelectAsync(EnrolledVehicle vehicle,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await EnsureToken(ct);
            Selected=vehicle;State=null;Location=null;Pending=null;
            State=await ReadCore(false,ct);
            nextRead=clock.GetUtcNow().AddSeconds(15);
        }
        catch { Selected=null;State=null;throw; }
        finally { gate.Release(); }
    }
    public async Task<LiveVehicleState> ReadAsync(bool wakeVehicle,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(clock.GetUtcNow() < (wakeVehicle?nextWake:nextRead))
                throw new BluelinkException(wakeVehicle?"Wait two minutes between wake requests.":"Wait 15 seconds between status requests.");
            await EnsureToken(ct);
            nextRead=clock.GetUtcNow().AddSeconds(15);
            if(wakeVehicle)nextWake=clock.GetUtcNow().AddMinutes(2);
            State=await ReadCore(wakeVehicle,ct);return State;
        }
        finally { gate.Release(); }
    }
    private async Task<LiveVehicleState> ReadCore(bool wake,CancellationToken ct)
    {
        var reply=await RequestAsync(HttpMethod.Get,"/ac/v2/rcs/rvs/vehicleStatus",null,true,true,ct,
            new Dictionary<string,string>{{"refresh",wake?"true":"false"}});
        return ParseState(reply.Body);
    }
    public async Task<RemoteReceipt> ExecuteAsync(VehicleCommand command,int? dcLimit,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(Pending?.Outcome is RemoteOutcome.Accepted && clock.GetUtcNow()-submittedAt<TimeSpan.FromMinutes(2))
                throw new BluelinkException("A command is being checked automatically. Please wait.");
            if(clock.GetUtcNow()<nextCommand)throw new BluelinkException("Wait 30 seconds between remote commands.");
            await EnsureToken(ct);
            string route;string? body=null;
            switch(command.Kind)
            {
                case ActionKind.Lock: case ActionKind.Unlock:
                    route="rcs/rdo/"+(command.Kind==ActionKind.Lock?"off":"on");
                    body=RequestJson.Doors(username,Selected!.Vin,pin);break;
                case ActionKind.Climate:
                    NeedElectric();
                    if(command.Value<62||command.Value>82)throw new BluelinkException("Choose 62–82°F.");
                    route="evc/fatc/"+(command.Enabled?"start":"stop");
                    if(command.Enabled)
                        body=RequestJson.Climate(command.Value,Selected!.Generation);
                    break;
                case ActionKind.Charge:
                    NeedElectric();route="evc/charge/"+(command.Enabled?"start":"stop");break;
                case ActionKind.ChargeLimit:
                    NeedElectric();
                    if(!ValidLimit(command.Value)||dcLimit is null||!ValidLimit(dcLimit.Value))
                        throw new BluelinkException("Choose both AC and DC targets: 50–100% in 10% steps.");
                    route="evc/charge/targetsoc/set";
                    body=RequestJson.ChargeLimits(command.Value,dcLimit.Value);break;
                default:throw new BluelinkException("Unsupported remote command.");
            }
            nextCommand=clock.GetUtcNow().AddSeconds(30);
            submittedAt=clock.GetUtcNow();
            Pending=null;
            try
            {
                var reply=await RequestAsync(HttpMethod.Post,"/ac/v2/"+route,body,true,true,ct,
                    command.Kind is ActionKind.Lock or ActionKind.Unlock?new Dictionary<string,string>{{"APPCLOUD-VIN",Selected!.Vin}}:null,stage:"Command");
                // HTTP success is acceptance, not evidence of vehicle state.
                Pending=new(RemoteOutcome.Accepted,$"Hyundai accepted the request (HTTP {reply.HttpStatus}). "+(reply.TransactionId is null?
                    "No tracking ID; completion unverified. Checking state automatically.":"Vehicle completion pending; checking automatically."),reply.TransactionId);
            }
            catch(OperationCanceledException) {
                Pending=new(RemoteOutcome.Unknown,"Request interrupted; outcome unknown. Check vehicle status before retrying.",null);throw;
            }
            catch(HttpRequestException) {
                Pending=new(RemoteOutcome.Unknown,"Connection lost; command outcome unknown. Refresh before retrying.",null);return Pending;
            }
            catch(BluelinkException) {
                Pending=new(RemoteOutcome.Unknown,"The last request was not confirmed. Refresh vehicle state before retrying.",null);throw;
            }
            return Pending;
        }
        finally { gate.Release(); }
    }
    public async Task<RemoteReceipt> CheckCommandAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(Pending is null)throw new BluelinkException("No command to check.");
            if(Pending.TransactionId is null)return Pending with {Message="No tracking ID is available. Refresh vehicle status to verify."};
            await EnsureToken(ct);
            var r=await RequestAsync(HttpMethod.Get,"/ac/v2/rmt/getRunningStatus",null,true,true,ct,
                new Dictionary<string,string>{{"tid",Pending.TransactionId},{"login_id",username},{"service_type","REMOTE_POLL"}},stage:"Command status");
            var status=Text(r.Body,"status");
            Pending=Pending with {
                Outcome=status=="SUCCESS"?RemoteOutcome.Confirmed:status=="ERROR"?RemoteOutcome.Failed:RemoteOutcome.Accepted,
                Message=status=="SUCCESS"?"Hyundai confirmed completion. Refresh for reported vehicle state.":
                    status=="ERROR"?"Hyundai reported command failure.":"Command still pending or status unavailable. No retry was sent."};
            if(clock.GetUtcNow()-submittedAt>TimeSpan.FromMinutes(2)&&Pending.Outcome==RemoteOutcome.Accepted)
                Pending=Pending with {Outcome=RemoteOutcome.Unknown,Message="Confirmation timed out. Refresh state before deciding to retry."};
            return Pending;
        }
        finally { gate.Release(); }
    }
    // Read-only follow-up. Never resubmits a command, even after transport failure.
    // The UI owns cancellation and prevents a second command while this is running.
    public Task MonitorCommandAsync(VehicleCommand command,int? dcLimit,LiveVehicleState? before,
        Action updated,CancellationToken ct)=>MonitorCommandAsync(command,dcLimit,before,updated,ct,
            (duration,token)=>Task.Delay(duration,clock,token));

    internal async Task MonitorCommandAsync(VehicleCommand command,int? dcLimit,LiveVehicleState? before,
        Action updated,CancellationToken ct,Func<TimeSpan,CancellationToken,Task> delay)
    {
        if(Pending==null)return;
        for(int attempt=0;attempt<8;attempt++)
        {
            await delay(TimeSpan.FromSeconds(15),ct);
            ct.ThrowIfCancellationRequested();
            try
            {
                if(Pending?.TransactionId!=null && Pending.Outcome is RemoteOutcome.Accepted or RemoteOutcome.Unknown)
                {
                    try {await CheckCommandAsync(ct);}
                    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch(BluelinkException ex) when(ex.HttpStatus is 401 or 403 or 429){throw;}
                    catch(Exception ex) when(ex is BluelinkException or HttpRequestException or OperationCanceledException)
                    { /* Tracking may be unavailable; still try the cached vehicle report. */ }
                }
                if(Pending?.Outcome==RemoteOutcome.Failed){updated();return;}
                // Cached read only; does not wake the vehicle or send another POST.
                if(clock.GetUtcNow()>=nextRead)await ReadAsync(false,ct);
                if(Pending?.Outcome!=RemoteOutcome.Confirmed && ChangedToRequestedState(command,dcLimit,before,State))
                    Pending=Pending! with {Outcome=RemoteOutcome.Confirmed,
                        Message="Updated vehicle report matches the requested state."};
                updated();
                if(Pending?.Outcome==RemoteOutcome.Confirmed)
                {
                    // Keep controls disabled until the existing command cooldown expires.
                    var remaining=nextCommand-clock.GetUtcNow();
                    if(remaining>TimeSpan.Zero)await delay(remaining,ct);
                    return;
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(BluelinkException ex) when(ex.HttpStatus is 401 or 403 or 429){break;}
            catch(Exception ex) when(ex is BluelinkException or HttpRequestException or OperationCanceledException)
            { /* Keep the last report. Only subsequent scheduled GETs may run. */ }
        }
        if(Pending!=null && Pending.Outcome is RemoteOutcome.Accepted or RemoteOutcome.Unknown)
            Pending=Pending with {Outcome=RemoteOutcome.Unknown,
                Message="Automatic checks ended; completion remains unverified. No command was retried."};
        updated();
    }

    internal static bool ChangedToRequestedState(VehicleCommand command,int? dc,LiveVehicleState? before,LiveVehicleState? after)
    {
        // A newly downloaded copy of an old report is not completion evidence.
        if(before==null||after==null||string.IsNullOrWhiteSpace(after.ReportedAt)||after.ReportedAt==before.ReportedAt)return false;
        return command.Kind switch {
            ActionKind.Lock=>before.Locked==false&&after.Locked==true,
            ActionKind.Unlock=>before.Locked==true&&after.Locked==false,
            ActionKind.Climate=>before.ClimateOn==!command.Enabled&&after.ClimateOn==command.Enabled ||
                command.Enabled&&after.ClimateOn==true&&before.TemperatureF!=command.Value&&after.TemperatureF==command.Value,
            ActionKind.Charge=>before.Charging==!command.Enabled&&after.Charging==command.Enabled,
            ActionKind.ChargeLimit=>(before.AcLimit!=command.Value||before.DcLimit!=dc)&&after.AcLimit==command.Value&&after.DcLimit==dc,
            _=>false
        };
    }

    public async Task<VehicleLocation> LocateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(clock.GetUtcNow()<nextLocation)throw new BluelinkException("Wait an hour between location requests to limit vehicle wakeups.");
            await EnsureToken(ct);nextLocation=clock.GetUtcNow().AddHours(1);
            var r=await RequestAsync(HttpMethod.Get,"/ac/v2/rcs/rfc/findMyCar",null,true,true,ct);
            var lat=Number(r.Body,"coord.lat");var lon=Number(r.Body,"coord.lon");
            if(lat is null||lon is null||Math.Abs(lat.Value)>90||Math.Abs(lon.Value)>180)
                throw new BluelinkException("Hyundai did not return a valid location.");
            Location=new(lat.Value,lon.Value,Text(r.Body,"time"));return Location;
        }
        finally { gate.Release(); }
    }
    private static bool ValidLimit(int n)=>n>=50&&n<=100&&n%10==0;
    private void NeedVehicle(){if(Selected is null)throw new BluelinkException("Select a vehicle first.");}
    private void NeedElectric(){if(Selected?.Electric!=true)throw new BluelinkException("This control is available only for enrolled electric vehicles.");}
    private async Task EnsureToken(CancellationToken ct)
    {
        if(access.Length==0)throw new BluelinkException("Sign in to Bluelink again.");
        if(clock.GetUtcNow()>=expires.AddSeconds(-60))await TokenAsync(true,ct);
    }
    private async Task TokenAsync(bool renewing,CancellationToken ct)
    {
        string body=RequestJson.Token(username,password,renewing?refresh:"");
        var r=await RequestAsync(HttpMethod.Post,"/v2/ac/oauth/token",body,false,false,ct,stage:"Authentication");
        var token=Text(r.Body,"access_token");var seconds=Number(r.Body,"expires_in");
        if(token.Length==0||seconds is null||!double.IsFinite(seconds.Value)||seconds<=0)throw new BluelinkException("Authentication: No supported session returned. MFA/challenge flows are not supported by this US adapter.");
        access=token;refresh=Text(r.Body,"refresh_token");expires=clock.GetUtcNow().AddSeconds(Math.Min(seconds.Value,86400));
    }
    private sealed record Reply(JsonElement Body,string? TransactionId,int HttpStatus);
    private async Task<Reply> RequestAsync(HttpMethod method,string path,string? body,bool authenticated,bool vehicle,
        CancellationToken ct,Dictionary<string,string>? extra=null,string stage="Request")
    {
        if(clock.GetUtcNow()<blockedUntil)throw new BluelinkException(stage+": Hyundai rate limit active. Wait before trying again.");
        using var request=new HttpRequestMessage(method,Origin+path);
        // Use fiveN CommsManager's minimal common header profile for all requests.
        // Door commands also match its body PIN; other command payloads are endpoint-specific.
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
            ["accept"]="application/json, text/plain, */*",
            ["user-agent"]="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            ["from"]="SPA",["to"]="ISS",["language"]="0",
            ["offset"]=((int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalHours).ToString(CultureInfo.InvariantCulture),
            ["refresh"]="false",["brandIndicator"]="H",
            ["client_id"]="m66129Bb-em93-SPAHYN-bZ91-am4540zp19920",["clientSecret"]="v558o935-6nne-423i-baa8"};
        if(authenticated){headers["username"]=username;headers["accessToken"]=access;}
        if(vehicle){
            NeedVehicle();headers["registrationId"]=Selected!.Id;headers["vin"]=Selected.Vin;headers["gen"]=Selected.Generation.ToString(CultureInfo.InvariantCulture);
            headers["blueLinkServicePin"]=pin;
        }
        if(extra!=null)foreach(var pair in extra)headers[pair.Key]=pair.Value;
        foreach(var pair in headers)request.Headers.Add(pair.Key,pair.Value);
        if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseContentRead,ct);
        string text=await response.Content.ReadAsStringAsync(ct);
        JsonElement json=default;
        bool malformed=false;
        if(!string.IsNullOrWhiteSpace(text))
        {
            try{using var doc=JsonDocument.Parse(text);json=doc.RootElement.Clone();}
            catch(JsonException){malformed=true;}
        }
        // Only three-digit numeric codes leave the transport. Never copy server messages,
        // URLs, arbitrary code strings, identifiers or raw bodies into exceptions/logs.
        var error=At(json,"errorCode");
        bool apiError=error.ValueKind!=JsonValueKind.Undefined;
        string rawCode=error.ValueKind==JsonValueKind.String?error.GetString()??"":error.ToString();
        int? code=rawCode.Length==3&&rawCode.All(char.IsAsciiDigit)&&int.TryParse(rawCode,out int number)?number:null;
        int status=(int)response.StatusCode;
        BluelinkException Failure(string detail)=>new($"{stage}: HTTP {status}"+
            (apiError?code is int n?$"; API code {n}":"; API error (code withheld)":"")+". "+detail,status,code);
        if(response.StatusCode==HttpStatusCode.TooManyRequests){blockedUntil=clock.GetUtcNow().Add(response.Headers.RetryAfter?.Delta??TimeSpan.FromMinutes(5));throw Failure("Hyundai is rate limiting requests. No automatic retry was sent.");}
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden){access="";refresh="";throw Failure("Hyundai denied access. Sign-in or account challenges may be required; challenges are not supported. No retry was sent.");}
        if(!response.IsSuccessStatusCode)throw Failure("Hyundai request failed; the cause is not established. No automatic retry was sent.");
        if(malformed)throw Failure("Unsupported response. No automatic retry was sent.");
        if(HasChallenge(json))throw Failure("MFA/account challenge required. This adapter cannot complete it. No bypass or automatic retry was attempted.");
        if(apiError||At(json,"error").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            throw Failure("Hyundai rejected the request; the cause is not established. MFA/challenges are not supported. No automatic retry was sent.");
        string? tid=null;
        foreach(var name in new[]{"tmsTid","transactionId","Xid"})if(response.Headers.TryGetValues(name,out var values)){tid=values.FirstOrDefault();break;}
        return new(json,tid,status);
    }
    private static bool HasChallenge(JsonElement json)
    {
        // Fail closed for explicit challenge markers, even if a token is also present.
        // This is detection only, not an implementation of Hyundai's challenge protocol.
        foreach(string field in new[]{"challenge","mfaRequired","otpRequired","captchaRequired"})
        {
            var value=At(json,field);
            if(value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False)continue;
            if(value.ValueKind==JsonValueKind.String&&value.GetString() is "" or "false")continue;
            if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int n)&&n==0)continue;
            return true;
        }
        return false;
    }
    public static LiveVehicleState ParseState(JsonElement json)
    {
        var v=At(json,"vehicleStatus");
        if(v.ValueKind!=JsonValueKind.Object)throw new BluelinkException("Vehicle status format is unsupported. No new vehicle state was loaded.");
        int? ac=null,dc=null;
        var targets=At(v,"evStatus.reservChargeInfos.targetSOClist");
        if(targets.ValueKind==JsonValueKind.Array)foreach(var t in targets.EnumerateArray()){
            if(Integer(t,"plugType")==1)ac=Percent(Integer(t,"targetSOClevel"));
            if(Integer(t,"plugType")==0)dc=Percent(Integer(t,"targetSOClevel"));
        }
        var range=Number(v,"evStatus.drvDistance.0.rangeByFuel.totalAvailableRange.value");
        var unit=Integer(v,"evStatus.drvDistance.0.rangeByFuel.totalAvailableRange.unit");
        range=unit==1?range/1.609344:unit is 2 or 3?range:null;
        if(range<0)range=null;
        var plugin=Integer(v,"evStatus.batteryPlugin");
        var temp=Integer(v,"airTemp.value");var tempUnit=Integer(v,"airTemp.unit");
        if(tempUnit==0&&temp!=null)temp=(int)Math.Round(temp.Value*1.8+32);
        if(tempUnit is not (0 or 1))temp=null;
        return new(Boolean(v,"doorLock"),Boolean(v,"airCtrlOn"),temp,Percent(Integer(v,"evStatus.batteryStatus")),
            plugin is null?Boolean(v,"evStatus.batteryPlugin"):plugin>0,Boolean(v,"evStatus.batteryCharge"),range,ac,dc,
            Text(v,"dateTime"),DateTimeOffset.UtcNow);
    }
    private static int? Percent(int? n)=>n is >=0 and <=100?n:null;
    public static JsonElement At(JsonElement e,string path)
    {
        foreach(var part in path.Split('.'))
        {
            if(e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(part,out var child))e=child;
            else if(e.ValueKind==JsonValueKind.Array&&int.TryParse(part,out var i)&&i>=0&&i<e.GetArrayLength())e=e[i];
            else return default;
        }
        return e;
    }
    private static string Text(JsonElement e,string p){var v=At(e,p);return v.ValueKind is JsonValueKind.String? v.GetString()??"":v.ValueKind==JsonValueKind.Number?v.GetRawText():"";}
    private static double? Number(JsonElement e,string p)=>double.TryParse(Text(e,p),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:null;
    private static int? Integer(JsonElement e,string p){var n=Number(e,p);return n is >=int.MinValue and <=int.MaxValue?(int)n.Value:null;}
    private static bool? Boolean(JsonElement e,string p)=>At(e,p).ValueKind switch {
        JsonValueKind.True=>true,JsonValueKind.False=>false,_=>Text(e,p).ToLowerInvariant() switch {"1" or "true"=>true,"0" or "false"=>false,_=>null}};
    private void Clear(){username="";password="";pin="";access="";refresh="";Selected=null;State=null;Pending=null;Location=null;}
    public void Dispose(){Clear();http.Dispose();}
}
