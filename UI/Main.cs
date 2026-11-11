using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Globalization;

public partial class Main : Control
{
    // Theme colors follow the chosen app color (UI/Skin.cs); red and yellow carry a meaning and stay fixed.
    private static Color Blue=>Skin.Accent;
    private static Color White=>Skin.Text;
    private static Color Muted=>Skin.Muted;
    private static Color Back=>Skin.Back;
    private static readonly Color Red = new("#f24a4d"), Yellow = new("#ffd129");
    private readonly List<(string Title,string Detail,bool Ok)> history = new();
    private Control canvas = null!;
    private Font regular = null!, bold = null!;
    // Large bold text (titles, the car name, battery and range): a heavier weight than small bold labels. With the
    // bundled font it's the same Bold.
    private Font display = null!;
    // The smallest text size on screen, in canvas units (0: as designed).
    private int textFloor;
    // The system UI font: San Francisco on Apple devices (Apple doesn't allow bundling it, but apps may use it).
    private void UseSystemFont()
    {
        // ".AppleSystemUIFont" is how Apple platforms name San Francisco; others fall back to their own sans-serif.
        string[] names = { ".AppleSystemUIFont", "SF Pro Text", "SF Pro", "sans-serif" };
        regular = new SystemFont { FontNames = names, FontWeight = 400, AllowSystemFallback = true };
        // Bold for small labels, Heavy for large titles and numbers (close to the original's weight). Godot finds no
        // semibold cut of the system font, so labels use Bold.
        bold = new SystemFont { FontNames = names, FontWeight = 700, AllowSystemFallback = true };
        display = new SystemFont { FontNames = names, FontWeight = 800, AllowSystemFallback = true };
#if DEBUG
        GD.Print($"FONT regular={regular.GetFontName()} w{regular.GetFontWeight()} bold={bold.GetFontName()} w{bold.GetFontWeight()}");
#endif
    }
    // Raises every text size below the floor, on every control just drawn.
    private void ApplyTextFloor(Node node)
    {
        if (node is Control c && c.HasThemeFontSizeOverride("font_size") && c.GetThemeFontSize("font_size") < textFloor)
            c.AddThemeFontSizeOverride("font_size", textFloor);
        foreach (var child in node.GetChildren()) ApplyTextFloor(child);
    }
    private Shader paintShader = null!, chargeOutline = null!;
    private string screen = "Connect", paintName = Brand.Flavor.DefaultPaintName, paintHex = Brand.Flavor.DefaultPaintHex;
    private string garageName = "My garage", notice = "";
    private bool noticeError;
    private string linkedVehicle="",accountKey="";
    private const string Prefs = "user://preferences.cfg";
    private bool smoke=false;
    // Paint presets for the picture's maker (Core/CarPaints.cs).
    private IReadOnlyList<(string Name,string Hex)> paints=>CarPaints.For(carModel);

    public override void _Ready()
    {
        regular = GD.Load<Font>("res://Assets/Body.ttf");
        bold = GD.Load<Font>("res://Assets/Bold.ttf");
        display = bold;
#if DEBUG
        // Typography mockups: --font=system uses the phone's own UI font (San Francisco on iPhone and Mac);
        // --text-floor=N raises every smaller text size to N canvas units.
        if (Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--font=system")) UseSystemFont();
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--display-weight=") && int.TryParse(arg["--display-weight=".Length..], out int weight) && regular is SystemFont)
                display = new SystemFont { FontNames = ((SystemFont)regular).FontNames, FontWeight = weight, AllowSystemFallback = true };
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--text-floor=") && int.TryParse(arg["--text-floor=".Length..], out int floor)) textFloor = floor;
#endif
        paintShader = GD.Load<Shader>("res://Assets/Paint.gdshader");
        chargeOutline = GD.Load<Shader>("res://Assets/ChargeOutline.gdshader");
#if DEBUG
        liveSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--live-smoke-test");
        vaultSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--secure-smoke-test");
        kiaSmoke = Array.Exists(OS.GetCmdlineUserArgs(), x => x == "--kia-smoke-test");
        liveSmoke |= vaultSmoke || kiaSmoke;
        smoke = liveSmoke;
#endif
        // The build's flavor: gtCTRL exports carry the "gtctrl" feature tag; desktop runs can pass --flavor=gtctrl.
        Brand.Use(OS.HasFeature("gtctrl")||Array.Exists(OS.GetCmdlineUserArgs(),x=>x=="--flavor=gtctrl")?Brand.GtCtrl:Brand.NCtrl);
        paintName=Brand.Flavor.DefaultPaintName;paintHex=Brand.Flavor.DefaultPaintHex;Skin.Use(AppTheme.DefaultHex);SelectCar();
        // gtCTRL: San Francisco (Heavy titles, Bold labels) at Apple's minimum text size.
        if (Brand.Flavor.SystemFont) UseSystemFont();
        if (Brand.Flavor.TextFloor > textFloor) textFloor = Brand.Flavor.TextFloor;
        if (!smoke) { LoadPrefs(); LoadSnapshot(); KeepFilesPrivate(); }
#if DEBUG
        budget = new RequestBudget(smoke ? new MemoryBudgetStore() : LimitsStore(), null, smoke ? SmokeLimits : null);
#else
        budget = new RequestBudget(LimitsStore());
#endif
        connectionFactory = () => NewConnection();
#if DEBUG
        weather = smoke ? new OutsideTemperature(weatherFixture = new WeatherFixture()) : new OutsideTemperature();
#else
        weather = new OutsideTemperature();
#endif
        InitializeVault();
        // Cold launch: open straight to the Car screen with the last saved status, if any.
        EnterOffline();
        _ = LoadPlaces();
        _ = LoadShared();
        // Smoke tests call the check themselves.
        if(!smoke){var chargeTimer=new Timer {WaitTime=30,Autostart=true};chargeTimer.Timeout+=CheckChargeOverdue;AddChild(chargeTimer);CallDeferred(nameof(ReadOnOpen));CallDeferred(nameof(CheckShared));CallDeferred(nameof(UpdateWidget));_ = UpdateOutsideTemperature();}
        Render();
#if DEBUG
        if (kiaSmoke) _ = KiaSmokeTest();
        else if (vaultSmoke) _ = SecureSmokeTest();
        else if (liveSmoke) _ = LiveSmokeTest();
#endif

    }

    public override void _ExitTree() { liveEpoch++; vault.Cancel(); liveLifetime.Cancel(); connection?.Dispose(); DiscardParked(); liveLifetime.Dispose(); weather.Dispose(); }
    private void LoadPrefs()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Prefs) == Error.Ok) ApplyPrefs(cfg);
    }
    private void ApplyPrefs(ConfigFile cfg)
    {
        // The service first: the disclaimer acceptance below is checked against it.
        Brand.ServiceId=cfg.GetValue("connection","service",Brand.ServiceId).AsString();
        string hex = cfg.GetValue("appearance","paint",Brand.Flavor.DefaultPaintHex).AsString();
        if (Color.HtmlIsValid(hex)) paintHex = hex;
        paintName = cfg.GetValue("appearance","name",Brand.Flavor.DefaultPaintName).AsString();
        if (paintName == "Cyber Gray") { paintName="Ecotonic Gray"; paintHex="747c79"; }
        garageName = cfg.GetValue("appearance","garage","My garage").AsString();
        Skin.Use(cfg.GetValue("appearance","theme",AppTheme.DefaultHex).AsString());
        widgetEnabled=cfg.GetValue("appearance","widget",true).AsBool();
        linkedVehicle=cfg.GetValue("connection","vehicle_fingerprint","").AsString();
        kiaDeviceId=cfg.GetValue("connection","kia_device","").AsString();
        detailsReadable=cfg.GetValue("details","readable",true).AsBool();
        cars.Clear();
        if(cfg.HasSection("cars"))foreach(string key in cfg.GetSectionKeys("cars")) {
            var car=cfg.GetValue("cars",key).AsStringArray();
            if(car.Length==2)cars[key]=(car[0],CarModels.CleanName(car[1]));
        }
        SelectCar();
        driverSeat=SeatPreference(cfg.GetValue("climate","driver_seat",0).AsInt32());
        passengerSeat=SeatPreference(cfg.GetValue("climate","passenger_seat",0).AsInt32());
        wheelHeat=cfg.GetValue("climate","steering_wheel",false).AsBool();
        climateDefrost=cfg.GetValue("climate","defrost",false).AsBool();
        climateOffer=cfg.GetValue("climate","offer_after_send",true).AsBool();
        warmPreset=LoadPreset(cfg,"preset_warm",ClimatePreset.WarmUp);coolPreset=LoadPreset(cfg,"preset_cool",ClimatePreset.CoolDown);
        units=Units.Parse(cfg.GetValue("units","system","imperial").AsString());
        var cold=ColdRangeSettings.Default;
        coldRange=new ColdRangeSettings(cfg.GetValue("cold_range","enabled",cold.Enabled).AsBool(),cfg.GetValue("cold_range","start_f",cold.StartF).AsDouble(),
            cfg.GetValue("cold_range","coldest_f",cold.ColdestF).AsDouble(),cfg.GetValue("cold_range","loss_percent",cold.LossPercent).AsInt32()).Valid();
        disclaimer=Disclaimer.Restore(cfg.GetValue("disclaimer","version",0).AsInt32(),
            cfg.GetValue("disclaimer","account","").AsString(),cfg.GetValue("disclaimer","accepted_at","").AsString());
    }
    private void SavePrefs()
    {
        // The demo saves nothing; its changes are undone when it ends.
        if(!demo)UpdateWidget();
        if(smoke||demo) return;
        if(PrefsConfig().Save(Prefs) != Error.Ok) { notice = "Could not save preferences on this device."; noticeError=true; }
    }
    private ConfigFile PrefsConfig()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("appearance","paint",paintHex);
        cfg.SetValue("appearance","name",paintName);
        cfg.SetValue("appearance","garage",garageName);
        cfg.SetValue("appearance","theme",Skin.Hex);
        cfg.SetValue("appearance","widget",widgetEnabled);
        cfg.SetValue("connection","vehicle_fingerprint",linkedVehicle);
        cfg.SetValue("connection","service",Brand.ServiceId);
        cfg.SetValue("details","readable",detailsReadable);
        if(kiaDeviceId.Length>0)cfg.SetValue("connection","kia_device",kiaDeviceId);
        foreach(var (key,car) in cars)cfg.SetValue("cars",key,new[]{car.Model,car.Name});
        // Your own climate choices, not the car's settings the Climate page may be showing.
        var (_,driver,passenger,wheel,front)=climateBeforeSync??(liveTemperature,driverSeat,passengerSeat,wheelHeat,climateDefrost);
        cfg.SetValue("climate","driver_seat",(int)driver);
        cfg.SetValue("climate","passenger_seat",(int)passenger);
        cfg.SetValue("climate","steering_wheel",wheel);
        cfg.SetValue("climate","defrost",front);
        cfg.SetValue("climate","offer_after_send",climateOffer);
        foreach(var (section,preset) in new[]{("preset_warm",warmPreset),("preset_cool",coolPreset)}) {
            cfg.SetValue(section,"temperature",preset.TemperatureF);cfg.SetValue(section,"driver_seat",(int)preset.DriverSeat);
            cfg.SetValue(section,"passenger_seat",(int)preset.PassengerSeat);cfg.SetValue(section,"steering_wheel",preset.SteeringWheel);
            cfg.SetValue(section,"defrost",preset.Defrost);
        }
        cfg.SetValue("units","system",Units.Name(units));
        cfg.SetValue("cold_range","enabled",coldRange.Enabled);cfg.SetValue("cold_range","start_f",coldRange.StartF);
        cfg.SetValue("cold_range","coldest_f",coldRange.ColdestF);cfg.SetValue("cold_range","loss_percent",coldRange.LossPercent);
        if(disclaimer!=null) {
            cfg.SetValue("disclaimer","version",disclaimer.Version);
            cfg.SetValue("disclaimer","account",disclaimer.AccountKey);
            cfg.SetValue("disclaimer","accepted_at",disclaimer.AcceptedAt.ToString("O",CultureInfo.InvariantCulture));
        }
        return cfg;
    }
    private static ClimatePreset LoadPreset(ConfigFile cfg,string section,ClimatePreset defaults)=>!cfg.HasSection(section)?defaults:
        defaults.With(cfg.GetValue(section,"temperature",defaults.TemperatureF).AsInt32(),cfg.GetValue(section,"driver_seat",(int)defaults.DriverSeat).AsInt32(),
            cfg.GetValue(section,"passenger_seat",(int)defaults.PassengerSeat).AsInt32(),cfg.GetValue(section,"steering_wheel",defaults.SteeringWheel).AsBool(),
            cfg.GetValue(section,"defrost",defaults.Defrost).AsBool());
    private static SeatMode SeatPreference(int stored)=>Enum.IsDefined(typeof(SeatMode),stored)?(SeatMode)stored:SeatMode.Off;
    private void PickPaint(string name,string hex)
    {
        paintName = name; paintHex = hex; SavePrefs(); UpdateAppIcon(); Render();
    }
    // gtCTRL: the app icon follows the car's paint (the nearest preset's icon). iOS shows its own notice on a change.
    private void UpdateAppIcon()
    {
        if(Brand.Flavor.PaintIcons&&!smoke)NativeVaultBridge.SetAppIcon(CarPaints.IconFor(paintHex));
    }
    private void Navigate(string target) { screen=target; notice=""; Render(); }

    private void Render()
    {
        // Until a real enrollment is selected, every route stays on the account flow (the one-time code is part of it).
        if(!liveMode)screen=screen=="Code"&&pendingCode!=null?"Code":"Connect";
        if(canvas != null) { RemoveChild(canvas); canvas.QueueFree(); }
        canvas = new Control { Name="Canvas", Size=new Vector2(460,920) };
        AddChild(canvas);
        Rect(0,0,460,920,Back);
        // Asymmetric motorsport detail; original artwork and layout.
        var header=Panel(0,0,460,96,Skin.Header,false,true);header.BottomEdgeOnly=true;
        Label(24,19,310,20,garageName,13,Muted);
        Label(24,44,340,41,liveMode?CarTitle:$"{Brand.App}",liveMode&&CarTitle.Length>14?23:29,White,true).Name="CarTitle";
        Button(365,20,70,28,demo?"DEMO":smoke?"TEST":liveMode?"LIVE":"LOCKED",11,()=>Navigate(liveMode?"Settings":"Connect"),false,"ConnectionBadge");
        int content=canvas.GetChildCount();
        // Nothing else is reachable until the current account has accepted the risk disclaimer.
        if(DisclaimerRequired) DisclaimerScreen();
        else if(screen=="Connect") ConnectionScreen();
        else if(screen=="Code") CodeScreen();
        else if(screen=="Paint") Paint();
        else if(screen=="Appearance") Appearance();
        else if(screen=="Model") ModelPicker();
        else if(screen=="Disclaimer") DisclaimerView();
        else if(screen=="Limits") LimitsScreen();
        else if(screen=="Places") PlacesScreen();
        else if(screen=="Range") RangeScreen();
        else if(screen=="AppColor") AppColorScreen();
        else if(screen=="Notify") AlertsScreen();
        else LiveScreen();
        PullLayer(content);
        if(liveMode)Nav();
        if(notice.Length>0)
        {
            Rect(20,778,420,65,noticeError?new("#47232a"):Skin.Notice);
            Label(31,784,397,53,notice,11,White,false,true);
        }
        if(pendingOverride!=null)OverrideDialog();
        else if(pendingConfirm!=null)ConfirmDialog();
        else if(pendingPlaceAt!=null)PlaceNameDialog();
        else if(sentDestination!=null)SentDialog();
        if (textFloor > 0) ApplyTextFloor(canvas);
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
        Panel(20,y,420,height,Skin.Hero,false);
        var tex = new TextureRect {
            Name="CarArtwork", Position=new Vector2(24,y+4),Size=new Vector2(412,height-8),
            Texture=CarTexture, ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter=TextureFilterEnum.Nearest, MouseFilter=MouseFilterEnum.Ignore
        };
        var material = new ShaderMaterial { Shader=paintShader };
        material.SetShaderParameter("paint",new Color(paintHex));ThemeBackdrop(material,carModel);
        tex.Material=material; canvas.AddChild(tex);
    }
    // Turns a car picture's backdrop to the app color (nothing to do for the default).
    private void ThemeBackdrop(ShaderMaterial material,CarModel model)
    {
        var (shift,vivid)=AppTheme.Shift(Skin.Hex);
        if(shift==0&&vivid==1)return;
        material.SetShaderParameter("car_mask",CarMask(model));
        material.SetShaderParameter("hue_shift",(float)(shift/360));material.SetShaderParameter("vivid",(float)vivid);
    }
    private void Paint()
    {
        Label(24,108,400,34,"MAKE IT YOURS.",25,White,true);
        Label(24,148,410,24,"Choose the paint for your pixel "+carModel.Name+".",12,Muted);
        Hero(185,215);
        Label(24,419,410,30,paintName.ToUpperInvariant(),20,Blue,true);
        Label(24,455,410,30,carModel.Id=="ev6gt"?"Wheels, glass, lights and neon calipers stay as they are.":"Wheels, glass, lights and trim stay as they are.",11,Muted);
        for(int i=0;i<paints.Count;i++)
        {
            var selected=paints[i];
            // Three wide columns for up to six presets; four narrower ones beyond that.
            int cols=paints.Count>6?4:3,w=cols==4?98:128,step=cols==4?104:142,h=cols==4?62:68;
            int x=24+(i%cols)*step,y=501+(i/cols)*(h+12);
            var b=Button(x,y,w,h,"",12,()=>PickPaint(selected.Name,selected.Hex),false,"Paint"+i);
            var swatch=new ColorRect {Position=new(8,8),Size=new(w-16,21),Color=new(selected.Hex),MouseFilter=MouseFilterEnum.Ignore};
            b.AddChild(swatch);
            var text=new Label {Position=new(2,33),Size=new(w-4,24),Text=selected.Name,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore,ClipText=true};
            // Long names on narrow tiles drop their last word ("Aurora Black Pearl" reads "Aurora Black").
            if(cols==4&&regular.GetStringSize(selected.Name,HorizontalAlignment.Left,-1,Math.Max(10,textFloor)).X>w-8)
                text.Text=selected.Name[..selected.Name.LastIndexOf(' ')];
            text.AddThemeFontOverride("font",regular);text.AddThemeFontSizeOverride("font_size",10);b.AddChild(text);
            if(selected.Hex==paintHex) b.AddThemeStyleboxOverride("normal",Style(Skin.Selected,Blue,2));
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
        picker.PopupClosed += ()=> { SavePrefs(); UpdateAppIcon(); Callable.From(Render).CallDeferred(); };
        canvas.AddChild(picker);
        Button(214,714,222,46,"Done  ›",14,()=>Navigate("Appearance"),true,"PaintDone");
        if(notice.Length==0) Label(24,794,410,40,"Preview colors are illustrative, not factory paint matching.",11,Muted,false,true);
    }
    private void Nav()
    {
        Rect(0,851,460,69,Skin.Nav);Rect(0,851,460,1,Skin.NavLine);
        string[] names={"Car","Energy","Details","Activity","Settings"};
        string selected=screen is "Appearance" or "Paint" or "Model" or "Disclaimer" or "Limits" or "Places" or "Range" or "AppColor" or "Notify"?"Settings":screen is "Climate" or "Locate" or "Alerts" or "Look" or "Send"?"Car":screen;
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];int x=i*92;
            var b=Button(x,856,92,64,"",12,()=>Navigate(name),false,"Nav"+name);
            b.AddThemeStyleboxOverride("normal",Style(Skin.Nav,Skin.Nav));
            var g=new Glyph {Kind=name,Ink=name==selected?Blue:Muted,Position=new(33,5),Size=new(26,26)};b.AddChild(g);
            var l=new Label {Position=new(0,35),Size=new(92,22),Text=name,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            l.AddThemeFontOverride("font",regular);l.AddThemeFontSizeOverride("font_size",11);l.AddThemeColorOverride("font_color",name==selected?Blue:Muted);b.AddChild(l);
            if(name==selected) Rect(x+20,917,52,3,Skin.Detail);
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
        n.AddThemeFontOverride("font",!strong?regular:size>=20?display:bold);n.AddThemeFontSizeOverride("font_size",size);n.AddThemeColorOverride("font_color",color);
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
        n.AddThemeStyleboxOverride("normal",Style(primary?Blue:Skin.Button,primary?Blue:Skin.ButtonEdge));
        n.AddThemeStyleboxOverride("hover",Style(primary?Skin.AccentHover:Skin.ButtonHover,Blue));
        n.AddThemeStyleboxOverride("pressed",Style(primary?Skin.AccentPressed:Skin.ButtonPressed,Blue));
        n.AddThemeStyleboxOverride("focus",Style(new Color(0,0,0,0),Blue,2));
        n.Pressed+=()=>click();canvas.AddChild(n);return n;
    }
    private void Glyph(float x,float y,string kind,int size)
        =>canvas.AddChild(new Glyph {Position=new(x,y),Size=new(size,size),Kind=kind,Ink=Blue});
}
