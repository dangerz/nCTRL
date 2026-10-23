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
        vault=new AccountVault(NativeVaultBridge.Create());vaultReady=true;
        if(vault.HasAccount){screen="Connect";manualAccountEntry=false;}
    }
    private void EnsureLive(int version)
    {if(version!=liveEpoch || liveLifetime.IsCancellationRequested)throw new OperationCanceledException();}
    private void EndLiveSession(bool showLogin)
    {
        liveEpoch++;vault.Cancel();
        liveLifetime.Cancel();liveLifetime.Dispose();liveLifetime=new CancellationTokenSource();
        connection?.Dispose();connection=null;enrollment=null;liveMode=false;liveBusy=false;
        history.Clear();manualAccountEntry=false;
        screen=showLogin?"Connect":"Settings";notice="";noticeError=false;
        foreach(var child in GetChildren())if(child is ConfirmationDialog dialog){dialog.Hide();dialog.QueueFree();}
        if(IsInsideTree())Render();
    }
    public override void _Notification(int what)
    {
        // Focus loss also occurs during Face ID, so only actual backgrounding ends the session.
        if(what==NotificationApplicationPaused && vaultReady)EndLiveSession(true);
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
        if(!vault.Available&&notice.Length==0)Label(24,778,412,62,"Biometrics are unavailable or have changed. Sign in manually or remove the saved account.",12,Muted,false,true);
    }
    private async Task UnlockAccount()
    {
        int version=liveEpoch;
        await RunLive(async()=>{
            var account=await vault.UnlockAsync(liveLifetime.Token);EnsureLive(version);
            connection?.Dispose();var active=connectionFactory();connection=active;
            var vehicles=await active.LoginAsync(account.Email,account.Password,account.Pin,liveLifetime.Token);EnsureLive(version);
            enrollment=vehicles;selectedIndex=0;manualAccountEntry=false;screen="Connect";
            notice="Unlocked and signed in. Select your vehicle.";
        },"Waiting for "+vault.BiometricName+"…");
    }
    private async Task ForgetAccount()
    {
        if(liveBusy)return;
        // End access immediately. If deletion fails, keep showing the failure and allow retry.
        EndLiveSession(true);
        if(!vault.Present){notice="Session cleared. This build has no saved account storage.";Render();return;}
        int version=liveEpoch;
        await RunLive(async()=>{
            await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
            manualAccountEntry=true;notice="Signed out. Saved account removed from this device.";
        },"Removing saved account…");
    }
}
