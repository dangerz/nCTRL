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
    private Func<HyundaiUsClient> connectionFactory = () => new HyundaiUsClient();
    private DateTimeOffset lastCheck;
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
            Label(24,359,412,100,"Selecting a vehicle reads its last reported status. Remote commands are sent only when you use a control and confirm the request.",14,Muted,false,true);
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
            "Secure storage is unavailable in this build or on this device. Manual login is session-only; nothing is saved.",12,Muted,false,true);
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
        connection?.Dispose();var active=connectionFactory();connection=active;enrollment=null;
        int version=liveEpoch;
        await RunLive(async()=>{
            var vehicles=await active.LoginAsync(email,password,pin,liveLifetime.Token);EnsureLive(version);
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
        },"Signing in to Hyundai…");
    }
    private async Task ChooseVehicle()
    {
        if(enrollment==null||connection==null)return;
        int version=liveEpoch;var active=connection;var selected=enrollment[selectedIndex];
        await RunLive(async()=>{
            await active.SelectAsync(selected,liveLifetime.Token);EnsureLive(version);
            liveMode=true;screen="Car";history.Clear();
            if(connection.State?.AcLimit is int ac)liveAc=ac;
            if(connection.State?.DcLimit is int dc)liveDc=dc;
            if(connection.State?.TemperatureF is int t&&t>=62&&t<=82)liveTemperature=t;
            history.Add(("Connected to Hyundai","Read-only status loaded. No remote command sent.",true));
            notice="Connected. Showing Hyundai’s last reported state.";
        },"Reading Hyundai vehicle status…");
    }
    private void Disconnect()
    {
        if(liveBusy)return;
        EndLiveSession();notice="Session locked. Saved login, if any, remains protected.";Render();
    }
    private async Task RunLive(Func<Task> work,string progress)
    {
        if(liveBusy)return;
        int version=liveEpoch;
        liveBusy=true;notice=progress;noticeError=false;Render();
        try {await work();EnsureLive(version);}
        catch(Exception) when(version!=liveEpoch) { }
        catch(VaultException ex){notice=ex.Message;noticeError=true;}
        catch(BluelinkException ex){notice=ex.Message;noticeError=true;}
        catch(OperationCanceledException){notice="Request interrupted. For a command, the outcome may be unknown. Refresh before retrying.";noticeError=true;}
        catch(HttpRequestException){notice="Could not reach Hyundai. Check your connection. No automatic retry was sent.";noticeError=true;}
        catch(Exception){notice="Could not complete the request. No automatic retry was sent.";noticeError=true;}
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
        Label(24,106,260,24,"YOUR HYUNDAI · LAST REPORTED",10,Muted,true);
        Button(294,100,142,32,"Customize paint  ›",11,()=>Navigate("Paint"));
        Hero(139,215);
        Label(24,369,190,20,"CHARGE",11,Muted,true);Label(294,369,130,20,"EST. RANGE",11,Muted,true);
        Label(21,391,180,60,Value(s?.Battery,"%"),46,Blue,true);
        Label(288,403,150,42,s?.RangeMiles is double range?$"{range:0} mi":"—",28,White,true);
        for(int i=0;i<20;i++)Rect(24+i*20.5f,459,17,9,s?.Battery is int soc && i<soc/5.0?Blue:new("#243446"));
        Label(24,482,412,24,"Doors: "+OnOff(s?.Locked,"Locked","Unlocked"),13,White);
        Label(24,509,412,36,"Reported: "+ReportedTime(s?.ReportedAt),11,Muted,false,true);
        LiveButton(24,563,94,49,"Lock",13,()=>ConfirmLive(new(ActionKind.Lock),"Lock the vehicle?"));
        LiveButton(130,563,94,49,"Unlock",13,()=>ConfirmLive(new(ActionKind.Unlock),"Unlock the vehicle?"));
        Button(236,563,94,49,"Climate",13,()=>Navigate("Climate"));
        Button(342,563,94,49,"Charge",13,()=>Navigate("Energy"));
        LiveButton(24,636,200,46,"Refresh cached status",12,()=>_ = RefreshLive(false));
        Button(236,636,200,46,"Vehicle location  ›",12,()=>Navigate("Locate"));
        LiveButton(24,704,412,46,"Check last command",13,()=>_ = CheckLive());
        if(notice.Length==0)Label(24,790,412,48,"Live account. Cached status may lag the vehicle. Missing data is shown as — or Unknown.",11,Muted,false,true);
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
        LiveButton(24,613,412,52,"Start / update climate",14,()=>ConfirmLive(new(ActionKind.Climate,liveTemperature,true),$"Start climate at {liveTemperature}°F?"),true);
        LiveButton(24,687,412,48,"Stop climate",14,()=>ConfirmLive(new(ActionKind.Climate,liveTemperature,false),"Stop remote climate?"));
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
        LiveButton(24,490,412,49,"Set AC and DC targets",13,()=>{
            ConfirmLive(new(ActionKind.ChargeLimit,liveAc),$"Set AC to {liveAc}% and DC to {liveDc}%?",liveDc);
        });
        Label(24,552,412,44,"Both displayed selections are sent together. They are not applied until you confirm.",11,Muted,false,true);
        LiveButton(24,622,198,52,"Start charging",14,()=>ConfirmLive(new(ActionKind.Charge,Enabled:true),"Start charging the vehicle?"),true);
        LiveButton(238,622,198,52,"Stop charging",14,()=>ConfirmLive(new(ActionKind.Charge,Enabled:false),"Stop charging the vehicle?"));
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
        Label(24,210,412,60,$"{v?.Name}\nVIN …{v?.Vin[^6..]}",17,White,true,true);
        Label(24,295,412,62,"Session: "+(connection?.IsAuthenticated==true?"Signed in":"Expired / denied — sign in again"),14,Muted,false,true);
        Button(24,381,412,48,"Appearance  /  "+paintName+"  ›",13,()=>Navigate("Appearance"));
        LiveButton(24,452,412,48,"Refresh cached status",13,()=>_ = RefreshLive(false));
        LiveButton(24,523,412,48,"Wake vehicle and refresh",13,()=>{
            ConfirmAction("Wake vehicle?","This asks Hyundai to contact your car for fresh status.",()=>_ = RefreshLive(true));
        });
        LiveButton(24,594,198,48,"Lock app",13,()=>EndLiveSession());
        LiveButton(238,594,198,48,"Sign out & forget",12,()=>_ = ForgetAccount());
        Label(24,676,412,96,vault.HasAccount?"Account saved securely on this device. "+vault.BiometricName+" unlocks it. Backgrounding locks the app; signing out removes the saved login.":
            "Session-only login. To enable saved login, lock the app and sign in with Save account selected on a supported phone.",12,Muted,false,true);
    }
    private void LiveActivity()
    {
        Title("YOUR RECENT ACTIVITY.","Hyundai requests · session-only history");
        int y=204;
        for(int i=history.Count-1;i>=Math.Max(0,history.Count-4);i--){var item=history[i];
            Panel(24,y,412,99,new("#0e2031"),false);Rect(24,y,3,99,item.Ok?Blue:Red);
            Label(42,y+7,375,53,item.Title,12,White,true,true);Label(42,y+64,375,24,item.Detail,10,Muted);y+=111;}
        LiveButton(24,704,412,43,"Check last command",13,()=>_ = CheckLive());
    }
    private void LiveLocation()
    {
        Title("FIND YOUR HYUNDAI.","Location is requested only when you tap below.");
        var loc=connection?.Location;
        Label(24,225,412,150,loc is null?"No location requested this session.":
            $"Latitude  {loc.Latitude:F6}\nLongitude  {loc.Longitude:F6}\nReported: {ReportedTime(loc.ReportedAt)}",18,White,true,true);
        LiveButton(24,440,412,52,"Request vehicle location",14,()=>_ = RunLive(async()=>{
            int version=liveEpoch;await connection!.LocateAsync(liveLifetime.Token);EnsureLive(version);notice="Location received from Hyundai.";
        },"Requesting location from Hyundai…"),true);
        if(loc!=null)Button(24,521,412,48,"Open location in maps",14,()=>{
            var point=loc.Latitude.ToString(CultureInfo.InvariantCulture)+","+loc.Longitude.ToString(CultureInfo.InvariantCulture);
            OS.ShellOpen("https://www.google.com/maps/search/?api=1&query="+Uri.EscapeDataString(point));
        });
        Label(24,607,412,105,"Location may be the last reported position. Requests are limited to one per hour. Opening Maps shares these coordinates with your map provider.",13,Muted,false,true);
    }
    private void ConfirmAction(string title,string detail,Action action)
    {
        if(liveBusy)return;
        var confirm=new ConfirmationDialog{Title=title,DialogText=detail,OkButtonText="Confirm",CancelButtonText="Cancel"};
        AddChild(confirm);confirm.Confirmed+=()=>{confirm.QueueFree();action();};confirm.Canceled+=()=>confirm.QueueFree();
        confirm.PopupCentered(new Vector2I(400,210));
    }
    private void ConfirmLive(VehicleCommand cmd,string question,int? dc=null)
    {
        int version=liveEpoch;
        ConfirmAction(question,$"This sends a real command to\n{connection?.Selected?.Name} · VIN …{connection?.Selected?.Vin[^6..]}",()=>_ = RunLive(async()=>{
            EnsureLive(version);var receipt=await connection!.ExecuteAsync(cmd,dc,liveLifetime.Token);EnsureLive(version);
            notice=receipt.Message;noticeError=receipt.Outcome is RemoteOutcome.Failed or RemoteOutcome.Unknown;
            history.Add((receipt.Message,DateTime.Now.ToString("h:mm tt")+" · "+cmd.Kind,!noticeError));
        },"Sending command to Hyundai…"));
    }
    private Task RefreshLive(bool wake)=>RunLive(async()=>{
        int version=liveEpoch;await connection!.ReadAsync(wake,liveLifetime.Token);EnsureLive(version);
        notice="Hyundai status received. Check the reported timestamp.";
    },wake?"Requesting fresh vehicle status…":"Reading Hyundai’s cached status…");
    private async Task CheckLive()
    {
        if(DateTimeOffset.UtcNow-lastCheck<TimeSpan.FromSeconds(5)){notice="Wait five seconds between command status checks.";Render();return;}
        lastCheck=DateTimeOffset.UtcNow;
        await RunLive(async()=>{
            int version=liveEpoch;var r=await connection!.CheckCommandAsync(liveLifetime.Token);EnsureLive(version);notice=r.Message;
            noticeError=r.Outcome is RemoteOutcome.Failed or RemoteOutcome.Unknown;
            history.Add((r.Message,DateTime.Now.ToString("h:mm tt")+" · Command status",!noticeError));
        },"Checking command status…");
    }
}
