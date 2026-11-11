using Godot;
using NGarage.Core;
using System.Collections.Generic;

// Appearance: garage name, and each car's name, picture and paint. Names and pictures are saved per
// vehicle (by its account-scoped fingerprint) in preferences on this device.
public partial class Main
{
    private readonly Dictionary<string,(string Model,string Name)> cars=new();
    private CarModel carModel=CarModels.Default;
    private string carName="";
    private Texture2D? carTexture;
    private string CarKey=>linkedVehicle.Length>0?linkedVehicle:"default";
    // The maker is dropped from the header only for your own service's cars ("EV6 GT" on Kia, "Kia EV6" on Hyundai).
    private string CarTitle=>carName.Length>0?carName:ModelName;
    private string ModelName=>carModel.Maker==Brand.Maker?carModel.ShortName:carModel.Name;
    // Only the current picture stays loaded; the picker's thumbnails are released when it closes.
    private Texture2D CarTexture=>carTexture??=CarPicture(carModel);
    private static Texture2D CarPicture(CarModel model)=>GD.Load<Texture2D>("res://Assets/"+model.Picture);
    // Each picture's outside shape, for the charging outline: worked out once, at half size, and kept.
    private readonly Dictionary<string,Texture2D> carMasks=new();
    private Texture2D CarMask(CarModel model)
    {
        if(carMasks.TryGetValue(model.Id,out var known))return known;
        var image=CarPicture(model).GetImage();
        image.Convert(Image.Format.Rgb8);image.Resize(image.GetWidth()/2,image.GetHeight()/2,Image.Interpolation.Bilinear);
        var mask=Silhouette.Mask(image.GetData(),image.GetWidth(),image.GetHeight());
        for(int i=0;i<mask.Length;i++)mask[i]*=255;
        var texture=ImageTexture.CreateFromImage(Image.CreateFromData(image.GetWidth(),image.GetHeight(),false,Image.Format.L8,mask));
        carMasks[model.Id]=texture;return texture;
    }

    private void SelectCar()
    {
        var (model,name)=cars.TryGetValue(CarKey,out var car)?car:(CarModels.Default.Id,"");
        if(model!=carModel.Id)carTexture=null;
        carModel=CarModels.ById(model);carName=name;
    }
    private void SaveCar(string model,string name)
    {
        cars[CarKey]=(model,CarModels.CleanName(name));SelectCar();SavePrefs();
    }
    private void Appearance()
    {
        Title("YOUR GARAGE.","Appearance saved on this device.");
        Label(24,190,412,22,"GARAGE NAME",11,Muted,true);
        var garage=NameInput(214,"GarageName",garageName,"My garage",30);
        Button(314,214,122,42,"Save",13,()=>{garageName=string.IsNullOrWhiteSpace(garage.Text)?"My garage":garage.Text.Trim();SavePrefs();Render();},true,"SaveGarage");
        Label(24,266,412,22,"CAR NAME",11,Muted,true);
        var name=NameInput(290,"CarName",carName,ModelName,CarModels.MaxName);
        Button(314,290,122,42,"Save",13,()=>{SaveCar(carModel.Id,name.Text);notice=carName.Length>0?"Car name saved.":"Car name reset to the model name.";noticeError=false;Render();},true,"SaveCarName");
        Button(24,344,412,44,"Model  /  "+ModelName+"  ›",13,()=>Navigate("Model"),false,"SettingsModel");
        Button(24,396,412,44,"Car color  /  "+paintName+"  ›",13,()=>Navigate("Paint"),false,"SettingsPaint");
        Button(24,448,412,44,"App color  /  "+AppTheme.Name(Skin.Hex)+"  ›",13,()=>Navigate("AppColor"),false,"SettingsAppColor");
        var widget=new CheckBox {Name="WidgetOn",Position=new(24,498),Size=new(412,36),Text=$"Show status in {Brand.App}'s widgets",ButtonPressed=widgetEnabled};
        widget.AddThemeFontOverride("font",regular);widget.AddThemeFontSizeOverride("font_size",13);
        // Off deletes what the widgets show; on fills them again from the last status.
        widget.Toggled+=value=>{widgetEnabled=value;if(!value)ClearWidget();SavePrefs();};
        canvas.AddChild(widget);
        Hero(540,136);
        Button(24,686,412,48,"Back to settings",13,()=>Navigate("Settings"));
    }
    private LineEdit NameInput(int y,string nodeName,string text,string placeholder,int maxLength)
    {
        var input=new LineEdit {Name=nodeName,Position=new(24,y),Size=new(278,42),Text=text,PlaceholderText=placeholder,MaxLength=maxLength};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);canvas.AddChild(input);
        return input;
    }
    // App color: one picked color sets the whole look (Core/AppTheme.cs). Car color has its own screen (Paint).
    private void AppColorScreen()
    {
        Title("APP COLOR.","One color sets the whole look. Saved on this device.");
        Label(24,192,412,30,AppTheme.Name(Skin.Hex).ToUpperInvariant(),20,Blue,true).Name="AppColorName";
        var presets=AppTheme.Presets;
        for(int i=0;i<presets.Count;i++) {
            var (name,hex)=presets[i];
            int x=24+(i%4)*104,y=232+(i/4)*80;
            var b=Button(x,y,96,68,"",12,()=>PickAppColor(hex),false,"AppColor"+i);
            // Swatches show each choice's own colors.
            b.AddChild(new ColorRect {Name="Swatch",Position=new(8,8),Size=new(80,23),Color=new(hex),MouseFilter=MouseFilterEnum.Ignore});
            b.AddChild(new ColorRect {Name="SwatchCard",Position=new(8,31),Size=new(80,4),Color=new(AppTheme.Derive(hex)[ThemeRole.Card]),MouseFilter=MouseFilterEnum.Ignore});
            b.AddChild(SwatchText(name,96,38));
            if(hex==Skin.Hex)b.AddThemeStyleboxOverride("normal",Style(Skin.Selected,Blue,2));
        }
        // Below the last row of presets (two rows for nCTRL's eight, three for gtCTRL's nine).
        int y0=232+(presets.Count+3)/4*80+4;
        Label(24,y0,200,22,"CUSTOM COLOR",11,Muted,true);
        // The picker button shows only its color, so its name sits beside it.
        var picker=new ColorPickerButton {Name="CustomAppColor",Position=new(24,y0+28),Size=new(56,46),Color=new(Skin.Hex),EditAlpha=false};
        Label(90,y0+40,140,22,"Choose color",12,White);
        // Applied when the picker closes: rebuilding the screen while its popup is open crashes Godot.
        string chosen=Skin.Hex;
        picker.ColorChanged+=value=>chosen=value.ToHtml(false);
        picker.PopupClosed+=()=>Callable.From(()=>PickAppColor(chosen)).CallDeferred();
        canvas.AddChild(picker);
        Button(238,y0+28,198,46,"Reset to default",13,()=>PickAppColor(AppTheme.DefaultHex),false,"AppColorReset");
        Label(24,y0+90,412,80,"Buttons, cards, text and the background all follow this color. Red for locked and alerts, yellow for charging and amber for warnings keep their meaning in every color.",
            11,Muted,false,true);
        Button(214,718,222,46,"Done  ›",14,()=>Navigate("Appearance"),true,"AppColorDone");
    }
    private Label SwatchText(string text,int width,int y=26)
    {
        var label=new Label {Position=new(2,y),Size=new(width-4,20),Text=text,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore,ClipText=true};
        label.AddThemeFontOverride("font",regular);label.AddThemeFontSizeOverride("font_size",10);return label;
    }
    private void PickAppColor(string hex)
    {
        Skin.Use(hex);SavePrefs();Render();
    }
    private void ModelPicker()
    {
        Title("PICK YOUR CAR.","Changes the picture only. Status and controls always come from your car.");
        var models=CarModels.Offered;
        for(int i=0;i<models.Count;i++)
        {
            var model=models[i];
            int x=24+(i%3)*142,y=192+(i/3)*104;
            var card=Button(x,y,128,96,"",12,()=>PickModel(model),false,"Model_"+model.Id);
            var thumb=new TextureRect {Position=new(4,4),Size=new(120,56),Texture=model==carModel?CarTexture:CarPicture(model),
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore};
            var material=new ShaderMaterial {Shader=paintShader};material.SetShaderParameter("paint",new Color(paintHex));ThemeBackdrop(material,model);thumb.Material=material;
            card.AddChild(thumb);
            card.AddChild(CardText(60,model.Maker==Brand.Maker?model.ShortName:model.Name,10,White));
            card.AddChild(CardText(76,CarModels.SupportFor(model) switch {CarSupport.Electric=>Brand.Service.ServiceName,CarSupport.Gas=>"Gas · "+Brand.Service.ServiceName,_=>"Picture only"},9,Muted));
            if(model==carModel)card.AddThemeStyleboxOverride("normal",Style(Skin.Selected,Blue,2));
        }
        Button(214,718,222,46,"Done  ›",14,()=>Navigate("Appearance"),true,"ModelDone");
    }
    private Label CardText(int y,string text,int size,Color color)
    {
        var label=new Label {Position=new(4,y),Size=new(120,16),Text=text,HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore,ClipText=true};
        label.AddThemeFontOverride("font",regular);label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color);
        return label;
    }
    private void PickModel(CarModel model)
    {
        SaveCar(model.Id,carName);
        notice=CarModels.SupportFor(model) switch {
            CarSupport.Gas=>model.Name+$" picked. {Brand.App}'s climate and charging controls are built for electric models.",
            CarSupport.PictureOnly=>model.Name+$" picked as a picture. {Brand.App} connects only to {Brand.Service.ServiceName} vehicles.",
            _=>model.Name+" picked."};
        noticeError=false;Render();
    }
}
