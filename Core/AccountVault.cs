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
    public string Pin { get; }
    public SavedAccount(string email,string password,string pin)
    {
        if(string.IsNullOrWhiteSpace(email)||email.Length>320||password.Length is <1 or >4096||pin.Length!=4)
            throw new VaultException("invalid_data");
        foreach(char c in pin)if(!char.IsAsciiDigit(c))throw new VaultException("invalid_data");
        Email=email;Password=password;Pin=pin;
    }
    public override string ToString()=>"SavedAccount [redacted]";
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)){
            writer.WriteStartObject();writer.WriteNumber("version",1);writer.WriteString("email",Email);
            writer.WriteString("password",Password);writer.WriteString("pin",Pin);writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static SavedAccount Decode(string json)
    {
        try {
            if(json.Length>65536)throw new VaultException("invalid_data");
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)throw new VaultException("invalid_data");
            return new(r.GetProperty("email").GetString()!,r.GetProperty("password").GetString()!,r.GetProperty("pin").GetString()!);
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
    public bool Available=>bridge.Present&&bridge.Availability()>0;
    public bool HasAccount=>bridge.Present&&bridge.HasAccount();
    public string BiometricName=>bridge.Availability() switch {1=>"Touch ID",2=>"Face ID",_=>"biometrics"};
    public string UnavailableReason=>bridge.Availability() switch {
        -1=>"Set a device passcode in Settings to use secure saved login.",
        -2=>"Set up Face ID or Touch ID in Settings to use secure saved login.",
        -3=>"Biometrics are locked out. Unlock your device with its passcode, then reopen nCTRL.",
        -4=>"This iOS build is missing its Face ID permission description. Re-export with nCTRL Secure Vault enabled.",
        -6=>"This build is missing the native secure-storage bridge. Install a rebuilt mobile app.",
        _=>Present?"Biometrics are unavailable. Check biometric enrollment and app permission in Settings.":
            "Secure saved login is available in supported phone builds. Manual login here is session-only."
    };
    public async Task SaveAsync(SavedAccount account,CancellationToken ct)
    {
        if(!Available)throw new VaultException("unavailable");
        await Request("save",account.Encode(),ct);
    }
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
    // The car's last known location: its own device-only item, shown by the app only after Face ID. Empty text means none.
    public Task<string> LoadLocationAsync(CancellationToken ct)=>bridge.Present?Request("location_load","",ct):throw new VaultException("unavailable");
    public Task SaveLocationAsync(string json,CancellationToken ct)=>bridge.Present?Request("location_save",json,ct):throw new VaultException("unavailable");
    public Task ForgetLocationAsync(CancellationToken ct)=>bridge.Present?Request("location_forget","",ct):throw new VaultException("unavailable");
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
