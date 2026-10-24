using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public partial class Main
{
    private HyundaiUsClient? connection;
    private CancellationTokenSource liveLifetime=new();
    private IReadOnlyList<EnrolledVehicle>? enrollment;
    private bool liveMode,liveBusy;
    private int selectedIndex,liveAc=80,liveDc=80,liveTemperature=72;
    // Last seat/steering-wheel choices, saved in preferences and sent with the next climate start.
    private SeatMode driverSeat,passengerSeat;
    private bool wheelHeat,climateDefrost;
    // One-tap climate starts, editable with Save as; kept in preferences.
    private ClimatePreset warmPreset=ClimatePreset.WarmUp,coolPreset=ClimatePreset.CoolDown;
    // Set in _Ready so every client shares the persisted request budget.
    private Func<HyundaiUsClient> connectionFactory = null!;
    private bool monitoringCommand;
    // Flash lights / horn & lights effect on the car art, shown when Hyundai accepts the request.
    private ulong flashEffectUntil;
    private bool hornEffect;
    private const ulong FlashEffectMs=4000;
    private ActionKind? feedbackKind;
    private bool feedbackEnabled;
    private int feedbackValue;
    private Task commandMonitor=Task.CompletedTask;
    private static string Value(int? value,string suffix="")=>value is null?"—":value+suffix;
    private static string OnOff(bool? value,string yes,string no)=>value is null?"Unknown":value.Value?yes:no;
    private Button LiveButton(float x,float y,float w,float h,string text,int size,Action action,bool primary=false,string name="")
    {var b=Button(x,y,w,h,text,size,action,primary,name);b.Disabled=liveBusy;return b;}

    private void ConnectionScreen()
    {
        if(enrollment==null && vault.HasAccount && !manualAccountEntry){SavedLoginScreen();return;}
        Title("CONNECT YOUR HYUNDAI.","US Bluelink accounts · direct connection from this app");
        if(enrollment!=null && connection?.IsAuthenticated==true)
        {
            Label(24,214,412,40,"Choose the vehicle to connect.",16,White,true);
            var vehicles=new OptionButton {Name="VehicleChoice",Position=new(24,277),Size=new(412,48)};
            vehicles.AddThemeFontOverride("font",regular);vehicles.AddThemeFontSizeOverride("font_size",13);
            foreach(var v in enrollment)vehicles.AddItem($"{(v.Name.Length>0?v.Name:v.Model)} · VIN …{v.Vin[^6..]}");
            vehicles.Select(selectedIndex);vehicles.ItemSelected+=i=>selectedIndex=(int)i;
            vehicles.Disabled=liveBusy;canvas.AddChild(vehicles);
            Label(24,359,412,100,"Choose your vehicle once. After you unlock or sign in again, nCTRL opens the same vehicle automatically.",14,Muted,false,true);
            LiveButton(24,510,412,52,liveBusy?"Reading vehicle…":"Connect selected vehicle",14,()=>_ = ChooseVehicle(),true,"ChooseVehicle");
            LiveButton(24,581,412,44,"Cancel",13,()=>EndLiveSession());
            return;
        }
        Label(24,204,412,23,"BLUELINK EMAIL",11,Muted,true);
        var email=AccountInput(24,232,412,"Email",false);
        email.VirtualKeyboardType=LineEdit.VirtualKeyboardTypeEnum.EmailAddress;
        Label(24,291,412,23,"PASSWORD",11,Muted,true);
        var pass=AccountInput(24,319,412,"Password",true);
        Label(24,378,412,23,"FOUR-DIGIT BLUELINK SERVICE PIN",11,Muted,true);
        var servicePin=AccountInput(24,406,412,"Service PIN",true);servicePin.MaxLength=4;
        servicePin.VirtualKeyboardType=LineEdit.VirtualKeyboardTypeEnum.Number;
        var remember=new CheckBox {Position=new(24,471),Size=new(412,40),Text="Save account with "+vault.BiometricName,
            ButtonPressed=rememberDevice && vault.Available,Disabled=liveBusy||!vault.Available};
        remember.AddThemeFontOverride("font",regular);remember.AddThemeFontSizeOverride("font_size",13);
        remember.Toggled+=value=>rememberDevice=value;canvas.AddChild(remember);
        Label(24,519,412,70,vault.Available?"Saved credentials stay on this phone. The app opens to your car's last status; "+vault.BiometricName+" is required for the first update or command each time you return.":
            vault.UnavailableReason+" Nothing will be saved.",12,Muted,false,true);
        LiveButton(24,610,412,53,liveBusy?"Signing in…":"Sign in to Hyundai",15,()=>{
            string e=email.Text,p=pass.Text,s=servicePin.Text;
            pass.Text="";servicePin.Text="";
            _ = SignIn(e,p,s,rememberDevice && vault.Available);
        },true,"SignIn");
        Label(24,685,412,76,"Unofficial integration. MFA or account challenges may block sign-in. The app never bypasses those checks.",12,Muted,false,true);
    }
    private LineEdit AccountInput(int x,int y,int width,string placeholder,bool secret)
    {
        var input=new LineEdit {Position=new(x,y),Size=new(width,44),PlaceholderText=placeholder,Secret=secret,Editable=!liveBusy};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);canvas.AddChild(input);return input;
    }
    private async Task SignIn(string email,string password,string pin,bool remember=false)
    {
        if(liveBusy)return;
        if(HoldForDisclaimer(email,password,pin,remember,false)){Render();return;}
        int version=liveEpoch;
        await RunLive(async()=>{
            DiscardParked();
            connection?.Dispose();connection=null;enrollment=null;
            var active=connectionFactory();connection=active;
            var vehicles=await active.LoginAsync(email,password,pin,liveLifetime.Token);EnsureLive(version);
            accountKey=VehicleLink.AccountKey(email);BindDisclaimer();
            // Typing the password verifies the owner for this foreground session.
            sessionVerified=true;
            enrollment=vehicles;selectedIndex=0;screen="Connect";notice="Signed in. Select your vehicle.";
            try {
                if(remember) {
                    var saved=new SavedAccount(email,password,pin);
                    await vault.SaveAsync(saved,liveLifetime.Token);EnsureLive(version);
                    sessionAccount=saved;
                    notice="Account saved securely. Select your vehicle.";
                } else if(vault.HasAccount) {
                    await vault.ForgetAsync(liveLifetime.Token);EnsureLive(version);
                    notice="Signed in for this session. Saved login removed.";
                }
            } catch(VaultException ex) {
                EnsureLive(version);notice="Signed in for this session. "+ex.Message;noticeError=true;
            }
            await RestoreLinkedVehicle(active,version);
        },"Signing in to Hyundai…",()=>SignIn(email,password,pin,remember));
    }
    private async Task ChooseVehicle()
    {
        if(enrollment==null||connection==null)return;
        int version=liveEpoch;var active=connection;var selected=enrollment[selectedIndex];
        await RunLive(()=>ConnectVehicle(active,selected,version),"Reading Hyundai vehicle status…",ChooseVehicle);
    }
    private async Task RestoreLinkedVehicle(HyundaiUsClient active,int version)
    {
        if(enrollment==null)return;
        int index=VehicleLink.Find(linkedVehicle,accountKey,enrollment);
        if(index<0)return;
        selectedIndex=index;var selected=enrollment[index];
        // Signed in already: an override reconnects this vehicle rather than repeating the sign-in.
        try {await ConnectVehicle(active,selected,version);}
        catch(RequestLimitException ex) when(ex.Overridable) {
            OfferOverride(ex,()=>{int v=liveEpoch;return RunLive(()=>ConnectVehicle(active,selected,v),"Reading Hyundai vehicle status…");});
        }
    }
    private async Task ConnectVehicle(HyundaiUsClient active,EnrolledVehicle selected,int version)
    {
        await active.SelectAsync(selected,liveLifetime.Token);EnsureLive(version);
        // From saved status the user stays on the screen they acted from.
        if(!liveMode){screen="Car";history.Clear();}
        liveMode=true;
        if(active.State?.AcLimit is int ac)liveAc=ac;
        if(active.State?.DcLimit is int dc)liveDc=dc;
        if(active.State?.TemperatureF is int t&&t>=62&&t<=82)liveTemperature=t;
        history.Add(("Connected to Hyundai","Read-only status loaded. No remote command sent.",true));
        if(!noticeError)notice="Connected. Showing Hyundai’s last reported state.";
        linkedVehicle=VehicleLink.Fingerprint(accountKey,selected.Vin);
        // First time with this car: pick the closest picture from Hyundai's model code.
        if(!cars.ContainsKey(linkedVehicle))cars[linkedVehicle]=((CarModels.Detect(selected.Model)??CarModels.Default).Id,"");
        SelectCar();SavePrefs();
    }
    private void Disconnect()
    {
        if(liveBusy)return;
        EndLiveSession();notice="Session ended. Saved login, if any, remains protected.";Render();
    }
    // retry re-runs a user-initiated request after an override; automatic work passes none.
    private async Task RunLive(Func<Task> work,string progress,Func<Task>? retry=null)
    {
        if(liveBusy)return;
        int version=liveEpoch;
        liveBusy=true;notice=progress;noticeError=false;Render();
        try {await work();EnsureLive(version);await RememberLocation(version);}
        catch(Exception) when(version!=liveEpoch) { }
        catch(VaultException ex){notice=ex.Message;noticeError=true;}
        catch(RequestLimitException ex){OfferOverride(ex,retry);}
        catch(BluelinkException ex){notice=ex.Message;noticeError=true;}
        catch(OperationCanceledException){notice="Request interrupted. For a command, the outcome may be unknown. Refresh before retrying.";noticeError=true;}
        catch(HttpRequestException){notice="Could not reach Hyundai. Check your connection. No automatic retry was sent.";noticeError=true;}
        catch(Exception ex){notice=progress.TrimEnd('…','.')+": "+SafeFailure.Describe(ex);noticeError=true;}
        finally {
            if(version==liveEpoch) {
            if(liveMode && noticeError)history.Add((notice,DateTime.Now.ToString("h:mm tt")+" · Request not confirmed",false));
            if(history.Count>30)history.RemoveRange(0,history.Count-30);
            PersistSnapshot();
            liveBusy=false;if(IsInsideTree())Render();
            }
        }
    }
    private void LiveScreen()
    {
        if(screen=="Car")LiveHome();
        else if(screen=="Climate")LiveClimate();
        else if(screen=="Energy")LiveEnergy();
        else if(screen=="Activity")LiveActivity();
        else if(screen=="Locate")LiveLocation();
        else if(screen=="Details")LiveDetails();
        else if(screen=="Alerts")LiveAlerts();
        else if(screen=="Look")LookAround();
        else LiveSettings();
    }
    private void LiveHome()
    {
        var s=CurrentState;
        Label(24,106,412,24,"YOUR HYUNDAI · LAST REPORTED",10,Muted,true);
        Hero(139,215);
        // Car screen only: door, climate, seat and steering-wheel state over the artwork.
        var overlay=new StatusOverlay {Name="StatusOverlay",Position=new(24,143),Size=new(412,207),TextureSize=CarTexture.GetSize(),Art=CarArt.For(carModel.Id),
            Lock=VehicleIndicators.Lock(s?.Locked),Windshield=VehicleIndicators.Windshield(s?.ClimateOn,s?.TemperatureF),
            DriverSeat=VehicleIndicators.Seat(s?.DriverSeat),PassengerSeat=VehicleIndicators.Seat(s?.PassengerSeat),WheelHeat=s?.SteeringWheelHeat==true,
            FlashUntilMs=flashEffectUntil,Horn=hornEffect,ExclaimFont=bold,
            // The art shows the car's left (driver's) side.
            FrontDoorOpen=s?.Health?.FrontLeftDoor==true,RearDoorOpen=s?.Health?.RearLeftDoor==true,
            FrontWindowOpen=s?.Health?.FrontLeftWindow==true,RearWindowOpen=s?.Health?.RearLeftWindow==true,HoodOpen=s?.Health?.Hood==true};
        canvas.AddChild(overlay);
        Label(24,369,190,20,"CHARGE",11,Muted,true);Label(294,369,130,20,"EST. RANGE",11,Muted,true);
        Label(21,391,180,60,Value(s?.Battery,"%"),46,Blue,true);
        Label(288,403,150,42,s?.RangeMiles is double range?$"{range:0} mi":"—",28,White,true);
        if(ChargeEta(s) is string eta)Label(294,440,142,16,char.ToUpperInvariant(eta[0])+eta[1..],10,Blue,true).Name="CarChargeEta";
        for(int i=0;i<20;i++)Rect(24+i*20.5f,459,17,9,s?.Battery is int soc && i<soc/5.0?Blue:new("#243446"));
        HealthStrip(s);
        Label(24,509,412,36,"Reported: "+ReportedTime(s?.ReportedAt),11,Muted,false,true);
        var doorAction=VehicleLink.DoorAction(s?.Locked);
        var door=LiveButton(24,563,200,49,doorAction==ActionKind.Unlock?"Unlock":doorAction==ActionKind.Lock?"Lock":"Door status unknown",13,
            ()=>{if(doorAction is ActionKind action)_ = SendLive(new(action));},false,"DoorControl");
        door.Disabled=liveBusy||monitoringCommand||doorAction==null;
        // Tapping the padlock on the car toggles the lock, exactly like the door button.
        var area=overlay.LockArea();
        var badge=new Button {Name="LockBadge",Position=overlay.Position+area.Position,Size=area.Size,Flat=true,
            Disabled=door.Disabled,TooltipText=door.Text,MouseDefaultCursorShape=CursorShape.PointingHand};
        foreach(string state in new[]{"normal","hover","pressed","disabled","focus"})badge.AddThemeStyleboxOverride(state,new StyleBoxEmpty());
        badge.Pressed+=()=>{if(doorAction is ActionKind action)_ = SendLive(new(action));};
        canvas.AddChild(badge);
        CommandFeedback(door,ActionKind.Lock,s?.Locked==true);
        CommandFeedback(Button(236,563,94,49,"Climate",13,()=>Navigate("Climate")),ActionKind.Climate,s?.ClimateOn==true);
        CommandFeedback(Button(342,563,94,49,"Charge",13,()=>Navigate("Energy")),ActionKind.Charge,false);
        LiveButton(24,636,200,46,"Update",13,()=>_ = RefreshLive(false),false,"Update");
        Label(24,686,200,20,Updated(s?.RetrievedAt),11,Muted).Name="LastUpdated";
        Button(236,636,200,46,"Vehicle location  ›",12,()=>_ = OpenVehicleLocation());
        CommandFeedback(RemoteButton(24,714,200,46,"Flash lights",13,()=>_ = SendLive(new(ActionKind.Lights)),false,"FlashLights"),ActionKind.Lights,false);
        CommandFeedback(RemoteButton(236,714,200,46,"Horn & lights",13,()=>_ = SendLive(new(ActionKind.HornLights)),false,"HornLights"),ActionKind.HornLights,false);

        if(notice.Length==0)Label(24,778,412,65,connection?.Pending?.Message??(Offline?"Showing the last saved status. Your first update or command asks for "+vault.BiometricName+".":
            "Cached status may lag the vehicle. Missing data is shown as — or Unknown."),11,Muted,false,true);
    }
    private static string ReportedTime(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return "Not supplied by Hyundai";
        // Preserve server text rather than guessing the timezone of compact timestamps.
        return raw.Length>48?"Timestamp format unsupported":raw;
    }
    private bool SeatsSupported=>VehicleGeneration==3;
    private ClimateOptions CurrentClimateOptions()=>
        new(SeatsSupported?driverSeat:SeatMode.Off,SeatsSupported?passengerSeat:SeatMode.Off,wheelHeat,climateDefrost);
    private void LiveClimate()
    {
        var s=CurrentState;
        Title("CABIN, YOUR WAY.","Hyundai remote climate · electric vehicles");
        Label(24,196,412,26,"Reported: climate "+OnOff(s?.ClimateOn,"on","off")+" · setpoint "+Value(s?.TemperatureF,"°F"),14,White,true);
        if(s?.DriverSeat!=null||s?.PassengerSeat!=null||s?.SteeringWheelHeat!=null)
            Label(24,224,412,22,"Seats: driver "+SeatCodes.Reported(s?.DriverSeat)+" · passenger "+SeatCodes.Reported(s?.PassengerSeat)+
                " · wheel "+OnOff(s?.SteeringWheelHeat,"on","off"),11,Muted);
        Panel(24,256,412,104,new("#0e2031"));
        Label(142,274,220,68,$"{liveTemperature}°F",40,White,true);
        LiveButton(43,282,51,51,"−",27,()=>{liveTemperature=Math.Max(62,liveTemperature-1);Render();});
        LiveButton(366,282,51,51,"+",27,()=>{liveTemperature=Math.Min(82,liveTemperature+1);Render();});
        Label(24,374,198,22,"DRIVER SEAT",11,Muted,true);Label(238,374,198,22,"PASSENGER SEAT",11,Muted,true);
        SeatChoice(24,398,driverSeat,"DriverSeat").ItemSelected+=i=>{driverSeat=(SeatMode)(int)i;SavePrefs();};
        SeatChoice(238,398,passengerSeat,"PassengerSeat").ItemSelected+=i=>{passengerSeat=(SeatMode)(int)i;SavePrefs();};
        var wheel=new CheckBox {Name="SteeringWheelHeat",Position=new(24,452),Size=new(210,40),Text="Heated steering wheel",ButtonPressed=wheelHeat,Disabled=liveBusy};
        wheel.AddThemeFontOverride("font",regular);wheel.AddThemeFontSizeOverride("font_size",13);
        wheel.Toggled+=value=>{wheelHeat=value;SavePrefs();};canvas.AddChild(wheel);
        var defrost=new CheckBox {Name="Defrost",Position=new(238,452),Size=new(198,40),Text="Front defrost",ButtonPressed=climateDefrost,Disabled=liveBusy};
        defrost.AddThemeFontOverride("font",regular);defrost.AddThemeFontSizeOverride("font_size",13);
        defrost.Toggled+=value=>{climateDefrost=value;SavePrefs();};canvas.AddChild(defrost);
        // Presets start climate right away with their saved settings.
        foreach(var (preset,x,name) in new[]{(warmPreset,24,"PresetWarm"),(coolPreset,238,"PresetCool")})
            CommandFeedback(RemoteButton(x,498,198,44,$"{preset.Name}  ·  {preset.TemperatureF}°F",13,()=>StartClimatePreset(preset),false,name),ActionKind.Climate,false,true,preset.TemperatureF);
        Label(24,546,412,52,(SeatsSupported?"Seats, steering wheel and defrost are sent with each climate start; rear seats and rear-window heat stay off. ":
            "Seat control needs a generation 3 vehicle; the steering wheel and defrost still apply. ")+
            "Generation 3 requests a five-minute duration.",10,Muted,false,true);
        CommandFeedback(RemoteButton(24,604,412,52,"Start / update climate",14,()=>_ = SendLive(new(ActionKind.Climate,liveTemperature,true,CurrentClimateOptions())),true,"ClimateStart"),
            ActionKind.Climate,s?.ClimateOn==true,true);
        CommandFeedback(RemoteButton(24,672,412,48,"Stop climate",14,()=>_ = SendLive(new(ActionKind.Climate,liveTemperature,false))),ActionKind.Climate,false,false);
        Button(24,728,198,36,"Save as Warm up",11,()=>SaveClimatePreset(true),false,"SaveWarm");
        Button(238,728,198,36,"Save as Cool down",11,()=>SaveClimatePreset(false),false,"SaveCool");
    }
    private void StartClimatePreset(ClimatePreset preset)
    {
        if(liveBusy||monitoringCommand)return;
        liveTemperature=preset.TemperatureF;driverSeat=preset.DriverSeat;passengerSeat=preset.PassengerSeat;
        wheelHeat=preset.SteeringWheel;climateDefrost=preset.Defrost;SavePrefs();
        _ = SendLive(new(ActionKind.Climate,preset.TemperatureF,true,preset.Options(SeatsSupported)));
    }
    private void SaveClimatePreset(bool warm)
    {
        var current=(warm?warmPreset:coolPreset).With(liveTemperature,(int)driverSeat,(int)passengerSeat,wheelHeat,climateDefrost);
        if(warm)warmPreset=current;else coolPreset=current;
        SavePrefs();notice="Saved as "+current.Name+".";noticeError=false;Render();
    }
    private OptionButton SeatChoice(int x,int y,SeatMode value,string name)
    {
        var b=new OptionButton{Name=name,Position=new(x,y),Size=new(198,44),Disabled=liveBusy||!SeatsSupported};
        foreach(var mode in Enum.GetValues<SeatMode>())b.AddItem(SeatCodes.Label(mode));
        b.Select((int)value);b.AddThemeFontOverride("font",regular);b.AddThemeFontSizeOverride("font_size",13);canvas.AddChild(b);return b;
    }
    private void LiveEnergy()
    {
        var s=CurrentState;
        Title("POWER YOUR NEXT DRIVE.","Hyundai charging controls · last reported status");
        Label(24,204,412,42,Value(s?.Battery,"%")+"  ·  "+OnOff(s?.Charging,"Charging","Not charging"),25,Blue,true);
        Label(24,248,412,22,"Cable: "+OnOff(s?.PluggedIn,"Connected","Disconnected"),13,Muted);
        // Hyundai's estimate while charging: power and when the current charge should finish.
        string? eta=ChargeEta(s);
        if(eta!=null||s?.Charging==true&&s.ChargingPowerKw!=null)
            Label(24,272,412,22,(s?.ChargingPowerKw is double kw?$"Charging at {kw:0.#} kW":"Charging")+(eta!=null?" · "+eta:""),13,White,true).Name="ChargeEta";
        Label(24,298,412,22,$"Reported targets  AC {Value(s?.AcLimit,"%")}  /  DC {Value(s?.DcLimit,"%")}",13,Muted);
        // One tap sends both targets.
        foreach(var (preset,x,name) in new[]{(ChargePreset.Daily,24,"PresetDaily"),(ChargePreset.RoadTrip,238,"PresetRoadTrip")})
            CommandFeedback(RemoteButton(x,330,198,44,preset.Name,13,()=>{liveAc=preset.Ac;liveDc=preset.Dc;_ = SendLive(new(ActionKind.ChargeLimit,preset.Ac),preset.Dc);},
                s?.AcLimit==preset.Ac&&s?.DcLimit==preset.Dc,name),ActionKind.ChargeLimit,false,null,preset.Ac);
        Label(24,386,412,20,"OR CHOOSE TARGETS",11,Muted,true);
        var ac=LimitChoice(24,410,198,liveAc);var dc=LimitChoice(238,410,198,liveDc);
        Label(24,456,198,20,"AC TARGET",11,Muted,true);Label(238,456,198,20,"DC TARGET",11,Muted,true);
        ac.ItemSelected+=i=>{liveAc=50+(int)i*10;};
        dc.ItemSelected+=i=>{liveDc=50+(int)i*10;};
        CommandFeedback(RemoteButton(24,484,412,46,"Set AC and DC targets",13,()=>{
            _ = SendLive(new(ActionKind.ChargeLimit,liveAc),liveDc);
        }),ActionKind.ChargeLimit,false);
        CommandFeedback(RemoteButton(24,552,198,52,"Start charging",14,()=>_ = SendLive(new(ActionKind.Charge,Enabled:true)),true),ActionKind.Charge,false,true);
        CommandFeedback(RemoteButton(238,552,198,52,"Stop charging",14,()=>_ = SendLive(new(ActionKind.Charge,Enabled:false))),ActionKind.Charge,false,false);
        LiveButton(24,708,412,46,"Update",13,()=>_ = RefreshLive(false));
        Label(24,758,412,18,Updated(s?.RetrievedAt),11,Muted);
    }
    // "full at 6:40 AM" (or with the weekday when it isn't today), from Hyundai's minutes-left estimate.
    private static string? ChargeEta(LiveVehicleState? s)
    {
        // An estimate that has already passed came from an old report; don't show it as current.
        if(ChargeEstimate.FullAt(s) is not DateTimeOffset at||at<=DateTimeOffset.UtcNow)return null;
        var local=at.ToLocalTime();
        return "full at "+(local.Date==DateTimeOffset.Now.Date?local.ToString("h:mm tt"):local.ToString("ddd h:mm tt"));
    }
    private OptionButton LimitChoice(int x,int y,int w,int value)
    {
        var b=new OptionButton{Position=new(x,y),Size=new(w,44),Disabled=liveBusy};
        foreach(var n in new[]{50,60,70,80,90,100})b.AddItem(n+"%");
        b.Select(Math.Clamp((value-50)/10,0,5));canvas.AddChild(b);return b;
    }
    private void LiveSettings()
    {
        Title("YOUR CONNECTED GARAGE.","Hyundai USA · device security");
        Label(24,204,412,52,$"{VehicleName}\nVIN …{VinTail}",17,White,true,true);
        Label(24,262,412,28,"Session: "+(connection?.IsAuthenticated==true?"Signed in":Offline?"Showing saved status · "+vault.BiometricName+" on first request":
            "Expired / denied — sign in again"),13,Muted,false,true);
        Button(24,302,412,46,"Appearance  /  "+paintName+"  ›",13,()=>Navigate("Appearance"),false,"SettingsAppearance");
        LiveButton(24,358,412,46,"Update",13,()=>_ = RefreshLive(false));
        LiveButton(24,414,412,46,"Wake vehicle and refresh",13,WakeWithCheck,false,"WakeRefresh");
        LiveButton(24,470,412,46,"Sign out & forget",13,()=>_ = ForgetAccount(),false,"SignOut");
        Button(24,526,412,46,"Request limits  ›",13,()=>Navigate("Limits"),false,"ViewLimits");
        Button(24,582,412,46,"View disclaimer  ›",13,()=>Navigate("Disclaimer"),false,"ViewDisclaimer");
        Button(24,638,412,46,"Ignored places  ›",13,()=>Navigate("Places"),false,"ViewPlaces");
        Label(24,694,412,64,vault.HasAccount?"Account saved securely on this device. The app opens to the last saved status; "+vault.BiometricName+
            " is required for the first update or command after you open it. Signing out removes the saved login and status.":
            "Session-only login. To enable saved login, sign out and sign in with Save account selected on a supported phone.",12,Muted,false,true);
    }
    private void LiveActivity()
    {
        Title("YOUR RECENT ACTIVITY.","Hyundai requests · session-only history");
        int y=204;
        for(int i=history.Count-1;i>=Math.Max(0,history.Count-4);i--){var item=history[i];
            Panel(24,y,412,99,new("#0e2031"),false);Rect(24,y,3,99,item.Ok?Blue:Red);
            Label(42,y+7,375,53,item.Title,12,White,true,true);Label(42,y+64,375,24,item.Detail,10,Muted);y+=111;}

    }
    private Button RemoteButton(float x,float y,float w,float h,string text,int size,Action action,bool primary=false,string name="")
    {
        var button=LiveButton(x,y,w,h,text,size,action,primary,name);
        button.Disabled|=monitoringCommand;return button;
    }
    // value limits the …/✓/!/? mark to the button whose command value was sent (presets of the same kind).
    private void CommandFeedback(Button button,ActionKind kind,bool active,bool? enabled=null,int? value=null)
    {
        if(active) {
            foreach(string state in new[]{"normal","hover","pressed","disabled"})
                button.AddThemeStyleboxOverride(state,Style(Red,Red));
            button.AddThemeColorOverride("font_color",White);
            button.AddThemeColorOverride("font_disabled_color",White);
        }
        bool match=feedbackKind==kind || kind==ActionKind.Lock&&feedbackKind==ActionKind.Unlock;
        if(!match || enabled!=null&&enabled!=feedbackEnabled || value!=null&&value!=feedbackValue)return;
        button.Text+=monitoringCommand?" …":connection?.Pending?.Outcome switch {
            RemoteOutcome.Confirmed=>" ✓",RemoteOutcome.Failed=>" !",RemoteOutcome.Unknown=>" ?",_=>""};
        button.TooltipText=connection?.Pending?.Message??"";
    }
    private async Task SendLive(VehicleCommand cmd,int? dc=null)
    {
        if(liveBusy||monitoringCommand||!liveMode)return;
        int version=liveEpoch;var token=liveLifetime.Token;
        await RunLive(async()=>{
            // The first command of a foreground session asks for Face ID before anything is sent.
            var (active,_)=await AcquireSession(version);
            if(active==null)return;
            var before=active.State;var previousReceipt=active.Pending;
            try {
                var receipt=await active.ExecuteAsync(cmd,dc,token);EnsureLive(version);
                notice=receipt.Message;noticeError=receipt.Outcome is RemoteOutcome.Failed or RemoteOutcome.Unknown;
                // Show the flash only once Hyundai has accepted the request; refused or failed requests show nothing.
                if(cmd.Kind is ActionKind.Lights or ActionKind.HornLights&&receipt.Outcome==RemoteOutcome.Accepted) {
                    flashEffectUntil=Time.GetTicksMsec()+FlashEffectMs;hornEffect=cmd.Kind==ActionKind.HornLights;
                }
                history.Add((receipt.Message,DateTime.Now.ToString("h:mm tt")+" · "+cmd.Kind,!noticeError));
            } finally {
                if(version==liveEpoch&&active.Pending!=null&&!ReferenceEquals(previousReceipt,active.Pending)) {
                    feedbackKind=cmd.Kind;
                    feedbackEnabled=cmd.Enabled;feedbackValue=cmd.Value;
                    monitoringCommand=true;
                    commandMonitor=MonitorLive(active,cmd,dc,before,version,token);
                }
            }
        },NeedsFaceId?"Waiting for "+vault.BiometricName+"…":"Sending command to Hyundai…",()=>SendLive(cmd,dc));
    }
    private async Task MonitorLive(HyundaiUsClient active,VehicleCommand cmd,int? dc,LiveVehicleState? before,int version,CancellationToken token)
    {
        try {
            await active.MonitorCommandAsync(cmd,dc,before,()=>{
                EnsureLive(version);
                // Other user operations own their notice while busy.
                PersistSnapshot();
                if(!liveBusy){notice=active.Pending?.Message??"";noticeError=active.Pending?.Outcome is RemoteOutcome.Failed or RemoteOutcome.Unknown;Render();}
            },token);
        } catch(OperationCanceledException) { }
        catch(Exception) {
            if(version==liveEpoch){notice="Automatic status checks stopped; completion unverified. No command was retried.";noticeError=true;}
        } finally {
            if(version==liveEpoch) {
                monitoringCommand=false;
                if(active.Pending is {} result)history.Add((result.Message,DateTime.Now.ToString("h:mm tt")+" · "+cmd.Kind,result.Outcome==RemoteOutcome.Confirmed));
                if(history.Count>30)history.RemoveRange(0,history.Count-30);
                if(IsInsideTree())Render();
            }
        }
    }
    // Update (cached read) or wake. The first request of a foreground session asks for Face ID.
    private Task RefreshLive(bool wake)=>RunLive(async()=>{
        int version=liveEpoch;
        var (active,freshRead)=await AcquireSession(version);
        if(active==null)return;
        // Signing in already read status; only a wake needs another request.
        if(!freshRead||wake){await active.ReadAsync(wake,liveLifetime.Token);EnsureLive(version);}
        notice="Hyundai status received. Check the reported timestamp.";
    },NeedsFaceId?"Waiting for "+vault.BiometricName+"…":wake?"Requesting fresh vehicle status…":"Reading Hyundai’s cached status…",()=>RefreshLive(wake));
}
