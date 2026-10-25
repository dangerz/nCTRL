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
        public int Status=2;
        public string? Data;
        public string Places="";
        string result="";
        public int Availability()=>Available?Status:0;
        public bool HasAccount()=>Saved;
        public void Start(string op,string payload){Starts++;if(Hang)return;if(Fail){result="{\"ok\":false,\"code\":\"cancelled\"}";return;}
            if(op=="save"){Data=payload;Saved=true;}if(op=="forget"){Data=null;Saved=false;}
            if(op=="places_save")Places=payload;if(op=="places_forget")Places="";
            result=RequestJson.Object(w=>{w.WriteBoolean("ok",true);w.WriteString("code","");w.WriteString("data",op=="unlock"?Data:op=="places_load"?Places:"");});}
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
        await Rejected(()=>absent.AuthenticateAsync("Confirm",default),"unavailable");
        var bridge=new Bridge();var vault=new AccountVault(bridge);
        await vault.AuthenticateAsync("Confirm",default);Check(!vault.HasAccount,"Device check needs no saved account and saves nothing");
        bridge.Available=false;await vault.AuthenticateAsync("Confirm",default);Check(true,"Device check allows passcode when biometrics are unavailable");bridge.Available=true;
        bridge.Fail=true;await Rejected(()=>vault.AuthenticateAsync("Confirm",default),"cancelled");bridge.Fail=false;
        await vault.SaveAsync(account,default);Check(vault.HasAccount,"Saved only after successful bridge response");
        Check(vault.BiometricName=="Face ID","iPhone biometric label");
        var read=await vault.UnlockAsync(default);Check(read.Password==account.Password,"Authenticated bridge data returned");
        var reopened=new AccountVault(bridge);Check(reopened.HasAccount&&(await reopened.UnlockAsync(default)).Password==account.Password,"New coordinator unlocks an existing native account");
        bridge.Fail=true;await Rejected(()=>vault.UnlockAsync(default),"cancelled");Check(vault.HasAccount,"Cancelled unlock preserves encrypted account");bridge.Fail=false;
        bridge.Available=false;Check(vault.Available&&(await vault.UnlockAsync(default)).Password==account.Password,"The saved login needs no biometrics");
        await vault.ForgetAsync(default);Check(!vault.HasAccount&&bridge.Data==null,"Forget still works without enrolled biometrics");bridge.Available=true;
        bridge.Hang=true;using(var cts=new CancellationTokenSource(20)){
            try{await vault.UnlockAsync(cts.Token);throw new Exception("No cancellation");}
            catch(OperationCanceledException){Check(bridge.Cancels==1,"Cancellation cancels native operation");}}
        bridge.Hang=false;await vault.SaveAsync(account,default);Check(vault.HasAccount,"Serialized gate recovers after cancellation");
        foreach(int status in new[]{-1,-2,-3,-4}) {
            bridge.Status=status;
            Check(vault.Available&&(await vault.UnlockAsync(default)).Pin==account.Pin,"Saving and reading the login don't depend on Face ID being set up");
        }
        await vault.ForgetAsync(default);Check(!vault.HasAccount,"Missing Face ID usage text still allows forgetting an account");
        bridge.Status=2;await vault.SaveAsync(account,default);
        var missingBridge=new AccountVault(new UnavailableVaultBridge(-6));
        Check(!missingBridge.Available&&missingBridge.UnavailableReason.Contains("native secure-storage bridge"),"Missing bridge distinguished from biometrics unavailable");
        int before=bridge.Starts;using(var cts=new CancellationTokenSource()){cts.Cancel();try{await vault.UnlockAsync(cts.Token);}catch(OperationCanceledException){}Check(bridge.Starts==before,"Pre-cancelled operation never starts native request");}
        try{SavedAccount.Decode("{\"version\":2}");throw new Exception("Accepted invalid format");}catch(VaultException){Check(true,"Reject unsupported schema");}
        try{SavedAccount.Decode("secret malformed payload");throw new Exception("Accepted malformed payload");}catch(VaultException e){Check(!e.Message.Contains("secret"),"Malformed response never reflected in error");}
        var placesVault=new AccountVault(new Bridge{Available=false});
        Check(await placesVault.LoadPlacesAsync(default)=="","No saved places reads as empty, without biometrics");
        await placesVault.SavePlacesAsync("{\"version\":1,\"places\":[]}",default);
        Check((await placesVault.LoadPlacesAsync(default)).Contains("places"),"Places are saved and read back");
        await placesVault.ForgetPlacesAsync(default);Check(await placesVault.LoadPlacesAsync(default)=="","Forgetting places clears them");
        await Rejected(()=>absent.LoadPlacesAsync(default),"unavailable");
        Console.WriteLine($"PASS: {checks} secure-login coordinator checks (fake native bridge, not device biometrics).");
    }
}
