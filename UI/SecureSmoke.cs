#if DEBUG
using Godot;
using NGarage.Core;
using System;
using System.Text.Json;
using System.Threading.Tasks;

public partial class Main
{
    private bool vaultSmoke;
    // Memory-only test double; never selected by normal startup or mobile exports.
    private sealed class VaultFixture : IVaultBridge
    {
        public bool Present=>true;
        public bool CancelNext,Hold;
        public int Unlocks;
        public string Places="",Location="";
        private string? saved;
        private string result="";
        public int Availability()=>2;
        public bool HasAccount()=>saved!=null;
        public void Start(string op,string data) {
            if(op=="unlock")Unlocks++;
            if(Hold)return;
            if(CancelNext&&!op.StartsWith("places_")&&!op.StartsWith("location_")){CancelNext=false;result="{\"ok\":false,\"code\":\"cancelled\"}";return;}
            if(op=="save")saved=data;if(op=="forget")saved=null;
            if(op=="places_save")Places=data;if(op=="places_forget")Places="";
            if(op=="location_save")Location=data;if(op=="location_forget")Location="";
            result=RequestJson.Object(w=>{w.WriteBoolean("ok",true);w.WriteString("code","");
                w.WriteString("data",op=="unlock"?saved:op=="places_load"?Places:op=="location_load"?Location:"");});
        }
        public string TakeResult(){var s=result;result="";return s;}
        public void Cancel(){result="";}
    }
    // Lets deferred resume work start, then waits for any unlock it began.
    private async Task Settle()
    {
        for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        for(int i=0;i<600&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
    private async Task SecureSmokeTest()
    {
        try {
            var fake=new VaultFixture();vault=new AccountVault(fake);
            var http=new ScreenFixture();connectionFactory=()=>new HyundaiUsClient(http,null,budget);
            // Exercise the phone path: acceptance requires the (fake) device-owner check.
            deviceCheckRequired=true;Render();
            fake.CancelNext=true;FillDisclaimerFixture().EmitSignal(Godot.Button.SignalName.Pressed);
            while(liveBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!DisclaimerRequired||disclaimer!=null)throw new Exception("Cancelled device check recorded disclaimer acceptance");
            await PassDisclaimerFixture();
            if(disclaimer?.AccountKey!="")throw new Exception("Acceptance was bound before any sign-in");
            Navigate("Connect");await Capture("19-secure-signin-test.png");
            await SignIn("tester@example.invalid","fixture-password","1234",true);
            if(!vault.HasAccount||enrollment==null)throw new Exception("Save flow failed");
            string testerKey=VehicleLink.AccountKey("tester@example.invalid");
            if(disclaimer?.AccountKey!=testerKey)throw new Exception("Successful sign-in did not bind disclaimer acceptance");
            await ChooseVehicle();
            if(snapshot==null||snapshot.State.Battery!=78||snapshot.AccountKey!=testerKey)throw new Exception("Connecting did not save the vehicle status");
            // Ignored places persist in the (fake) Keychain item, separate from the login.
            await LoadPlaces();if(!placesPersisted)throw new Exception("Ignored places are not stored with the native bridge");
            await StorePlaces(new(){new IgnoredPlace("Home",40.111111,-74.222222)},"Saved.");
            if(!fake.Places.Contains("\"Home\""))throw new Exception("Ignored place was not saved to the Keychain item");
            // Backgrounding parks the idle session; returning opens the saved status with no Face ID or request.
            int tokens=http.Tokens,unlocks=fake.Unlocks,sent=http.Requests;
            _Notification((int)NotificationApplicationPaused);
            if(connection!=null||enrollment!=null)throw new Exception("Background did not clear the live session");
            if(canvas.Visible)throw new Exception("Background left the old vehicle canvas visible");
            if(parked==null)throw new Exception("Background did not park the idle signed-in session");
            _Notification((int)NotificationApplicationResumed);await Settle();
            if(!canvas.Visible||!Offline||screen!="Car"||CurrentState?.Battery!=78)throw new Exception("Returning did not open the Car screen with the saved status");
            if(fake.Unlocks!=unlocks||http.Requests!=sent)throw new Exception("Opening the app asked for Face ID or sent a request");
            if(canvas.GetNodeOrNull<Label>("LastUpdated")?.Text.StartsWith("Updated")!=true)throw new Exception("Car screen does not show when status last updated");
            await Capture("20-saved-status-test.png");
            // The status report's location was kept in its own (fake) Keychain item, by vehicle fingerprint only.
            if(!fake.Location.Contains("40.111111")||fake.Location.Contains("TESTVIN")||fake.Location.Contains("tester"))
                throw new Exception("Last known location was not stored by vehicle fingerprint");
            // As after a relaunch: shown from the Keychain without Face ID, marked old. Cancelling the new request's Face ID keeps it.
            savedLocation=null;storedLocation=null;savedLoaded=false;
            fake.CancelNext=true;await OpenVehicleLocation();
            if(fake.Unlocks!=unlocks+1||http.Requests!=sent||canvas.GetNodeOrNull<Label>("LocationOld")==null||canvas.GetNodeOrNull<Button>("VehicleMap")==null)
                throw new Exception("Saved status did not show the old location without Face ID");
            await Capture("50-old-location-saved-status-test.png");
            Navigate("Car");unlocks=fake.Unlocks;
            // The first request asks for Face ID; cancelling sends nothing.
            fake.CancelNext=true;await RefreshLive(false);
            if(fake.Unlocks!=unlocks+1||http.Requests!=sent||connection!=null||!Offline)throw new Exception("Cancelled Face ID still sent a request");
            // Face ID succeeds: the parked session is reused without a new sign-in.
            await RefreshLive(false);
            if(fake.Unlocks!=unlocks+2||connection==null||parked!=null||http.Tokens!=tokens)throw new Exception("Face ID did not resume the parked session without signing in");
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            // On phones an override needs the device check; cancelling it sends nothing.
            sent=http.Requests;await RefreshLive(false);
            if(pendingOverride==null||http.Requests!=sent)throw new Exception("Read cooldown did not warn");
            fake.CancelNext=true;canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(pendingOverride!=null||http.Requests!=sent||!notice.Contains("not confirmed"))throw new Exception("Cancelled device check still sent an override request");
            // Later requests in the same session need no further Face ID.
            // Tapping the padlock on the car sends the same toggle as the door button.
            unlocks=fake.Unlocks;Navigate("Car");canvas.GetNode<Button>("LockBadge").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(fake.Unlocks!=unlocks||http.Commands!=1)throw new Exception("A second request in the same session asked for Face ID again");
            // After backgrounding, the next request asks again.
            _Notification((int)NotificationApplicationPaused);_Notification((int)NotificationApplicationResumed);await Settle();
            if(!Offline||fake.Unlocks!=unlocks)throw new Exception("Returning to the app asked for Face ID");
            await RefreshLive(false);
            if(fake.Unlocks!=unlocks+1)throw new Exception("The first request after backgrounding did not ask for Face ID");
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            Navigate("Settings");
            if(canvas.GetNodeOrNull<Button>("SignOut")==null)throw new Exception("Settings is missing Sign out & forget");
            await Capture("21-secure-settings-test.png");
            Navigate("Disclaimer");
            if(canvas.GetNodeOrNull<Button>("DisclaimerBack")==null)throw new Exception("Settings cannot show the accepted disclaimer");
            await Capture("24-disclaimer-view-test.png");
            // A different account is held before any request, and can back out.
            EndLiveSession();Render();int requests=http.Requests;
            await SignIn("other@example.invalid","fixture-password","1234");
            if(pendingSignIn==null||!DisclaimerRequired||http.Requests!=requests||connection!=null)
                throw new Exception("Different account was not held at the disclaimer before any request");
            canvas.GetNode<Button>("DisclaimerCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingSignIn!=null||DisclaimerRequired)throw new Exception("Cancelling a different account did not restore the gate");
            // A saved account whose acceptance belongs to someone else resumes only after accepting.
            disclaimer=Disclaimer.Accept(VehicleLink.AccountKey("other@example.invalid"),DateTimeOffset.UtcNow);Render();
            await UnlockAccount();
            if(pendingSignIn?.FromVault!=true||http.Requests!=requests||connection!=null)
                throw new Exception("Saved account was not held at the disclaimer before any request");
            await PassDisclaimerFixture();
            for(int i=0;i<600&&(connection==null||liveBusy);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!liveMode||screen!="Car"||connection==null||disclaimer?.AccountKey!=testerKey)throw new Exception("Accepting did not resume the held saved-account sign-in");
            // A late Face ID result after backgrounding never restores the session.
            EndLiveSession();fake.Hold=true;
            var waiting=RefreshLive(false);_Notification((int)NotificationApplicationPaused);await waiting;
            if(connection!=null||liveBusy||!Offline)throw new Exception("Late unlock restored a session after backgrounding");
            fake.Hold=false;
            _Notification((int)NotificationApplicationResumed);await Settle();
            await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            _Notification((int)NotificationApplicationPaused);_Notification((int)NotificationApplicationResumed);await Settle();
            if(parked==null)throw new Exception("Session was not parked before sign-out");
            await ForgetAccount();if(parked!=null)throw new Exception("Sign out did not discard the parked session");if(vault.HasAccount||connection!=null)throw new Exception("Forget did not clear account");
            if(linkedVehicle.Length!=0||snapshot!=null)throw new Exception("Forget retained the linked vehicle or saved status");
            if(fake.Places.Length!=0||ignoredPlaces.Count!=0)throw new Exception("Sign out did not delete ignored places");
            if(fake.Location.Length!=0||savedLocation!=null)throw new Exception("Sign out did not delete the last known location");
            if(disclaimer!=null||canvas.GetNodeOrNull<Button>("DisclaimerSubmit")==null)throw new Exception("Sign out did not immediately require the disclaimer again");
            AssertAccountOnly();
            GD.Print("SECURE SMOKE PASS: disclaimer gate, save, saved-status open, Face ID on first request, session reuse, biometric cancellation, account re-acceptance, stale-result rejection, ignored places storage, old location without Face ID, forget. Fake native bridge only.");
            GetTree().Quit();
        }catch(Exception ex){GD.PushError("SECURE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}

#endif
