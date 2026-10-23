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
            connectionFactory=()=>new HyundaiUsClient(new ScreenFixture());
            Navigate("Connect");await Capture("19-secure-signin-test.png");
            await SignIn("tester@example.invalid","fixture-password","1234",true);
            if(!vault.HasAccount||enrollment==null)throw new Exception("Save flow failed");
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
            EndLiveSession();fake.Hold=true;
            var waiting=UnlockAccount();_Notification((int)NotificationApplicationPaused);await waiting;
            if(connection!=null||liveMode||liveBusy)throw new Exception("Late unlock restored locked session");
            fake.Hold=false;
            _Notification((int)NotificationApplicationResumed);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ForgetAccount();if(vault.HasAccount||connection!=null)throw new Exception("Forget did not clear account");
            if(linkedVehicle.Length!=0)throw new Exception("Forget retained the linked vehicle preference");
            AssertAccountOnly();
            GD.Print("SECURE SMOKE PASS: save, background lock, biometric cancellation, unlock, stale-result rejection, forget. Fake native bridge only.");
            GetTree().Quit();
        }catch(Exception ex){GD.PushError("SECURE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}

#endif
