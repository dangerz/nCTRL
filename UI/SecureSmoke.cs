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
            result=JsonSerializer.Serialize(new{ok=true,code="",data=op=="unlock"?saved:""});
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
            await Capture("20-biometric-unlock-test.png");
            fake.CancelNext=true;await UnlockAccount();
            if(connection!=null||enrollment!=null||!vault.HasAccount)throw new Exception("Cancelled biometric leaked account access");
            await UnlockAccount();if(enrollment==null)throw new Exception("Unlock failed");
            await ChooseVehicle();Navigate("Settings");await Capture("21-secure-settings-test.png");
            EndLiveSession(true);fake.Hold=true;
            var waiting=UnlockAccount();_Notification((int)NotificationApplicationPaused);await waiting;
            if(connection!=null||liveMode||liveBusy)throw new Exception("Late unlock restored locked session");
            fake.Hold=false;
            await ForgetAccount();if(vault.HasAccount||connection!=null)throw new Exception("Forget did not clear account");
            GD.Print("SECURE SMOKE PASS: save, background lock, biometric cancellation, unlock, stale-result rejection, forget. Fake native bridge only.");
            GetTree().Quit();
        }catch(Exception ex){GD.PushError("SECURE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}
