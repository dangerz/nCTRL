#if DEBUG
using Godot;
using NGarage.Core;
using System;
using System.Linq;
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
        public string Places="",Location="",Shared="";
        public readonly System.Collections.Generic.List<string> Ops=new();
        private string? saved;
        private string result="";
        public int Availability()=>2;
        public bool HasAccount()=>saved!=null;
        public void Start(string op,string data) {
            Ops.Add(op);
            if(op=="unlock")Unlocks++;
            if(Hold)return;
            if(CancelNext&&!op.StartsWith("places_")&&!op.StartsWith("location_")&&!op.StartsWith("shared_")){CancelNext=false;result="{\"ok\":false,\"code\":\"cancelled\"}";return;}
            if(op=="save")saved=data;if(op=="forget")saved=null;
            if(op=="places_save")Places=data;if(op=="places_forget")Places="";
            if(op=="location_save")Location=data;if(op=="location_forget")Location="";
            if(op=="shared_save")Shared=data;if(op=="shared_forget")Shared="";
            result=RequestJson.Object(w=>{w.WriteBoolean("ok",true);w.WriteString("code","");
                w.WriteString("data",op=="unlock"?saved:op=="places_load"?Places:op=="location_load"?Location:op=="shared_load"?Shared:"");});
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
            // A share recorded on launch while the saved Shared list is still loading isn't lost when the list arrives.
            var older=new SharedEntry(DateTimeOffset.UtcNow.AddHours(-1),"Apple Maps","link",new Destination("Older place","",40.7,-74),"");
            fake.Shared=SharedHistory.Encode(new[]{older});sharedHistory=Array.Empty<SharedEntry>();sharedPersisted=false;
            await RecordShare(new SharedEntry(DateTimeOffset.UtcNow,"Google Maps","text",null,"The app didn't hand over the place in time"));
            await LoadShared();
            if(sharedHistory.Count!=2||sharedHistory[0].Source!="Google Maps"||sharedHistory[1].Place?.Name!="Older place"||!fake.Shared.Contains("hand over"))
                throw new Exception("A share recorded while the Shared list was loading was lost");
            // Ignored places persist in the (fake) Keychain item, separate from the login.
            await LoadPlaces();if(!placesPersisted)throw new Exception("Ignored places are not stored with the native bridge");
            await StorePlaces(new(){new IgnoredPlace("Home",40.111111,-74.222222)},"Saved.");
            if(!fake.Places.Contains("\"Home\""))throw new Exception("Ignored place was not saved to the Keychain item");
            // Backgrounding parks the idle session; returning shows the saved status straight away.
            int tokens=http.Tokens,sent=http.Requests;
            int Prompts()=>fake.Ops.Count(op=>op=="authenticate");int prompts=Prompts();
            _Notification((int)NotificationApplicationPaused);
            if(connection!=null||enrollment!=null)throw new Exception("Background did not clear the live session");
            if(canvas.Visible)throw new Exception("Background left the old vehicle canvas visible");
            if(parked==null)throw new Exception("Background did not park the idle signed-in session");
            _Notification((int)NotificationApplicationResumed);await Settle();
            if(!canvas.Visible||!Offline||screen!="Car"||CurrentState?.Battery!=78)throw new Exception("Returning did not open the Car screen with the saved status");
            if(canvas.GetNodeOrNull<Label>("LastUpdated")?.Text.StartsWith("Updated")!=true)throw new Exception("Car screen does not show when status last updated");
            await Capture("20-saved-status-test.png");
            // Reading on open: inside the read limit it is skipped quietly, with no warning.
            ReadOnOpen();await Settle();
            if(http.Requests!=sent||pendingOverride!=null||noticeError)throw new Exception("Reading on open inside the read limit sent a request or warned");
            // Past the limit it reads once, reusing the parked session: no Face ID, no new sign-in.
            await ToSignal(GetTree().CreateTimer(16),SceneTreeTimer.SignalName.Timeout);
            ReadOnOpen();await Settle();
            if(http.Requests!=sent+1||!http.LastPath.EndsWith("/vehicleStatus")||connection==null||parked!=null||http.Tokens!=tokens||Prompts()!=prompts)
                throw new Exception("Reading on open did not read once without Face ID or a new sign-in");
            // The status report's location was kept in its own (fake) Keychain item, by vehicle fingerprint only.
            if(!fake.Location.Contains("40.111111")||fake.Location.Contains("TESTVIN")||fake.Location.Contains("tester"))
                throw new Exception("Last known location was not stored by vehicle fingerprint");
            // As after a relaunch: the old location shows from the Keychain, marked old, then a new one is requested with no prompt.
            EndLiveSession();savedLocation=null;storedLocation=null;savedLoaded=false;sent=http.Requests;
            Navigate("Locate");await ShowOldLocation();
            if(http.Requests!=sent||canvas.GetNodeOrNull<Label>("LocationOld")==null||canvas.GetNodeOrNull<Button>("VehicleMap")==null)
                throw new Exception("Saved status did not show the old location");
            await Capture("50-old-location-saved-status-test.png");
            await UpdateVehicleLocation();
            if(!http.LastPath.EndsWith("/findMyCar")||Prompts()!=prompts||canvas.GetNodeOrNull<Label>("LocationOld")!=null)
                throw new Exception("A new location was not requested without Face ID");
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            // On phones an override still needs the device check; cancelling it sends nothing.
            Navigate("Car");sent=http.Requests;await RefreshLive(false);
            if(pendingOverride==null||http.Requests!=sent)throw new Exception("Read cooldown did not warn");
            fake.CancelNext=true;canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(pendingOverride!=null||http.Requests!=sent||!notice.Contains("not confirmed"))throw new Exception("Cancelled device check still sent an override request");
            // Commands need no Face ID either. Tapping the padlock sends the same toggle as the door button.
            prompts=Prompts();Navigate("Car");canvas.GetNode<Button>("LockBadge").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(Prompts()!=prompts||http.Commands!=1)throw new Exception("A command asked for Face ID or was not sent");
            // Saved status still charging past Hyundai's finish time: the app reads once, with no prompt.
            http.Charging=http.ChargingOverdue=true;
            await ToSignal(GetTree().CreateTimer(16),SceneTreeTimer.SignalName.Timeout);
            await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(!ChargeEstimate.Overdue(snapshot?.State,DateTimeOffset.UtcNow))throw new Exception("Saved status did not keep the overdue charge");
            _Notification((int)NotificationApplicationPaused);_Notification((int)NotificationApplicationResumed);await Settle();
            if(!Offline||canvas.GetNodeOrNull<Label>("ChargeLine")?.Text!="CHARGING")throw new Exception("Saved status does not show the out-of-date charge");
            await ToSignal(GetTree().CreateTimer(16),SceneTreeTimer.SignalName.Timeout);
            int overdueSent=http.Requests;
            CheckChargeOverdue();
            for(int i=0;i<600&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(Prompts()!=prompts||http.Requests<=overdueSent||connection==null)throw new Exception("An overdue charge on saved status did not update without a prompt");
            http.Charging=http.ChargingOverdue=false;
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
            // A late Keychain result after backgrounding never restores the session.
            EndLiveSession();fake.Hold=true;
            var waiting=RefreshLive(false);_Notification((int)NotificationApplicationPaused);await waiting;
            if(connection!=null||liveBusy||!Offline)throw new Exception("Late unlock restored a session after backgrounding");
            fake.Hold=false;
            _Notification((int)NotificationApplicationResumed);await Settle();
            await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            _Notification((int)NotificationApplicationPaused);_Notification((int)NotificationApplicationResumed);await Settle();
            if(parked==null)throw new Exception("Session was not parked before sign-out");
            if(!sharedFixture.ContainsKey("status.json"))throw new Exception("The widget was not filled from the signed-in status");
            await ForgetAccount();if(parked!=null)throw new Exception("Sign out did not discard the parked session");if(vault.HasAccount||connection!=null)throw new Exception("Forget did not clear account");
            if(linkedVehicle.Length!=0||snapshot!=null)throw new Exception("Forget retained the linked vehicle or saved status");
            if(fake.Places.Length!=0||ignoredPlaces.Count!=0)throw new Exception("Sign out did not delete ignored places");
            if(fake.Location.Length!=0||savedLocation!=null)throw new Exception("Sign out did not delete the last known location");
            if(sharedFixture.ContainsKey("status.json")||sharedFixture.ContainsKey("car.png"))throw new Exception("Sign out did not clear the widget");
            if(!fake.Ops.Contains("shared_forget")||sharedHistory.Count!=0)throw new Exception("Sign out did not delete the shared places");
            if(!fake.Ops.Contains("session_save")||!fake.Ops.Contains("session_forget"))throw new Exception("Hyundai's session was not shared with the Share sheet, or not deleted on sign-out");
            if(disclaimer!=null||canvas.GetNodeOrNull<Button>("DisclaimerSubmit")==null)throw new Exception("Sign out did not immediately require the disclaimer again");
            // The demo is open before acceptance, never touches secure storage, and leaves the gate as it was.
            int ops=fake.Ops.Count;
            await Capture("55-disclaimer-demo-test.png");
            canvas.GetNode<Button>("DemoStart").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<600&&(liveBusy||connection?.State==null);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!demo||connection?.State==null||DisclaimerRequired)throw new Exception("The demo did not open from the disclaimer screen");
            EndLiveSession();
            for(int i=0;i<10;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(demo||disclaimer!=null||!DisclaimerRequired||fake.Ops.Skip(ops).Any(op=>op is not ("places_load" or "shared_load")))
                throw new Exception("The demo touched secure storage or the disclaimer");
            AssertAccountOnly();
            GD.Print("SECURE SMOKE PASS: disclaimer gate, save, saved-status open, reading on open without Face ID, session reuse, commands and location without Face ID, override device check, account re-acceptance, stale-result rejection, ignored places storage, old location, overdue charge update, widget filled then cleared on forget, demo isolation. Fake native bridge only.");
            GetTree().Quit();
        }catch(Exception ex){GD.PushError("SECURE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}

#endif
