using Godot;
using NGarage.Core;
using System;
using System.Threading.Tasks;

// Opening the app shows the Car screen from the last saved status without any request or Face ID.
// The first Update, command or location request of each foreground session asks for Face ID
// (which also unlocks the saved login), then the rest of that session needs no further prompts.
public partial class Main
{
    private const string SnapshotFile="user://vehicle-snapshot.json";
    private VehicleSnapshot? snapshot;
    private LiveVehicleState? persistedState;
    // Face ID (or a typed sign-in) has verified the owner since the app last came to the foreground.
    private bool sessionVerified;
    // The saved login unlocked by that Face ID, kept in memory for this foreground session only.
    private SavedAccount? sessionAccount;

    private bool Offline=>liveMode&&connection==null;
    private LiveVehicleState? CurrentState=>connection?.State??(Offline?snapshot?.State:null);
    private string VehicleName=>connection?.Selected is {} v?(v.Name.Length>0?v.Name:v.Model):snapshot?.Name??"";
    private string VinTail=>connection?.Selected is {} v?v.Vin[^6..]:snapshot?.VinTail??"";
    private int? VehicleGeneration=>connection?.Selected?.Generation??(Offline?snapshot?.Generation:null);
    private bool NeedsFaceId=>vault.HasAccount&&sessionAccount==null&&connection is not {Selected: not null,IsAuthenticated: true};

    private bool EnterOffline()
    {
        if(snapshot==null||!vault.HasAccount||snapshot.VehicleFingerprint!=linkedVehicle||!Disclaimer.Allows(disclaimer,snapshot.AccountKey))return false;
        liveMode=true;screen="Car";accountKey=snapshot.AccountKey;manualAccountEntry=false;
        return true;
    }
    private void LoadSnapshot()
    {
        using var file=FileAccess.Open(SnapshotFile,FileAccess.ModeFlags.Read);
        snapshot=file==null?null:VehicleSnapshot.Decode(file.GetAsText());
    }
    private void PersistSnapshot()
    {
        if(connection is not {State: {} state,Selected: {} vehicle}||accountKey.Length==0||ReferenceEquals(state,persistedState))return;
        persistedState=state;
        string name=vehicle.Name.Length>0?vehicle.Name:vehicle.Model;
        snapshot=new(accountKey,VehicleLink.Fingerprint(accountKey,vehicle.Vin),name.Length>64?name[..64]:name,vehicle.Vin[^6..],vehicle.Generation,state,IgnoredHere!=null);
        if(smoke||demo)return;
        using var file=FileAccess.Open(SnapshotFile,FileAccess.ModeFlags.Write);
        file?.StoreString(snapshot.Encode());
    }
    private void ClearSnapshot()
    {
        snapshot=null;persistedState=null;
        if(!smoke&&FileAccess.FileExists(SnapshotFile))DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SnapshotFile));
    }

    /// <summary>Returns a signed-in client for the selected vehicle, asking for Face ID first if this
    /// foreground session has not been verified. FreshRead is true when signing in already read status.
    /// Returns no client when the user must choose a vehicle or accept the disclaimer first.</summary>
    private async Task<(HyundaiUsClient? Client,bool FreshRead)> AcquireSession(int version)
    {
        if(connection is {Selected: not null,IsAuthenticated: true} ready&&(sessionVerified||!vault.HasAccount))return (ready,false);
        if(!vault.HasAccount) {
            // Session-only login that Hyundai ended: the typed credentials are gone, so sign in again.
            liveMode=false;screen="Connect";
            throw new BluelinkException("Sign in to Bluelink again.");
        }
        sessionAccount??=await vault.UnlockAsync(liveLifetime.Token);EnsureLive(version);
        sessionVerified=true;
        var account=sessionAccount;
        if(connection is {Selected: not null,IsAuthenticated: true} existing)return (existing,false);
        if(HoldForDisclaimer(account.Email,account.Password,account.Pin,true,true)){DiscardParked();return (null,false);}
        if(TakeParked(account)) {
            if(!liveMode){liveMode=true;screen="Car";}
            return (connection,false);
        }
        connection?.Dispose();var active=connectionFactory();connection=active;
        var vehicles=await active.LoginAsync(account.Email,account.Password,account.Pin,liveLifetime.Token,implicitSignIn:true);EnsureLive(version);
        accountKey=VehicleLink.AccountKey(account.Email);BindDisclaimer();
        int index=VehicleLink.Find(linkedVehicle,accountKey,vehicles);
        if(index<0) {
            enrollment=vehicles;selectedIndex=0;liveMode=false;screen="Connect";
            notice="Signed in. Select your vehicle.";return (null,false);
        }
        selectedIndex=index;
        await ConnectVehicle(active,vehicles[index],version);
        return (active,true);
    }
    // Reuses the session kept while backgrounded, only for the same account with a still-valid token.
    private bool TakeParked(SavedAccount account)
    {
        var active=parked;string key=parkedAccountKey;parked=null;parkedAccountKey="";
        if(active==null)return false;
        if(!active.SessionValid||active.Selected==null||key!=VehicleLink.AccountKey(account.Email)){active.Dispose();return false;}
        connection?.Dispose();connection=active;accountKey=key;
        history.Add(("Session resumed",vault.BiometricName+" verified. No new sign-in was sent.",true));
        return true;
    }
    private static string Updated(DateTimeOffset? at)
    {
        if(at is not DateTimeOffset value||value==default)return "Not updated yet";
        var local=value.ToLocalTime();var today=DateTimeOffset.Now.Date;
        string day=local.Date==today?"Today":local.Date==today.AddDays(-1)?"Yesterday":local.ToString("MMM d");
        return "Updated "+day+" "+local.ToString("h:mm tt");
    }
}
