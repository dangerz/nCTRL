// Protocol adapted from Hyundai-Kia-Connect/hyundai_kia_connect_api KiaUvoApiCA.py (MIT),
// revision 2dd630bd3f2898bdabb2525943145ee1c02567e0, with the one-time code flow and field fallbacks cross-checked
// against schmidtwmark/BetterBlueKit HyundaiCanadaAPI (MIT), revision 015720bc771487ee12e2bd17d8fd2ee772ad41e3.
// See THIRD_PARTY_NOTICES.md. No Python or Swift dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

public enum CanadaBrand { Hyundai, Kia }

/// <summary>Unofficial Hyundai Bluelink Canada / Kia Connect Canada adapter: one API (/tods/api/) on two hosts.
/// Secrets are memory-only here; the app decides what the vault keeps. Canada reports km and °C; this client converts
/// to miles and °F at the boundary so the rest of the app sees the same units as the US clients.
/// Never retries a command. Canada has no refresh request in this client: an expired or rejected session is replaced
/// by signing in again with the saved login and the same device id (so no new code). A read is then sent once more;
/// a command never is.</summary>
public sealed class CanadaClient : IVehicleConnection, IOneTimeCodeSignIn
{
    public static readonly ServiceInfo HyundaiInfo=new("Bluelink Canada","Hyundai",NeedsPin:true,OneTimeCode:true,
        LightsAndHorn:false,SurroundView:false,SendToCar:false,FrontSeats:true,RequestLocation:true,Heading:false);
    public static readonly ServiceInfo KiaInfo=new("Kia Connect Canada","Kia",NeedsPin:true,OneTimeCode:true,
        LightsAndHorn:false,SurroundView:false,SendToCar:false,FrontSeats:true,RequestLocation:true,Heading:false);
    // Public interoperability values from upstream (both brands use the Hyundai pair), not an owner's credentials.
    private const string ClientId="HATAHSPACA0232141ED9722C67715A0B",ClientSecret="CLISCR01AHSPA";
    // Upstream mfaApiCode for the sign-in challenge.
    private const string MfaCode="0107";
    // Session errors (upstream / BetterBlueKit): 7403 expired, 7602 token deleted, 7606 token bound to an IP the phone
    // no longer has (network switch). Canada checks these before acting, so a command refused with one never ran.
    private static readonly int[] SessionCodes={7403,7602,7606};
    // 7406 is "could not be processed"; BetterBlueKit sees it for expired tokens. Repaired for reads, never assumed for commands.
    private const int MaybeSessionCode=7406;

    private readonly CanadaBrand brand;
    private readonly string host,api;
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly RequestBudget budget;
    private string email="",password="",pin="",access="";
    private DateTimeOffset expires;
    private string mfaUuid="",mfaEmail="",mfaPhone="",mfaOtpKey="";
    private CodeDestinations destinations=new(null,null);
    // Model year per vehicle id: the climate temperature scale changed with model year 2020 (upstream).
    private readonly Dictionary<string,int> years=new();
    private DateTimeOffset submittedAt;

    public ServiceInfo Service { get; }
    public CanadaBrand CarBrand=>brand;
    /// <summary>Canada's refresh token from the latest sign-in. Canada recognizes the device by <see cref="DeviceId"/>;
    /// this token is kept so the saved login stores the device id with it. Secret.</summary>
    public string RememberToken { get; set; }="";
    public string DeviceId { get; set; }=Guid.NewGuid().ToString().ToUpperInvariant();
    public bool IsAuthenticated=>access.Length>0;
    public bool SessionValid=>access.Length>0&&clock.GetUtcNow()<expires.AddSeconds(-60);
    public EnrolledVehicle? Selected { get; private set; }
    public LiveVehicleState? State { get; private set; }
    public JsonElement? RawStatus { get; private set; }
    public RemoteReceipt? Pending { get; private set; }
    public VehicleLocation? Location { get; private set; }
    public SurroundView? SurroundView=>null;
    public bool CanRequestLocation=>budget.CanAcquire(RequestKind.Location);
    public RequestBudget Budget=>budget;
    private string Maker=>Service.Maker;

    public CanadaClient(CanadaBrand brand,RequestBudget? budget=null) : this(brand,new HttpClientHandler {
        // Never forward secrets on redirects. Cookies stay on (memory only): Cloudflare in front of both hosts sets them.
        AllowAutoRedirect=false, AutomaticDecompression=DecompressionMethods.All
    },null,budget) { }
    // Injection is for deterministic offline protocol tests; the host stays fixed per brand.
    public CanadaClient(CanadaBrand brand,HttpMessageHandler handler,TimeProvider? clock=null,RequestBudget? budget=null)
    {
        this.brand=brand;
        Service=brand==CanadaBrand.Kia?KiaInfo:HyundaiInfo;
        host=brand==CanadaBrand.Kia?"kiaconnect.ca":"mybluelink.ca";
        api="https://"+host+"/tods/api/";
        this.clock=clock??TimeProvider.System;
        this.budget=budget??new RequestBudget(new MemoryBudgetStore(),this.clock);
        // A live status read waits for the car to answer.
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(60),MaxResponseContentBufferSize=8*1024*1024};
    }

    public SharedSession? ExportSession()=>access.Length>0&&Selected is {} vehicle?new(access,RememberToken,expires,vehicle):null;
    public void ResumeSession(string email,string secret,string servicePin,SharedSession session)
    {
        Clear();
        this.email=email.Trim();password=secret;pin=servicePin;
        access=session.AccessToken;if(session.RefreshToken.Length>0)RememberToken=session.RefreshToken;
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
            if(string.IsNullOrWhiteSpace(email)||string.IsNullOrEmpty(secret)||servicePin.Length!=4||!servicePin.All(char.IsAsciiDigit))
                throw new BluelinkException($"Enter your {Service.ServiceName} email, password and four-digit PIN.");
            this.email=email.Trim();password=secret;pin=servicePin;
            budget.Acquire(RequestKind.SignIn,enforceCooldown:!implicitSignIn);
            if(!await SignInCore(ct))
                throw new OneTimeCodeRequiredException(destinations);
            stage="Vehicle list";
            return await VehiclesCore(ct,repair:false);
        }
        catch(OneTimeCodeRequiredException) { throw; } // Keep the account and the code request for the code steps.
        catch(Exception ex) { throw Fail(stage,ex,ct); }
        finally { gate.Release(); }
    }
    public async Task SendCodeAsync(CodeChannel channel,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(mfaUuid.Length==0)throw new BluelinkException($"Sign in first; {Maker} hasn't asked for a code.");
            if(channel==CodeChannel.Text&&mfaPhone.Length==0)throw new BluelinkException($"{Maker} has no phone number for a text. Nothing was sent.");
            // Each code counts toward the daily sign-in cap.
            budget.Acquire(RequestKind.SignIn,enforceCooldown:false,startsCooldown:false);
            var r=await RequestAsync(RequestKind.SignIn,"mfa/sendotp",RequestJson.Object(w=>{
                w.WriteString("otpMethod",channel==CodeChannel.Text?"S":"E");w.WriteString("mfaApiCode",MfaCode);
                w.WriteString("userAccount",mfaEmail);w.WriteString("userPhone",channel==CodeChannel.Text?mfaPhone:"");
                w.WriteString("userInfoUuid",mfaUuid);
            }),ct,"One-time code",session:false,device:true);
            if(Text(r.Body,"result.otpKey") is not {Length:>0} key)throw Failure("One-time code",r,$"{Maker} didn't confirm sending a code. Ask again.");
            mfaOtpKey=key;
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
            if(mfaUuid.Length==0)throw new BluelinkException($"Sign in first; {Maker} hasn't asked for a code.");
            if(mfaOtpKey.Length==0)throw new BluelinkException("Ask for a code first.");
            if(code.Length is <4 or >10||!code.All(char.IsAsciiDigit))throw new BluelinkException($"Enter the code {Maker} sent you (digits only).");
            budget.Acquire(RequestKind.SignIn,enforceCooldown:false,startsCooldown:false);
            // userAccount is the email as typed: Canada refuses its own upper-cased echo here (BetterBlueKit, code 7999).
            var checkedCode=await RequestAsync(RequestKind.SignIn,"mfa/validateotp",RequestJson.Object(w=>{
                w.WriteString("otpNo",code);w.WriteString("userAccount",email);w.WriteString("otpKey",mfaOtpKey);w.WriteString("mfaApiCode",MfaCode);
            }),ct,stage,session:false,device:true,checkStatus:false);
            if(!checkedCode.Ok||Bool(checkedCode.Body,"result.verifiedOtp")!=true||Text(checkedCode.Body,"result.otpValidationKey") is not {Length:>0} validation)
                throw new BluelinkException($"One-time code: {Maker} didn't accept that code. Ask for a new one and try again.",checkedCode.HttpStatus,ErrorCode(checkedCode.Body));
            var token=await RequestAsync(RequestKind.SignIn,"mfa/genmfatkn",RequestJson.Object(w=>{
                w.WriteString("userAccount",email);w.WriteString("otpEmail",mfaEmail);w.WriteString("mfaApiCode",MfaCode);
                w.WriteString("otpValidationKey",validation);w.WriteString("mfaYn","Y");
            }),ct,stage,session:false,device:true);
            if(!TakeToken(token.Body))throw new BluelinkException($"One-time code: {Maker} accepted the code but sent no session. Sign in again.");
            ClearMfa();
            stage="Vehicle list";
            return await VehiclesCore(ct,repair:false);
        }
        catch(Exception ex) { throw Fail(stage,ex,ct,clear:false); }
        finally { gate.Release(); }
    }
    /// <summary>True with a session; false when Canada wants a one-time code (its request is kept).</summary>
    private async Task<bool> SignInCore(CancellationToken ct)
    {
        var r=await RequestAsync(RequestKind.SignIn,"v2/login",RequestJson.Object(w=>{w.WriteString("loginId",email);w.WriteString("password",password);}),
            ct,"Sign-in",session:false,device:true,checkStatus:false);
        if(r.Ok)
        {
            if(TakeToken(r.Body))return true;
            throw Failure("Sign-in",r,$"{Maker} sent no session. Nothing else was sent.");
        }
        int? code=ErrorCode(r.Body);
        if(code==7110)
        {
            // A device Canada doesn't know yet: ask where it can send a code.
            var m=await RequestAsync(RequestKind.SignIn,"mfa/selverifmeth",RequestJson.Object(w=>{w.WriteString("mfaApiCode",MfaCode);w.WriteString("userAccount",email);}),
                ct,"Sign-in",session:false,device:true);
            mfaUuid=Text(m.Body,"result.userInfoUuid");
            if(mfaUuid.Length==0)throw Failure("Sign-in",m,$"{Maker} asked for a code but sent no code request. Nothing else was sent.");
            var list=At(m.Body,"result.emailList");
            string listed=list.ValueKind==JsonValueKind.Array&&list.GetArrayLength()>0&&list[0].ValueKind==JsonValueKind.String?list[0].GetString()??"":"";
            // Upstream falls back to the email as typed; email is always offered.
            mfaEmail=listed.Length>0?listed:email;mfaPhone=Text(m.Body,"result.userPhone");mfaOtpKey="";
            destinations=new(mfaEmail,mfaPhone.Length>0?mfaPhone:null);
            return false;
        }
        throw code switch {
            7401 or 7404 or 7435 or 7603=>new BluelinkException($"Sign-in: {Maker} didn't accept that email and password.",r.HttpStatus,code),
            7402 or 7530 or 7531 or 7532=>new BluelinkException($"Sign-in: {Maker} has locked this account after too many tries. Unlock it on {host}, then sign in again.",r.HttpStatus,code),
            7549=>new BluelinkException($"Sign-in: {Maker} wants a check this app can't complete. Sign in once on {host}, then try again. Nothing else was sent.",r.HttpStatus,code),
            _=>Failure("Sign-in",r,$"{Maker} sent no session. Nothing else was sent.")};
    }
    private bool TakeToken(JsonElement body)
    {
        var token=At(body,"result.token");
        if(Text(token,"accessToken") is not {Length:>0} value)return false;
        // Upstream subtracts 60 s; SessionValid and EnsureSession do the same here.
        double seconds=Number(token,"expireIn") is double s&&s>0?Math.Min(s,86400):1800;
        access=value;expires=clock.GetUtcNow().AddSeconds(seconds);
        if(Text(token,"refreshToken") is {Length:>0} refresh)RememberToken=refresh;
        return true;
    }
    private async Task<IReadOnlyList<EnrolledVehicle>> VehiclesCore(CancellationToken ct,bool repair)
    {
        Reply r;
        for(int attempt=0;;attempt++)
        {
            try{r=await RequestAsync(RequestKind.SignIn,"vhcllst",null,ct,"Vehicle list");break;}
            catch(SessionExpired) when(repair&&attempt==0){await RepairSession(ct);}
        }
        var list=At(r.Body,"result.vehicles");
        if(list.ValueKind!=JsonValueKind.Array)throw new BluelinkException($"Vehicle list: {Maker}'s reply had no vehicles.");
        var result=new List<EnrolledVehicle>();
        foreach(var item in list.EnumerateArray())
        {
            string id=Text(item,"vehicleId"),vin=Text(item,"vin");
            if(id.Length==0||vin.Length!=17)continue;
            string model=Text(item,"modelName").Trim();
            // fuelKindCode: E electric, P plug-in hybrid, G gas (upstream). Only E counts as electric here.
            // Generation 3 marks front-seat heat/vent support in the UI (Canada takes seat settings with climate).
            result.Add(new(id,vin,Text(item,"nickName"),model,3,Text(item,"fuelKindCode").Equals("E",StringComparison.OrdinalIgnoreCase)));
            years[id]=Integer(item,"modelYear") is int y&&y is >1990 and <2100?y:0;
        }
        if(result.Count==0)throw new BluelinkException($"Vehicle list: No vehicles were returned for this {Service.ServiceName} account.");
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
            Selected=vehicle;State=null;Location=null;Pending=null;
            await EnsureSession(ct);
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
        // lstvhclsts is Canada's cached report; rltmvhclsts asks the car for a new one (wakes it).
        var r=await VehicleRead(kind,wake?"rltmvhclsts":"lstvhclsts",ct,wake?"Wake vehicle":"Vehicle status");
        var status=At(r.Body,"result.status");
        var state=ParseState(status,Year(),clock.GetUtcNow());
        // Charge targets come from their own request (upstream evc/selsoc). A failure there keeps the status.
        if(Selected!.Electric&&(state.AcLimit is null||state.DcLimit is null))
        {
            try
            {
                var soc=await VehicleRead(kind,"evc/selsoc",ct,"Charge targets");
                var (ac,dc)=Targets(At(soc.Body,"result").ValueKind==JsonValueKind.Array?At(soc.Body,"result"):At(soc.Body,"result.tsoc"),"level");
                state=state with {AcLimit=state.AcLimit??ac,DcLimit=state.DcLimit??dc};
            }
            catch(BluelinkException ex) when(ex is not (RequestLimitException or SessionExpired)&&ex.HttpStatus is not (401 or 403 or 429)) { }
        }
        RawStatus=status;
        return state;
    }
    /// <summary>Find my car (upstream fndmcr): a PIN check, then the location request.</summary>
    public async Task<VehicleLocation> LocateAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            NeedVehicle();
            budget.Acquire(RequestKind.Location);
            await EnsureSession(ct);
            Reply r;
            for(int attempt=0;;attempt++)
            {
                try
                {
                    string pAuth=await PinToken(RequestKind.Location,ct);
                    r=await RequestAsync(RequestKind.Location,"fndmcr",PinBody(),ct,"Location",vehicle:true,pAuth:pAuth,extra:RemoteHeaders(false));
                    break;
                }
                catch(SessionExpired) when(attempt==0){await RepairSession(ct);}
            }
            var result=At(r.Body,"result");
            var coord=At(result,"coord").ValueKind==JsonValueKind.Object?At(result,"coord"):At(result,"gpsDetail.coord");
            var lat=Number(coord,"lat");var lon=Number(coord,"lon");
            if(lat is not double y||lon is not double x||Math.Abs(y)>90||Math.Abs(x)>180||y==0&&x==0)
                throw new BluelinkException($"{Maker} did not return a valid location.");
            string time=Text(result,"time").Length>0?Text(result,"time"):Text(result,"gpsDetail.time");
            Location=new(y,x,Reported(time,clock.GetUtcNow()),clock.GetUtcNow(),Heading(result));
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
            string route;Func<string> body=PinBody;Dictionary<string,string>? extra=null;
            switch(command.Kind)
            {
                case ActionKind.Lock: route="drlck";break;
                case ActionKind.Unlock: route="drulck";break;
                case ActionKind.Climate:
                    // Upstream sends gas and plug-in hybrid climate to rmtstrt/rmtstp; not offered here yet.
                    NeedElectric();
                    if(command.Enabled) {
                        if(command.Value<62||command.Value>82)throw new BluelinkException("Choose 62–82°F.");
                        route="evc/rfon";body=()=>ClimateBody(command.Value,command.Climate);
                    } else route="evc/rfoff";
                    break;
                case ActionKind.Charge:
                    NeedElectric();route=command.Enabled?"evc/rcstrt":"evc/rcstp";break;
                case ActionKind.ChargeLimit:
                    NeedElectric();
                    if(!ValidLimit(command.Value)||dcLimit is null||!ValidLimit(dcLimit.Value))
                        throw new BluelinkException("Choose both AC and DC targets: 50–100% in 10% steps.");
                    route="evc/setsoc";int ac=command.Value,dc=dcLimit.Value;
                    body=()=>RequestJson.Object(w=>{
                        w.WriteStartArray("tsoc");
                        w.WriteStartObject();w.WriteNumber("plugType",0);w.WriteNumber("level",dc);w.WriteEndObject();
                        w.WriteStartObject();w.WriteNumber("plugType",1);w.WriteNumber("level",ac);w.WriteEndObject();
                        w.WriteEndArray();w.WriteString("pin",pin);
                    });
                    extra=RemoteHeaders(true);break;
                case ActionKind.Lights: case ActionKind.HornLights:
                    throw new BluelinkException($"Lights and horn aren't available through {Service.ServiceName} in {Brand.App} yet. Nothing was sent.");
                default: throw new BluelinkException("Unsupported remote command.");
            }
            budget.Acquire(RequestKind.Command);
            await EnsureSession(ct);
            Pending=null;
            string pAuth;
            try { pAuth=await PinToken(RequestKind.Command,ct); }
            catch(SessionExpired) {
                await RepairSession(ct);
                throw new BluelinkException($"{Maker}'s session had expired. It's renewed now and nothing was sent; tap again.");
            }
            catch(HttpRequestException) { throw new BluelinkException("PIN check: Connection failed. Nothing was sent to the car."); }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new BluelinkException("PIN check: Request timed out. Nothing was sent to the car."); }
            string json=body();
            submittedAt=clock.GetUtcNow();
            try
            {
                var reply=await RequestAsync(RequestKind.Command,route,json,ct,"Command",vehicle:true,pAuth:pAuth,extra:extra);
                // HTTP success is acceptance, not evidence of vehicle state.
                string? tid=reply.Header("transactionId");
                Pending=new(RemoteOutcome.Accepted,$"{Maker} accepted the request (HTTP {reply.HttpStatus}). "+(tid is null?
                    "No tracking ID; completion unverified. Checking state automatically.":"Vehicle completion pending; checking automatically."),tid);
            }
            catch(OperationCanceledException) {
                Pending=new(RemoteOutcome.Unknown,"Request interrupted; outcome unknown. Check vehicle status before retrying.",null);throw;
            }
            catch(HttpRequestException) {
                Pending=new(RemoteOutcome.Unknown,"Connection lost; command outcome unknown. Refresh before retrying.",null);return Pending;
            }
            catch(SessionExpired expired) when(expired.Definite) {
                // Canada checks the session before acting, so nothing reached the car. Repair it for the next tap.
                Pending=null;
                await RepairSession(ct);
                throw new BluelinkException($"{Maker}'s session had expired. It's renewed now and nothing was sent; tap again.");
            }
            catch(SessionExpired expired) {
                // Possibly an expired session; not proof that nothing ran. Renew on the next request, never resend.
                expires=clock.GetUtcNow();
                Pending=new(RemoteOutcome.Unknown,"The last request was not confirmed. Refresh vehicle state before retrying.",null);
                throw new BluelinkException($"Command: {Maker} code {expired.Code}; the session may have expired. It will be renewed on the next request. Refresh before retrying; nothing was resent.",200,expired.Code);
            }
            catch(BluelinkException) {
                Pending=new(RemoteOutcome.Unknown,"The last request was not confirmed. Refresh vehicle state before retrying.",null);throw;
            }
            return Pending;
        }
        finally { gate.Release(); }
    }
    public Task<string?> SendDestinationAsync(Destination place,CancellationToken ct)=>
        throw new BluelinkException($"Send to car isn't available through {Service.ServiceName} in {Brand.App} yet. Nothing was sent.");
    public Task<SurroundView> CaptureSurroundViewAsync(Action<int>? checkedCount,CancellationToken ct)=>
        throw new BluelinkException($"Surround View isn't available through {Service.ServiceName} in {Brand.App} yet. Nothing was sent.");

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
            Reply r;
            for(int attempt=0;;attempt++)
            {
                try
                {
                    // Upstream sends a fresh PIN authorization with every status check.
                    string pAuth=await PinToken(RequestKind.Read,ct);
                    r=await RequestAsync(RequestKind.Read,"rmtsts",null,ct,"Command status",vehicle:true,pAuth:pAuth,
                        extra:new(StringComparer.OrdinalIgnoreCase){{"transactionId",Pending.TransactionId}});
                    break;
                }
                catch(SessionExpired) when(attempt==0){await RepairSession(ct);}
            }
            // Upstream: apiResult C is complete, P pending, anything else failed.
            string result=Text(r.Body,"result.transaction.apiResult");
            var outcome=result switch {"C"=>RemoteOutcome.Confirmed,"P" or ""=>RemoteOutcome.Accepted,_=>RemoteOutcome.Failed};
            Pending=Pending with {Outcome=outcome,Message=outcome switch {
                RemoteOutcome.Confirmed=>$"{Maker} confirmed completion. Refresh for reported vehicle state.",
                RemoteOutcome.Failed=>$"{Maker} reported command failure.",
                _=>"Command still pending or status unavailable. No retry was sent."}};
            if(clock.GetUtcNow()-submittedAt>TimeSpan.FromMinutes(2)&&Pending.Outcome==RemoteOutcome.Accepted)
                Pending=Pending with {Outcome=RemoteOutcome.Unknown,Message="Confirmation timed out. Refresh state before deciding to retry."};
            return Pending;
        }
        finally { gate.Release(); }
    }
    public Task MonitorCommandAsync(VehicleCommand command,int? dcLimit,LiveVehicleState? before,Action updated,CancellationToken ct)=>
        MonitorCommandAsync(command,dcLimit,before,updated,ct,(duration,token)=>Task.Delay(duration,clock,token));
    // Same follow-up as the US clients: read-only, never resubmits a command.
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
        if(access.Length==0)throw new BluelinkException($"Sign in to {Service.ServiceName} again.");
        if(clock.GetUtcNow()>=expires.AddSeconds(-60))await RepairSession(ct);
        // A resumed session has no model year yet (the climate temperature scale depends on it).
        if(Selected is {} v&&!years.ContainsKey(v.Id))await VehiclesCore(ct,repair:true);
    }
    // Canada has no refresh request here: sign in again with the saved login and the same device id, so Canada
    // recognizes the phone and asks no code. Automatic renewals are not budgeted, like the US clients' renewals.
    private async Task RepairSession(CancellationToken ct)
    {
        access="";
        if(email.Length==0||password.Length==0)throw new BluelinkException($"{Service.ServiceName} session ended. Sign in again.");
        if(!await SignInCore(ct)){ClearMfa();throw new BluelinkException($"{Maker} wants a new one-time code. Sign in again.");}
    }
    private sealed class SessionExpired(string maker,int code) : BluelinkException(maker+"'s session expired. Sign in again.",200,code)
    {
        public int Code { get; }=code;
        public bool Definite=>SessionCodes.Contains(Code);
    }
    // A read whose session Canada rejects is repaired and sent once more.
    private async Task<Reply> VehicleRead(RequestKind kind,string path,CancellationToken ct,string stage)
    {
        for(int attempt=0;;attempt++)
        {
            try { return await RequestAsync(kind,path,null,ct,stage,vehicle:true); }
            catch(SessionExpired) when(attempt==0) { await RepairSession(ct); }
        }
    }
    /// <summary>Upstream vrfypin: the four-digit PIN becomes a short-lived pAuth header for one remote request.</summary>
    private async Task<string> PinToken(RequestKind kind,CancellationToken ct)
    {
        Reply r;
        try { r=await RequestAsync(kind,"vrfypin",PinBody(),ct,"PIN check",vehicle:true); }
        catch(BluelinkException ex) when(ex is not (SessionExpired or RequestLimitException)&&ex.ApiErrorCode is int code&&PinProblem(code) is string problem)
        { throw new BluelinkException("PIN check: "+problem+" Nothing was sent to the car.",ex.HttpStatus,code); }
        if(Text(r.Body,"result.pAuth") is not {Length:>0} token)throw Failure("PIN check",r,$"{Maker} sent no PIN authorization. Nothing was sent to the car.");
        return token;
    }
    private string? PinProblem(int code)=>code switch {
        7310 or 7311 or 7312 or 7314 or 7315=>$"{Maker} didn't accept the four-digit PIN. Check the PIN saved with this login.",
        7316=>$"The PIN is locked after too many tries. Reset it in the car or on {host}.",
        7313=>$"{Maker}'s PIN authorization expired. Try again.",
        _=>null};
    private string PinBody()=>RequestJson.Object(w=>w.WriteString("pin",pin));
    // fndmcr and setsoc take the app channel (from SPA; kiaconnect.ca refuses CWP with 6459) and, on Kia, the portal's
    // remote-page referer. Kia's setsoc also carries offset -8 and a priority hint (upstream kia_uvo#957).
    private Dictionary<string,string> RemoteHeaders(bool setsoc)
    {
        var h=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"from","SPA"}};
        if(brand==CanadaBrand.Kia) {
            h["Referer"]=$"https://{host}/remote/";
            if(setsoc){h["offset"]="-8";h["priority"]="u=1, i";}
        }
        return h;
    }

    // ---- Transport ----

    private sealed record Reply(JsonElement Body,int HttpStatus,bool Ok,HttpResponseHeaders Headers)
    {
        public string? Header(string name)=>Headers.TryGetValues(name,out var v)?v.FirstOrDefault(x=>!string.IsNullOrEmpty(x)):null;
    }
    // Every Canada request is a POST. checkStatus=false hands Canada's own failure (and its HTTP status) to the caller.
    private async Task<Reply> RequestAsync(RequestKind kind,string path,string? body,CancellationToken ct,string stage,
        bool session=true,bool vehicle=false,string? pAuth=null,Dictionary<string,string>? extra=null,bool checkStatus=true,bool device=false)
    {
        budget.ThrowIfHyundaiBlocked(stage,kind);
        using var request=new HttpRequestMessage(HttpMethod.Post,api+path);
        var headers=Headers();
        // Upstream sends the device id with sign-in and code requests: it is how Canada recognizes the phone.
        if(device)headers["Deviceid"]=DeviceId;
        if(session){if(access.Length==0)throw new BluelinkException($"Sign in to {Service.ServiceName} again.");headers["accessToken"]=access;}
        if(vehicle){NeedVehicle();headers["vehicleId"]=Selected!.Id;}
        if(pAuth!=null)headers["pAuth"]=pAuth;
        if(extra!=null)foreach(var pair in extra)headers[pair.Key]=pair.Value;
        foreach(var pair in headers)request.Headers.TryAddWithoutValidation(pair.Key,pair.Value);
        // Upstream sends its JSON content-type on every request, bodiless ones included.
        request.Content=body is null?new ByteArrayContent(Array.Empty<byte>()):new StringContent(body,Encoding.UTF8);
        request.Content.Headers.ContentType=MediaTypeHeaderValue.Parse("application/json;charset=UTF-8");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseContentRead,ct);
        string text=await response.Content.ReadAsStringAsync(ct);
        JsonElement json=default;bool malformed=false;
        if(!string.IsNullOrWhiteSpace(text))
        {
            try{using var doc=JsonDocument.Parse(text);json=doc.RootElement.Clone();}
            catch(JsonException){malformed=true;}
        }
        bool shaped=At(json,"responseHeader.responseCode").ValueKind!=JsonValueKind.Undefined;
        var reply=new Reply(json,(int)response.StatusCode,response.IsSuccessStatusCode&&Success(json),response.Headers);
        if(response.StatusCode==HttpStatusCode.TooManyRequests){budget.RecordRateLimited(kind,response.Headers.RetryAfter?.Delta);throw Failure(stage,reply,$"{Maker} is rate limiting requests. No automatic retry was sent.");}
        if(!checkStatus&&shaped)return reply;
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden){access="";throw Failure(stage,reply,$"{Maker} denied access. Sign in again. No retry was sent.");}
        if(!response.IsSuccessStatusCode)throw Failure(stage,reply,$"{Maker} request failed; the cause is not established. No automatic retry was sent.");
        if(malformed||!shaped)throw Failure(stage,reply,"Unsupported response. No automatic retry was sent.");
        if(checkStatus&&!reply.Ok)
        {
            if(session&&ErrorCode(json) is int code&&(SessionCodes.Contains(code)||code==MaybeSessionCode))throw new SessionExpired(Maker,code);
            throw Failure(stage,reply,$"{Maker} refused the request; the cause is not established. No automatic retry was sent.");
        }
        return reply;
    }
    // Hyundai: upstream's app profile (myHyundai CA, kia_uvo#2054). Kia: upstream's web-portal profile, because
    // kiaconnect.ca refuses app-shaped sign-ins (7404). Accept-Encoding comes from the handler's decompression.
    private Dictionary<string,string> Headers()
    {
        var h=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if(brand==CanadaBrand.Hyundai) {
            h["User-Agent"]="okhttp/4.12.0";h["from"]="SPA";
            h["offset"]=((int)TimeZoneInfo.Local.GetUtcOffset(clock.GetUtcNow()).TotalHours).ToString(CultureInfo.InvariantCulture);
        } else {
            h["User-Agent"]="Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";
            h["Accept"]="application/json, text/plain, */*";h["Accept-Language"]="en-CA,en-US;q=0.8,en;q=0.5,fr;q=0.3";
            h["from"]="CWP";h["offset"]="-5";
            h["Sec-Fetch-Dest"]="empty";h["Sec-Fetch-Mode"]="cors";h["Sec-Fetch-Site"]="same-origin";h["Priority"]="u=0";
            h["Pragma"]="no-cache";h["Cache-Control"]="no-cache";
            h["Origin"]=$"https://{host}";h["Referer"]=$"https://{host}/login";
        }
        h["language"]="0";h["client_id"]=ClientId;h["client_secret"]=ClientSecret;
        return h;
    }
    // responseCode 0 is success, 1 failure. Booleans and strings are accepted too (BetterBlueKit).
    private static bool Success(JsonElement json)
    {
        var c=At(json,"responseHeader.responseCode");
        return c.ValueKind switch {
            JsonValueKind.Number=>c.TryGetInt32(out int n)&&n==0,JsonValueKind.False=>true,
            JsonValueKind.String=>c.GetString() is "0" or "false",_=>false};
    }
    private static int? ErrorCode(JsonElement json)=>Text(json,"error.errorCode") is {Length:>0 and <=6} t&&t.All(char.IsAsciiDigit)?int.Parse(t,CultureInfo.InvariantCulture):null;
    // Only Canada's numeric codes leave the transport. Never copy errorDesc, identifiers or bodies into errors.
    private BluelinkException Failure(string stage,Reply r,string detail)
    {
        int? code=ErrorCode(r.Body);
        return new($"{stage}: HTTP {r.HttpStatus}"+(code is int n?$"; {Maker} code {n}":"")+". "+detail,r.HttpStatus,code);
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

    /// <summary>Maps Canada's result.status (lstvhclsts / rltmvhclsts; upstream _update_vehicle_properties_base) to the
    /// app's units: miles and °F.</summary>
    public static LiveVehicleState ParseState(JsonElement v,int modelYear,DateTimeOffset received)
    {
        if(v.ValueKind!=JsonValueKind.Object)throw new BluelinkException("Vehicle status format is unsupported. No new vehicle state was loaded.");
        var (ac,dc)=Targets(At(v,"evStatus.reservChargeInfos.targetSOClist").ValueKind==JsonValueKind.Array?
            At(v,"evStatus.reservChargeInfos.targetSOClist"):At(v,"evStatus.targetSOC"),"targetSOClevel");
        double? range=Miles(v,"evStatus.drvDistance.0.rangeByFuel.totalAvailableRange")??Miles(v,"evStatus.drvDistance.0.rangeByFuel.evModeRange")??Miles(v,"dte");
        bool? plugged=At(v,"evStatus.batteryPlugin").ValueKind==JsonValueKind.Number?Integer(v,"evStatus.batteryPlugin")>0:Bool(v,"evStatus.batteryPlugin");
        bool? Lamp(string name)=>Bool(v,"tirePressureLamp.tirePressureLamp"+name)??Bool(v,"tirePressureLamp.tirePressureWarningLamp"+name);
        bool? tires=Lamp("All")??Bool(v,"tirePressureLamp.all");
        if(tires==null) {
            var each=new[]{Lamp("FL")??Lamp("FrontLeft"),Lamp("FR")??Lamp("FrontRight"),Lamp("RL")??Lamp("RearLeft"),Lamp("RR")??Lamp("RearRight")};
            tires=each.Any(t=>t==true)?true:each.All(t=>t==false)?false:null;
        }
        bool? Open(string key)=>Integer(v,key) is int n?n!=0:Bool(v,key);
        var health=new VehicleHealth(Percent(Integer(v,"battery.batSoc")),
            Open("doorOpen.frontLeft"),Open("doorOpen.frontRight"),Open("doorOpen.backLeft"),Open("doorOpen.backRight"),
            Open("trunkOpen"),Open("hoodOpen"),
            Open("windowOpen.frontLeft"),Open("windowOpen.frontRight"),Open("windowOpen.backLeft"),Open("windowOpen.backRight"),
            tires,Bool(v,"washerFluidStatus"),Bool(v,"breakOilStatus"),Bool(v,"smartKeyBatteryWarning"));
        // Canada's status has no location (upstream asks fndmcr separately); use one only if it appears.
        var loc=At(v,"vehicleLocation").ValueKind==JsonValueKind.Object?At(v,"vehicleLocation"):v;
        var lat=Number(loc,"coord.lat");var lon=Number(loc,"coord.lon");
        var located=lat is double y&&lon is double x&&Math.Abs(y)<=90&&Math.Abs(x)<=180&&!(y==0&&x==0)?
            new VehicleLocation(y,x,Reported(Text(loc,"time"),received),received,Heading(loc)):null;
        bool? charging=Bool(v,"evStatus.batteryCharge");
        // remainTime2.atc is the current charge's estimate in minutes (unit 1).
        int? minutes=Integer(v,"evStatus.remainTime2.atc.unit") is null or 1&&Integer(v,"evStatus.remainTime2.atc.value") is int left&&left is >0 and <10000?left:null;
        double? power=Number(v,"evStatus.realTimePower")??
            (Number(v,"evStatus.batteryPower.batteryFstChrgPower") is double fast&&Number(v,"evStatus.batteryPower.batteryStndChrgPower") is double slow?Math.Max(fast,slow):null);
        static int? Seat(int? code)=>code is >=0 and <=8?code:null;
        return new(Bool(v,"doorLock"),Bool(v,"airCtrlOn")??Bool(v,"airCtrl"),TemperatureF(v,modelYear),Percent(Integer(v,"evStatus.batteryStatus")),
            plugged,charging,range,ac,dc,Reported(Text(v,"lastStatusDate"),received),received,
            DriverSeat:Seat(Integer(v,"seatHeaterVentState.flSeatHeatState")),PassengerSeat:Seat(Integer(v,"seatHeaterVentState.frSeatHeatState")),
            SteeringWheelHeat:Integer(v,"steerWheelHeat") is int wheel?wheel!=0:Bool(v,"steerWheelHeat"),
            Health:health,StatusLocation:located,
            ChargingPowerKw:charging==true&&power is double kw&&kw>=0&&kw<1000?kw:null,
            ChargeMinutesLeft:charging==true?minutes:null,
            Moving:null,Defrost:Bool(v,"defrost"));
    }
    // Last matching entry wins (upstream). Levels outside 1–100 are not limits.
    private static (int? Ac,int? Dc) Targets(JsonElement list,string field)
    {
        int? ac=null,dc=null;
        if(list.ValueKind==JsonValueKind.Array)foreach(var t in list.EnumerateArray()) {
            if(Integer(t,field) is not int level||level is <=0 or >100)continue;
            if(Integer(t,"plugType")==1)ac=level;
            if(Integer(t,"plugType")==0)dc=level;
        }
        return (ac,dc);
    }
    /// <summary>Canada's set temperature in °F. Canada sends a hex index ("10H") into a half-degree Celsius scale that
    /// starts at 14.0 °C from model year 2020 and 16.0 °C before (upstream temperature_range_c_new / _old).</summary>
    internal static int? TemperatureF(JsonElement v,int modelYear)
    {
        string raw=Text(v,"airTemp.value").Trim().ToUpperInvariant();
        int? unit=Integer(v,"airTemp.unit");
        if(raw is "LO" or "LOW")return 62;
        if(raw is "HI" or "HIGH")return 82;
        if(raw.Length is >=2 and <=3&&raw.EndsWith('H')&&int.TryParse(raw[..^1],NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out int index))
        {
            if(unit is not (null or 0))return null;
            var (start,max)=Scale(modelYear);
            return index>=0&&index<=max?(int)Math.Round(Units.Fahrenheit(start+index*0.5),MidpointRounding.AwayFromZero):null;
        }
        if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n))return null;
        return unit switch {0=>(int)Math.Round(Units.Fahrenheit(n),MidpointRounding.AwayFromZero),1=>(int)Math.Round(n,MidpointRounding.AwayFromZero),_=>null};
    }
    /// <summary>The hex code Canada takes for a °F setpoint: converted to Celsius and rounded to the half degree, the
    /// way <see cref="Units.Setpoint"/> shows it, so the car gets the °C the user saw.</summary>
    internal static string HexTemperature(int fahrenheit,int modelYear)
    {
        double celsius=Math.Round(Units.Celsius(fahrenheit)*2)/2;
        var (start,max)=Scale(modelYear);
        int index=Math.Clamp((int)Math.Round((celsius-start)*2),0,max);
        return index.ToString("X2",CultureInfo.InvariantCulture)+"H";
    }
    // Upstream: an unknown model year uses the older scale.
    private static (double Start,int Max) Scale(int modelYear)=>modelYear>=2020?(14.0,35):(16.0,31);
    private int Year()=>Selected is {} v&&years.TryGetValue(v.Id,out int y)?y:0;
    // evc/rfon, upstream start_climate (EV). Rear seats stay off; heating1 3 is the steering wheel (upstream HEAT_STATUS).
    // EV9 and IONIQ 9 take the same settings under "remoteControl" (upstream picks by name; here by model).
    private string ClimateBody(int fahrenheit,ClimateOptions? options)=>RequestJson.Object(w=>{
        options??=new();
        string model=Selected!.Model.ToUpperInvariant();
        bool remote=brand==CanadaBrand.Kia?model.Contains("EV9"):model.Contains("IONIQ 9");
        w.WriteString("pin",pin);
        w.WriteStartObject(remote?"remoteControl":"hvacInfo");
        w.WriteNumber("airCtrl",1);w.WriteBoolean("defrost",options.Defrost);w.WriteNumber("heating1",options.SteeringWheel?3:0);
        w.WriteStartObject("airTemp");w.WriteString("value",HexTemperature(fahrenheit,Year()));w.WriteNumber("unit",0);w.WriteNumber("hvacTempType",1);w.WriteEndObject();
        w.WriteNumber("igniOnDuration",5);
        w.WriteStartObject("seatHeaterVentCMD");
        w.WriteNumber("drvSeatOptCmd",SeatCodes.Code(options.DriverSeat));w.WriteNumber("astSeatOptCmd",SeatCodes.Code(options.PassengerSeat));
        w.WriteNumber("rlSeatOptCmd",0);w.WriteNumber("rrSeatOptCmd",0);
        w.WriteEndObject();
        w.WriteEndObject();
    });
    /// <summary>Canada's compact time (yyyyMMddHHmmss). Upstream sees both UTC and the car's Canadian local time here,
    /// so it becomes ISO UTC only when it reads as within 20 minutes of now (a local time read as UTC is hours off);
    /// otherwise it stays as sent, which the app shows without guessing a timezone.</summary>
    internal static string Reported(string compact,DateTimeOffset received)=>
        compact.Length==14&&DateTime.TryParseExact(compact,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var t)&&
        (new DateTimeOffset(t,TimeSpan.Zero)-received).Duration()<=TimeSpan.FromMinutes(20)?
            t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture):compact;
    // Distance units (upstream DISTANCE_UNITS): 1 km, 2 and 3 miles. Canada reports km.
    private static double? Miles(JsonElement v,string path)
    {
        var value=Number(v,path+".value");var unit=Integer(v,path+".unit");
        double? miles=unit==1?value/Units.KmPerMile:unit is 2 or 3?value:null;
        return miles is >=0?miles:null;
    }
    private static double? Heading(JsonElement e)=>Number(e,"head") is double h&&h>=0&&h<=360?h%360:null;

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

    private void ClearMfa(){mfaUuid="";mfaEmail="";mfaPhone="";mfaOtpKey="";}
    private void Clear(){email="";password="";pin="";access="";ClearMfa();years.Clear();Selected=null;State=null;RawStatus=null;Pending=null;Location=null;}
    public void Dispose(){Clear();RememberToken="";http.Dispose();}
}
