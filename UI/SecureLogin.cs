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
    // A signed-in session kept in memory while backgrounded. Face ID must succeed before it is used again.
    private HyundaiUsClient? parked;
    private string parkedAccountKey="";
    private void InitializeVault()
    {
        vault=new AccountVault(smoke?new UnavailableVaultBridge():NativeVaultBridge.Create());vaultReady=true;
        deviceCheckRequired=OS.GetName() is "iOS" or "Android";
        if(vault.HasAccount){screen="Connect";manualAccountEntry=false;}
    }
    private void EnsureLive(int version)
    {if(version!=liveEpoch || liveLifetime.IsCancellationRequested)throw new OperationCanceledException();}
    private void EndLiveSession(bool render=true,bool park=false)
    {
        // Backgrounding may park an idle session (selected vehicle, valid token, saved account) so
        // Face ID can resume it without a new sign-in. Sign-out always discards it.
        bool keep=park&&connection is {Selected: not null,SessionValid: true}&&!liveBusy&&!monitoringCommand&&
            accountKey.Length>0&&vault.HasAccount;
        // Backgrounding again from the locked screen keeps the earlier parked session.
        if(!(park&&connection==null))DiscardParked();
        if(keep){parked=connection;parkedAccountKey=accountKey;}
        else connection?.Dispose();
        liveEpoch++;vault.Cancel();
        liveLifetime.Cancel();liveLifetime.Dispose();liveLifetime=new CancellationTokenSource();
        connection=null;enrollment=null;liveMode=false;liveBusy=false;
        history.Clear();manualAccountEntry=false;accountKey="";pendingSignIn=null;pendingOverride=null;pendingConfirm=null;pendingPlaceAt=null;
        lookView=null;lookTexture=null;lookCapturing=false;monitoringCommand=false;feedbackKind=null;mapTiles.Clear();mapLocation=null;
        sessionVerified=false;sessionAccount=null;
        screen="Connect";notice="";noticeError=false;
        // With a saved account and status, return to the Car screen showing that (stale) status.
        EnterOffline();
        if(render&&IsInsideTree())Render();
    }
    public override void _Notification(int what)
    {
        // Focus loss also occurs during Face ID, so only actual backgrounding ends the session.
        if(!vaultReady)return;
        if(what==NotificationApplicationPaused) {
            // Hide immediately, then rebuild after the engine resumes its scene tree.
            if(canvas!=null)canvas.Hide();
            EndLiveSession(false,park:true);
        }
        if(what==NotificationApplicationResumed)CallDeferred(nameof(RestoreForeground));
    }
    private void RestoreForeground()
    {
        if(!IsInsideTree())return;
        Render();
        if(!smoke)NativeVaultBridge.RestoreForeground();
    }
    private void DiscardParked(){parked?.Dispose();parked=null;parkedAccountKey="";}
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
    // Saved-login screen (no saved status yet): Face ID, then sign in and open the remembered vehicle.
    // An override retry keeps the unlocked account, so Face ID is not asked twice.
    private async Task UnlockAccount()
    {
        int version=liveEpoch;
        await RunLive(async()=>{
            var (active,_)=await AcquireSession(version);
            if(active!=null&&!liveMode){liveMode=true;screen="Car";}
        },NeedsFaceId?"Waiting for "+vault.BiometricName+"…":"Signing in to Hyundai…",UnlockAccount);
    }
    private async Task ForgetAccount()
    {
        if(liveBusy)return;
        // End access immediately. If deletion fails, keep showing the failure and allow retry.
        ClearSnapshot();ignoredPlaces.Clear();EndLiveSession();
        // Signing out resets the risk acceptance; the next account must accept again.
        linkedVehicle="";disclaimer=null;SavePrefs();
        if(!vault.Present){notice="Session cleared. This build has no saved account storage.";Render();return;}
        int version=liveEpoch;
        await RunLive(async()=>{
            await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
            // Ignored places go with the account.
            await vault.ForgetPlacesAsync(liveLifetime.Token);EnsureLive(version);
            manualAccountEntry=true;notice="Signed out. Saved account removed from this device.";
        },"Removing saved account…");
    }
}
