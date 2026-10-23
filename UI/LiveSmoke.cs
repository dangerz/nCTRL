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
        public string FailStage="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string route=request.RequestUri!.AbsolutePath;
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
            notice="";Render();await Capture("11-vehicle-selection-test.png");
            await ChooseVehicle();
            if(!liveMode||connection?.State?.Battery!=78||fixture.Commands!=0)throw new Exception("Connect failed or sent a command");
            PickPaint("Ecotonic Gray","747c79");notice="";Render();await Capture("12-connected-test.png");
            Navigate("Paint");await Capture("13-ecotonic-gray.png");
            Navigate("Energy");await Capture("14-live-energy-test.png");
            Navigate("Climate");await Capture("15-live-climate-test.png");
            Navigate("Settings");await Capture("16-live-settings-test.png");
            Navigate("Car");ConfirmLive(new(ActionKind.Unlock),"Unlock the vehicle?");
            ConfirmationDialog? dialog=null;
            foreach(var child in GetChildren())if(child is ConfirmationDialog d)dialog=d;
            if(dialog==null||fixture.Commands!=0)throw new Exception("Command sent before confirmation");
            await Capture("17-command-confirmation-test.png");
            dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            while(liveBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(fixture.Commands!=1||connection!.State!.Locked!=true)throw new Exception("Command count or optimistic update error");
            await CheckLive();
            if(connection.Pending?.Outcome!=RemoteOutcome.Confirmed)throw new Exception("Command confirmation missing");
            Navigate("Activity");await Capture("18-live-activity-test.png");
            Disconnect();
            if(liveMode||connection!=null)throw new Exception("Disconnect failed");
            GD.Print("LIVE SMOKE PASS: offline authentication/enrollment 502 recovery, sign-in, selection, screens, confirmation, tracking, disconnect. No real API calls.");
            GetTree().Quit();
        }
        catch(Exception ex){GD.PushError("LIVE SMOKE FAIL: "+ex.Message);GetTree().Quit(1);}
    }
}
