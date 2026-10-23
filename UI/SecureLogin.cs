using Godot;
using NGarage.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

public partial class Main
{
    private AccountVault vault=new(new UnavailableVaultBridge());
    private bool rememberDevice=true,manualAccountEntry;
    private int liveEpoch;
    private bool vaultReady;
    private void InitializeVault()
    {
        vault=new AccountVault(smoke?new UnavailableVaultBridge():NativeVaultBridge.Create());vaultReady=true;
        deviceCheckRequired=OS.GetName() is "iOS" or "Android";
        if(vault.HasAccount){screen="Connect";manualAccountEntry=false;}
    }
    private void EnsureLive(int version)
    {if(version!=liveEpoch || liveLifetime.IsCancellationRequested)throw new OperationCanceledException();}
    private void EndLiveSession(bool render=true)
    {
        liveEpoch++;vault.Cancel();
        liveLifetime.Cancel();liveLifetime.Dispose();liveLifetime=new CancellationTokenSource();
        connection?.Dispose();connection=null;enrollment=null;liveMode=false;liveBusy=false;
        history.Clear();manualAccountEntry=false;accountKey="";pendingSignIn=null;pendingOverride=null;monitoringCommand=false;feedbackKind=null;mapTiles.Clear();mapLocation=null;
        screen="Connect";notice="";noticeError=false;
        if(render&&IsInsideTree())Render();
    }
    public override void _Notification(int what)
    {
        // Focus loss also occurs during Face ID, so only actual backgrounding ends the session.
        if(!vaultReady)return;
        if(what==NotificationApplicationPaused) {
            // Hide immediately, then rebuild after the engine resumes its scene tree.
            if(canvas!=null)canvas.Hide();
            EndLiveSession(false);
        }
        if(what==NotificationApplicationResumed)CallDeferred(nameof(RestoreForeground));
    }
    private void RestoreForeground()
    {
        if(!IsInsideTree())return;
        Render();
        if(!smoke)NativeVaultBridge.RestoreForeground();
    }
    private void SavedLoginScreen()
    {
        Title("WELCOME BACK.","Your saved Hyundai account is locked on this device.");
        Hero(208,215);
        Label(24,454,412,72,"Unlock your saved login with "+vault.BiometricName+". Your phone verifies you before releasing the credentials.",14,Muted,false,true);
        var unlock=LiveButton(24,558,412,52,"Unlock with "+vault.BiometricName,15,()=>_ = UnlockAccount(),true,"BiometricUnlock");
        unlock.Disabled=liveBusy||!vault.Available;
        LiveButton(24,635,412,46,"Sign in manually",13,()=>{manualAccountEntry=true;Render();});
        LiveButton(24,704,412,46,"Forget saved account",13,()=>_ = ForgetAccount());
        if(!vault.Available&&notice.Length==0)Label(24,778,412,62,vault.UnavailableReason,12,Muted,false,true);
    }
    private async Task UnlockAccount()
    {
        int version=liveEpoch;SavedAccount? account=null;
        await RunLive(async()=>{
            account=await vault.UnlockAsync(liveLifetime.Token);EnsureLive(version);
            if(HoldForDisclaimer(account.Email,account.Password,account.Pin,true,true))return;
            await SignInSaved(account.Email,account.Password,account.Pin,version);
        },"Waiting for "+vault.BiometricName+"…",
        // An override retries with the already unlocked account instead of asking for Face ID twice.
        ()=>account==null?UnlockAccount():SignInSavedLive(account.Email,account.Password,account.Pin));
    }
    private Task SignInSavedLive(string email,string password,string pin)
    {
        int version=liveEpoch;
        return RunLive(()=>SignInSaved(email,password,pin,version),"Signing in to Hyundai…",()=>SignInSavedLive(email,password,pin));
    }
    private async Task SignInSaved(string email,string password,string pin,int version)
    {
        connection?.Dispose();var active=connectionFactory();connection=active;
        var vehicles=await active.LoginAsync(email,password,pin,liveLifetime.Token);EnsureLive(version);
        accountKey=VehicleLink.AccountKey(email);BindDisclaimer();
        enrollment=vehicles;selectedIndex=0;manualAccountEntry=false;screen="Connect";
        notice="Unlocked and signed in. Select your vehicle.";
        await RestoreLinkedVehicle(active,version);
    }
    private async Task ForgetAccount()
    {
        if(liveBusy)return;
        // End access immediately. If deletion fails, keep showing the failure and allow retry.
        EndLiveSession();
        // Signing out resets the risk acceptance; the next account must accept again.
        linkedVehicle="";disclaimer=null;SavePrefs();
        if(!vault.Present){notice="Session cleared. This build has no saved account storage.";Render();return;}
        int version=liveEpoch;
        await RunLive(async()=>{
            await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
            manualAccountEntry=true;notice="Signed out. Saved account removed from this device.";
        },"Removing saved account…");
    }
}
