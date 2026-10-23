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
    // 0 unavailable, 1 Touch ID, 2 Face ID, 3 Android strong biometrics.
    int Availability();
    bool HasAccount();
    void Start(string operation,string payload);
    string TakeResult();
    void Cancel();
}
public sealed class UnavailableVaultBridge : IVaultBridge
{
    public bool Present=>false;
    public int Availability()=>0;
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
