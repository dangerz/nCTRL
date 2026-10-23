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
        private string? saved;
        private string result="";
        public int Availability()=>2;
        public bool HasAccount()=>saved!=null;
        public void Start(string op,string data) {
            if(Hold)return;
            if(CancelNext){CancelNext=false;result="{\"ok\":false,\"code\":\"cancelled\"}";return;}
            if(op=="save")saved=data;if(op=="forget")saved=null;
            result=RequestJson.Object(w=>{w.WriteBoolean("ok",true);w.WriteString("code","");w.WriteString("data",op=="unlock"?saved:"");});
        }
        public string TakeResult(){var s=result;result="";return s;}
        public void Cancel(){result="";}
    }
    private async Task SecureSmokeTest()
    {
        try {
            var fake=new VaultFixture();vault=new AccountVault(fake);
            var http=new ScreenFixture();connectionFactory=()=>new HyundaiUsClient(http);
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
            _Notification((int)NotificationApplicationPaused);
            if(connection!=null||liveMode||enrollment!=null||screen!="Connect")throw new Exception("Background did not clear session");
            if(canvas.Visible)throw new Exception("Background left the old vehicle canvas visible");
            _Notification((int)NotificationApplicationResumed);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!canvas.Visible||canvas.GetNodeOrNull<Button>("BiometricUnlock")==null)throw new Exception("Resume did not rebuild the unlock screen");
            AssertAccountOnly();
            await Capture("20-biometric-unlock-test.png");
            fake.CancelNext=true;await UnlockAccount();
            if(connection!=null||enrollment!=null||!vault.HasAccount)throw new Exception("Cancelled biometric leaked account access");
            await UnlockAccount();if(!liveMode||screen!="Car")throw new Exception("Unlock did not restore the previously selected vehicle");
            Navigate("Settings");await Capture("21-secure-settings-test.png");
            Navigate("Disclaimer");
            if(canvas.GetNodeOrNull<Button>("DisclaimerBack")==null)throw new Exception("Settings cannot show the accepted disclaimer");
            await Capture("24-disclaimer-view-test.png");
            // A different account is held before any request, and can back out.
            EndLiveSession();manualAccountEntry=true;Render();int requests=http.Requests;
            await SignIn("other@example.invalid","fixture-password","1234");
            if(pendingSignIn==null||!DisclaimerRequired||http.Requests!=requests||connection!=null)
                throw new Exception("Different account was not held at the disclaimer before any request");
            canvas.GetNode<Button>("DisclaimerCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingSignIn!=null||DisclaimerRequired)throw new Exception("Cancelling a different account did not restore the gate");
            // A saved account whose acceptance belongs to someone else resumes only after accepting.
            disclaimer=Disclaimer.Accept(VehicleLink.AccountKey("other@example.invalid"),DateTimeOffset.UtcNow);manualAccountEntry=false;Render();
            await UnlockAccount();
            if(pendingSignIn?.FromVault!=true||http.Requests!=requests||connection!=null)
                throw new Exception("Saved account was not held at the disclaimer before any request");
            await PassDisclaimerFixture();
            for(int i=0;i<600&&(!liveMode||liveBusy);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!liveMode||screen!="Car"||disclaimer?.AccountKey!=testerKey)throw new Exception("Accepting did not resume the held saved-account sign-in");
            EndLiveSession();fake.Hold=true;
            var waiting=UnlockAccount();_Notification((int)NotificationApplicationPaused);await waiting;
            if(connection!=null||liveMode||liveBusy)throw new Exception("Late unlock restored locked session");
            fake.Hold=false;
            _Notification((int)NotificationApplicationResumed);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ForgetAccount();if(vault.HasAccount||connection!=null)throw new Exception("Forget did not clear account");
            if(linkedVehicle.Length!=0)throw new Exception("Forget retained the linked vehicle preference");
            if(disclaimer!=null||canvas.GetNodeOrNull<Button>("DisclaimerSubmit")==null)throw new Exception("Sign out did not immediately require the disclaimer again");
            AssertAccountOnly();
            GD.Print("SECURE SMOKE PASS: disclaimer gate, save, background lock, biometric cancellation, unlock, account re-acceptance, stale-result rejection, forget. Fake native bridge only.");
            GetTree().Quit();
        }catch(Exception ex){GD.PushError("SECURE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}

#endif
