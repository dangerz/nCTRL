using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public partial class Main : Control
{
    private static readonly Color Blue = new("#8ecbfa"), White = new("#eef5fb"),
        Muted = new("#9ab0c6"), Red = new("#f24a4d"), Back = new("#08121d");
    private readonly DemoHyundaiClient client = new();
    private readonly List<(string Title,string Detail,bool Ok)> history = new();
    private Control canvas = null!;
    private Font regular = null!, bold = null!;
    private Texture2D car = null!;
    private Shader paintShader = null!;
    private string screen = "Car", paintName = "Performance Blue", paintHex = "89b7f0";
    private string garageName = "Roy’s garage", notice = "", lastSeen = "Demo snapshot";
    private int temperature = 72, chargeLimit = 80;
    private bool busy, noticeError;
    private readonly CancellationTokenSource lifetime = new();
    private const string Prefs = "user://preferences.cfg";
    private bool smoke;
    private readonly (string Name,string Hex)[] paints = {
        ("Performance Blue","89b7f0"),("Atlas White","e4e8ed"),
        ("Ecotonic Gray","747c79"),("Phantom Black","202630"),
        ("Soultronic Orange","ee632f"),("Gravity Gold","b6ab8a")
    };

    public override void _Ready()
    {
        regular = GD.Load<Font>("res://Assets/Body.ttf");
        bold = GD.Load<Font>("res://Assets/Bold.ttf");
        car = GD.Load<Texture2D>("res://Assets/ioniq_pixel.png");
        paintShader = GD.Load<Shader>("res://Assets/Paint.gdshader");
        smoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--smoke-test");
        liveSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--live-smoke-test");
        vaultSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--secure-smoke-test");
        liveSmoke |= vaultSmoke;
        smoke |= liveSmoke;
        if (!smoke) LoadPrefs();
        InitializeVault();
        history.Add(("Welcome to nCTRL", "Demo vehicle ready. No account connected.",true));
        Render();
        if (vaultSmoke) _ = SecureSmokeTest();
        else if (liveSmoke) _ = LiveSmokeTest();
        else if (smoke) _ = SmokeTest();
    }

    public override void _ExitTree() { liveEpoch++; vault.Cancel(); lifetime.Cancel(); liveLifetime.Cancel(); connection?.Dispose(); lifetime.Dispose(); liveLifetime.Dispose(); }
    private void LoadPrefs()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Prefs) != Error.Ok) return;
        string hex = cfg.GetValue("appearance","paint","89b7f0").AsString();
        if (Color.HtmlIsValid(hex)) paintHex = hex;
        paintName = cfg.GetValue("appearance","name","Performance Blue").AsString();
        if (paintName == "Cyber Gray") { paintName="Ecotonic Gray"; paintHex="747c79"; }
        garageName = cfg.GetValue("appearance","garage","Roy’s garage").AsString();
    }
    private void SavePrefs()
    {
        if(smoke) return;
        var cfg = new ConfigFile();
        cfg.SetValue("appearance","paint",paintHex);
        cfg.SetValue("appearance","name",paintName);
        cfg.SetValue("appearance","garage",garageName);
        if(cfg.Save(Prefs) != Error.Ok) { notice = "Could not save appearance on this device."; noticeError=true; }
    }
    private void PickPaint(string name,string hex)
    {
        paintName = name; paintHex = hex; SavePrefs(); Render();
    }
    private void Navigate(string target) { screen=target; notice=""; Render(); }

    private void Render()
    {
        if(canvas != null) { RemoveChild(canvas); canvas.QueueFree(); }
        canvas = new Control { Name="Canvas", Size=new Vector2(460,920) };
        AddChild(canvas);
        Rect(0,0,460,920,Back);
        // N-inspired asymmetric motorsport detail; original artwork and layout.
        Panel(0,0,460,96,new("#0c1825"),false,true);
        Label(24,19,310,20,garageName,13,Muted);
        Label(24,44,340,41,"IONIQ 5 N",29,White,true);
        Button(365,20,70,28,vaultSmoke?"TEST":liveMode?(liveSmoke?"TEST":"LIVE"):"DEMO",11,()=>Navigate("Settings"),false,"DemoBadge");
        if(screen=="Connect") ConnectionScreen();
        else if(liveMode && screen!="Paint") LiveScreen();
        else if(screen=="Car") Home();
        else if(screen=="Paint") Paint();
        else if(screen=="Climate") Climate();
        else if(screen=="Energy") Energy();
        else if(screen=="Activity") Activity();
        else if(screen=="Settings") Settings();
        else if(screen=="Locate") Locate();
        Nav();
        if(notice.Length>0)
        {
            Rect(20,778,420,65,noticeError?new("#47232a"):new("#183749"));
            Label(31,784,397,53,notice,11,White,false,true);
        }
    }
    private void Title(string title,string sub)
    {
        int titleSize=25;
        while(titleSize>16 && bold.GetStringSize(title,HorizontalAlignment.Left,-1,titleSize).X>410)titleSize--;
        Label(24,112,410,36,title,titleSize,White,true);
        Label(24,152,410,34,sub,12,Muted,false,true);
    }
    private void Hero(int y,int height=215)
    {
        Panel(20,y,420,height,new("#101820"),false);
        var tex = new TextureRect {
            Name="CarArtwork", Position=new Vector2(24,y+4),Size=new Vector2(412,height-8),
            Texture=car, ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter=TextureFilterEnum.Nearest, MouseFilter=MouseFilterEnum.Ignore
        };
        var material = new ShaderMaterial { Shader=paintShader };
        material.SetShaderParameter("paint",new Color(paintHex));
        tex.Material=material; canvas.AddChild(tex);
    }
    private void Home()
    {
        Label(24,106,245,21,"YOUR DAILY DOSE OF N.",12,Muted,true);
        Button(294,100,142,32,"Customize paint  ›",11,()=>Navigate("Paint"),false,"OpenPaint");
        Hero(139,215);
        Label(24,369,190,20,"CHARGE",11,Muted,true);
        Label(294,369,130,20,"EST. RANGE",11,Muted,true);
        Label(21,391,180,60,$"{client.State.Battery}%",46,Blue,true);
        Label(288,403,145,42,$"{client.State.RangeMiles} mi",28,White,true);
        for(int i=0;i<20;i++) Rect(24+i*20.5f,459,17,9,i<16?Blue:new("#243446"));
        Glyph(24,488,"Lock",19);
        Label(53,485,240,25,$"Parked · {(client.State.Locked?"Locked":"Unlocked")}",13,Muted);
        Label(285,485,151,25,lastSeen,10,Muted);
        Quick(24,532,"Lock",client.State.Locked?"Unlock":"Lock",ToggleLock,"LockAction");
        Quick(130,532,"Climate","Climate",()=>Navigate("Climate"),"ClimateAction");
        Quick(236,532,"Charge","Charge",()=>Navigate("Energy"),"ChargeAction");
        Quick(342,532,"Locate","Locate",()=>Navigate("Locate"),"LocateAction");
        Panel(24,641,200,137,new("#0e2031"));
        Glyph(39,658,"Climate",23);
        Label(72,654,135,22,"Cabin · demo",12,Muted);
        Label(72,680,135,35,$"{client.State.TargetF}°F",27,White,true);
        Button(37,728,174,36,client.State.ClimateOn?"Stop climate":"Start climate",12,
            ()=>_ = Send(new(ActionKind.Climate,client.State.TargetF,!client.State.ClimateOn)),true,"HomeClimate");
        Panel(236,641,200,137,new("#0e2031"));
        Glyph(252,658,"Charge",23);
        Label(284,654,140,22,"Charge limit",12,Muted);
        Label(284,680,135,35,$"{client.State.ChargeLimit}%",27,White,true);
        Label(251,733,171,32,client.State.Charging?"Charging · simulated":client.State.PluggedIn?"Plugged in · paused":"Not plugged in",11,Muted);
        if(notice.Length==0) Label(24,799,410,35,"SIMULATED VEHICLE  /  Appearance saved on this device",10,Muted);
    }
    private void Paint()
    {
        Label(24,108,400,34,"MAKE IT YOURS.",25,White,true);
        Label(24,148,410,24,"Choose the paint for your pixel Ioniq.",12,Muted);
        Hero(185,215);
        Label(24,419,410,30,paintName.ToUpperInvariant(),20,Blue,true);
        Label(24,455,410,30,"Wheels, glass and signature red trim stay intact.",11,Muted);
        for(int i=0;i<paints.Length;i++)
        {
            var selected=paints[i];
            int x=24+(i%3)*142,y=501+(i/3)*82;
            var b=Button(x,y,128,68,"",12,()=>PickPaint(selected.Name,selected.Hex),false,"Paint"+i);
            var swatch=new ColorRect {Position=new(8,8),Size=new(112,23),Color=new(selected.Hex),MouseFilter=MouseFilterEnum.Ignore};
            b.AddChild(swatch);
            var text=new Label {Position=new(4,36),Size=new(120,24),Text=selected.Name,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            text.AddThemeFontOverride("font",regular);text.AddThemeFontSizeOverride("font_size",10);b.AddChild(text);
            if(selected.Hex==paintHex) b.AddThemeStyleboxOverride("normal",Style(new("#142b40"),Blue,2));
        }
        Label(24,682,200,22,"CUSTOM COLOR",11,Muted,true);
        var picker = new ColorPickerButton {Name="CustomPaint",Position=new(24,714),Size=new(174,46),Color=new(paintHex),EditAlpha=false,Text="Choose color"};
        picker.AddThemeFontOverride("font",bold);picker.AddThemeFontSizeOverride("font_size",12);
        // Update live without rebuilding/freeing the open popup.
        picker.ColorChanged += value => {
            paintName="Custom color";paintHex=value.ToHtml(false);
            if(canvas.GetNodeOrNull<TextureRect>("CarArtwork")?.Material is ShaderMaterial mat)
                mat.SetShaderParameter("paint",value);
        };
        picker.PopupClosed += ()=> { SavePrefs(); Render(); };
        canvas.AddChild(picker);
        Button(214,714,222,46,"Done  ›",14,()=>Navigate("Car"),true,"PaintDone");
        if(notice.Length==0) Label(24,794,410,40,"Preview colors are illustrative, not factory paint matching.",11,Muted,false,true);
    }
    private void Climate()
    {
        Title("CABIN, YOUR WAY.","Set a temperature, then send it to the demo vehicle.");
        Panel(24,207,412,238,new("#0e2031"),true,true);
        Label(46,230,350,24,client.State.ClimateOn?"CLIMATE IS RUNNING":"READY WHEN YOU ARE",12,Blue,true);
        Label(111,280,250,85,$"{temperature}°",65,White,true);
        Label(133,369,220,22,"TARGET TEMPERATURE · °F",10,Muted);
        Button(43,299,51,51,"−",27,()=> {temperature=Math.Max(62,temperature-1);Render();},false,"TempDown");
        Button(366,299,51,51,"+",27,()=> {temperature=Math.Min(82,temperature+1);Render();},false,"TempUp");
        Label(24,474,400,22,"QUICK PRESETS",11,Muted,true);
        Button(24,511,129,48,"Cool · 66°",13,()=>{temperature=66;Render();},false,"CoolPreset");
        Button(165,511,129,48,"Daily · 72°",13,()=>{temperature=72;Render();},false,"DailyPreset");
        Button(306,511,130,48,"Warm · 78°",13,()=>{temperature=78;Render();},false,"WarmPreset");
        Panel(24,593,412,76,new("#0c1b2a"),false);
        Label(41,607,375,48,"Your preset is a request. The status changes only after the simulated vehicle confirms it.",12,Muted,false,true);
        Button(24,704,412,52,busy?"Sending request…":client.State.ClimateOn?"Update temperature":"Start climate",15,
            ()=>_ = Send(new(ActionKind.Climate,temperature,true)),true,"StartClimate");
        if(client.State.ClimateOn) Button(24,766,412,26,"Stop climate",12,()=>_ = Send(new(ActionKind.Climate,temperature,false)),false,"StopClimate");
    }
    private void Energy()
    {
        Title("POWER YOUR NEXT DRIVE.","Charging controls · simulated vehicle");
        Panel(24,206,412,172,new("#0e2031"),true,true);
        Label(43,228,200,65,$"{client.State.Battery}%",48,Blue,true);
        Label(262,245,153,33,$"{client.State.RangeMiles} mi",26,White,true);
        for(int i=0;i<20;i++) Rect(44+i*18.4f,313,15,13,i<16?Blue:new("#25394d"));
        Label(44,341,370,25,client.State.Charging?"Charging · simulated session":client.State.PluggedIn?"Connected · charging paused":"Cable disconnected",12,Muted);
        Label(24,408,240,26,"AC CHARGE TARGET",12,Muted,true);
        Label(331,399,105,41,$"{chargeLimit}%",29,White,true).Name="LimitValue";
        var slider = new HSlider {Name="ChargeLimitSlider",Position=new(24,457),Size=new(412,34),MinValue=50,MaxValue=100,Step=10,Value=chargeLimit};
        slider.ValueChanged += v=>{chargeLimit=(int)v;var label=canvas.GetNodeOrNull<Label>("LimitReadout");if(label!=null)label.Text=$"Selected target: {chargeLimit}%";canvas.GetNode<Label>("LimitValue").Text=$"{chargeLimit}%";};
        canvas.AddChild(slider);
        Label(24,498,412,24,$"Selected target: {chargeLimit}%",12,Muted).Name="LimitReadout";
        Button(24,537,412,45,"Save charge target",13,()=>_ = Send(new(ActionKind.ChargeLimit,chargeLimit)),false,"SaveLimit");
        Button(24,611,412,53,client.State.Charging?"Pause charging":"Start charging",15,
            ()=>_ = Send(new(ActionKind.Charge,Enabled:!client.State.Charging)),true,"ToggleCharging");
        Button(24,686,412,42,client.State.PluggedIn?"Demo cable: plugged in  ·  Disconnect":"Demo cable: unplugged  ·  Connect",12,
            ()=>{if(busy)return;client.SetPluggedIn(!client.State.PluggedIn);Render();},false,"ToggleCable");
        Label(24,746,412,38,"Demo energy data. No charging hardware is connected.",11,Muted,false,true);
    }
    private void Activity()
    {
        Title("YOUR RECENT ACTIVITY.","Your recent demo commands and their results.");
        if(history.Count==0) Label(24,217,400,50,"No activity yet. Try a vehicle control.",14,Muted);
        int y=204;
        for(int i=history.Count-1;i>=Math.Max(0,history.Count-6);i--)
        {
            var item=history[i];
            Panel(24,y,412,84,new("#0e2031"),false);
            Rect(24,y,3,84,item.Ok?Blue:Red);
            Label(42,y+6,375,39,item.Title,13,White,true,true);
            Label(42,y+42,375,32,item.Detail,11,Muted,false,true);
            y+=94;
        }
        if(notice.Length==0) Label(24,799,412,37,"Activity is session-only in this prototype.",11,Muted);
    }
    private void Settings()
    {
        Title("YOUR GARAGE.","Appearance and prototype controls.");
        Label(24,204,412,22,"GARAGE NAME",11,Muted,true);
        var input = new LineEdit {Name="GarageName",Position=new(24,238),Size=new(278,44),Text=garageName,MaxLength=30};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);canvas.AddChild(input);
        Button(314,238,122,44,"Save",13,()=>{garageName=string.IsNullOrWhiteSpace(input.Text)?"My garage":input.Text.Trim();SavePrefs();Render();},true,"SaveGarage");
        Button(24,306,412,51,"Car color  /  "+paintName+"  ›",13,()=>Navigate("Paint"),false,"SettingsPaint");
        Label(24,388,412,22,"DEMO LAB",11,Muted,true);
        Button(24,425,412,48,client.Offline?"Offline simulation: ON":"Offline simulation: OFF",13,()=>{
            client.Offline=!client.Offline;Render();},false,"OfflineToggle");
        Button(24,489,412,48,client.FailNext?"Next request will fail":"Simulate one failed request",13,()=>{client.FailNext=!client.FailNext;Render();},false,"FailToggle");
        Button(24,553,412,48,"Refresh demo state",13,()=>_ = Send(new(ActionKind.Refresh)),false,"Refresh");
        Button(24,617,412,48,"Reset demo vehicle",13,()=>{if(busy)return;client.Reset();history.Clear();temperature=72;chargeLimit=80;notice="Demo reset. Your paint preference is saved.";noticeError=false;Render();},false,"ResetDemo");
        Button(24,694,412,48,"Connect Hyundai / Bluelink (USA)  ›",13,()=>Navigate("Connect"),true,"ConnectBluelink");
        Label(24,751,412,34,"Demo mode. Connect to use your real vehicle.",11,Muted,false,true);
    }
    private void Locate()
    {
        Title("RIGHT WHERE YOU LEFT IT.","Illustrative location · not your vehicle’s position");
        Panel(24,211,412,340,new("#112231"),false);
        for(int i=0;i<8;i++){Rect(36+i*52,225,3,310,new("#1d3548"));Rect(37,231+i*42,382,3,new("#1d3548"));}
        Rect(172,225,15,310,new("#30485b"));Rect(37,365,382,15,new("#30485b"));
        Glyph(207,336,"Locate",48);Label(245,350,150,23,"DEMO VEHICLE",11,Blue,true);
        Label(44,260,115,20,"nCTRL",12,Muted,true);
        Label(44,477,210,20,"SAMPLE MAP",10,Muted);
        Label(24,585,412,65,"A future direct Hyundai client will supply the last reported location and its timestamp.",14,Muted,false,true);
        Button(24,692,412,52,"Back to your car",14,()=>Navigate("Car"),true,"MapBack");
    }
    private void Nav()
    {
        Rect(0,851,460,69,new("#0b1520"));Rect(0,851,460,1,new("#294053"));
        string[] names={"Car","Energy","Activity","Settings"};
        string selected=screen is "Paint" or "Climate" or "Locate"?"Car":screen;
        for(int i=0;i<4;i++)
        {
            string name=names[i];int x=i*115;
            var b=Button(x,856,115,64,"",12,()=>Navigate(name),false,"Nav"+name);
            b.AddThemeStyleboxOverride("normal",Style(new("#0b1520"),new("#0b1520")));
            var g=new Glyph {Kind=name,Ink=name==selected?Blue:Muted,Position=new(44,5),Size=new(26,26)};b.AddChild(g);
            var l=new Label {Position=new(0,35),Size=new(115,22),Text=name,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            l.AddThemeFontOverride("font",regular);l.AddThemeFontSizeOverride("font_size",11);l.AddThemeColorOverride("font_color",name==selected?Blue:Muted);b.AddChild(l);
            if(name==selected) Rect(x+31,917,53,3,Red);
        }
    }
    private void ToggleLock()
    {
        if(busy)return;
        if(!client.State.Locked){_ = Send(new(ActionKind.Lock));return;}
        var confirm=new ConfirmationDialog {Title="Unlock demo vehicle?",DialogText="This simulates unlocking the car.\nNo real vehicle is connected.",OkButtonText="Unlock demo",CancelButtonText="Cancel"};
        AddChild(confirm);
        confirm.Confirmed += ()=>{confirm.QueueFree();_ = Send(new(ActionKind.Unlock));};
        confirm.Canceled += ()=>confirm.QueueFree();
        confirm.PopupCentered(new Vector2I(380,190));
    }
    private async Task Send(VehicleCommand cmd)
    {
        if(busy)return;
        busy=true;notice="Sending demo request…";noticeError=false;Render();
        try
        {
            var result=await client.ExecuteAsync(cmd,lifetime.Token);
            if(!IsInsideTree())return;
            busy=false;notice=result.Message;noticeError=!result.Success;
            if(result.Success)lastSeen="Updated just now";
            history.Add((result.Message,DateTime.Now.ToString("h:mm tt")+" · "+(result.Success?"Confirmed by demo vehicle":"Failed · not retried"),result.Success));
            if(history.Count>30)history.RemoveAt(0);
            Render();
        }
        catch(OperationCanceledException) { busy=false; }
        catch(Exception ex)
        {
            busy=false;notice="Could not complete the demo request.";noticeError=true;
            GD.PushError(ex.Message);if(IsInsideTree())Render();
        }
    }
    private void Rect(float x,float y,float w,float h,Color color)
    {
        canvas.AddChild(new ColorRect {Position=new(x,y),Size=new(w,h),Color=color,MouseFilter=MouseFilterEnum.Ignore});
    }
    private PanelArt Panel(float x,float y,float w,float h,Color fill,bool accent=true,bool stripes=false)
    {
        var n=new PanelArt {Position=new(x,y),Size=new(w,h),Fill=fill,Accent=accent,Stripes=stripes};canvas.AddChild(n);return n;
    }
    private Label Label(float x,float y,float w,float h,string text,int size,Color color,bool strong=false,bool wrap=false)
    {
        var n=new Label {Position=new(x,y),Size=new(w,h),Text=text,MouseFilter=MouseFilterEnum.Ignore,VerticalAlignment=VerticalAlignment.Center};
        n.AddThemeFontOverride("font",strong?bold:regular);n.AddThemeFontSizeOverride("font_size",size);n.AddThemeColorOverride("font_color",color);
        if(wrap)n.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        canvas.AddChild(n);return n;
    }
    private static StyleBoxFlat Style(Color bg,Color edge,int width=1)
        =>new(){BgColor=bg,BorderColor=edge,BorderWidthLeft=width,BorderWidthRight=width,BorderWidthTop=width,BorderWidthBottom=width,CornerRadiusTopLeft=2,CornerRadiusTopRight=2,CornerRadiusBottomLeft=2,CornerRadiusBottomRight=2,ContentMarginLeft=6,ContentMarginRight=6};
    private Button Button(float x,float y,float w,float h,string text,int size,Action click,bool primary=false,string name="")
    {
        var n=new Button {Position=new(x,y),Size=new(w,h),Text=text,FocusMode=FocusModeEnum.All,MouseDefaultCursorShape=CursorShape.PointingHand};
        if(name!="")n.Name=name;
        n.AddThemeFontOverride("font",primary?bold:regular);n.AddThemeFontSizeOverride("font_size",size);
        n.AddThemeColorOverride("font_color",primary?Back:White);
        n.AddThemeColorOverride("font_hover_color",primary?Back:White);
        n.AddThemeColorOverride("font_pressed_color",primary?Back:Blue);
        n.AddThemeStyleboxOverride("normal",Style(primary?Blue:new("#0c1c2c"),primary?Blue:new("#31526c")));
        n.AddThemeStyleboxOverride("hover",Style(primary?new("#b0ddff"):new("#18334b"),Blue));
        n.AddThemeStyleboxOverride("pressed",Style(primary?new("#72b6e8"):new("#1d3a53"),Blue));
        n.AddThemeStyleboxOverride("focus",Style(new Color(0,0,0,0),Blue,2));
        n.Pressed+=()=>click();canvas.AddChild(n);return n;
    }
    private void Glyph(float x,float y,string kind,int size)
        =>canvas.AddChild(new Glyph {Position=new(x,y),Size=new(size,size),Kind=kind,Ink=Blue});
    private void Quick(int x,int y,string glyph,string text,Action click,string name)
    {
        Button(x,y,94,86,"",12,click,false,name);
        Glyph(x+33,y+13,glyph,28);
        var label=Label(x,y+48,94,27,text,12,White);
        label.HorizontalAlignment=HorizontalAlignment.Center;
        Rect(x+1,y+1,13,3,Red);
    }
    private async Task Capture(string filename)
    {
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string directory="user://";
        foreach(string arg in OS.GetCmdlineUserArgs())
            if(arg.StartsWith("--smoke-output="))directory=arg.Substring("--smoke-output=".Length).TrimEnd('/','\\')+"/";
        if(GetViewport().GetTexture().GetImage().SavePng(directory+filename)!=Error.Ok)
            throw new Exception("Could not write smoke-test screenshot.");
    }
    private async Task SmokeTest()
    {
        try
        {
            client.LatencyMs=5;
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await Capture("01-dashboard.png");
            canvas.GetNode<Button>("OpenPaint").EmitSignal(BaseButton.SignalName.Pressed);
            await Capture("02-color-picker.png");
            canvas.GetNode<Button>("Paint4").EmitSignal(BaseButton.SignalName.Pressed);
            if(paintHex!="ee632f")throw new Exception("Color selection failed");
            Navigate("Car");await Capture("03-orange.png");
            PickPaint("Phantom Black","202630");await Capture("04-black.png");
            PickPaint("Performance Blue","89b7f0");
            Navigate("Climate");
            canvas.GetNode<Button>("WarmPreset").EmitSignal(BaseButton.SignalName.Pressed);
            if(temperature!=78)throw new Exception("Climate preset failed");
            await Send(new(ActionKind.Climate,temperature,true));
            if(!client.State.ClimateOn||client.State.TargetF!=78)throw new Exception("Climate state failed");
            notice="";Render();await Capture("05-climate.png");
            Navigate("Energy");chargeLimit=90;await Send(new(ActionKind.ChargeLimit,chargeLimit));
            await Send(new(ActionKind.Charge,Enabled:true));
            if(!client.State.Charging)throw new Exception("Charging failed");
            notice="";Render();await Capture("06-energy.png");
            client.FailNext=true;await Send(new(ActionKind.Unlock));
            if(!client.State.Locked)throw new Exception("Failure mutated state");
            Navigate("Activity");await Capture("07-activity.png");
            Navigate("Settings");await Capture("08-settings.png");
            Navigate("Locate");await Capture("09-location.png");
            GD.Print("SMOKE PASS: screens, paint, climate, charging and failure handling.");
            GetTree().Quit();
        }
        catch(Exception ex){GD.PushError("SMOKE FAIL: "+ex);GetTree().Quit(1);}
    }
}
