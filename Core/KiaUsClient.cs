// Protocol adapted from Hyundai-Kia-Connect/hyundai_kia_connect_api KiaUvoApiUSA.py (MIT),
// revision bd93ccd38e8f582cbca03a786bb73a02159502ab. See THIRD_PARTY_NOTICES.md. No Python dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>Unofficial Kia Connect (US) adapter. Secrets are memory-only here; the app decides what the vault keeps.
/// Never retries a command. A read whose session Kia reports expired is repaired (signing in with the remember
/// token) and sent once more; a command is not.</summary>
public sealed class KiaUsClient : IVehicleConnection, IOneTimeCodeSignIn
{
    private const string Api="https://api.owners.kia.com/apigw/v1/";
    public static readonly ServiceInfo Info=new("Kia Connect","Kia",NeedsPin:false,OneTimeCode:true,
        LightsAndHorn:false,SurroundView:false,SendToCar:false,FrontSeats:true,RequestLocation:false,Heading:false);
    // Upstream LOGIN_TOKEN_LIFETIME.
    private static readonly TimeSpan SessionLifetime=TimeSpan.FromHours(23);
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly RequestBudget budget;
    private string email="",password="",sid="";
    private DateTimeOffset expires;
    private string otpKey="",otpXid="";
    // Kia's vehicle key ("vinkey") changes with every session, so it is kept apart from the saved vehicle.
    private readonly Dictionary<string,string> keys=new();
    private DateTimeOffset submittedAt;

    public ServiceInfo Service=>Info;
    public string RememberToken { get; set; }="";
    public string DeviceId { get; set; }=Guid.NewGuid().ToString().ToUpperInvariant();
    public bool IsAuthenticated=>sid.Length>0;
    public bool SessionValid=>sid.Length>0&&clock.GetUtcNow()<expires.AddSeconds(-60);
    public EnrolledVehicle? Selected { get; private set; }
    public LiveVehicleState? State { get; private set; }
    public JsonElement? RawStatus { get; private set; }
    public RemoteReceipt? Pending { get; private set; }
    public VehicleLocation? Location { get; private set; }
    public SurroundView? SurroundView=>null;
    public bool CanRequestLocation=>budget.CanAcquire(RequestKind.Location);
    public RequestBudget Budget=>budget;

    public KiaUsClient(RequestBudget? budget=null) : this(new HttpClientHandler {
        // Never forward secrets on redirects.
        AllowAutoRedirect=false, AutomaticDecompression=DecompressionMethods.All
    },null,budget) { }
    // Injection is for deterministic offline protocol tests; the host stays fixed.
    public KiaUsClient(HttpMessageHandler handler,TimeProvider? clock=null,RequestBudget? budget=null)
    {
        this.clock=clock??TimeProvider.System;
        this.budget=budget??new RequestBudget(new MemoryBudgetStore(),this.clock);
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(45),MaxResponseContentBufferSize=8*1024*1024};
    }

    public SharedSession? ExportSession()=>sid.Length>0&&Selected is {} vehicle?new(sid,RememberToken,expires,vehicle):null;
    public void ResumeSession(string email,string secret,string servicePin,SharedSession session)
    {
        Clear();
        this.email=email.Trim();password=secret;
        sid=session.AccessToken;if(session.RefreshToken.Length>0)RememberToken=session.RefreshToken;
        expires=session.Expires;Selected=session.Vehicle;
    }
    public void UseVehicle(EnrolledVehicle vehicle){Selected=vehicle;State=null;Location=null;Pending=null;}

    // ---- Sign-in ----

    public async Task<IReadOnlyList<EnrolledVehicle>> LoginAsync(string email,string secret,string servicePin,CancellationToken ct,bool implicitSignIn=false)
    {
        await gate.WaitAsync(ct);
        string stage="Sign-in";
        try
        {
            Clear();
            if(string.IsNullOrWhiteSpace(email)||string.IsNullOrEmpty(secret))
                throw new BluelinkException("Enter your Kia Connect email and password.");
            this.email=email.Trim();password=secret;
            budget.Acquire(RequestKind.SignIn,enforceCooldown:!implicitSignIn);
            if(!await SignInCore(ct))
                throw new OneTimeCodeRequiredException(destinations);
            stage="Vehicle list";
            return await VehiclesCore(ct);
        }
        catch(OneTimeCodeRequiredException) { throw; } // Keep the account and the code request for VerifyCodeAsync.
        catch(Exception ex) { throw Fail(stage,ex,ct); }
        finally { gate.Release(); }
    }
    private CodeDestinations destinations=new(null,null);
    public async Task SendCodeAsync(CodeChannel channel,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(otpKey.Length==0)throw new BluelinkException("Sign in first; Kia hasn't asked for a code.");
            // Each code counts toward the daily sign-in cap; Kia limits codes too.
            budget.Acquire(RequestKind.SignIn,enforceCooldown:false,startsCooldown:false);
            await RequestAsync(RequestKind.SignIn,HttpMethod.Post,"cmm/sendOTP","{}",ct,"One-time code",session:false,
                extra:new(){{"otpkey",otpKey},{"notifytype",channel==CodeChannel.Text?"SMS":"EMAIL"},{"xid",otpXid}});
        }
        catch(Exception ex) when(ex is not BluelinkException) { throw Fail("One-time code",ex,ct,clear:false); }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<EnrolledVehicle>> VerifyCodeAsync(string code,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        string stage="One-time code";
        try
        {
            code=code.Trim();
            if(otpKey.Length==0)throw new BluelinkException("Sign in first; Kia hasn't asked for a code.");
            if(code.Length is <4 or >10||!code.All(char.IsAsciiDigit))throw new BluelinkException("Enter the code Kia sent you (digits only).");
            var verified=await RequestAsync(RequestKind.SignIn,HttpMethod.Post,"cmm/verifyOTP",RequestJsonKia.Object(w=>w.WriteString("otp",code)),ct,stage,
                session:false,extra:new(){{"otpkey",otpKey},{"xid",otpXid}},checkStatus:false);
            if(Status(verified.Body) is not 0||verified.Header("sid") is not {} first||verified.Header("rmtoken") is not {} remember)
                throw new BluelinkException("One-time code: Kia didn't accept that code. Ask for a new one and try again.");
            var done=await RequestAsync(RequestKind.SignIn,HttpMethod.Post,"prof/authUser",LoginBody(false),ct,stage,session:false,
                extra:new(){{"sid",first},{"rmtoken",remember}},checkStatus:false);
            if(done.Header("sid") is not {} final)throw new BluelinkException("One-time code: Kia accepted the code but sent no session. Sign in again.");
            sid=final;RememberToken=remember;expires=clock.GetUtcNow()+SessionLifetime;otpKey="";otpXid="";
            stage="Vehicle list";
            return await VehiclesCore(ct);
        }
        catch(Exception ex) { throw Fail(stage,ex,ct,clear:false); }
        finally { gate.Release(); }
    }
    /// <summary>True with a session; false when Kia wants a one-time code (its request is kept).</summary>
    private async Task<bool> SignInCore(CancellationToken ct)
    {
        var extra=RememberToken.Length>0?new Dictionary<string,string>{{"rmtoken",RememberToken}}:null;
        var r=await RequestAsync(RequestKind.SignIn,HttpMethod.Post,"prof/authUser",LoginBody(true),ct,"Sign-in",session:false,extra:extra,checkStatus:false);
        if(r.Header("sid") is {} session){sid=session;expires=clock.GetUtcNow()+SessionLifetime;return true;}
        var payload=At(r.Body,"payload");
        if(Text(payload,"otpKey") is {Length:>0} key)
        {
            otpKey=key;otpXid=r.Header("xid")??"";
            // A remember token Kia no longer honors is useless; forget it so the next one replaces it.
            if(Bool(payload,"rmTokenExpired")==true)RememberToken="";
            destinations=new(Bool(payload,"hasEmail")==true?Text(payload,"email"):null,Bool(payload,"hasPhone")==true?Text(payload,"phone"):null);
            return false;
        }
        if(Integer(r.Body,"status.errorCode")==1001)throw new BluelinkException("Sign-in: Kia didn't accept that email and password.");
        throw Failure("Sign-in",r,"Kia sent no session. Nothing else was sent.");
    }
    private string LoginBody(bool first)=>RequestJsonKia.Object(w=>{
        w.WriteString("deviceKey",DeviceId);w.WriteNumber("deviceType",2);
        w.WriteStartObject("userCredential");w.WriteString("userId",email);w.WriteString("password",password);w.WriteEndObject();
        if(first)w.WriteNumber("tncFlag",1);
    });
    private async Task<IReadOnlyList<EnrolledVehicle>> VehiclesCore(CancellationToken ct)
    {
        var r=await RequestAsync(RequestKind.SignIn,HttpMethod.Get,"ownr/gvl",null,ct,"Vehicle list");
        var list=At(r.Body,"payload.vehicleSummary");
        if(list.ValueKind!=JsonValueKind.Array)throw new BluelinkException("Vehicle list: Kia's reply had no vehicles.");
        var result=new List<EnrolledVehicle>();keys.Clear();
        foreach(var item in list.EnumerateArray())
        {
            string id=Text(item,"vehicleIdentifier"),key=Text(item,"vehicleKey");
            if(id.Length==0||key.Length==0)continue;
            // Kia's list may omit the VIN; the identifier then stands in (it is only used as a fingerprint).
            string vin=Text(item,"vin") is {Length:17} v?v:id;
            string model=Text(item,"modelName").Trim();
            if(Text(item,"trim") is {Length:>0} trim&&!model.Contains(trim,StringComparison.OrdinalIgnoreCase))model+=" "+trim.Trim();
            // fuelType 4 is EV (upstream: confirmed on a Niro EV; an EV6 reports 4 too).
            // Generation 3 marks front-seat heat/vent support in the UI (Kia takes seat settings in rems/start).
            result.Add(new(id,vin,Text(item,"nickName"),model,3,Integer(item,"fuelType")==4));
            keys[id]=key;
        }
        if(result.Count==0)throw new BluelinkException("Vehicle list: No vehicles were returned for this Kia account.");
        if(Selected is {} chosen&&result.FirstOrDefault(v=>v.Id==chosen.Id) is {} again)Selected=again;
        return result;
    }

    // ---- Reads ----

    public async Task SelectAsync(EnrolledVehicle vehicle,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            budget.Acquire(RequestKind.Read,enforceCooldown:false);
            await EnsureSession(ct);
            Selected=vehicle;State=null;Location=null;Pending=null;
            State=await ReadCore(false,RequestKind.Read,ct);
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
            var kind=wakeVehicle?RequestKind.Wake:RequestKind.Read;
            budget.Acquire(kind);
            await EnsureSession(ct);
            State=await ReadCore(wakeVehicle,kind,ct);return State;
        }
        finally { gate.Release(); }
    }
    private async Task<LiveVehicleState> ReadCore(bool wake,RequestKind kind,CancellationToken ct)
    {
        // rems/rvs asks the car for a new report; Kia then serves it from cmm/gvi like any cached read.
        if(wake)await VehicleRequest(kind,HttpMethod.Post,"rems/rvs","{\"requestType\":0}",ct,"Wake vehicle",repair:true);
        var r=await VehicleRequest(kind,HttpMethod.Post,"cmm/gvi",null,ct,"Vehicle status",repair:true,gvi:true);
        var info=At(r.Body,"payload.vehicleInfoList.0");
        var state=ParseState(info,clock.GetUtcNow());
        RawStatus=At(info,"lastVehicleInfo.vehicleStatusRpt.vehicleStatus");
        return state;
    }
    /// <summary>Kia USA has no separate "find my car" request: the location comes with the cached status.
    /// This reads status (counted as a location request) and returns its location.</summary>
    public async Task<VehicleLocation> LocateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            budget.Acquire(RequestKind.Location);
            await EnsureSession(ct);
            State=await ReadCore(false,RequestKind.Location,ct);
            Location=State.StatusLocation??throw new BluelinkException("Kia's last report has no location for this car.");
            return Location;
        }
        finally { gate.Release(); }
    }

    // ---- Commands ----

    public async Task<RemoteReceipt> ExecuteAsync(VehicleCommand command,int? dcLimit,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(Pending?.Outcome is RemoteOutcome.Accepted&&clock.GetUtcNow()-submittedAt<TimeSpan.FromMinutes(2))
                throw new BluelinkException("A command is being checked automatically. Please wait.");
            HttpMethod method=HttpMethod.Get;string route;string? body=null;
            switch(command.Kind)
            {
                case ActionKind.Lock: route="rems/door/lock";break;
                case ActionKind.Unlock: route="rems/door/unlock";break;
                case ActionKind.Climate:
                    NeedElectric();
                    if(command.Enabled) {
                        if(command.Value<62||command.Value>82)throw new BluelinkException("Choose 62–82°F.");
                        method=HttpMethod.Post;route="rems/start";body=RequestJsonKia.Climate(command.Value,command.Climate);
                    } else route="rems/stop";
                    break;
                case ActionKind.Charge:
                    NeedElectric();
                    if(command.Enabled){method=HttpMethod.Post;route="evc/charge";body="{\"chargeRatio\":100}";}
                    else route="evc/cancel";
                    break;
                case ActionKind.ChargeLimit:
                    NeedElectric();
                    if(!ValidLimit(command.Value)||dcLimit is null||!ValidLimit(dcLimit.Value))
                        throw new BluelinkException("Choose both AC and DC targets: 50–100% in 10% steps.");
                    method=HttpMethod.Post;route="evc/sts";body=RequestJsonKia.ChargeLimits(command.Value,dcLimit.Value);break;
                case ActionKind.Lights: case ActionKind.HornLights:
                    throw new BluelinkException("Lights and horn aren't available through Kia Connect in "+Brand.App+" yet. Nothing was sent.");
                default: throw new BluelinkException("Unsupported remote command.");
            }
            await EnsureSession(ct);
            budget.Acquire(RequestKind.Command);
            submittedAt=clock.GetUtcNow();
            Pending=null;
            try
            {
                var reply=await VehicleRequest(RequestKind.Command,method,route,body,ct,"Command",repair:false);
                // HTTP success is acceptance, not evidence of vehicle state.
                string? xid=reply.Header("Xid");
                Pending=new(RemoteOutcome.Accepted,$"Kia accepted the request (HTTP {reply.HttpStatus}). "+(xid is null?
                    "No tracking ID; completion unverified. Checking state automatically.":"Vehicle completion pending; checking automatically."),xid);
            }
            catch(OperationCanceledException) {
                Pending=new(RemoteOutcome.Unknown,"Request interrupted; outcome unknown. Check vehicle status before retrying.",null);throw;
            }
            catch(HttpRequestException) {
                Pending=new(RemoteOutcome.Unknown,"Connection lost; command outcome unknown. Refresh before retrying.",null);return Pending;
            }
            catch(SessionExpired) {
                // Kia checks the session before acting, so nothing reached the car. Repair it for the next tap.
                Pending=null;
                await RepairSession(ct);
                throw new BluelinkException("Kia's session had expired. It's renewed now and nothing was sent; tap again.");
            }
            catch(BluelinkException) {
                Pending=new(RemoteOutcome.Unknown,"The last request was not confirmed. Refresh vehicle state before retrying.",null);throw;
            }
            return Pending;
        }
        finally { gate.Release(); }
    }
    public Task<string?> SendDestinationAsync(Destination place,CancellationToken ct)=>
        throw new BluelinkException("Send to car isn't available through Kia Connect in "+Brand.App+" yet. Nothing was sent.");
    public Task<SurroundView> CaptureSurroundViewAsync(Action<int>? checkedCount,CancellationToken ct)=>
        throw new BluelinkException("Surround View isn't available through Kia Connect. Nothing was sent.");

    public async Task<RemoteReceipt> CheckCommandAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            if(Pending is null)throw new BluelinkException("No command to check.");
            if(Pending.TransactionId is null)return Pending with {Message="No tracking ID is available. Refresh vehicle status to verify."};
            budget.Acquire(RequestKind.Read,enforceCooldown:false,startsCooldown:false);
            await EnsureSession(ct);
            var r=await VehicleRequest(RequestKind.Read,HttpMethod.Post,"cmm/gts",RequestJsonKia.Object(w=>w.WriteString("xid",Pending.TransactionId)),ct,"Command status",repair:true);
            // Upstream: every pending-command counter at 0 means the car finished. Kia reports no failure state here.
            var payload=At(r.Body,"payload");
            bool done=payload.ValueKind==JsonValueKind.Object&&payload.EnumerateObject().Any()&&
                payload.EnumerateObject().All(p=>p.Value.ValueKind==JsonValueKind.Number&&p.Value.TryGetDouble(out double n)&&n==0);
            Pending=Pending with {
                Outcome=done?RemoteOutcome.Confirmed:RemoteOutcome.Accepted,
                Message=done?"Kia confirmed completion. Refresh for reported vehicle state.":"Command still pending or status unavailable. No retry was sent."};
            if(clock.GetUtcNow()-submittedAt>TimeSpan.FromMinutes(2)&&Pending.Outcome==RemoteOutcome.Accepted)
                Pending=Pending with {Outcome=RemoteOutcome.Unknown,Message="Confirmation timed out. Refresh state before deciding to retry."};
            return Pending;
        }
        finally { gate.Release(); }
    }
    public Task MonitorCommandAsync(VehicleCommand command,int? dcLimit,LiveVehicleState? before,Action updated,CancellationToken ct)=>
        MonitorCommandAsync(command,dcLimit,before,updated,ct,(duration,token)=>Task.Delay(duration,clock,token));
    // Same follow-up as the Hyundai client: read-only, never resubmits a command.
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
                if(Pending?.TransactionId!=null&&Pending.Outcome is RemoteOutcome.Accepted or RemoteOutcome.Unknown&&
                   budget.CanAcquire(RequestKind.Read,enforceCooldown:false))
                {
                    try {await CheckCommandAsync(ct);}
                    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch(BluelinkException ex) when(ex.HttpStatus is 401 or 403 or 429){throw;}
                    catch(Exception ex) when(ex is BluelinkException or HttpRequestException or OperationCanceledException) { }
                }
                if(Pending?.Outcome==RemoteOutcome.Failed){updated();return;}
                if(budget.CanAcquire(RequestKind.Read))await ReadAsync(false,ct);
                if(Pending?.Outcome!=RemoteOutcome.Confirmed&&HyundaiUsClient.ChangedToRequestedState(command,dcLimit,before,State))
                    Pending=Pending! with {Outcome=RemoteOutcome.Confirmed,Message="Updated vehicle report matches the requested state."};
                updated();
                if(Pending?.Outcome==RemoteOutcome.Confirmed)
                {
                    var remaining=budget.NextAllowed(RequestKind.Command)-clock.GetUtcNow();
                    if(remaining>TimeSpan.Zero)await delay(remaining,ct);
                    return;
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(BluelinkException ex) when(ex.HttpStatus is 401 or 403 or 429){break;}
            catch(Exception ex) when(ex is BluelinkException or HttpRequestException or OperationCanceledException) { }
        }
        if(Pending!=null&&Pending.Outcome is RemoteOutcome.Accepted or RemoteOutcome.Unknown)
            Pending=Pending with {Outcome=RemoteOutcome.Unknown,Message="Automatic checks ended; completion remains unverified. No command was retried."};
        updated();
    }

    // ---- Session ----

    private async Task EnsureSession(CancellationToken ct)
    {
        if(sid.Length==0)throw new BluelinkException("Sign in to Kia Connect again.");
        if(clock.GetUtcNow()>=expires.AddSeconds(-60))await RepairSession(ct);
        else if(Selected is {} v&&!keys.ContainsKey(v.Id))await VehiclesCore(ct); // A resumed session has no vehicle key yet.
    }
    // Automatic renewals use the remember token (no code) and are not budgeted, like Hyundai token renewals.
    private async Task RepairSession(CancellationToken ct)
    {
        if(RememberToken.Length==0||email.Length==0||password.Length==0){sid="";throw new BluelinkException("Kia's session ended. Sign in again.");}
        sid="";
        if(!await SignInCore(ct)){otpKey="";throw new BluelinkException("Kia wants a new one-time code. Sign in again.");}
        await VehiclesCore(ct);
    }
    private sealed class SessionExpired() : BluelinkException("Kia's session expired.");

    private async Task<Reply> VehicleRequest(RequestKind kind,HttpMethod method,string path,string? body,CancellationToken ct,string stage,bool repair,bool gvi=false)
    {
        NeedVehicle();
        for(int attempt=0;;attempt++)
        {
            if(!keys.TryGetValue(Selected!.Id,out var key))throw new BluelinkException("This car isn't on the Kia account any more. Sign in again.");
            string? json=gvi?RequestJsonKia.Gvi(key):body;
            try { return await RequestAsync(kind,method,path,json,ct,stage,extra:new(){{"vinkey",key}}); }
            catch(SessionExpired) when(repair&&attempt==0) { await RepairSession(ct); }
        }
    }

    // ---- Transport ----

    private sealed record Reply(JsonElement Body,int HttpStatus,HttpResponseHeaders Headers)
    {
        public string? Header(string name)=>Headers.TryGetValues(name,out var v)?v.FirstOrDefault(x=>!string.IsNullOrEmpty(x)):null;
    }
    private async Task<Reply> RequestAsync(RequestKind kind,HttpMethod method,string path,string? body,CancellationToken ct,string stage,
        bool session=true,Dictionary<string,string>? extra=null,bool checkStatus=true)
    {
        budget.ThrowIfHyundaiBlocked(stage,kind);
        using var request=new HttpRequestMessage(method,Api+path);
        foreach(var (name,value) in Headers())request.Headers.TryAddWithoutValidation(name,value);
        request.Headers.Date=clock.GetUtcNow();
        if(session){if(sid.Length==0)throw new BluelinkException("Sign in to Kia Connect again.");request.Headers.TryAddWithoutValidation("sid",sid);}
        if(extra!=null)foreach(var pair in extra)request.Headers.TryAddWithoutValidation(pair.Key,pair.Value);
        // Kia refuses any request without content-type, GETs included (errorCode 9200), so GETs carry an empty body.
        request.Content=body is null?new ByteArrayContent(Array.Empty<byte>()):new StringContent(body,Encoding.UTF8);
        request.Content.Headers.ContentType=MediaTypeHeaderValue.Parse("application/json;charset=utf-8");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseContentRead,ct);
        string text=await response.Content.ReadAsStringAsync(ct);
        JsonElement json=default;bool malformed=false;
        if(!string.IsNullOrWhiteSpace(text))
        {
            try{using var doc=JsonDocument.Parse(text);json=doc.RootElement.Clone();}
            catch(JsonException){malformed=true;}
        }
        var reply=new Reply(json,(int)response.StatusCode,response.Headers);
        if(response.StatusCode==HttpStatusCode.TooManyRequests){budget.RecordRateLimited(kind,response.Headers.RetryAfter?.Delta);throw Failure(stage,reply,"Kia is rate limiting requests. No automatic retry was sent.");}
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden){sid="";throw Failure(stage,reply,"Kia denied access. Sign in again. No retry was sent.");}
        if(!response.IsSuccessStatusCode)throw Failure(stage,reply,"Kia request failed; the cause is not established. No automatic retry was sent.");
        if(malformed)throw Failure(stage,reply,"Unsupported response. No automatic retry was sent.");
        if(checkStatus&&Status(json) is not 0)
        {
            if(Status(json)==1&&Integer(json,"status.errorType")==1&&Integer(json,"status.errorCode") is 1003 or 1005)throw new SessionExpired();
            throw Failure(stage,reply,"Kia refused the request; the cause is not established. No automatic retry was sent.");
        }
        return reply;
    }
    private IEnumerable<(string,string)> Headers()=>new[]{
        ("accept","application/json"),("accept-language","en-US,en;q=0.9"),("accept-charset","utf-8"),
        ("apptype","L"),("appversion","7.22.0"),("clientid","SPACL716-APL"),("clientuuid",Uuid5Dns(DeviceId)),
        ("from","SPA"),("language","0"),
        ("offset",((int)TimeZoneInfo.Local.GetUtcOffset(clock.GetUtcNow()).TotalHours).ToString(CultureInfo.InvariantCulture)),
        ("ostype","iOS"),("osversion","15.8.5"),("phonebrand","iPhone"),("secretkey","sydnat-9kykci-Kuhtep-h5nK"),
        ("to","APIGW"),("tokentype","A"),("user-agent","KIAPrimo_iOS/37 CFNetwork/1335.0.3.4 Darwin/21.6.0"),("deviceid",DeviceId)};
    private static int? Status(JsonElement body)=>Integer(body,"status.statusCode");
    // Only Kia's numeric codes leave the transport. Never copy server messages, identifiers or bodies into errors.
    private static BluelinkException Failure(string stage,Reply r,string detail)
    {
        int? code=Integer(r.Body,"status.errorCode") is int c&&c is >0 and <100000?c:null;
        return new($"{stage}: HTTP {r.HttpStatus}"+(code is int n?$"; Kia code {n}":"")+". "+detail,r.HttpStatus,code);
    }
    // Sanitizes a failure; a cancellation the caller asked for stays a cancellation.
    private Exception Fail(string stage,Exception ex,CancellationToken ct,bool clear=true)
    {
        if(clear){Clear();}
        return ex switch {
            BluelinkException b=>b,
            HttpRequestException=>new BluelinkException(stage+": Connection failed. No automatic retry was sent."),
            OperationCanceledException when !ct.IsCancellationRequested=>new BluelinkException(stage+": Request timed out. No automatic retry was sent."),
            OperationCanceledException=>new OperationCanceledException(ct),
            _=>new BluelinkException(stage+": "+SafeFailure.Describe(ex))};
    }

    // ---- Status mapping ----

    /// <summary>Maps one entry of Kia's cmm/gvi vehicleInfoList (upstream _update_vehicle_properties).</summary>
    public static LiveVehicleState ParseState(JsonElement info,DateTimeOffset received)
    {
        var v=At(info,"lastVehicleInfo.vehicleStatusRpt.vehicleStatus");
        if(v.ValueKind!=JsonValueKind.Object)throw new BluelinkException("Vehicle status format is unsupported. No new vehicle state was loaded.");
        int? ac=null,dc=null;
        var targets=At(v,"evStatus.targetSOC");
        // Last matching entry wins (upstream). A first read of a session may report 0: ignore it.
        if(targets.ValueKind==JsonValueKind.Array)foreach(var t in targets.EnumerateArray()) {
            int? level=Integer(t,"targetSOClevel") is int l&&l is >0 and <=100?l:null;
            if(level is null)continue;
            if(Integer(t,"plugType")==1)ac=level;
            if(Integer(t,"plugType")==0)dc=level;
        }
        double? range=Miles(v,"evStatus.drvDistance.0.rangeByFuel.totalAvailableRange")??Miles(v,"evStatus.drvDistance.0.rangeByFuel.evModeRange");
        var plugin=At(v,"evStatus.batteryPlugin");
        bool? plugged=plugin.ValueKind==JsonValueKind.Number?Integer(v,"evStatus.batteryPlugin")>0:Bool(v,"evStatus.batteryPlugin");
        int? temp=Text(v,"climate.airTemp.value").ToUpperInvariant() switch {
            "LOW"=>62,"HIGH"=>82,"OFF" or ""=>null,var s=>int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?n:
                double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out double d)?(int)Math.Round(d):null};
        int? tempUnit=Integer(v,"climate.airTemp.unit");
        if(tempUnit==0&&temp!=null)temp=(int)Math.Round(temp.Value*1.8+32);
        else if(tempUnit is not (null or 1))temp=null;
        bool? Window(string side,string kiaEv)=>Bool(v,"windowOpen."+side)??Bool(v,"evStatus.windowStatus."+kiaEv)??Bool(v,"windowStatus."+kiaEv);
        var health=new VehicleHealth(Percent(Integer(v,"batteryStatus.stateOfCharge")),
            Bool(v,"doorStatus.frontLeft"),Bool(v,"doorStatus.frontRight"),Bool(v,"doorStatus.backLeft"),Bool(v,"doorStatus.backRight"),
            Bool(v,"doorStatus.trunk"),Bool(v,"doorStatus.hood"),
            Window("frontLeft","windowFL"),Window("frontRight","windowFR"),Window("backLeft","windowRL"),Window("backRight","windowRR"),
            Bool(v,"tirePressure.all"),Bool(v,"washerFluidStatus"),Bool(v,"breakOilStatus"),Bool(v,"smartKeyBatteryWarning"));
        var loc=At(info,"lastVehicleInfo.location");
        var lat=Number(loc,"coord.lat");var lon=Number(loc,"coord.lon");
        var located=lat is double y&&lon is double x&&Math.Abs(y)<=90&&Math.Abs(x)<=180&&!(y==0&&x==0)?
            new VehicleLocation(y,x,Iso(Text(loc,"syncDate.utc")),received,Number(loc,"head") is double h&&h>=0&&h<=360?h%360:null):null;
        bool? charging=Bool(v,"evStatus.batteryCharge");
        int? minutes=Integer(v,"evStatus.remainChargeTime.0.timeInterval.value") is int left&&left is >0 and <10000?left:null;
        return new(Bool(v,"doorLock"),Bool(v,"climate.airCtrl"),temp,Percent(Integer(v,"evStatus.batteryStatus")),
            plugged,charging,range,ac,dc,Iso(Text(v,"syncDate.utc")),received,
            DriverSeat:null,PassengerSeat:null,SteeringWheelHeat:Bool(v,"climate.heatingAccessory.steeringWheel"),
            Health:health,StatusLocation:located,
            ChargingPowerKw:Number(v,"evStatus.realTimePower") is double kw&&kw>=0&&kw<1000?kw:null,
            ChargeMinutesLeft:charging==true?minutes:null,
            Moving:Number(loc,"speed.value") is double speed&&speed>=0&&speed<500?speed>0:null,
            Defrost:Bool(v,"climate.defrost"));
    }
    // Kia's compact UTC time (yyyyMMddHHmmss) as ISO 8601, which the rest of the app reads as a real time.
    internal static string Iso(string kia)=>
        kia.Length==14&&DateTime.TryParseExact(kia,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var t)?
            t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture):kia;
    // Distance units (upstream DISTANCE_UNITS): 1 km, 2 and 3 miles.
    private static double? Miles(JsonElement v,string path)
    {
        var value=Number(v,path+".value");var unit=Integer(v,path+".unit");
        double? miles=unit==1?value/1.609344:unit is 2 or 3?value:null;
        return miles is >=0?miles:null;
    }

    private static bool ValidLimit(int n)=>n>=50&&n<=100&&n%10==0;
    private void NeedVehicle(){if(Selected is null)throw new BluelinkException("Select a vehicle first.");}
    private void NeedElectric(){if(Selected?.Electric!=true)throw new BluelinkException("This control is available only for electric vehicles.");}
    private static int? Percent(int? n)=>n is >=0 and <=100?n:null;
    private static JsonElement At(JsonElement e,string path)=>HyundaiUsClient.At(e,path);
    private static string Text(JsonElement e,string p)=>HyundaiUsClient.Text(e,p);
    private static double? Number(JsonElement e,string p)=>double.TryParse(Text(e,p),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:null;
    private static int? Integer(JsonElement e,string p){var n=Number(e,p);return n is >=int.MinValue and <=int.MaxValue?(int)n.Value:null;}
    private static bool? Bool(JsonElement e,string p)=>At(e,p).ValueKind switch {
        JsonValueKind.True=>true,JsonValueKind.False=>false,_=>Text(e,p).ToLowerInvariant() switch {"1" or "true"=>true,"0" or "false"=>false,_=>null}};

    /// <summary>RFC 4122 version 5 UUID in the DNS namespace, as Python's uuid.uuid5(uuid.NAMESPACE_DNS, name).</summary>
    internal static string Uuid5Dns(string name)
    {
        byte[] ns=Convert.FromHexString("6ba7b8109dad11d180b400c04fd430c8");
        byte[] hash=SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes(name)).ToArray());
        hash[6]=(byte)((hash[6]&0x0F)|0x50);hash[8]=(byte)((hash[8]&0x3F)|0x80);
        string h=Convert.ToHexString(hash,0,16).ToLowerInvariant();
        return $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..32]}";
    }
    private void Clear(){email="";password="";sid="";otpKey="";otpXid="";keys.Clear();Selected=null;State=null;RawStatus=null;Pending=null;Location=null;}
    public void Dispose(){Clear();RememberToken="";http.Dispose();}
}

// Explicit JSON writing (no reflection, works in NativeAOT). Payloads may hold secrets; never log them.
internal static class RequestJsonKia
{
    internal static string Object(Action<Utf8JsonWriter> fields)=>RequestJson.Object(fields);
    internal static string Gvi(string vehicleKey)=>Object(w=>{
        w.WriteStartObject("vehicleConfigReq");
        w.WriteString("airTempRange","0");w.WriteString("maintenance","1");w.WriteString("seatHeatCoolOption","0");w.WriteString("vehicle","1");w.WriteString("vehicleFeature","0");
        w.WriteEndObject();
        w.WriteStartObject("vehicleInfoReq");
        // "drivingActivty" is Kia's spelling.
        w.WriteString("drivingActivty","0");w.WriteString("dtc","1");w.WriteString("enrollment","1");w.WriteString("functionalCards","0");
        w.WriteString("location","1");w.WriteString("vehicleStatus","1");w.WriteString("weather","0");
        w.WriteEndObject();
        w.WriteStartArray("vinKey");w.WriteStringValue(vehicleKey);w.WriteEndArray();
    });
    // rems/start, upstream start_climate. Rear-window and mirror heat stay off; seats only when one is chosen.
    internal static string Climate(int temperature,ClimateOptions? options)=>Object(w=>{
        options??=new();
        w.WriteStartObject("remoteClimate");
        w.WriteStartObject("airTemp");w.WriteNumber("unit",1);w.WriteString("value",temperature.ToString(CultureInfo.InvariantCulture));w.WriteEndObject();
        w.WriteBoolean("airCtrl",true);w.WriteBoolean("defrost",options.Defrost);
        w.WriteStartObject("heatingAccessory");
        w.WriteNumber("rearWindow",0);w.WriteNumber("sideMirror",0);
        w.WriteNumber("steeringWheel",options.SteeringWheel?1:0);w.WriteNumber("steeringWheelStep",options.SteeringWheel?1:0);
        w.WriteEndObject();
        w.WriteStartObject("ignitionOnDuration");w.WriteNumber("unit",4);w.WriteNumber("value",5);w.WriteEndObject();
        if(options.HasSeats) {
            w.WriteStartObject("heatVentSeat");
            Seat(w,"driverSeat",options.DriverSeat);Seat(w,"passengerSeat",options.PassengerSeat);
            Seat(w,"rearLeftSeat",SeatMode.Off);Seat(w,"rearRightSeat",SeatMode.Off);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    });
    // Upstream _seat_settings (found empirically, kia_uvo issue 718): type 1 heat / 2 cool, level 4..2 high..low, step 1..3.
    private static void Seat(Utf8JsonWriter w,string name,SeatMode mode)
    {
        var (type,level,step)=mode switch {
            SeatMode.HeatHigh=>(1,4,1),SeatMode.HeatMedium=>(1,3,2),SeatMode.HeatLow=>(1,2,3),
            SeatMode.CoolHigh=>(2,4,1),SeatMode.CoolMedium=>(2,3,2),SeatMode.CoolLow=>(2,2,3),_=>(0,1,0)};
        w.WriteStartObject(name);w.WriteNumber("heatVentType",type);w.WriteNumber("heatVentLevel",level);w.WriteNumber("heatVentStep",step);w.WriteEndObject();
    }
    internal static string ChargeLimits(int ac,int dc)=>RequestJson.ChargeLimits(ac,dc);
}
