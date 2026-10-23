using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

public partial class Main : Control
{
    private static readonly Color Blue = new("#8ecbfa"), White = new("#eef5fb"),
        Muted = new("#9ab0c6"), Red = new("#f24a4d"), Back = new("#08121d");
    private readonly List<(string Title,string Detail,bool Ok)> history = new();
    private Control canvas = null!;
    private Font regular = null!, bold = null!;
    private Texture2D car = null!;
    private Shader paintShader = null!;
    private string screen = "Connect", paintName = "Performance Blue", paintHex = "89b7f0";
    private string garageName = "My garage", notice = "";
    private bool noticeError;
    private string linkedVehicle="",accountKey="";
    private const string Prefs = "user://preferences.cfg";
    private bool smoke=false;
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
#if DEBUG
        liveSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--live-smoke-test");
        vaultSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--secure-smoke-test");
        liveSmoke |= vaultSmoke;
        smoke = liveSmoke;
#endif
        if (!smoke) { LoadPrefs(); LoadSnapshot(); }
#if DEBUG
        budget = new RequestBudget(smoke ? new MemoryBudgetStore() : new FileBudgetStore(LimitsFile), null, smoke ? SmokeLimits : null);
#else
        budget = new RequestBudget(new FileBudgetStore(LimitsFile));
#endif
        connectionFactory = () => new HyundaiUsClient(budget);
        InitializeVault();
        // Cold launch: open straight to the Car screen with the last saved status, if any.
        EnterOffline();
        Render();
#if DEBUG
        if (vaultSmoke) _ = SecureSmokeTest();
        else if (liveSmoke) _ = LiveSmokeTest();
#endif

    }

    public override void _ExitTree() { liveEpoch++; vault.Cancel(); liveLifetime.Cancel(); connection?.Dispose(); DiscardParked(); liveLifetime.Dispose(); }
    private void LoadPrefs()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Prefs) != Error.Ok) return;
        string hex = cfg.GetValue("appearance","paint","89b7f0").AsString();
        if (Color.HtmlIsValid(hex)) paintHex = hex;
        paintName = cfg.GetValue("appearance","name","Performance Blue").AsString();
        if (paintName == "Cyber Gray") { paintName="Ecotonic Gray"; paintHex="747c79"; }
        garageName = cfg.GetValue("appearance","garage","My garage").AsString();
        linkedVehicle=cfg.GetValue("connection","vehicle_fingerprint","").AsString();
        driverSeat=SeatPreference(cfg.GetValue("climate","driver_seat",0).AsInt32());
        passengerSeat=SeatPreference(cfg.GetValue("climate","passenger_seat",0).AsInt32());
        wheelHeat=cfg.GetValue("climate","steering_wheel",false).AsBool();
        disclaimer=Disclaimer.Restore(cfg.GetValue("disclaimer","version",0).AsInt32(),
            cfg.GetValue("disclaimer","account","").AsString(),cfg.GetValue("disclaimer","accepted_at","").AsString());
    }
    private void SavePrefs()
    {
        if(smoke) return;
        var cfg = new ConfigFile();
        cfg.SetValue("appearance","paint",paintHex);
        cfg.SetValue("appearance","name",paintName);
        cfg.SetValue("appearance","garage",garageName);
        cfg.SetValue("connection","vehicle_fingerprint",linkedVehicle);
        cfg.SetValue("climate","driver_seat",(int)driverSeat);
        cfg.SetValue("climate","passenger_seat",(int)passengerSeat);
        cfg.SetValue("climate","steering_wheel",wheelHeat);
        if(disclaimer!=null) {
            cfg.SetValue("disclaimer","version",disclaimer.Version);
            cfg.SetValue("disclaimer","account",disclaimer.AccountKey);
            cfg.SetValue("disclaimer","accepted_at",disclaimer.AcceptedAt.ToString("O",CultureInfo.InvariantCulture));
        }
        if(cfg.Save(Prefs) != Error.Ok) { notice = "Could not save preferences on this device."; noticeError=true; }
    }
    private static SeatMode SeatPreference(int stored)=>Enum.IsDefined(typeof(SeatMode),stored)?(SeatMode)stored:SeatMode.Off;
    private void PickPaint(string name,string hex)
    {
        paintName = name; paintHex = hex; SavePrefs(); Render();
    }
    private void Navigate(string target) { screen=target; notice=""; Render(); }

    private void Render()
    {
        // Until a real enrollment is selected, every route stays on the account flow.
        if(!liveMode)screen="Connect";
        if(canvas != null) { RemoveChild(canvas); canvas.QueueFree(); }
        canvas = new Control { Name="Canvas", Size=new Vector2(460,920) };
        AddChild(canvas);
        Rect(0,0,460,920,Back);
        // N-inspired asymmetric motorsport detail; original artwork and layout.
        var header=Panel(0,0,460,96,new("#0c1825"),false,true);header.BottomEdgeOnly=true;
        Label(24,19,310,20,garageName,13,Muted);
        Label(24,44,340,41,liveMode?"IONIQ 5 N":"nCTRL",29,White,true);
        Button(365,20,70,28,smoke?"TEST":liveMode?"LIVE":"LOCKED",11,()=>Navigate(liveMode?"Settings":"Connect"),false,"ConnectionBadge");
        // Nothing else is reachable until the current account has accepted the risk disclaimer.
        if(DisclaimerRequired) DisclaimerScreen();
        else if(screen=="Connect") ConnectionScreen();
        else if(screen=="Paint") Paint();
        else if(screen=="Appearance") Appearance();
        else if(screen=="Disclaimer") DisclaimerView();
        else if(screen=="Limits") LimitsScreen();
        else LiveScreen();
        if(liveMode)Nav();
        if(notice.Length>0)
        {
            Rect(20,778,420,65,noticeError?new("#47232a"):new("#183749"));
            Label(31,784,397,53,notice,11,White,false,true);
        }
        if(pendingOverride!=null)OverrideDialog();
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
        // Rebuild on the next frame: freeing the picker while its popup is still closing crashes Godot.
        picker.PopupClosed += ()=> { SavePrefs(); Callable.From(Render).CallDeferred(); };
        canvas.AddChild(picker);
        Button(214,714,222,46,"Done  ›",14,()=>Navigate("Appearance"),true,"PaintDone");
        if(notice.Length==0) Label(24,794,410,40,"Preview colors are illustrative, not factory paint matching.",11,Muted,false,true);
    }
    private void Appearance()
    {
        Title("YOUR GARAGE.","Appearance saved on this device.");
        Label(24,204,412,22,"GARAGE NAME",11,Muted,true);
        var input=new LineEdit {Name="GarageName",Position=new(24,238),Size=new(278,44),Text=garageName,MaxLength=30};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);canvas.AddChild(input);
        Button(314,238,122,44,"Save",13,()=>{garageName=string.IsNullOrWhiteSpace(input.Text)?"My garage":input.Text.Trim();SavePrefs();Render();},true,"SaveGarage");
        Button(24,306,412,51,"Car color  /  "+paintName+"  ›",13,()=>Navigate("Paint"),false,"SettingsPaint");
        Hero(390,215);
        Button(24,686,412,48,"Back to settings",13,()=>Navigate("Settings"));
    }
    private void Nav()
    {
        Rect(0,851,460,69,new("#0b1520"));Rect(0,851,460,1,new("#294053"));
        string[] names={"Car","Energy","Details","Activity","Settings"};
        string selected=screen is "Appearance" or "Paint" or "Disclaimer" or "Limits"?"Settings":screen is "Climate" or "Locate"?"Car":screen;
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];int x=i*92;
            var b=Button(x,856,92,64,"",12,()=>Navigate(name),false,"Nav"+name);
            b.AddThemeStyleboxOverride("normal",Style(new("#0b1520"),new("#0b1520")));
            var g=new Glyph {Kind=name,Ink=name==selected?Blue:Muted,Position=new(33,5),Size=new(26,26)};b.AddChild(g);
            var l=new Label {Position=new(0,35),Size=new(92,22),Text=name,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            l.AddThemeFontOverride("font",regular);l.AddThemeFontSizeOverride("font_size",11);l.AddThemeColorOverride("font_color",name==selected?Blue:Muted);b.AddChild(l);
            if(name==selected) Rect(x+20,917,52,3,Red);
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
}
