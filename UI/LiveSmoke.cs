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

public partial class Main
{
    private bool liveSmoke;
    // Real read and location limits; no sign-in or command cooldown so flows can run back to back.
    private static readonly IReadOnlyDictionary<RequestKind,RequestLimit> SmokeLimits=
        new Dictionary<RequestKind,RequestLimit>(RequestBudget.Defaults){[RequestKind.SignIn]=new(TimeSpan.Zero,100),[RequestKind.Command]=new(TimeSpan.Zero,100)};
    // Explicit offline smoke mode only. Reserved .invalid account; no network.
    private sealed class ScreenFixture : HttpMessageHandler
    {
        public int Commands;
        public int Requests;
        public int Tokens;
        public string LastBody="",LastPath="";
        // Turns the reported car unlocked, with a door and a window open and a weak 12-volt battery.
        public bool Unhealthy;
        // Turns the reported car into one that is charging at 7.2 kW with 95 minutes left.
        public bool Charging;
        // Charging, but reported three hours ago, so Hyundai's finish time has passed.
        public bool ChargingOverdue;
        // The reported location carries a speed.
        public bool Moving;
        // The car reports climate on at 68°F: driver seat cool low, passenger off, steering wheel and defrost off.
        public bool ClimateOn;
        // Surround View: a picture whose capture time moves forward once a capture is requested.
        public string SvmImage="";public int SvmRequests;
        // Destinations sent to the car, and climate starts.
        public int Destinations,ClimateStarts;
        public string FailStage="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string route=request.RequestUri!.AbsolutePath;
            Requests++;if(route.EndsWith("/oauth/token"))Tokens++;LastPath=route;
            LastBody=request.Content==null?"":request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            if((FailStage=="Authentication"&&route.EndsWith("/oauth/token"))||
               (FailStage=="Vehicle list"&&route.Contains("/enrollment/details/")))
            {
                FailStage="";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway){Content=new StringContent("offline gateway fixture")});
            }
            if(route=="/ac/v2/svm/getSVMDetails") {
                string time=SvmRequests>0?DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"):"2026-10-20T08:00:00Z";
                var svm="{\"svmDetails\":[{\"svmDetail\":{\"svmImage\":\""+SvmImage+"\",\"gpsDetail\":{\"time\":\""+time+"\",\"coord\":{\"lat\":40.111111,\"lon\":-74.222222},\"head\":90},\"trunkOpen\":false}}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(svm,Encoding.UTF8,"application/json")});
            }
            if(route=="/ac/v2/svm/findMyCarSVM")SvmRequests++;
            if(route=="/ac/v2/rcs/stc/sendPoi")Destinations++;
            if(route=="/ac/v2/evc/fatc/start")ClimateStarts++;
            string body=route switch {
                "/v2/ac/oauth/token"=>"{\"access_token\":\"fixture\",\"refresh_token\":\"fixture\",\"expires_in\":3600}",
                "/ac/v2/enrollment/details/tester%40example.invalid" or "/ac/v2/enrollment/details/tester@example.invalid"=>"{\"enrolledVehicleDetails\":[{\"vehicleDetails\":{\"regid\":\"test\",\"vin\":\"TESTVIN0000000001\",\"nickName\":\"Test Ioniq 5 N\",\"modelCode\":\"IONIQ5N\",\"vehicleGeneration\":3,\"evStatus\":\"E\"}}]}",
                "/ac/v2/rcs/rvs/vehicleStatus"=>"{\"vehicleStatus\":{\"dateTime\":\"2026-10-22T10:00:00Z\",\"doorLock\":true,\"battery\":{\"batSoc\":87},\"doorOpen\":{\"frontLeft\":0,\"frontRight\":0},\"tirePressure\":{\"tirePressureFrontLeft\":36,\"tirePressureFrontRight\":36,\"tirePressureRearLeft\":35,\"tirePressureRearRight\":29},\"tirePressureLamp\":{\"tirePressureWarningLampAll\":1,\"tirePressureWarningLampRearRight\":1},\"vehicleLocation\":{\"coord\":{\"lat\":40.111111,\"lon\":-74.222222}},\"airCtrlOn\":false,\"airTemp\":{\"value\":\"72\",\"unit\":1},\"evStatus\":{\"batteryStatus\":78,\"batteryPlugin\":2,\"batteryCharge\":false,\"drvDistance\":[{\"rangeByFuel\":{\"totalAvailableRange\":{\"value\":184,\"unit\":3}}}],\"reservChargeInfos\":{\"targetSOClist\":[{\"plugType\":0,\"targetSOClevel\":90},{\"plugType\":1,\"targetSOClevel\":80}]}}}}",
                "/ac/v2/rcs/rdo/on" or "/ac/v2/evc/fatc/start" or "/ac/v2/rcs/rhl/light" or "/ac/v2/rcs/rhl/hnl" or "/ac/v2/evc/charge/targetsoc/set" or "/ac/v2/svm/findMyCarSVM" or "/ac/v2/rcs/stc/sendPoi"=>"",
                "/ac/v2/rmt/getRunningStatus"=>"{\"status\":\"SUCCESS\"}",
                "/ac/v2/rcs/rfc/findMyCar"=>"{\"coord\":{\"lat\":40.25,\"lon\":-74.5},\"head\":135,\"time\":\"2026-10-23T10:00:00Z\"}",
                _=>throw new Exception("Unexpected fixture route")
            };
            if(Unhealthy&&route.EndsWith("/vehicleStatus"))
                body=body.Replace("\"doorLock\":true","\"doorLock\":false").Replace("\"batSoc\":87","\"batSoc\":41")
                    .Replace("\"doorOpen\":{\"frontLeft\":0,\"frontRight\":0}","\"doorOpen\":{\"frontLeft\":1,\"frontRight\":0},\"windowOpen\":{\"backLeft\":1,\"backRight\":1},\"hoodOpen\":true");
            if(Charging&&route.EndsWith("/vehicleStatus"))
                body=body.Replace("\"batteryCharge\":false","\"batteryCharge\":true,\"realTimePower\":7.2,\"remainTime2\":{\"atc\":{\"value\":95,\"unit\":1}}")
                    .Replace("2026-10-22T10:00:00Z",DateTimeOffset.UtcNow.AddHours(ChargingOverdue?-3:0).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            if(ClimateOn&&route.EndsWith("/vehicleStatus"))
                body=body.Replace("\"airCtrlOn\":false,\"airTemp\":{\"value\":\"72\",\"unit\":1}",
                    "\"airCtrlOn\":true,\"airTemp\":{\"value\":\"68\",\"unit\":1},\"seatHeaterVentState\":{\"flSeatHeatState\":3,\"frSeatHeatState\":0},\"steerWheelHeat\":0,\"defrost\":false");
            if(Moving&&route.EndsWith("/vehicleStatus"))
                body=body.Replace("\"lon\":-74.222222}}","\"lon\":-74.222222},\"speed\":{\"value\":34,\"unit\":1}}");
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
            if(route.EndsWith("/rdo/on")){Commands++;response.Headers.Add("tmsTid","fixture-command");}
            // Tracked, so the follow-up check confirms on its first try instead of reading status for two minutes.
            if(route.EndsWith("/fatc/start")||route.EndsWith("/targetsoc/set")||route.EndsWith("/sendPoi"))response.Headers.Add("tmsTid","fixture-command");
            return Task.FromResult(response);
        }
    }
    // A Google Maps short link, for smoke runs: redirects once to a full Google Maps place address.
    private sealed class RedirectFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.Host!="maps.app.goo.gl")throw new Exception("Unexpected short link host");
            var response=new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location=new Uri("https://www.google.com/maps/place/Corner+Cafe/@40.2,-74.4,15z/data=!3m1!4b1!3d40.26!4d-74.51");
            return Task.FromResult(response);
        }
    }
    // The weather service, for smoke runs: always 28°F, never the network.
    private WeatherFixture? weatherFixture;
    private sealed class WeatherFixture : HttpMessageHandler
    {
        public int Requests;public string LastUrl="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;LastUrl=request.RequestUri!.ToString();
            if(request.RequestUri.Host!=OutsideTemperature.Host)throw new Exception("Unexpected weather host");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"current\":{\"temperature_2m\":28}}",Encoding.UTF8,"application/json")});
        }
    }
    // Every color drawn is from the current theme or a fixed meaning color: none left over from the default blue
    // (App color's swatches excepted, since they show each choice).
    private void AssertThemed(string where)
    {
        var defaults=AppTheme.Default.Values.ToHashSet();var current=Skin.All.Select(c=>c.ToHtml(false)).ToHashSet();
        var stray=new List<string>();
        void Look(Node node,string what,Color color){string hex=color.ToHtml(false);if(defaults.Contains(hex)&&!current.Contains(hex))stray.Add($"{node.Name} {what} #{hex}");}
        var nodes=new List<Node>{canvas};
        for(int i=0;i<nodes.Count;i++)nodes.AddRange(nodes[i].GetChildren());
        foreach(var node in nodes) {
            if(node.Name.ToString().StartsWith("Swatch"))continue;
            if(node is ColorRect rect)Look(node,"color",rect.Color);
            if(node is PanelArt panel){Look(node,"fill",panel.Fill);Look(node,"edge",panel.Edge);}
            if(node is Glyph glyph)Look(node,"ink",glyph.Ink);
            if(node is Control control) {
                foreach(string name in new[]{"font_color","font_hover_color","font_pressed_color"})
                    if(control.HasThemeColorOverride(name))Look(node,name,control.GetThemeColor(name));
                foreach(string state in new[]{"normal","hover","pressed","focus","disabled"})
                    if(control.HasThemeStyleboxOverride(state)&&control.GetThemeStylebox(state) is StyleBoxFlat box){Look(node,state,box.BgColor);Look(node,state+" edge",box.BorderColor);}
            }
        }
        if(stray.Count>0)throw new Exception("Default colors left on "+where+": "+string.Join(", ",stray.Take(5)));
    }
    private async Task WaitFrames(int frames=20){for(int i=0;i<frames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
    private async Task LiveSmokeTest()
    {
        try
        {
            var fixture=new ScreenFixture();connectionFactory=()=>new HyundaiUsClient(fixture,null,budget);
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
            // The cold-weather estimate: one weather request with only the rounded area of the car's status location.
            await WaitFrames();
            if(weatherFixture!.Requests!=1||!weatherFixture.LastUrl.Contains("latitude=40.1&longitude=-74.2&")||weatherFixture.LastUrl.Contains("40.11")||
                canvas.GetNodeOrNull<Label>("ColdRange")?.Text!="~129 mi at 28°F"||canvas.GetNode<Label>("Range").Text!="184 mi")
                throw new Exception("The cold-weather range did not show after one rounded weather request");
            // The connect read starts the read cooldown: warn every time, Cancel sends nothing, an override sends exactly one.
            int reads=fixture.Requests;
            await RefreshLive(false);
            if(pendingOverride==null||canvas.GetNodeOrNull<Button>("OverrideConfirm")==null||fixture.Requests!=reads)
                throw new Exception("Read cooldown did not warn before sending");
            await Capture("25-limit-override-test.png");
            canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingOverride!=null||fixture.Requests!=reads)throw new Exception("Cancelled override sent a request");
            await RefreshLive(false);
            if(pendingOverride==null)throw new Exception("A second refused request was not warned again");
            canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||fixture.Requests==reads);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(fixture.Requests!=reads+1||pendingOverride!=null)throw new Exception("Override did not send exactly one request");
            await RefreshLive(false);
            if(pendingOverride==null||fixture.Requests!=reads+1)throw new Exception("Override applied to more than one request");
            canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            Navigate("Settings");canvas.GetNode<Button>("ViewLimits").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Limits"||canvas.GetNodeOrNull<Button>("LimitsBack")==null)throw new Exception("Request limits screen missing");
            await Capture("26-request-limits-test.png");
            Navigate("Car");
            PickPaint("Ecotonic Gray","747c79");notice="";Render();await Capture("12-connected-test.png");
            // Car art follows the reported state (locked, climate off, seats unreported), and every tint renders.
            var overlay=canvas.GetNode<StatusOverlay>("StatusOverlay");
            if(overlay.Lock!=LockEffect.Locked||overlay.Windshield!=ComfortTint.None||overlay.DriverSeat!=ComfortTint.None||overlay.WheelHeat)
                throw new Exception("Car art does not reflect reported lock, climate and seat state");
            if(canvas.GetNodeOrNull<Button>("LockBadge") is not {Disabled: false} badge||!new Rect2(badge.Position,badge.Size).Encloses(new Rect2(badge.Position,new Vector2(44,44))))
                throw new Exception("The padlock on the car is not a usable tap target");
            foreach(var (windshield,driverTint,passengerTint,wheel,file) in new[]{
                (ComfortTint.Heat,ComfortTint.Heat,ComfortTint.Heat,true,"27-art-heat-test.png"),
                (ComfortTint.Cool,ComfortTint.Cool,ComfortTint.None,false,"28-art-cool-test.png")}) {
                overlay.Windshield=windshield;overlay.DriverSeat=driverTint;overlay.PassengerSeat=passengerTint;overlay.WheelHeat=wheel;overlay.QueueRedraw();
                for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await Capture(file);
            }
            Render();
            foreach(var child in canvas.GetChildren())
                if(child is Button b&&b.Text.Contains("paint",StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Car screen still offers paint customization");
            await PullChecks(fixture);
            // Paint is reached only through Settings → Appearance, and returns there.
            Navigate("Settings");
            if(canvas.GetNodeOrNull("StatusOverlay")!=null)throw new Exception("Car-art animation keeps running off the Car screen");
            canvas.GetNode<Button>("SettingsAppearance").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Appearance")throw new Exception("Settings does not open Appearance");
            canvas.GetNode<Button>("SettingsPaint").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Paint")throw new Exception("Appearance does not open car color");
            await Capture("13-ecotonic-gray.png");
            // Custom color, as a user does it: open the picker, tap a color, tap outside to close.
            var picker=canvas.GetNode<ColorPickerButton>("CustomPaint");
            await Tap(picker.GlobalPosition+picker.Size/2);
            var popup=picker.GetPopup();
            if(!popup.Visible)throw new Exception("Custom color picker did not open");
            await Tap(popup.Position+new Vector2(40,40));
            await Capture("32-custom-color-open-test.png");
            await Tap(new Vector2(440,120));
            for(int i=0;i<10;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            // The screen is rebuilt after the picker closes, so the old popup is freed by now.
            if(GodotObject.IsInstanceValid(popup)&&popup.Visible||screen!="Paint"||paintName!="Custom color"||canvas.GetNodeOrNull<ColorPickerButton>("CustomPaint")==null)
                throw new Exception("Custom color was not applied after closing the picker");
            await Capture("33-custom-color-applied-test.png");
            canvas.GetNode<Button>("PaintDone").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Appearance")throw new Exception("Paint Done does not return to Appearance");
            Navigate("Energy");await Capture("14-live-energy-test.png");
            Navigate("Climate");await Capture("15-live-climate-test.png");
            Navigate("Settings");await Capture("16-live-settings-test.png");
            // Details tab: tires first, then every reported field, never the vehicle's coordinates.
            Navigate("Details");
            if(canvas.GetNodeOrNull<Button>("NavDetails")==null||canvas.GetNodeOrNull<Label>("Tire0")?.Text!="36 psi"||canvas.GetNodeOrNull<Label>("Tire3")?.Text!="29 psi · Low")
                throw new Exception("Details tab does not show tire pressures and warnings");
            var detailText=string.Join("|",canvas.GetNode("DetailScroll").FindChildren("*","Label",true,false).Cast<Label>().Select(l=>l.Text));
            if(!detailText.Contains("12-volt battery|87%")||detailText.Contains("40.111")||detailText.Contains("74.222")||detailText.Contains("coord"))
                throw new Exception("Full detail is missing fields or shows the vehicle location");
            await Capture("31-details-test.png");
            Navigate("Appearance");
            if(canvas.GetNodeOrNull<LineEdit>("GarageName")==null)throw new Exception("Garage appearance editor missing");
            // Each car's name and picture: Hyundai's model code picked the IONIQ 5 N, and both can be changed.
            if(carModel.Id!="ioniq5n"||canvas.GetNodeOrNull<Label>("CarTitle")?.Text!="IONIQ 5 N")throw new Exception("Model code did not pick the IONIQ 5 N picture and name");
            canvas.GetNode<LineEdit>("CarName").Text="  Track   Toy ";canvas.GetNode<Button>("SaveCarName").EmitSignal(Godot.Button.SignalName.Pressed);
            if(canvas.GetNodeOrNull<Label>("CarTitle")?.Text!="Track Toy"||cars[linkedVehicle].Name!="Track Toy")throw new Exception("Car name was not saved for this vehicle");
            canvas.GetNode<Button>("SettingsModel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Model"||canvas.GetChildren().OfType<Button>().Count(b=>b.Name.ToString().StartsWith("Model_"))!=CarModels.All.Count)
                throw new Exception("The model picker does not list every picture");
            await Capture("51-model-picker-test.png");
            canvas.GetNode<Button>("Model_ev6").EmitSignal(Godot.Button.SignalName.Pressed);
            if(carModel.Id!="ev6"||!notice.Contains("as a picture")||canvas.GetNodeOrNull<Label>("CarTitle")?.Text!="Track Toy")
                throw new Exception("Picking a picture-only model did not keep the name and explain it");
            canvas.GetNode<Button>("ModelDone").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Appearance")throw new Exception("Model Done does not return to Appearance");
            await Capture("52-appearance-car-test.png");
            canvas.GetNode<LineEdit>("CarName").Text=" ";canvas.GetNode<Button>("SaveCarName").EmitSignal(Godot.Button.SignalName.Pressed);
            if(canvas.GetNodeOrNull<Label>("CarTitle")?.Text!="Kia EV6")throw new Exception("A blank car name does not fall back to the model name");
            // Back to the car's own picture for the remaining captures.
            PickModel(CarModels.ById("ioniq5n"));
            Navigate("Car");
            await OpenVehicleLocation();
            if(connection!.Location==null||canvas.GetNodeOrNull<Button>("VehicleMap")==null||fixture.Commands!=0)
                throw new Exception("Location entry did not automatically load map without a command");
            if(canvas.GetNodeOrNull<CarMarker>("VehicleMap/CarMarker") is not {Heading: 135,Picture: not null} marker||marker.Position!=new Vector2(176,130))
                throw new Exception("The car marker is not its top-down picture, centered and turned the way the car faces");
            await Capture("19-vehicle-map-test.png");
            // Your phone close by: the map zooms out to show both. Far away: the car stays close up and an arrow points to you.
            ShowPhone(40.27,-74.52,25);for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(mapFrame is not {Zoom: <15} near||!near.Shows(40.27,-74.52,VehicleMap.FitMargin)||canvas.GetNodeOrNull<MapMarks>("VehicleMap/MapMarks")?.Phone==null)
                throw new Exception("A nearby phone position did not fit the map to show both");
            await Capture("19-vehicle-map-phone-near-test.png");
            ShowPhone(40.6,-74.9,25);for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(mapFrame is not {Zoom: 15} far||far.Shows(40.6,-74.9))throw new Exception("A distant phone position moved the map off the car");
            await Capture("19-vehicle-map-phone-far-test.png");
            phoneFix=null;
            // Send to car: an address that isn't found, one that is (confirmed, then cancelled: nothing sent), then coordinates sent once.
            lookupFixture=query=>query.Contains("Main")?new Destination("Corner Cafe","1 Main St, Springfield, DC 20500",40.26,-74.51,"20500"):null;
            canvas.GetNode<Button>("SendToCar").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Send"||canvas.GetNodeOrNull<LineEdit>("DestinationInput")==null)throw new Exception("Send to car does not open from the map");
            await FindDestination("Nowhere at all");
            if(pendingDestination!=null||!noticeError)throw new Exception("An address that wasn't found was not reported");
            await FindDestination("1 Main St");
            if(pendingDestination?.Name!="Corner Cafe"||canvas.GetNodeOrNull<Label>("DestinationName")?.Text!="Corner Cafe"||canvas.GetNodeOrNull<PlacePin>("DestinationMap/DestinationPin")==null)
                throw new Exception("A found address is not shown for confirming");
            await Capture("71-send-confirm-test.png");
            int sends=fixture.Destinations;
            canvas.GetNode<Button>("DestinationCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingDestination!=null||fixture.Destinations!=sends)throw new Exception("Cancel sent the destination");
            await FindDestination("40.26, -74.51");
            canvas.GetNode<Button>("DestinationSend").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(fixture.Destinations!=sends+1||canvas.GetNodeOrNull<Label>("SentTitle")?.Text!="SENT TO CAR"||!fixture.LastBody.Contains("\"actionType\":\"SAVE\"")||
                !fixture.LastBody.Contains("\"lat\":\"40.260000\""))throw new Exception("Send to car did not send once and show Sent To Car");
            // It's 28°F at the (test) car, so the dialog offers your Warm up preset; starting it sends one climate start.
            if(canvas.GetNodeOrNull<Button>("SentClimate")?.Text.StartsWith("Start Warm up  ·  "+Units.Setpoint(warmPreset.TemperatureF,units))!=true||
                canvas.GetNodeOrNull<Button>("SentDisable")==null)throw new Exception("Sent To Car does not offer to start the Warm up preset");
            await Capture("72-sent-to-car-test.png");
            int climateStarts=fixture.ClimateStarts;
            canvas.GetNode<Button>("SentClimate").EmitSignal(Godot.Button.SignalName.Pressed);
            await WaitForCommandChecks();
            if(sentDestination!=null||screen!="Car"||fixture.ClimateStarts!=climateStarts+1)
                throw new Exception("Starting climate from Sent To Car did not close the dialog and start Warm up");
            // The car reports climate on (started from anywhere): the Climate page shows its settings; when it reports
            // off, your own choices come back.
            var mine=(driverSeat,passengerSeat,wheelHeat,climateDefrost);
            async Task ReadAgain(){
                await ToSignal(GetTree().CreateTimer(16),SceneTreeTimer.SignalName.Timeout);await RefreshLive(false);
                if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            }
            fixture.ClimateOn=true;await ReadAgain();Navigate("Climate");
            if(canvas.GetNode<Label>("ClimateStatus").Text!="CLIMATE ON  ·  68°F"||canvas.GetNode<Label>("ClimateTemperature").Text!="68°F"||
                canvas.GetNode<OptionButton>("DriverSeat").Selected!=(int)SeatMode.CoolLow||canvas.GetNode<OptionButton>("PassengerSeat").Selected!=(int)SeatMode.Off||
                canvas.GetNode<CheckBox>("SteeringWheelHeat").ButtonPressed||canvas.GetNode<CheckBox>("Defrost").ButtonPressed)
                throw new Exception("The Climate page does not show the car's reported climate settings");
            await Capture("76-climate-on-test.png");
            fixture.ClimateOn=false;await ReadAgain();Navigate("Climate");
            if(canvas.GetNode<Label>("ClimateStatus").Text!="CLIMATE OFF"||(driverSeat,passengerSeat,wheelHeat,climateDefrost)!=mine)
                throw new Exception("Your climate choices did not come back when the car reported climate off");
            await Capture("77-climate-off-test.png");
            // The widget: the status and painted car are in the (test) App Group, with nothing identifying in them.
            if(!sharedFixture.TryGetValue("status.json",out var widgetJson)||!sharedFixture.TryGetValue("car.png",out var widgetCar))
                throw new Exception("The widget's status and picture were not written");
            string widgetText=Encoding.UTF8.GetString(widgetJson);
            using(var widgetImage=new Image()) {
                if(widgetImage.LoadPngFromBuffer(widgetCar)!=Error.Ok||widgetImage.DetectAlpha()==Image.AlphaMode.None||widgetImage.GetWidth()>460)
                    throw new Exception("The widget's car picture is not a small picture with a transparent background");
            }
            if(!widgetText.Contains("\"battery\":")||widgetText.Contains("TESTVIN")||widgetText.Contains("40.1")||widgetText.Contains("74.")||widgetText.Contains("tester"))
                throw new Exception("The widget's status is missing or holds identifying details");
            // Turning it off deletes what the widget shows; on writes it again.
            Navigate("Appearance");
            canvas.GetNode<CheckBox>("WidgetOn").ButtonPressed=false;
            if(sharedFixture.ContainsKey("status.json")||sharedFixture.ContainsKey("car.png"))throw new Exception("Turning the widget off did not delete its files");
            canvas.GetNode<CheckBox>("WidgetOn").ButtonPressed=true;
            if(!sharedFixture.ContainsKey("status.json")||!sharedFixture.ContainsKey("car.png"))throw new Exception("Turning the widget on did not fill it again");
            await Capture("73-appearance-widget-test.png");
            SaveSmokeFile("widget-status.json",sharedFixture["status.json"]);SaveSmokeFile("widget-car.png",sharedFixture["car.png"]);
            // Shared from Apple Maps (its contact card has the place): sent to the car right away, then Sent To Car.
            async Task SharedSent(){for(int i=0;i<600&&(lookingUp||liveBusy||sentDestination==null);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            int shared=fixture.Destinations;
            sharedFixture["share.json"]=Encoding.UTF8.GetBytes("{\"version\":1,\"urls\":[\"https://maps.apple/p/abc\"],\"texts\":[\"BEGIN:VCARD\\r\\nFN:Central Park\\r\\nitem1.ADR;type=WORK:;;5th Ave;New York;NY;10022;\\r\\nitem2.URL;type=pref:http://maps.apple.com/?ll=40.7812\\\\,-73.9665\\r\\nEND:VCARD\"]}");
            CheckShared();await SharedSent();
            if(sentDestination is not {Name: "Central Park",Address: "5th Ave, New York, NY 10022"}||fixture.Destinations!=shared+1||sharedFixture.ContainsKey("share.json")||
                canvas.GetNodeOrNull<Label>("SentTitle")==null)throw new Exception("An Apple Maps share was not sent to the car right away");
            await Capture("74-shared-place-test.png");
            // Disable this: no more offers, saved as a preference.
            canvas.GetNode<Button>("SentDisable").EmitSignal(Godot.Button.SignalName.Pressed);
            if(climateOffer||sentDestination!=null||!notice.Contains("Turn it back on in Settings"))throw new Exception("Disable this did not turn off the climate offer");
            // Shared from Google Maps (name, address and a short link): looked up by its address, keeping its name.
            sharedFixture["share.json"]=Encoding.UTF8.GetBytes("{\"version\":1,\"urls\":[\"https://maps.app.goo.gl/xyz\"],\"texts\":[\"Corner Cafe\\n1 Main St, Springfield\\nhttps://maps.app.goo.gl/xyz\"]}");
            CheckShared();await SharedSent();
            if(sentDestination is not {Name: "Corner Cafe",Latitude: 40.26}||fixture.Destinations!=shared+2)throw new Exception("A Google Maps share was not looked up by its address and sent");
            if(canvas.GetNodeOrNull<Button>("SentClimate")!=null||canvas.GetNode<Button>("SentOk").Text!="OK")throw new Exception("The climate offer still showed after Disable this");
            await Capture("74-sent-without-offer-test.png");
            canvas.GetNode<Button>("SentOk").EmitSignal(Godot.Button.SignalName.Pressed);
            // Settings turns it back on.
            Navigate("Settings");
            if(canvas.GetNode<Button>("SettingsClimateOffer").Text!="Offer climate after Send to car  /  Off")throw new Exception("Settings does not show the climate offer is off");
            canvas.GetNode<Button>("SettingsClimateOffer").EmitSignal(Godot.Button.SignalName.Pressed);
            if(!climateOffer||canvas.GetNode<Button>("SettingsClimateOffer").Text!="Offer climate after Send to car  /  On")throw new Exception("Settings did not turn the climate offer back on");
            await Capture("74-settings-climate-offer-test.png");
            // A Google Maps share with only its short link: opened once (a test redirect) for the place's pin and name.
            // The extension's receipts come too, including a share it couldn't save.
            shortLinkFixture=new RedirectFixture();
            string at=DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            sharedFixture["share.json"]=Encoding.UTF8.GetBytes("{\"version\":1,\"id\":\"g1\",\"urls\":[\"https://maps.app.goo.gl/only\"],\"texts\":[],\"kinds\":[\"link\"]}");
            sharedFixture["share-log.json"]=Encoding.UTF8.GetBytes("[{\"id\":\"g1\",\"at\":\""+at+"\",\"kinds\":[\"link\"],\"hosts\":[\"maps.app.goo.gl\"],\"saved\":true,\"opened\":false},"+
                "{\"id\":\"x\",\"at\":\""+at+"\",\"kinds\":[\"text\"],\"hosts\":[],\"saved\":false,\"opened\":false}]");
            CheckShared();await SharedSent();
            if(sentDestination is not {Name: "Corner Cafe",Latitude: 40.26,Longitude: -74.51}||fixture.Destinations!=shared+3)
                throw new Exception("A Google Maps short link was not read for its pin and name, and sent");
            canvas.GetNode<Button>("SentOk").EmitSignal(Godot.Button.SignalName.Pressed);
            // Sent by the Share sheet itself while nCTRL was closed: listed in Activity, nothing sent again.
            sharedFixture["share-log.json"]=Encoding.UTF8.GetBytes("[{\"id\":\"s1\",\"at\":\""+at+"\",\"kinds\":[\"link\"],\"hosts\":[\"maps.apple.com\"],\"saved\":true,\"opened\":false,\"sent\":true,"+
                "\"place\":{\"name\":\"The Ellipse\",\"address\":\"Washington, DC\",\"zip\":\"20500\",\"lat\":38.8939,\"lon\":-77.0365}}]");
            int destinationsBefore=fixture.Destinations;
            CheckShared();
            if(sharedHistory[0].Place?.Name!="The Ellipse"||!history.Any(h=>h.Title=="Sent to car: The Ellipse"&&h.Detail.EndsWith("from the Share sheet"))||fixture.Destinations!=destinationsBefore)
                throw new Exception("A place sent from the Share sheet was not listed, or was sent again");
            // The Share sheet tried and Hyundai refused: nCTRL shows the place but doesn't send it again by itself.
            sharedFixture["share.json"]=Encoding.UTF8.GetBytes("{\"version\":1,\"id\":\"u1\",\"urls\":[\"https://maps.apple.com/?ll=38.8977,-77.0365&q=Lafayette%20Square\"],\"texts\":[],\"unsent\":\"Hyundai is rate limiting requests.\"}");
            CheckShared();for(int i=0;i<60;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(pendingDestination?.Name!="Lafayette Square"||sentDestination!=null||fixture.Destinations!=destinationsBefore||!notice.Contains("couldn't send it"))
                throw new Exception("A share the Share sheet couldn't send was sent again, or not shown");
            pendingDestination=null;
            // Activity → Shared lists every share, newest first; a place opens again in Send to car; Clear empties it.
            Navigate("Activity");canvas.GetNode<Button>("ActivityShared").EmitSignal(Godot.Button.SignalName.Pressed);
            if(sharedHistory.Count!=6||sharedHistory[2] is not {Source: "Google Maps",Opened: false,Place.Name: "Corner Cafe"}||sharedHistory[3].Place!=null||
                !sharedHistory[3].Problem.StartsWith("Nothing in it")||sharedHistory[5].Place?.Name!="Central Park"||canvas.FindChild("Shared0",true,false)==null)
                throw new Exception("Activity → Shared does not list the shares");
            await Capture("75-activity-shared-test.png");
            ((Button)canvas.FindChild("Shared5",true,false)).EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Send"||pendingDestination?.Name!="Central Park"||fixture.Destinations!=shared+3)throw new Exception("A shared place did not open again in Send to car (without sending)");
            pendingDestination=null;Navigate("Activity");
            canvas.GetNode<Button>("SharedClear").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<10;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(sharedHistory.Count!=0)throw new Exception("Clear did not empty the shared places");
            activityTab="Requests";
            Navigate("Car");
            // Within the hourly limit, reopening shows the last known location marked old, without a request.
            int located=fixture.Requests;await OpenVehicleLocation();
            if(fixture.Requests!=located||pendingOverride!=null||canvas.GetNodeOrNull<Label>("LocationOld")?.Text.StartsWith("OLD LOCATION")!=true||
                canvas.GetNodeOrNull<Button>("VehicleMap")==null)throw new Exception("Reopening within the location limit did not show the old location");
            // Asking anyway warns about the limit and keeps the old map up.
            canvas.GetNode<Button>("UpdateLocation").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(pendingOverride?.Kind!=RequestKind.Location||fixture.Requests!=located||canvas.GetNodeOrNull<Label>("LocationOld")==null)
                throw new Exception("A refused location request did not keep the old location");
            await Capture("49-old-location-test.png");
            canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);Navigate("Car");
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
            // Flash lights, then horn & lights: one request each; headlights blink (and the horn "!" shows) for about 4 seconds.
            Navigate("Car");
            canvas.GetNode<Button>("FlashLights").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||monitoringCommand);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            var art=canvas.GetNode<StatusOverlay>("StatusOverlay");
            if(!fixture.LastPath.EndsWith("/rcs/rhl/light")||!art.Flashing||art.Horn)throw new Exception("Flash lights did not send its request or light the headlights");
            await Capture("34-flash-lights-test.png");
            canvas.GetNode<Button>("HornLights").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||monitoringCommand);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            art=canvas.GetNode<StatusOverlay>("StatusOverlay");
            if(!fixture.LastPath.EndsWith("/rcs/rhl/hnl")||!art.Flashing||!art.Horn)throw new Exception("Horn & lights did not send its request or show the horn");
            await Capture("35-horn-lights-test.png");
            await ToSignal(GetTree().CreateTimer(4.3),SceneTreeTimer.SignalName.Timeout);
            if(canvas.GetNode<StatusOverlay>("StatusOverlay").Flashing)throw new Exception("Headlights and horn did not clear after a few seconds");
            // Seat and steering-wheel choices ride on the single climate start.
            Navigate("Climate");
            var driver=canvas.GetNode<OptionButton>("DriverSeat");driver.Select((int)SeatMode.HeatHigh);driver.EmitSignal(OptionButton.SignalName.ItemSelected,(long)SeatMode.HeatHigh);
            var passenger=canvas.GetNode<OptionButton>("PassengerSeat");passenger.Select((int)SeatMode.CoolLow);passenger.EmitSignal(OptionButton.SignalName.ItemSelected,(long)SeatMode.CoolLow);
            canvas.GetNode<CheckBox>("SteeringWheelHeat").ButtonPressed=true;
            await Capture("30-climate-seats-test.png");
            canvas.GetNode<Button>("ClimateStart").EmitSignal(Godot.Button.SignalName.Pressed);
            while(liveBusy)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!fixture.LastBody.Contains("\"drvSeatHeatState\":8")||!fixture.LastBody.Contains("\"astSeatHeatState\":3")||
               !fixture.LastBody.Contains("\"rlSeatHeatState\":0")||!fixture.LastBody.Contains("\"heating1\":3"))
                throw new Exception("Climate start did not carry the chosen seats and steering wheel");
            // Climate presets: Warm up starts climate with its saved settings; Save as stores the current ones.
            await WaitForCommandChecks();
            Navigate("Climate");
            canvas.GetNode<Button>("PresetWarm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!fixture.LastPath.EndsWith("/fatc/start")||!fixture.LastBody.Contains("\"value\":\"76\"")||!fixture.LastBody.Contains("\"drvSeatHeatState\":8")||
               !fixture.LastBody.Contains("\"astSeatHeatState\":8")||!fixture.LastBody.Contains("\"heating1\":3")||!fixture.LastBody.Contains("\"defrost\":true"))
                throw new Exception("Warm up did not start climate with its preset");
            await Capture("43-climate-presets-test.png");
            await WaitForCommandChecks();
            liveTemperature=70;Render();canvas.GetNode<Button>("SaveWarm").EmitSignal(Godot.Button.SignalName.Pressed);
            if(warmPreset.TemperatureF!=70||!canvas.GetNode<Button>("PresetWarm").Text.Contains("70°F"))throw new Exception("Save as Warm up did not store the current settings");
            // Charge presets: Road trip sends 100% for both targets in one request.
            Navigate("Energy");
            canvas.GetNode<Button>("PresetRoadTrip").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!fixture.LastPath.EndsWith("/targetsoc/set")||fixture.LastBody.Split("\"targetSOClevel\":100").Length!=3)
                throw new Exception("Road trip did not send 100% AC and DC targets");
            if(canvas.GetNode<Button>("PresetDaily").Text.Contains("…")||!canvas.GetNode<Button>("PresetRoadTrip").Text.Contains("…"))
                throw new Exception("Command feedback is not limited to the preset that was tapped");
            await Capture("44-charge-presets-test.png");
            await WaitForCommandChecks();
            // Car health: the strip, Check your car with Lock, and a wake confirmation that sends nothing when cancelled.
            fixture.Unhealthy=true;
            await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||connection?.State?.Locked!=false);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Navigate("Car");
            var strip=canvas.GetNodeOrNull<Button>("AlertStrip");
            if(strip==null||!strip.Text.Contains("Doors unlocked")||!strip.Text.Contains("+6 more"))throw new Exception("Car screen does not lead with the unlocked alert");
            var panels=canvas.GetNode<StatusOverlay>("StatusOverlay");
            if(!panels.FrontDoorOpen||!panels.RearWindowOpen||!panels.HoodOpen||panels.RearDoorOpen||panels.FrontWindowOpen)
                throw new Exception("Car art does not outline exactly the reported open driver-side panels");
            await Capture("36-alert-strip-test.png");
            // Every visible panel outlined at once, for reviewing the shapes.
            panels.RearDoorOpen=panels.FrontWindowOpen=true;panels.QueueRedraw();
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await Capture("39-open-panels-test.png");
            strip.EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Alerts"||canvas.GetNodeOrNull<Button>("AlertLock")==null)throw new Exception("Check your car does not list alerts with Lock");
            await Capture("37-check-your-car-test.png");
            Navigate("Settings");int beforeWake=fixture.Requests;
            canvas.GetNode<Button>("WakeRefresh").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingConfirm==null||canvas.GetNodeOrNull<Button>("ConfirmGo")==null||fixture.Requests!=beforeWake)
                throw new Exception("Waking with a weak 12-volt battery did not ask first");
            await Capture("38-wake-confirm-test.png");
            canvas.GetNode<Button>("ConfirmCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingConfirm!=null||fixture.Requests!=beforeWake)throw new Exception("Cancelling the wake confirmation sent a request");
            // Ignore this location: the unlocked alert hides at the named place and returns when it is deleted.
            Navigate("Alerts");
            var ignore=canvas.GetNode<Button>("AlertIgnore");
            if(ignore.Disabled)throw new Exception("Ignore here is disabled despite a fresh car location");
            ignore.EmitSignal(Godot.Button.SignalName.Pressed);
            var placeName=canvas.GetNodeOrNull<LineEdit>("PlaceName")??throw new Exception("Ignore here did not ask for a name");
            if(placeName.Text!="Home")throw new Exception("The first place is not suggested as Home");
            await Capture("40-ignore-place-test.png");
            canvas.GetNode<Button>("PlaceSave").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(ignoredPlaces.Count!=1||ignoredPlaces[0].Name!="Home"||!AtIgnoredPlace||canvas.GetNodeOrNull<Label>("IgnoredNote")==null)
                throw new Exception("Saving the place did not hide the unlocked alert");
            if(snapshot?.AtIgnoredPlace!=true)throw new Exception("Saved status does not remember being at an ignored place");
            // With the unlocked alert ignored, the Car screen still says the car is unlocked.
            Navigate("Car");
            if(canvas.GetNodeOrNull<Button>("AlertStrip") is {} remaining&&remaining.Text.Contains("Doors unlocked"))throw new Exception("Unlocked doors still flagged at an ignored place");
            Navigate("Alerts");
            await Capture("41-ignored-place-applied-test.png");
            Navigate("Car");
            if(canvas.GetNode<Button>("AlertStrip").Text.Contains("Doors unlocked"))throw new Exception("Car screen still flags unlocked doors at an ignored place");
            Navigate("Settings");canvas.GetNode<Button>("ViewPlaces").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Places"||canvas.GetNodeOrNull<Button>("PlaceDelete0")==null)throw new Exception("Ignored places screen does not list the place");
            await Capture("42-ignored-places-test.png");
            canvas.GetNode<Button>("PlaceDelete0").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Navigate("Car");
            if(ignoredPlaces.Count!=0||!canvas.GetNode<Button>("AlertStrip").Text.Contains("Doors unlocked"))throw new Exception("Deleting the place did not bring the alert back");
            // Charging ETA from Hyundai's minutes-left estimate, on Energy and the Car screen.
            if(canvas.GetNodeOrNull<Label>("ChargeLine")?.Text!="NOT CHARGING"||canvas.GetNodeOrNull<TextureRect>("ChargeOutline")!=null||canvas.GetNode<CarFrame>("CarFrame").Charging)
                throw new Exception("A car that isn't charging is not shown as Not charging");
            fixture.Charging=true;
            await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||connection?.State?.Charging!=true);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Navigate("Energy");
            var eta=canvas.GetNodeOrNull<Label>("ChargeEta")?.Text??"";
            if(!eta.Contains("Charging at 7.2 kW")||!eta.Contains("full at"))throw new Exception("Energy screen does not show charging power and finish time");
            await Capture("45-charging-eta-test.png");
            Navigate("Car");
            var line=canvas.GetNodeOrNull<Label>("ChargeLine");
            if(line?.Text.StartsWith("CHARGING · FINISHING AROUND ")!=true||line.GetThemeColor("font_color")!=Yellow||canvas.GetNodeOrNull<Label>("CarChargeEta")!=null)
                throw new Exception("The Car screen does not say Charging, in yellow, with the finish time");
            if(canvas.GetNodeOrNull<TextureRect>("ChargeOutline")==null||!canvas.GetNode<CarFrame>("CarFrame").Charging)throw new Exception("Charging does not outline the car and its frame");
            for(int i=0;i<40;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await Capture("56-charging-car-test.png");
            // Moving: a white line along the bottom of the frame.
            fixture.Moving=true;await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||connection?.State?.Moving!=true);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Navigate("Car");
            if(!canvas.GetNode<CarFrame>("CarFrame").Moving)throw new Exception("A moving car does not show the moving line");
            // The dash enters from the right edge about half a second in.
            await ToSignal(GetTree().CreateTimer(1.2),SceneTreeTimer.SignalName.Timeout);
            await Capture("57-moving-test.png");
            // Past Hyundai's finish time: one automatic status read for that estimate, and no more.
            fixture.Moving=false;fixture.ChargingOverdue=true;await RefreshLive(false);
            if(pendingOverride!=null)canvas.GetNode<Button>("OverrideConfirm").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<300&&(liveBusy||!ChargeEstimate.Overdue(connection?.State,DateTimeOffset.UtcNow));i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            Navigate("Car");
            if(canvas.GetNodeOrNull<Label>("ChargeLine")?.Text!="CHARGING")throw new Exception("An out-of-date charging report still shows a finish time");
            // Wait out the read cooldown so the automatic read is allowed.
            await ToSignal(GetTree().CreateTimer(16),SceneTreeTimer.SignalName.Timeout);
            int beforeCheck=fixture.Requests;
            CheckChargeOverdue();
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            CheckChargeOverdue();
            for(int i=0;i<300&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(fixture.Requests!=beforeCheck+1||pendingOverride!=null)throw new Exception("An overdue charge did not read status exactly once, without prompting");
            fixture.ChargingOverdue=false;
            // Surround View: a warning first, then the car's picture, saved through a stand-in for Photos.
            var picture=Image.CreateEmpty(32,16,false,Image.Format.Rgb8);picture.Fill(new Color("#3aa0ff"));
            fixture.SvmImage=Convert.ToBase64String(picture.SavePngToBuffer());
            byte[]? savedPicture=null;savePhoto=bytes=>{savedPicture=bytes;return Task.FromResult("Saved to Photos.");};
            Navigate("Locate");canvas.GetNode<Button>("LookAround").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Look")throw new Exception("Vehicle location does not open Look around");
            canvas.GetNode<Button>("LookCapture").EmitSignal(Godot.Button.SignalName.Pressed);
            if(pendingConfirm==null||!pendingConfirm.Message.Contains("wakes the car")||!pendingConfirm.Message.Contains("12-volt battery is at 41%")||fixture.SvmRequests!=0)
                throw new Exception("Surround View did not warn before capturing");
            await Capture("46-look-warning-test.png");
            canvas.GetNode<Button>("ConfirmGo").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<40&&!lookCapturing;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(canvas.GetNodeOrNull<Label>("LookProgress")==null)throw new Exception("Surround View does not show its progress");
            await Capture("47-look-progress-test.png");
            for(int i=0;i<200&&(liveBusy||lookTexture==null);i++)await ToSignal(GetTree().CreateTimer(0.25),SceneTreeTimer.SignalName.Timeout);
            if(lookTexture==null||canvas.GetNodeOrNull<TextureRect>("LookPicture")==null||fixture.SvmRequests!=1)throw new Exception("Surround View picture did not appear after one request");
            await Capture("48-look-picture-test.png");
            canvas.GetNode<Button>("LookSave").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<100&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(savedPicture==null||Convert.ToBase64String(savedPicture)!=fixture.SvmImage||notice!="Saved to Photos.")throw new Exception("Save to Photos did not save the captured picture");
            // A second capture within 5 minutes warns before anything is sent.
            canvas.GetNode<Button>("LookCapture").EmitSignal(Godot.Button.SignalName.Pressed);
            canvas.GetNode<Button>("ConfirmGo").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<100&&liveBusy;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(pendingOverride?.Kind!=RequestKind.Camera||fixture.SvmRequests!=1)throw new Exception("A second capture within 5 minutes was not refused first");
            canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            if(Array.Exists(OS.GetCmdlineUserArgs(),x=>x=="--art-gallery"))await ArtGallery();
            // Range & units: metric everywhere, the loss setting, and turning the estimate off and on.
            int weatherBefore=weatherFixture.Requests;
            Navigate("Settings");canvas.GetNode<Button>("SettingsRange").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="Range"||canvas.GetNode<Label>("ColdStartValue").Text!="70°F"||canvas.GetNode<Label>("ColdLowValue").Text!="0°F"||canvas.GetNode<Label>("ColdLossValue").Text!="50%")
                throw new Exception("Range & units does not show the default settings");
            await Capture("60-range-units-test.png");
            canvas.GetNode<Button>("UnitsMetric").EmitSignal(Godot.Button.SignalName.Pressed);
            canvas.GetNode<Button>("ColdLossUp").EmitSignal(Godot.Button.SignalName.Pressed);
            canvas.GetNode<Button>("ColdStartDown").EmitSignal(Godot.Button.SignalName.Pressed);
            if(canvas.GetNode<Label>("ColdLossValue").Text!="55%"||canvas.GetNode<Label>("ColdStartValue").Text!="19°C"||canvas.GetNode<Label>("ColdLowValue").Text!="-18°C")
                throw new Exception("Range & units steppers did not change the settings in metric");
            await Capture("61-range-units-metric-test.png");
            Navigate("Car");
            // 184 mi at 28°F, losing 55% at 0°F from 66.2°F: about 126 mi.
            if(canvas.GetNode<Label>("Range").Text!="296 km"||canvas.GetNodeOrNull<Label>("ColdRange")?.Text!="~202 km at -2°C")
                throw new Exception("The Car screen did not switch to metric");
            await Capture("62-car-metric-test.png");
            Navigate("Climate");
            if(canvas.GetNode<Label>("ClimateTemperature").Text!=Units.Setpoint(liveTemperature,UnitSystem.Metric)||!canvas.GetNode<Button>("PresetWarm").Text.EndsWith("°C"))
                throw new Exception("Climate did not switch to °C");
            await Capture("63-climate-metric-test.png");
            Navigate("Details");
            if(canvas.GetNode<Label>("Tire0").Text!="248 kPa")throw new Exception("Tires did not switch to kPa");
            await Capture("64-details-metric-test.png");
            Navigate("Range");
            var coldOn=canvas.GetNode<CheckBox>("ColdRangeOn");coldOn.ButtonPressed=false;await WaitFrames(3);
            Navigate("Car");
            if(canvas.GetNodeOrNull<Label>("ColdRange")!=null||outside!=null||coldRange.Enabled)throw new Exception("Turning the estimate off did not hide it");
            await RefreshLive(false);if(pendingOverride!=null)canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
            await WaitFrames();
            if(weatherFixture.Requests!=weatherBefore)throw new Exception("A weather request was sent while the estimate was off");
            Navigate("Range");canvas.GetNode<CheckBox>("ColdRangeOn").ButtonPressed=true;await WaitFrames();
            if(weatherFixture.Requests!=weatherBefore+1||outside?.TemperatureF!=28)throw new Exception("Turning the estimate on did not read the temperature once");
            units=UnitSystem.Imperial;coldRange=ColdRangeSettings.Default;
            // App color: every screen follows one picked color, with nothing left in the default blue.
            Navigate("Appearance");canvas.GetNode<Button>("SettingsAppColor").EmitSignal(Godot.Button.SignalName.Pressed);
            if(screen!="AppColor"||canvas.GetNode<Label>("AppColorName").Text!="PERFORMANCE BLUE")throw new Exception("App color does not open on the default");
            await Capture("65-app-color-test.png");
            canvas.GetNode<Button>("AppColor3").EmitSignal(Godot.Button.SignalName.Pressed);
            if(Skin.Hex!="bb8efa"||canvas.GetNode<Label>("AppColorName").Text!="PURPLE"||Skin.Accent==new Color("#8ecbfa"))throw new Exception("Picking Purple did not change the app color");
            await Capture("66-app-color-purple-test.png");
            foreach(var (hex,name) in new[]{("bb8efa","purple"),("ff8800","custom"),("d0d0d0","graphite")}) {
                PickAppColor(hex);
                foreach(string route in new[]{"Car","Climate","Energy","Details","Activity","Settings","Appearance","Range","Limits","Places","Locate","Send","Alerts","Paint","Model","AppColor","Disclaimer"})
                {Navigate(route);AssertThemed(route+" in "+name);}
                Navigate("Car");await Capture($"67-car-{name}-test.png");
            }
            PickAppColor("bb8efa");
            foreach(string route in new[]{"Climate","Settings","Details","Model","Paint"}){Navigate(route);await Capture($"68-{route.ToLowerInvariant()}-purple-test.png");}
            Navigate("AppColor");canvas.GetNode<Button>("AppColorReset").EmitSignal(Godot.Button.SignalName.Pressed);
            if(Skin.Hex!=AppTheme.DefaultHex||Skin.Accent!=new Color("#8ecbfa"))throw new Exception("Reset to default did not bring back Performance Blue");
            PickAppColor("bb8efa");
            // The demo: a pretend car inside the app. Nothing reaches the (test) Hyundai server, and ending it puts everything back.
            var budgetBefore=budget;var linkedBefore=linkedVehicle;var paintBefore=paintHex;int sentBefore=fixture.Requests;
            string widgetBefore=Encoding.UTF8.GetString(sharedFixture["status.json"]);
            EndLiveSession();
            canvas.GetNode<Button>("DemoStart").EmitSignal(Godot.Button.SignalName.Pressed);
            for(int i=0;i<600&&(liveBusy||connection?.State==null);i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!demo||connection?.Selected?.Vin!=DemoHyundai.Vin||screen!="Car"||canvas.GetNodeOrNull<Button>("ConnectionBadge")?.Text!="DEMO"||
                fixture.Requests!=sentBefore||ReferenceEquals(budget,budgetBefore))throw new Exception("Try the demo did not open the pretend car on its own");
            canvas.GetNode<Button>("DoorControl").EmitSignal(Godot.Button.SignalName.Pressed);
            await WaitForCommandChecks();
            if(connection?.State?.Locked!=false||connection.Pending?.Outcome!=RemoteOutcome.Confirmed)throw new Exception("The demo car did not confirm and report the unlock");
            Navigate("Car");
            if(canvas.GetNodeOrNull<Label>("ColdRange")?.Text!="~"+Units.Distance(connection.State.RangeMiles!.Value*ColdRange.Factor(DemoHyundai.OutsideF,coldRange),units)+" at 28°F"||
                weatherFixture.Requests!=weatherBefore+1)throw new Exception("The demo did not show its pretend temperature without a weather request");
            await Capture("53-demo-car-test.png");
            PickPaint("Gravity Gold","b6ab8a");PickAppColor("8efaec");Navigate("Settings");
            if(canvas.GetNodeOrNull<Button>("SignOut")!=null||canvas.GetNodeOrNull<Button>("DemoEnd")==null)throw new Exception("The demo offers Sign out & forget instead of End demo");
            await Capture("54-demo-settings-test.png");
            canvas.GetNode<Button>("DemoEnd").EmitSignal(Godot.Button.SignalName.Pressed);
            if(demo||connection!=null||!ReferenceEquals(budget,budgetBefore)||linkedVehicle!=linkedBefore||paintHex!=paintBefore||fixture.Requests!=sentBefore)
                throw new Exception("Ending the demo did not put everything back");
            if(Skin.Hex!="bb8efa")throw new Exception("Ending the demo did not put back the app color");
            if(Encoding.UTF8.GetString(sharedFixture["status.json"])!=widgetBefore)throw new Exception("The demo changed your widget");
            Skin.Use(AppTheme.DefaultHex);
            GD.Print("LIVE SMOKE PASS: offline sign-in/recovery, selection, screens, swipe down to update, direct door control, tracking, remembered vehicle, climate seats and steering wheel, car health alerts, ignored places, climate and charge presets, charging ETA, charging line and outline, moving line, overdue charge check, Surround View, old location, range and units, cold-weather range, app color, car marker and phone position, send to car with the climate offer (start, disable, Settings), Climate page following the car, widget files, shared places and short links, Share-sheet sends and refusals, Activity shared tab, demo. No real API calls.");
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
    private async Task WaitForCommandChecks()
    {
        for(int i=0;i<200&&(liveBusy||monitoringCommand);i++)await ToSignal(GetTree().CreateTimer(0.25),SceneTreeTimer.SignalName.Timeout);
        if(monitoringCommand)throw new Exception("Command checks did not finish");
    }
    // Swipe down to update, with touches the way the phone sends them (each also arrives as the mouse events made from it).
    private async Task Swipe(Vector2 from,Vector2 to,int steps=10)
    {
        // Touches arrive in window points; the app's 460×920 layout is stretched to the window.
        var window=GetTree().Root.GetFinalTransform();from=window*from;to=window*to;
        Input.ParseInputEvent(new InputEventScreenTouch {Index=0,Pressed=true,Position=from});
        await WaitFrames(2);
        for(int i=1;i<=steps;i++) {
            Input.ParseInputEvent(new InputEventScreenDrag {Index=0,Position=from.Lerp(to,(float)i/steps),Relative=(to-from)/steps});
            await WaitFrames(1);
        }
        Input.ParseInputEvent(new InputEventScreenTouch {Index=0,Pressed=false,Position=to});
        await WaitFrames(2);
        await ToSignal(GetTree().CreateTimer(1.2),SceneTreeTimer.SignalName.Timeout);
    }
    private async Task PullChecks(ScreenFixture fixture)
    {
        // The read cooldown is still on from the checks above, so an update shows its warning instead of sending:
        // that warning appearing is the Update button's request being made, and nothing reaches Hyundai.
        int requests=fixture.Requests,commands=fixture.Commands;
        void Settled(string what){if(pullState!=PullState.Idle||pullOffset!=0||fixture.Requests!=requests||fixture.Commands!=commands)throw new Exception(what);}
        await Swipe(new(230,400),new(230,450));
        if(pendingOverride!=null)throw new Exception("A short pull updated");
        Settled("A short pull didn't spring back cleanly");
        await Swipe(new(100,400),new(320,430));
        if(pendingOverride!=null)throw new Exception("A sideways drag updated");
        Settled("A sideways drag moved the screen");
        await Swipe(new(230,400),new(230,560));
        if(pendingOverride==null)throw new Exception("A full pull didn't do what Update does");
        Settled("A full pull didn't settle and slide away");
        // With a dialog open, pulling does nothing.
        await Swipe(new(230,400),new(230,560));
        if(pendingOverride==null)throw new Exception("Pulling under a dialog changed it");
        Settled("Pulling under a dialog moved the screen");
        canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
        // A pull that starts on the door button updates without pressing it.
        var door=canvas.GetNode<Button>("DoorControl");var center=door.Position+door.Size/2;
        await Swipe(center,center+new Vector2(0,160));
        if(pendingOverride==null||pendingConfirm!=null)throw new Exception("A pull that started on the door button pressed it");
        Settled("A pull from the door button sent something");
        canvas.GetNode<Button>("OverrideCancel").EmitSignal(Godot.Button.SignalName.Pressed);
        // Buttons still work normally afterwards.
        await Tap(new(283,587));
        if(screen!="Climate")throw new Exception("A tap after a pull didn't reach its button");
        Navigate("Car");
        // Each step, for checking by eye.
        foreach(var (state,offset,file) in new[]{(PullState.Pulling,30f,"80-pull-start-test.png"),(PullState.Pulling,64f,"81-pull-ready-test.png"),
            (PullState.Updating,PullToUpdate.Ready,"82-pull-updating-test.png")}) {
            pullState=state;SetPull(offset);await WaitFrames(8);await Capture(file);
        }
        pullState=PullState.Idle;SetPull(0);
    }
    private async Task Tap(Vector2 at)
    {
        var down=new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=at,GlobalPosition=at,ButtonMask=MouseButtonMask.Left};
        GetViewport().PushInput(down,true);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        var up=new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=false,Position=at,GlobalPosition=at};
        GetViewport().PushInput(up,true);
        for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    }
    private void AssertAccountOnly()
    {
        if(screen!="Connect"||liveMode||canvas.GetNodeOrNull<Button>("NavCar")!=null||connection?.Selected!=null)
            throw new Exception("Vehicle UI is accessible without a selected live vehicle");
    }
    // Every picture with every overlay drawn, for checking the positions by eye.
    private async Task ArtGallery()
    {
        var (keep,name)=cars[CarKey];
        foreach(var model in CarModels.All) {
            cars[CarKey]=(model.Id,"");SelectCar();Navigate("Car");
            var art=canvas.GetNode<StatusOverlay>("StatusOverlay");
            art.Windshield=ComfortTint.Heat;art.DriverSeat=ComfortTint.Heat;art.PassengerSeat=ComfortTint.Cool;art.WheelHeat=true;
            art.FrontDoorOpen=art.RearDoorOpen=art.FrontWindowOpen=art.RearWindowOpen=art.HoodOpen=true;
            art.Horn=true;art.FlashUntilMs=Time.GetTicksMsec()+60000;art.QueueRedraw();
            // Capture while the headlights are on in their blink.
            do await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); while(Time.GetTicksMsec()%600>200);
            await Capture($"art-{model.Id}.png");
        }
        cars[CarKey]=(keep,name);SelectCar();Render();
    }
    // Saves a file next to the screenshots (windowed runs only), e.g. what the widget would show.
    private void SaveSmokeFile(string filename,byte[] bytes)
    {
        if(DisplayServer.GetName()=="headless")return;
        string directory="user://";
        foreach(string arg in OS.GetCmdlineUserArgs())
            if(arg.StartsWith("--smoke-output="))directory=arg.Substring("--smoke-output=".Length).TrimEnd('/','\\')+"/";
        using var file=Godot.FileAccess.Open(directory+filename,Godot.FileAccess.ModeFlags.Write);
        file?.StoreBuffer(bytes);
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
