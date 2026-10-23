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
    // Set in _Ready so every client shares the persisted request budget.
    private Func<HyundaiUsClient> connectionFactory = null!;
    private bool monitoringCommand;
    private ActionKind? feedbackKind;
    private bool feedbackEnabled;
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
            LiveButton(24,581,412,44,"Cancel and lock session",13,()=>EndLiveSession());
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
        Label(24,519,412,70,vault.Available?"Saved credentials stay on this phone and require biometrics to unlock. The app locks when backgrounded.":
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
            enrollment=vehicles;selectedIndex=0;screen="Connect";notice="Signed in. Select your vehicle.";
            try {
                if(remember) {
                    await vault.SaveAsync(new SavedAccount(email,password,pin),liveLifetime.Token);EnsureLive(version);
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
        liveMode=true;screen="Car";history.Clear();
        if(active.State?.AcLimit is int ac)liveAc=ac;
        if(active.State?.DcLimit is int dc)liveDc=dc;
        if(active.State?.TemperatureF is int t&&t>=62&&t<=82)liveTemperature=t;
        history.Add(("Connected to Hyundai","Read-only status loaded. No remote command sent.",true));
        if(!noticeError)notice="Connected. Showing Hyundai’s last reported state.";
        linkedVehicle=VehicleLink.Fingerprint(accountKey,selected.Vin);SavePrefs();
    }
    private void Disconnect()
    {
        if(liveBusy)return;
        EndLiveSession();notice="Session locked. Saved login, if any, remains protected.";Render();
    }
    // retry re-runs a user-initiated request after an override; automatic work passes none.
    private async Task RunLive(Func<Task> work,string progress,Func<Task>? retry=null)
    {
        if(liveBusy)return;
        int version=liveEpoch;
        liveBusy=true;notice=progress;noticeError=false;Render();
        try {await work();EnsureLive(version);}
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
        else LiveSettings();
    }
    private void LiveHome()
    {
        var s=connection?.State;
        Label(24,106,412,24,"YOUR HYUNDAI · LAST REPORTED",10,Muted,true);
        Hero(139,215);
        Label(24,369,190,20,"CHARGE",11,Muted,true);Label(294,369,130,20,"EST. RANGE",11,Muted,true);
        Label(21,391,180,60,Value(s?.Battery,"%"),46,Blue,true);
        Label(288,403,150,42,s?.RangeMiles is double range?$"{range:0} mi":"—",28,White,true);
        for(int i=0;i<20;i++)Rect(24+i*20.5f,459,17,9,s?.Battery is int soc && i<soc/5.0?Blue:new("#243446"));
        Label(24,482,412,24,"Doors: "+OnOff(s?.Locked,"Locked","Unlocked"),13,White);
        Label(24,509,412,36,"Reported: "+ReportedTime(s?.ReportedAt),11,Muted,false,true);
        var doorAction=VehicleLink.DoorAction(s?.Locked);
        var door=LiveButton(24,563,200,49,doorAction==ActionKind.Unlock?"Unlock":doorAction==ActionKind.Lock?"Lock":"Door status unknown",13,
            ()=>{if(doorAction is ActionKind action)_ = SendLive(new(action));},false,"DoorControl");
        door.Disabled=liveBusy||monitoringCommand||doorAction==null;
        CommandFeedback(door,ActionKind.Lock,s?.Locked==true);
        CommandFeedback(Button(236,563,94,49,"Climate",13,()=>Navigate("Climate")),ActionKind.Climate,s?.ClimateOn==true);
        CommandFeedback(Button(342,563,94,49,"Charge",13,()=>Navigate("Energy")),ActionKind.Charge,false);
        LiveButton(24,636,200,46,"Refresh cached status",12,()=>_ = RefreshLive(false));
        Button(236,636,200,46,"Vehicle location  ›",12,()=>_ = OpenVehicleLocation());

        if(notice.Length==0)Label(24,778,412,65,connection?.Pending?.Message??"Cached status may lag the vehicle. Missing data is shown as — or Unknown.",11,Muted,false,true);
    }
    private static string ReportedTime(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return "Not supplied by Hyundai";
        // Preserve server text rather than guessing the timezone of compact timestamps.
        return raw.Length>48?"Timestamp format unsupported":raw;
    }
    private void LiveClimate()
    {
        var s=connection?.State;
        Title("CABIN, YOUR WAY.","Hyundai remote climate · electric vehicles");
        Label(24,210,412,40,"Reported climate: "+OnOff(s?.ClimateOn,"On","Off"),15,White,true);
        Label(24,254,412,30,"Reported setpoint: "+Value(s?.TemperatureF,"°F"),13,Muted);
        Panel(24,317,412,160,new("#0e2031"));
        Label(142,352,220,68,$"{liveTemperature}°F",40,White,true);
        LiveButton(43,363,51,51,"−",27,()=>{liveTemperature=Math.Max(62,liveTemperature-1);Render();});
        LiveButton(366,363,51,51,"+",27,()=>{liveTemperature=Math.Min(82,liveTemperature+1);Render();});
        Label(24,505,412,70,"Starts climate with defrost and supplemental heaters off. Generation 3 requests a five-minute duration.",12,Muted,false,true);
        CommandFeedback(RemoteButton(24,613,412,52,"Start / update climate",14,()=>_ = SendLive(new(ActionKind.Climate,liveTemperature,true)),true),ActionKind.Climate,s?.ClimateOn==true,true);
        CommandFeedback(RemoteButton(24,687,412,48,"Stop climate",14,()=>_ = SendLive(new(ActionKind.Climate,liveTemperature,false))),ActionKind.Climate,false,false);
    }
    private void LiveEnergy()
    {
        var s=connection?.State;
        Title("POWER YOUR NEXT DRIVE.","Hyundai charging controls · last reported status");
        Label(24,207,412,42,Value(s?.Battery,"%")+"  ·  "+OnOff(s?.Charging,"Charging","Not charging"),25,Blue,true);
        Label(24,254,412,26,"Cable: "+OnOff(s?.PluggedIn,"Connected","Disconnected"),13,Muted);
        Label(24,295,412,26,$"Reported targets  AC {Value(s?.AcLimit,"%")}  /  DC {Value(s?.DcLimit,"%")}",13,Muted);
        Label(24,343,412,26,"Choose both targets before saving",13,White,true);
        var ac=LimitChoice(24,388,198,liveAc);var dc=LimitChoice(238,388,198,liveDc);
        Label(24,438,198,22,"AC TARGET",11,Muted,true);Label(238,438,198,22,"DC TARGET",11,Muted,true);
        ac.ItemSelected+=i=>{liveAc=50+(int)i*10;};
        dc.ItemSelected+=i=>{liveDc=50+(int)i*10;};
        CommandFeedback(RemoteButton(24,490,412,49,"Set AC and DC targets",13,()=>{
            _ = SendLive(new(ActionKind.ChargeLimit,liveAc),liveDc);
        }),ActionKind.ChargeLimit,false);
        Label(24,552,412,44,"Tap Set AC and DC targets to apply both displayed selections.",11,Muted,false,true);
        CommandFeedback(RemoteButton(24,622,198,52,"Start charging",14,()=>_ = SendLive(new(ActionKind.Charge,Enabled:true)),true),ActionKind.Charge,false,true);
        CommandFeedback(RemoteButton(238,622,198,52,"Stop charging",14,()=>_ = SendLive(new(ActionKind.Charge,Enabled:false))),ActionKind.Charge,false,false);
        LiveButton(24,708,412,46,"Refresh cached status",13,()=>_ = RefreshLive(false));
    }
    private OptionButton LimitChoice(int x,int y,int w,int value)
    {
        var b=new OptionButton{Position=new(x,y),Size=new(w,44),Disabled=liveBusy};
        foreach(var n in new[]{50,60,70,80,90,100})b.AddItem(n+"%");
        b.Select(Math.Clamp((value-50)/10,0,5));canvas.AddChild(b);return b;
    }
    private void LiveSettings()
    {
        var v=connection?.Selected;
        Title("YOUR CONNECTED GARAGE.","Hyundai USA · device security");
        Label(24,204,412,52,$"{v?.Name}\nVIN …{v?.Vin[^6..]}",17,White,true,true);
        Label(24,262,412,28,"Session: "+(connection?.IsAuthenticated==true?"Signed in":"Expired / denied — sign in again"),13,Muted,false,true);
        Button(24,302,412,46,"Appearance  /  "+paintName+"  ›",13,()=>Navigate("Appearance"),false,"SettingsAppearance");
        LiveButton(24,358,412,46,"Refresh cached status",13,()=>_ = RefreshLive(false));
        LiveButton(24,414,412,46,"Wake vehicle and refresh",13,()=>_ = RefreshLive(true));
        LiveButton(24,470,198,46,"Lock app",13,()=>EndLiveSession());
        LiveButton(238,470,198,46,"Sign out & forget",12,()=>_ = ForgetAccount());
        Button(24,526,412,46,"Request limits  ›",13,()=>Navigate("Limits"),false,"ViewLimits");
        Button(24,582,412,46,"View disclaimer  ›",13,()=>Navigate("Disclaimer"),false,"ViewDisclaimer");
        Label(24,642,412,64,vault.HasAccount?"Account saved securely on this device. "+vault.BiometricName+" unlocks it. Backgrounding locks the app; signing out removes the saved login.":
            "Session-only login. To enable saved login, lock the app and sign in with Save account selected on a supported phone.",12,Muted,false,true);
    }
    private void LiveActivity()
    {
        Title("YOUR RECENT ACTIVITY.","Hyundai requests · session-only history");
        int y=204;
        for(int i=history.Count-1;i>=Math.Max(0,history.Count-4);i--){var item=history[i];
            Panel(24,y,412,99,new("#0e2031"),false);Rect(24,y,3,99,item.Ok?Blue:Red);
            Label(42,y+7,375,53,item.Title,12,White,true,true);Label(42,y+64,375,24,item.Detail,10,Muted);y+=111;}

    }
    private Button RemoteButton(float x,float y,float w,float h,string text,int size,Action action,bool primary=false)
    {
        var button=LiveButton(x,y,w,h,text,size,action,primary);
        button.Disabled|=monitoringCommand;return button;
    }
    private void CommandFeedback(Button button,ActionKind kind,bool active,bool? enabled=null)
    {
        if(active) {
            foreach(string state in new[]{"normal","hover","pressed","disabled"})
                button.AddThemeStyleboxOverride(state,Style(Red,Red));
            button.AddThemeColorOverride("font_color",White);
            button.AddThemeColorOverride("font_disabled_color",White);
        }
        bool match=feedbackKind==kind || kind==ActionKind.Lock&&feedbackKind==ActionKind.Unlock;
        if(!match || enabled!=null&&enabled!=feedbackEnabled)return;
        button.Text+=monitoringCommand?" …":connection?.Pending?.Outcome switch {
            RemoteOutcome.Confirmed=>" ✓",RemoteOutcome.Failed=>" !",RemoteOutcome.Unknown=>" ?",_=>""};
        button.TooltipText=connection?.Pending?.Message??"";
    }
    private async Task SendLive(VehicleCommand cmd,int? dc=null)
    {
        if(liveBusy||monitoringCommand||connection==null)return;
        int version=liveEpoch;var active=connection;var before=active.State;var token=liveLifetime.Token;
        var previousReceipt=active.Pending;
        await RunLive(async()=>{
            try {
                var receipt=await active.ExecuteAsync(cmd,dc,token);EnsureLive(version);
                notice=receipt.Message;noticeError=receipt.Outcome is RemoteOutcome.Failed or RemoteOutcome.Unknown;
                history.Add((receipt.Message,DateTime.Now.ToString("h:mm tt")+" · "+cmd.Kind,!noticeError));
            } finally {
                if(version==liveEpoch&&active.Pending!=null&&!ReferenceEquals(previousReceipt,active.Pending)) {
                    feedbackKind=cmd.Kind;
                    feedbackEnabled=cmd.Enabled;
                    monitoringCommand=true;
                    commandMonitor=MonitorLive(active,cmd,dc,before,version,token);
                }
            }
        },"Sending command to Hyundai…",()=>SendLive(cmd,dc));
    }
    private async Task MonitorLive(HyundaiUsClient active,VehicleCommand cmd,int? dc,LiveVehicleState? before,int version,CancellationToken token)
    {
        try {
            await active.MonitorCommandAsync(cmd,dc,before,()=>{
                EnsureLive(version);
                // Other user operations own their notice while busy.
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
    private Task RefreshLive(bool wake)=>RunLive(async()=>{
        int version=liveEpoch;await connection!.ReadAsync(wake,liveLifetime.Token);EnsureLive(version);
        notice="Hyundai status received. Check the reported timestamp.";
    },wake?"Requesting fresh vehicle status…":"Reading Hyundai’s cached status…",()=>RefreshLive(wake));
}
