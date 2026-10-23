using NGarage.Core;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

static class VaultTests
{
    sealed class Bridge : IVaultBridge
    {
        public bool Present=>true;
        public bool Available=true,Saved,Hang,Fail;
        public int Starts,Cancels;
        public string? Data;
        string result="";
        public int Availability()=>Available?2:0;
        public bool HasAccount()=>Saved;
        public void Start(string op,string payload){Starts++;if(Hang)return;if(Fail){result="{\"ok\":false,\"code\":\"cancelled\"}";return;}
            if(op=="save"){Data=payload;Saved=true;}if(op=="forget"){Data=null;Saved=false;}
            result=JsonSerializer.Serialize(new{ok=true,code="",data=op=="unlock"?Data:""});}
        public string TakeResult(){var r=result;result="";return r;}
        public void Cancel(){Cancels++;result="";}
    }
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception("Vault: "+name);checks++;}
    static async Task Rejected(Func<Task> f,string code){try{await f();}catch(VaultException e){Check(e.Code==code,"Expected error "+code);return;}throw new Exception("Vault did not reject");}
    public static async Task Run()
    {
        var account=new SavedAccount("tester@example.invalid","p@ss\"日本語\\word","1234");
        var decoded=SavedAccount.Decode(account.Encode());Check(decoded.Password==account.Password&&decoded.Email==account.Email&&decoded.Pin==account.Pin,"Round-trip secrets without altering Unicode");
        Check(!account.ToString().Contains(account.Password)&&!account.ToString().Contains(account.Email),"ToString redacted");
        var absent=new AccountVault(new UnavailableVaultBridge());
        Check(!absent.Available&&!absent.HasAccount,"Unsupported platform has no storage fallback");
        await Rejected(()=>absent.SaveAsync(account,default),"unavailable");
        await Rejected(()=>absent.UnlockAsync(default),"unavailable");
        var bridge=new Bridge();var vault=new AccountVault(bridge);
        await vault.SaveAsync(account,default);Check(vault.HasAccount,"Saved only after successful bridge response");
        Check(vault.BiometricName=="Face ID","iPhone biometric label");
        var read=await vault.UnlockAsync(default);Check(read.Password==account.Password,"Authenticated bridge data returned");
        var reopened=new AccountVault(bridge);Check(reopened.HasAccount&&(await reopened.UnlockAsync(default)).Password==account.Password,"New coordinator unlocks an existing native account");
        bridge.Fail=true;await Rejected(()=>vault.UnlockAsync(default),"cancelled");Check(vault.HasAccount,"Cancelled unlock preserves encrypted account");bridge.Fail=false;
        bridge.Available=false;await Rejected(()=>vault.UnlockAsync(default),"unavailable");
        await vault.ForgetAsync(default);Check(!vault.HasAccount&&bridge.Data==null,"Forget still works without enrolled biometrics");bridge.Available=true;
        bridge.Hang=true;using(var cts=new CancellationTokenSource(20)){
            try{await vault.UnlockAsync(cts.Token);throw new Exception("No cancellation");}
            catch(OperationCanceledException){Check(bridge.Cancels==1,"Cancellation cancels native operation");}}
        bridge.Hang=false;await vault.SaveAsync(account,default);Check(vault.HasAccount,"Serialized gate recovers after cancellation");
        int before=bridge.Starts;using(var cts=new CancellationTokenSource()){cts.Cancel();try{await vault.UnlockAsync(cts.Token);}catch(OperationCanceledException){}Check(bridge.Starts==before,"Pre-cancelled operation never starts native request");}
        try{SavedAccount.Decode("{\"version\":2}");throw new Exception("Accepted invalid format");}catch(VaultException){Check(true,"Reject unsupported schema");}
        try{SavedAccount.Decode("secret malformed payload");throw new Exception("Accepted malformed payload");}catch(VaultException e){Check(!e.Message.Contains("secret"),"Malformed response never reflected in error");}
        Console.WriteLine($"PASS: {checks} secure-login coordinator checks (fake native bridge, not device biometrics).");
    }
}
