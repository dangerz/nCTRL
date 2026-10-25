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
    // A signed-in session kept in memory while backgrounded, reused on return while its token is valid.
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
        // the app can resume it without a new sign-in. Sign-out always discards it.
        bool keep=!demo&&park&&connection is {Selected: not null,SessionValid: true}&&!liveBusy&&!monitoringCommand&&
            accountKey.Length>0&&vault.HasAccount;
        // Backgrounding again from the locked screen keeps the earlier parked session.
        if(!(park&&connection==null))DiscardParked();
        if(keep){parked=connection;parkedAccountKey=accountKey;}
        else connection?.Dispose();
        liveEpoch++;vault.Cancel();
        liveLifetime.Cancel();liveLifetime.Dispose();liveLifetime=new CancellationTokenSource();
        connection=null;enrollment=null;liveMode=false;liveBusy=false;
        history.Clear();manualAccountEntry=false;accountKey="";pendingSignIn=null;pendingOverride=null;pendingConfirm=null;pendingPlaceAt=null;
        lookView=null;lookTexture=null;lookCapturing=false;monitoringCommand=false;feedbackKind=null;mapTiles.Clear();mapLocation=null;mapFrame=null;shownLocation=null;shownOld=false;phoneFix=null;phoneNote="";pendingDestination=null;sentDestination=null;destinationText="";destinationTiles.Clear();destinationTilesFor=null;
        sessionAccount=null;
        screen="Connect";notice="";noticeError=false;
        if(demo)LeaveDemo();
        // With a saved account and status, return to the Car screen showing that (stale) status.
        EnterOffline();
        if(render&&IsInsideTree())Render();
    }
    public override void _Notification(int what)
    {
        // Focus loss also occurs during Face ID, so only actual backgrounding ends the session.
        if(!vaultReady)return;
        // The demo has nothing to protect, so it carries on after the app is backgrounded.
        if(what==NotificationApplicationPaused&&!demo) {
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
        // Like the Hyundai app, read status on every return.
        // The Share sheet may have sent through the same request limits while nCTRL was in the background.
        budget.Reload();
        if(!smoke){ReadOnOpen();CheckShared();_ = UpdateOutsideTemperature();}
    }
    private void DiscardParked(){parked?.Dispose();parked=null;parkedAccountKey="";}
    private void SavedLoginScreen()
    {
        Title("WELCOME BACK.","Your Hyundai account is saved on this device.");
        Hero(208,215);
        Label(24,454,412,72,"nCTRL signs in with your saved login and opens your car.",14,Muted,false,true);
        var unlock=LiveButton(24,558,412,52,liveBusy?"Signing in…":"Open my car",15,()=>_ = UnlockAccount(),true,"BiometricUnlock");
        unlock.Disabled=liveBusy||!vault.Available;
        LiveButton(24,635,412,46,"Sign in manually",13,()=>{manualAccountEntry=true;Render();});
        LiveButton(24,704,412,46,"Forget saved account",13,()=>_ = ForgetAccount());
        if(!vault.Available&&notice.Length==0)Label(24,778,412,62,vault.UnavailableReason,12,Muted,false,true);
    }
    // Saved-login screen (no saved status yet): sign in and open the remembered vehicle. Quiet when automatic.
    private async Task UnlockAccount(bool quiet=false)
    {
        int version=liveEpoch;
        await RunLive(async()=>{
            var (active,_)=await AcquireSession(version);
            if(active!=null&&!liveMode){liveMode=true;screen="Car";}
        },"Signing in to Hyundai…",quiet?null:()=>UnlockAccount(),quiet);
    }
    private async Task ForgetAccount()
    {
        if(liveBusy)return;
        if(demo){EndLiveSession();return;}
        // End access immediately. If deletion fails, keep showing the failure and allow retry.
        ClearSnapshot();ignoredPlaces.Clear();savedLocation=null;storedLocation=null;savedFor="";savedLoaded=false;ForgetOutside();ClearWidget();heldShare=null;sharedHistory=Array.Empty<SharedEntry>();EndLiveSession();
        // Signing out resets the risk acceptance; the next account must accept again.
        // Car names and pictures go with the account too.
        linkedVehicle="";disclaimer=null;cars.Clear();SelectCar();SavePrefs();
        if(!vault.Present){notice="Session cleared. This build has no saved account storage.";Render();return;}
        int version=liveEpoch;
        await RunLive(async()=>{
            await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
            // Ignored places go with the account.
            await vault.ForgetPlacesAsync(liveLifetime.Token);EnsureLive(version);
            await vault.ForgetLocationAsync(liveLifetime.Token);EnsureLive(version);
            await vault.ForgetSharedAsync(liveLifetime.Token);EnsureLive(version);
            await vault.ForgetSessionAsync(liveLifetime.Token);EnsureLive(version);sessionShared="";
            manualAccountEntry=true;notice="Signed out. Saved account removed from this device.";
        },"Removing saved account…");
    }
}
