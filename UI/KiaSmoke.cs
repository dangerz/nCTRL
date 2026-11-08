#if DEBUG
using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Kia Connect smoke test: a fake Kia server inside the app (no network), walked through every gtCTRL screen,
// including the one-time code. Run: godot --path . -- --kia-smoke-test [--smoke-output=/absolute/folder]
public partial class Main
{
    private bool kiaSmoke;
    private sealed class KiaFixture : HttpMessageHandler
    {
        public int Requests,Codes;
        public readonly List<string> Paths=new();
        const string Ok="{\"status\":{\"statusCode\":0,\"errorType\":0,\"errorCode\":0}}";
        public static readonly string Status="""
        {"status":{"statusCode":0},"payload":{"vehicleInfoList":[{"lastVehicleInfo":{
          "vehicleStatusRpt":{"vehicleStatus":{"syncDate":{"utc":"20261108141440"},"doorLock":true,
            "climate":{"airCtrl":false,"defrost":false,"airTemp":{"value":"72","unit":1},"heatingAccessory":{"steeringWheel":0}},
            "doorStatus":{"frontLeft":0,"frontRight":0,"backLeft":0,"backRight":0,"trunk":0,"hood":0},
            "windowStatus":{"windowFL":0,"windowFR":0,"windowRL":0,"windowRR":0},
            "tirePressure":{"all":0},"batteryStatus":{"stateOfCharge":88},"washerFluidStatus":false,"breakOilStatus":false,"smartKeyBatteryWarning":false,
            "evStatus":{"batteryStatus":77,"batteryCharge":false,"batteryPlugin":0,"realTimePower":0,
              "drvDistance":[{"type":2,"rangeByFuel":{"evModeRange":{"value":184.0,"unit":3},"totalAvailableRange":{"value":184.0,"unit":3}}}],
              "targetSOC":[{"plugType":0,"targetSOClevel":100},{"plugType":1,"targetSOClevel":90}]}}},
          "location":{"coord":{"lat":40.7812,"lon":-73.9665},"syncDate":{"utc":"20261108141000"}}}}]}}
        """;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.Host!="api.owners.kia.com")throw new Exception("Unexpected Kia fixture host");
            string route=request.RequestUri.AbsolutePath["/apigw/v1/".Length..];
            Requests++;Paths.Add(route);
            bool has(string h)=>request.Headers.Contains(h);
            var headers=new List<(string,string)>();
            string body=route switch {
                // First sign-in asks for a code; completing it (with the verified session) or a remembered phone gets a session.
                "prof/authUser" when has("sid")||has("rmtoken")=>Ok,
                "prof/authUser"=>"""{"status":{"statusCode":0},"payload":{"otpKey":"fixture-otp","hasEmail":true,"email":"t*****@example.invalid","hasPhone":true,"phone":"(***) ***-0000"}}""",
                "cmm/sendOTP"=>Ok,
                "cmm/verifyOTP"=>Ok,
                "ownr/gvl"=>"""{"status":{"statusCode":0},"payload":{"vehicleSummary":[{"vehicleIdentifier":"fixture-car","nickName":"FLAREON2","modelName":"EV6","modelYear":"2024","fuelType":4,"vehicleKey":"fixture-key"}]}}""",
                "cmm/gvi"=>Status,
                "cmm/gts"=>"{\"status\":{\"statusCode\":0},\"payload\":{\"remoteStatus\":0}}",
                "rems/door/lock" or "rems/door/unlock" or "rems/start" or "rems/stop" or "evc/charge" or "evc/cancel" or "evc/sts" or "rems/rvs"=>Ok,
                _=>throw new Exception("Unexpected Kia fixture route "+route)
            };
            if(route=="prof/authUser"&&body!=Ok)headers.Add(("xid","fixture-xid"));
            if(route=="prof/authUser"&&body==Ok)headers.Add(("sid","fixture-sid"));
            if(route=="cmm/sendOTP")Codes++;
            if(route=="cmm/verifyOTP"){headers.Add(("sid","fixture-temp"));headers.Add(("rmtoken","fixture-remember"));}
            if(route.StartsWith("rems/")||route.StartsWith("evc/"))headers.Add(("Xid","fixture-command"));
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
            foreach(var (k,v) in headers)response.Headers.TryAddWithoutValidation(k,v);
            return Task.FromResult(response);
        }
    }
    private async Task KiaSmokeTest()
    {
        try
        {
            var fixture=new KiaFixture();
            connectionFactory=()=>{var c=new KiaUsClient(fixture,null,budget);c.DeviceId="FIXTURE-DEVICE";return c;};
            AssertAccountOnly();
            if(canvas.GetNodeOrNull<Button>("DemoStart")!=null&&!Brand.Flavor.Demo)throw new Exception("Demo mode is offered in a flavor without it");
            await Capture(Brand.Flavor.Id+"-k01-disclaimer.png");
            if(Brand.Flavor==Brand.GtCtrl&&(!Disclaimer.Text.Contains("Kia America")||Disclaimer.Text.Contains("nCTRL")))throw new Exception("Disclaimer is not the gtCTRL / Kia text");
            await PassDisclaimerFixture();
            Navigate("Connect");
            // nCTRL: the sign-in screen offers Hyundai (the default) and Kia. gtCTRL is Kia only.
            if(Brand.ChoosesService) {
                if(Brand.ServiceId!=Brand.Hyundai||canvas.GetNodeOrNull<Button>("Service_kia")==null||canvas.GetNodeOrNull<Button>("DemoStart")==null)
                    throw new Exception("nCTRL doesn't start on Hyundai with a Kia choice and the demo");
                await Capture("n01-signin-hyundai.png");
                canvas.GetNode<Button>("Service_kia").EmitSignal(Godot.Button.SignalName.Pressed);
                if(Brand.ServiceId!=Brand.Kia||!DisclaimerRequired)throw new Exception("Choosing Kia didn't switch the service and ask for Kia's disclaimer");
                await Capture("n02-disclaimer-kia.png");
                await PassDisclaimerFixture();Navigate("Connect");
            }
            else if(canvas.GetNodeOrNull<Button>("Service_hyundai")!=null)throw new Exception("gtCTRL offers Hyundai");
            if(canvas.GetNodeOrNull<LineEdit>("SignIn")!=null||canvas.GetNode<Button>("SignIn").Text!="Sign in to Kia Connect")throw new Exception("Sign-in button is not Kia Connect");
            if(canvas.GetNodeOrNull<Button>("DemoStart")!=null&&!Brand.Flavor.Demo)throw new Exception("Demo mode is offered in a flavor without it");
            await Capture(Brand.Flavor.Id+"-k02-signin.png");

            // Sign-in: Kia asks for a code. Nothing beyond the sign-in request is sent until the user picks where.
            await SignIn("tester@example.invalid","fixture-password","");
            if(screen!="Code"||pendingCode==null||fixture.Requests!=1||canvas.GetNodeOrNull<Button>("CodeText")==null||canvas.GetNode<Button>("CodeVerify").Disabled==false)
                throw new Exception($"Sign-in did not stop at the one-time code screen (screen {screen}, pending {pendingCode!=null}, requests {fixture.Requests} [{string.Join(",",fixture.Paths)}], notice '{notice}', text button {canvas.GetNodeOrNull<Button>("CodeText")!=null}, verify disabled {canvas.GetNodeOrNull<Button>("CodeVerify")?.Disabled})");
            await Capture(Brand.Flavor.Id+"-k03-code-choose.png");
            canvas.GetNode<Button>("CodeText").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||fixture.Codes==0);i++)await WaitFrames(1);
            if(fixture.Codes!=1||codeSentTo!="(***) ***-0000"||canvas.GetNode<Button>("CodeVerify").Disabled)throw new Exception("Text me a code did not send one code");
            await Capture(Brand.Flavor.Id+"-k04-code-sent.png");
            canvas.GetNode<LineEdit>("CodeInput").Text="123456";
            canvas.GetNode<Button>("CodeVerify").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||enrollment==null);i++)await WaitFrames(1);
            if(enrollment?.Count!=1||pendingCode!=null||(connection as IOneTimeCodeSignIn)?.RememberToken!="fixture-remember")
                throw new Exception("Verifying the code did not finish sign-in");
            notice="";Render();await Capture(Brand.Flavor.Id+"-k05-choose-vehicle.png");
            await ChooseVehicle();
            if(!liveMode||screen!="Car"||connection?.State?.Battery!=77||carModel.Id!=CarModels.Default.Id||carModel.Maker!="Kia")throw new Exception("The Kia car did not open on the Car screen");

            // Unsupported features are gone; supported ones are there.
            await WaitFrames();
            foreach(string gone in new[]{"FlashLights","HornLights","DemoEnd"})
                if(canvas.GetNodeOrNull(gone)!=null)throw new Exception(gone+" is still shown");
            if(canvas.GetNodeOrNull<Button>("DoorControl")?.Text!="Unlock")throw new Exception("Door control missing");
            notice="";Render();await Capture(Brand.Flavor.Id+"-k06-car.png");
            foreach(var (route,file) in new[]{("Climate","k07-climate.png"),("Energy","k08-energy.png"),("Details","k09-details.png"),
                ("Alerts","k10-alerts.png"),("Activity","k11-activity.png"),("Settings","k12-settings.png"),("Appearance","k13-appearance.png"),
                ("Model","k14-model.png"),("Paint","k15-paint.png"),("AppColor","k16-app-color.png"),("Range","k17-range.png"),("Limits","k18-limits.png")})
            {
                Navigate(route);notice="";Render();await WaitFrames(3);
                if(route=="Settings"&&(canvas.GetNodeOrNull("SettingsClimateOffer")!=null||canvas.GetNodeOrNull("SignOut")==null))throw new Exception("Settings still offers Send to car climate, or lost Sign out");
                if(route=="Activity"&&canvas.GetNodeOrNull("ActivityShared")!=null)throw new Exception("Activity still has the Shared tab");
                if(route=="Model"&&(canvas.GetNodeOrNull("Model_ioniq5n")!=null||canvas.GetNodeOrNull("Model_ev6gt")==null))throw new Exception("Model picker isn't Kia only for a Kia account");
                await Capture(Brand.Flavor.Id+"-"+file);
            }
            // App & car color: both on one screen. The default car is gray; a car swatch repaints the car.
            Navigate("AppColor");
            if(paintHex!=Brand.Flavor.DefaultPaintHex||canvas.GetNodeOrNull<Button>("CarColor0")==null||canvas.GetNodeOrNull<Button>("AppColor8")==null||canvas.GetNodeOrNull("CustomCarColor")==null)
                throw new Exception("App & car color screen is missing the car colors, or the default paint isn't gray");
            canvas.GetNode<Button>("CarColor1").EmitSignal(Godot.Button.SignalName.Pressed);
            if(paintHex!="2b4f86"||canvas.GetNode<Label>("CarColorName").Text!="YACHT BLUE")throw new Exception("Picking a car color on the app color screen didn't repaint the car");
            await Capture(Brand.Flavor.Id+"-k20-app-car-color-blue.png");
            canvas.GetNode<Button>("CarColor0").EmitSignal(Godot.Button.SignalName.Pressed);
            if(paintHex!="525252")throw new Exception("Couldn't pick the gray back");
            // One command: unlock sends exactly one Kia unlock request.
            Navigate("Car");
            int sent=fixture.Paths.Count(p=>p=="rems/door/unlock");
            canvas.GetNode<Button>("DoorControl").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&fixture.Paths.Count(p=>p=="rems/door/unlock")==sent;i++)await WaitFrames(1);
            if(fixture.Paths.Count(p=>p=="rems/door/unlock")!=sent+1)throw new Exception("Unlock did not send one Kia unlock");
            await WaitFrames(5);notice="";Render();await Capture(Brand.Flavor.Id+"-k19-car-after-unlock.png");
            GD.Print($"KIA SMOKE PASS ({Brand.App}): {(Brand.ChoosesService?"Hyundai/Kia choice, Kia disclaimer, ":"")}disclaimer, sign-in, one-time code (text), remember token, EV6 GT Car screen, every settings screen, unsupported features hidden, unlock. {fixture.Requests} fake Kia requests, no network.");
            GetTree().Quit();
        }
        catch(Exception ex){GD.PushError("KIA SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}
#endif
