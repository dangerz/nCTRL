#if DEBUG
using Godot;
using NGarage.Core;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public partial class Main
{
    private bool liveSmoke;
    // Explicit offline smoke mode only. Reserved .invalid account; no network.
    private sealed class ScreenFixture : HttpMessageHandler
    {
        public int Commands;
        public int Requests;
        public string FailStage="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string route=request.RequestUri!.AbsolutePath;
            Requests++;
            if((FailStage=="Authentication"&&route.EndsWith("/oauth/token"))||
               (FailStage=="Vehicle list"&&route.Contains("/enrollment/details/")))
            {
                FailStage="";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway){Content=new StringContent("offline gateway fixture")});
            }
            string body=route switch {
                "/v2/ac/oauth/token"=>"{\"access_token\":\"fixture\",\"refresh_token\":\"fixture\",\"expires_in\":3600}",
                "/ac/v2/enrollment/details/tester%40example.invalid" or "/ac/v2/enrollment/details/tester@example.invalid"=>"{\"enrolledVehicleDetails\":[{\"vehicleDetails\":{\"regid\":\"test\",\"vin\":\"TESTVIN0000000001\",\"nickName\":\"Test Ioniq 5 N\",\"modelCode\":\"IONIQ5N\",\"vehicleGeneration\":3,\"evStatus\":\"E\"}}]}",
                "/ac/v2/rcs/rvs/vehicleStatus"=>"{\"vehicleStatus\":{\"dateTime\":\"2026-10-22T10:00:00Z\",\"doorLock\":true,\"airCtrlOn\":false,\"airTemp\":{\"value\":\"72\",\"unit\":1},\"evStatus\":{\"batteryStatus\":78,\"batteryPlugin\":2,\"batteryCharge\":false,\"drvDistance\":[{\"rangeByFuel\":{\"totalAvailableRange\":{\"value\":184,\"unit\":3}}}],\"reservChargeInfos\":{\"targetSOClist\":[{\"plugType\":0,\"targetSOClevel\":90},{\"plugType\":1,\"targetSOClevel\":80}]}}}}",
                "/ac/v2/rcs/rdo/on"=>"",
                "/ac/v2/rmt/getRunningStatus"=>"{\"status\":\"SUCCESS\"}",
                "/ac/v2/rcs/rfc/findMyCar"=>"{\"coord\":{\"lat\":40.25,\"lon\":-74.5},\"time\":\"2026-10-23T10:00:00Z\"}",
                _=>throw new Exception("Unexpected fixture route")
            };
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
            if(route.EndsWith("/rdo/on")){Commands++;response.Headers.Add("tmsTid","fixture-command");}
            return Task.FromResult(response);
        }
    }
    private async Task LiveSmokeTest()
    {
        try
        {
            var fixture=new ScreenFixture();connectionFactory=()=>new HyundaiUsClient(fixture);
            AssertAccountOnly();
            foreach(string route in new[]{"Car","Energy","Activity","Settings","Paint","Appearance","Climate","Locate"})
            {Navigate(route);AssertAccountOnly();}
            if(fixture.Requests!=0)throw new Exception("Startup/navigation sent an unsolicited request");
            await Capture("09-disclaimer-test.png");
            await PassDisclaimerFixture();
            Navigate("Connect");await Capture("10-bluelink-signin.png");
            foreach(string stage in new[]{"Authentication","Vehicle list"})
            {
                fixture.FailStage=stage;
                await SignIn("tester@example.invalid","fixture-password","1234");
                if(!notice.StartsWith(stage+": HTTP 502")||liveBusy||enrollment!=null||liveMode||connection?.IsAuthenticated==true)
                    throw new Exception("Failed sign-in did not return to a usable, sanitized login screen");
                if(canvas.GetNode<Button>("SignIn").Disabled)throw new Exception("Manual sign-in is disabled after failure");
                await Capture(stage=="Authentication"?"22-authentication-502-test.png":"23-enrollment-502-test.png");
            }
            await SignIn("tester@example.invalid","fixture-password","1234");
            if(enrollment?.Count!=1)throw new Exception("Enrollment screen not loaded");
            Navigate("Car");AssertAccountOnly();
            notice="";Render();await Capture("11-vehicle-selection-test.png");
            await ChooseVehicle();
            if(!liveMode||connection?.State?.Battery!=78||fixture.Commands!=0)throw new Exception("Connect failed or sent a command");
            PickPaint("Ecotonic Gray","747c79");notice="";Render();await Capture("12-connected-test.png");
            Navigate("Paint");await Capture("13-ecotonic-gray.png");
            Navigate("Energy");await Capture("14-live-energy-test.png");
            Navigate("Climate");await Capture("15-live-climate-test.png");
            Navigate("Settings");await Capture("16-live-settings-test.png");
            Navigate("Appearance");
            if(canvas.GetNodeOrNull<LineEdit>("GarageName")==null)throw new Exception("Garage appearance editor missing");
            Navigate("Car");
            await OpenVehicleLocation();
            if(connection!.Location==null||canvas.GetNodeOrNull<Button>("VehicleMap")==null||fixture.Commands!=0)
                throw new Exception("Location entry did not automatically load map without a command");
            await Capture("19-vehicle-map-test.png");Navigate("Car");
            var door=canvas.GetNode<Button>("DoorControl");
            if(door.GetThemeStylebox("normal") is not StyleBoxFlat style||style.BgColor!=Red)
                throw new Exception("Locked door control is missing the N red accent");
            foreach(var child in canvas.GetChildren())
                if(child is Button b&&b.Text.Contains("Check last command",StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Manual command check is still visible");
            if(door.Text!="Unlock"||door.Disabled)throw new Exception("Combined door button does not match reported lock state");
            door.EmitSignal(Godot.Button.SignalName.Pressed);
            while(liveBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            foreach(var child in GetChildren())if(child is ConfirmationDialog)throw new Exception("Direct control opened a confirmation");
            if(fixture.Commands!=1||connection!.State!.Locked!=true)throw new Exception("Command count or optimistic update error");
            await Capture("17-command-accepted-test.png");
            await commandMonitor;
            if(connection.Pending?.Outcome!=RemoteOutcome.Confirmed)throw new Exception("Command confirmation missing");
            Navigate("Activity");await Capture("18-live-activity-test.png");
            Disconnect();
            if(liveMode||connection!=null)throw new Exception("Disconnect failed");
            AssertAccountOnly();
            await SignIn("tester@example.invalid","fixture-password","1234");
            if(!liveMode||screen!="Car"||fixture.Commands!=1)throw new Exception("Remembered vehicle did not reconnect without commands");
            GD.Print("LIVE SMOKE PASS: offline sign-in/recovery, selection, screens, direct door control, tracking, remembered vehicle. No real API calls.");
            GetTree().Quit();
        }
        catch(Exception ex){GD.PushError("LIVE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
    private Button FillDisclaimerFixture()
    {
        var submit=canvas.GetNodeOrNull<Button>("DisclaimerSubmit");
        if(submit==null||canvas.GetNodeOrNull<Button>("SignIn")!=null||canvas.GetNodeOrNull<Button>("BiometricUnlock")!=null)
            throw new Exception("Disclaimer gate is not the only reachable screen");
        var phrase=canvas.GetNode<LineEdit>("DisclaimerPhrase");var check=canvas.GetNode<CheckBox>("DisclaimerCheck");
        check.ButtonPressed=false;phrase.Text="";phrase.EmitSignal(LineEdit.SignalName.TextChanged,phrase.Text);
        if(!submit.Disabled)throw new Exception("Disclaimer submit enabled before confirmation");
        phrase.Text="I agreed";phrase.EmitSignal(LineEdit.SignalName.TextChanged,phrase.Text);check.ButtonPressed=true;
        if(!submit.Disabled)throw new Exception("Wrong phrase enabled disclaimer submit");
        phrase.Text=" i agree ";phrase.EmitSignal(LineEdit.SignalName.TextChanged,phrase.Text);check.ButtonPressed=false;
        if(!submit.Disabled)throw new Exception("Unchecked box enabled disclaimer submit");
        check.ButtonPressed=true;
        if(submit.Disabled)throw new Exception("Matching phrase and checkbox did not enable disclaimer submit");
        return submit;
    }
    private async Task PassDisclaimerFixture()
    {
        FillDisclaimerFixture().EmitSignal(Godot.Button.SignalName.Pressed);
        while(liveBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(DisclaimerRequired)throw new Exception("Disclaimer acceptance was not recorded");
    }
    private void AssertAccountOnly()
    {
        if(screen!="Connect"||liveMode||canvas.GetNodeOrNull<Button>("NavCar")!=null||connection?.Selected!=null)
            throw new Exception("Vehicle UI is accessible without a selected live vehicle");
    }
    private async Task Capture(string filename)
    {
        // Headless runs verify UI/session behavior only; they do not claim visual coverage.
        if(DisplayServer.GetName()=="headless")return;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string directory="user://";
        foreach(string arg in OS.GetCmdlineUserArgs())
            if(arg.StartsWith("--smoke-output="))directory=arg.Substring("--smoke-output=".Length).TrimEnd('/','\\')+"/";
        if(GetViewport().GetTexture().GetImage().SavePng(directory+filename)!=Error.Ok)
            throw new Exception("Could not write smoke-test screenshot.");
    }
}

#endif
