using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

// Deliberately not a record: generated ToString() must not expose credentials.
public sealed class SavedAccount
{
    public string Email { get; }
    public string Password { get; }
    // Hyundai's four-digit service PIN; empty for services without one (Kia Connect).
    public string Pin { get; }
    // Kia Connect's remember token (lets sign-in skip the one-time code) and the device id it belongs to.
    public string RememberToken { get; }
    public string DeviceId { get; }
    // The car service this login belongs to ("hyundai" or "kia"). Logins saved before Kia support are Hyundai's.
    public string Service { get; }
    public SavedAccount(string email,string password,string pin,string rememberToken="",string deviceId="",string service=Brand.Hyundai)
    {
        if(string.IsNullOrWhiteSpace(email)||email.Length>320||password.Length is <1 or >4096||pin.Length is not (0 or 4)||
           rememberToken.Length>8192||deviceId.Length>128||service is not (Brand.Hyundai or Brand.Kia))
            throw new VaultException("invalid_data");
        foreach(char c in pin)if(!char.IsAsciiDigit(c))throw new VaultException("invalid_data");
        Email=email;Password=password;Pin=pin;RememberToken=rememberToken;DeviceId=deviceId;Service=service;
    }
    public SavedAccount WithRemember(string rememberToken,string deviceId)=>new(Email,Password,Pin,rememberToken,deviceId,Service);
    public override string ToString()=>"SavedAccount [redacted]";
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)){
            writer.WriteStartObject();writer.WriteNumber("version",2);writer.WriteString("email",Email);
            writer.WriteString("password",Password);writer.WriteString("pin",Pin);
            writer.WriteString("remember",RememberToken);writer.WriteString("device",DeviceId);writer.WriteString("service",Service);writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static SavedAccount Decode(string json)
    {
        try {
            if(json.Length>65536)throw new VaultException("invalid_data");
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            int version=r.GetProperty("version").GetInt32();
            if(version is not (1 or 2))throw new VaultException("invalid_data");
            string Optional(string key)=>version==2&&r.TryGetProperty(key,out var v)?v.GetString()??"":"";
            return new(r.GetProperty("email").GetString()!,r.GetProperty("password").GetString()!,r.GetProperty("pin").GetString()!,
                Optional("remember"),Optional("device"),Optional("service") is {Length:>0} service?service:Brand.Hyundai);
        } catch(VaultException){throw;}catch{throw new VaultException("invalid_data");}
    }
}
public sealed class VaultException : Exception
{
    public string Code { get; }
    public VaultException(string code):base(code switch {
        "cancelled"=>"Biometric authentication was cancelled. Your account remains locked.",
        "unavailable"=>"Secure biometric storage is unavailable. Sign in manually; nothing will be saved.",
        "missing"=>"No saved account is available. Sign in to save it again.",
        "invalidated"=>"Saved access is no longer valid. Sign in again after changing biometrics or your device lock.",
        "busy"=>"Another secure-storage request is in progress.",
        "auth_failed"=>"Biometric authentication did not succeed. Your account remains locked.",
        _=>"Secure storage could not complete the request. Sign in manually or try again."
    }) { Code=code; }
}
public interface IVaultBridge
{
    bool Present { get; }
    // Positive: 1 Touch ID, 2 Face ID, 3 Android strong. Zero: unavailable.
    // Negative: -1 passcode missing, -2 not enrolled, -3 locked out, -4 iOS usage text missing, -6 bridge missing.
    int Availability();
    bool HasAccount();
    void Start(string operation,string payload);
    string TakeResult();
    void Cancel();
}
public sealed class UnavailableVaultBridge(int reason=0) : IVaultBridge
{
    public bool Present=>false;
    public int Availability()=>reason;
    public bool HasAccount()=>false;
    public void Start(string operation,string payload)=>throw new VaultException("unavailable");
    public string TakeResult()=>"";
    public void Cancel() { }
}
public sealed class AccountVault
{
    private readonly IVaultBridge bridge;
    private readonly SemaphoreSlim gate=new(1,1);
    public AccountVault(IVaultBridge bridge)=>this.bridge=bridge;
    public bool Present=>bridge.Present;
    // The saved login needs no Face ID (like the Hyundai app), only the native Keychain bridge.
    public bool Available=>bridge.Present;
    public bool HasAccount=>bridge.Present&&bridge.HasAccount();
    public string BiometricName=>bridge.Availability() switch {1=>"Touch ID",2=>"Face ID",_=>"biometrics"};
    public string UnavailableReason=>bridge.Availability()==-6?"This build is missing the native secure-storage bridge. Install a rebuilt mobile app.":
        "Saved login is available in supported phone builds. Manual login here is session-only.";
    public async Task SaveAsync(SavedAccount account,CancellationToken ct)
    {
        if(!Available)throw new VaultException("unavailable");
        await Request("save",account.Encode(),ct);
    }
    // Reads the saved login without a prompt. A login saved by an older build (behind Face ID) asks for
    // Face ID once and is moved to the new item.
    public async Task<SavedAccount> UnlockAsync(CancellationToken ct)
    {
        if(!Available)throw new VaultException("unavailable");
        string data=await Request("unlock","",ct);
        return SavedAccount.Decode(data);
    }
    // Device-owner check (biometrics with passcode fallback) for acknowledgements and overrides.
    // Needs only the native bridge, not enrolled biometrics or a saved account.
    public async Task AuthenticateAsync(string reason,CancellationToken ct)
    {
        if(!bridge.Present)throw new VaultException("unavailable");
        await Request("authenticate",reason,ct);
    }
    // Ignored places live in their own Keychain item, without a biometric gate. Empty text means none saved.
    public Task<string> LoadPlacesAsync(CancellationToken ct)=>bridge.Present?Request("places_load","",ct):throw new VaultException("unavailable");
    public Task SavePlacesAsync(string json,CancellationToken ct)=>bridge.Present?Request("places_save",json,ct):throw new VaultException("unavailable");
    public Task ForgetPlacesAsync(CancellationToken ct)=>bridge.Present?Request("places_forget","",ct):throw new VaultException("unavailable");
    // The car's last known location: its own device-only item. Empty text means none.
    public Task<string> LoadLocationAsync(CancellationToken ct)=>bridge.Present?Request("location_load","",ct):throw new VaultException("unavailable");
    public Task SaveLocationAsync(string json,CancellationToken ct)=>bridge.Present?Request("location_save",json,ct):throw new VaultException("unavailable");
    public Task ForgetLocationAsync(CancellationToken ct)=>bridge.Present?Request("location_forget","",ct):throw new VaultException("unavailable");
    // Hyundai's session for the Share extension (SharedSession), in the App Group's Keychain group. Empty text means none.
    public Task<string> LoadSessionAsync(CancellationToken ct)=>bridge.Present?Request("session_load","",ct):throw new VaultException("unavailable");
    public Task SaveSessionAsync(string json,CancellationToken ct)=>bridge.Present?Request("session_save",json,ct):throw new VaultException("unavailable");
    public Task ForgetSessionAsync(CancellationToken ct)=>bridge.Present?Request("session_forget","",ct):throw new VaultException("unavailable");
    // Places shared to nCTRL from other apps: their own device-only item. Empty text means none.
    public Task<string> LoadSharedAsync(CancellationToken ct)=>bridge.Present?Request("shared_load","",ct):throw new VaultException("unavailable");
    public Task SaveSharedAsync(string json,CancellationToken ct)=>bridge.Present?Request("shared_save",json,ct):throw new VaultException("unavailable");
    public Task ForgetSharedAsync(CancellationToken ct)=>bridge.Present?Request("shared_forget","",ct):throw new VaultException("unavailable");
    public async Task ForgetAsync(CancellationToken ct)
    {
        // Deletion must still work when enrollment was changed or biometrics are unavailable.
        if(!bridge.Present)throw new VaultException("unavailable");
        await Request("forget","",ct);
    }
    private async Task<string> Request(string operation,string payload,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        bool completed=false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try {
            timeout.Token.ThrowIfCancellationRequested();
            bridge.Start(operation,payload);
            while(true) {
                timeout.Token.ThrowIfCancellationRequested();
                string result=bridge.TakeResult();
                if(result.Length>0) {
                    completed=true;
                    try {
                        using var doc=JsonDocument.Parse(result);var r=doc.RootElement;
                        if(!r.GetProperty("ok").GetBoolean())throw new VaultException(r.GetProperty("code").GetString()??"failed");
                        return r.TryGetProperty("data",out var data)?data.GetString()??"":"";
                    }catch(VaultException){throw;}catch{throw new VaultException("invalid_data");}
                }
                await Task.Delay(40,timeout.Token);
            }
        }
        finally {if(!completed)bridge.Cancel();gate.Release();}
    }
    public void Cancel()=>bridge.Cancel();
}
