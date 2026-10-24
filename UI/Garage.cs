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
    private string CarTitle=>carName.Length>0?carName:carModel.Name;
    // Only the current picture stays loaded; the picker's thumbnails are released when it closes.
    private Texture2D CarTexture=>carTexture??=CarPicture(carModel);
    private static Texture2D CarPicture(CarModel model)=>GD.Load<Texture2D>("res://Assets/"+model.Picture);

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
        var name=NameInput(290,"CarName",carName,carModel.Name,CarModels.MaxName);
        Button(314,290,122,42,"Save",13,()=>{SaveCar(carModel.Id,name.Text);notice=carName.Length>0?"Car name saved.":"Car name reset to the model name.";noticeError=false;Render();},true,"SaveCarName");
        Button(24,346,412,46,"Model  /  "+carModel.Name+"  ›",13,()=>Navigate("Model"),false,"SettingsModel");
        Button(24,402,412,46,"Car color  /  "+paintName+"  ›",13,()=>Navigate("Paint"),false,"SettingsPaint");
        Hero(460,212);
        Button(24,686,412,48,"Back to settings",13,()=>Navigate("Settings"));
    }
    private LineEdit NameInput(int y,string nodeName,string text,string placeholder,int maxLength)
    {
        var input=new LineEdit {Name=nodeName,Position=new(24,y),Size=new(278,42),Text=text,PlaceholderText=placeholder,MaxLength=maxLength};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);canvas.AddChild(input);
        return input;
    }
    private void ModelPicker()
    {
        Title("PICK YOUR CAR.","Changes the picture only. Status and controls always come from your car.");
        var models=CarModels.All;
        for(int i=0;i<models.Count;i++)
        {
            var model=models[i];
            int x=24+(i%3)*142,y=192+(i/3)*104;
            var card=Button(x,y,128,96,"",12,()=>PickModel(model),false,"Model_"+model.Id);
            var thumb=new TextureRect {Position=new(4,4),Size=new(120,56),Texture=model==carModel?CarTexture:CarPicture(model),
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore};
            var material=new ShaderMaterial {Shader=paintShader};material.SetShaderParameter("paint",new Color(paintHex));thumb.Material=material;
            card.AddChild(thumb);
            card.AddChild(CardText(60,model.Name,10,White));
            card.AddChild(CardText(76,model.Support switch {CarSupport.Electric=>"Hyundai USA",CarSupport.Gas=>"Gas · Hyundai USA",_=>"Picture only"},9,Muted));
            if(model==carModel)card.AddThemeStyleboxOverride("normal",Style(new("#142b40"),Blue,2));
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
        notice=model.Support switch {
            CarSupport.Gas=>model.Name+" picked. nCTRL's climate and charging controls are built for electric models.",
            CarSupport.PictureOnly=>model.Name+" picked as a picture. nCTRL connects only to Hyundai USA vehicles.",
            _=>model.Name+" picked."};
        noticeError=false;Render();
    }
}
