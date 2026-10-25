using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;

// The home-screen widget and the Share extension (Native/iOS/Extensions), through the App Group they share
// with the app. The widget gets the car's status and painted picture from here; nothing in it is sent anywhere.
// A place shared to nCTRL from another app opens Send to car, filled in, to confirm.
public partial class Main
{
    private bool widgetEnabled=true;
    private string widgetWritten="",widgetPictureFor="";
    private bool widgetCleared;
    private string? heldShare;
#if DEBUG
    // Smoke tests keep the App Group's files in memory.
    private readonly Dictionary<string,byte[]> sharedFixture=new();
#endif

    private bool WriteShared(string name,byte[]? bytes)
    {
#if DEBUG
        if(smoke){if(bytes==null)sharedFixture.Remove(name);else sharedFixture[name]=bytes;return true;}
#endif
        return NativeVaultBridge.GroupWrite(name,bytes);
    }
    // Called after each update and settings change. Writes only when something the widget shows has changed.
    private void UpdateWidget()
    {
        if(demo)return; // The demo never touches your widget.
        if(!widgetEnabled){if(!widgetCleared)ClearWidget();return;}
        if(!liveMode||CurrentState is not {} state)return;
        widgetCleared=false;
        var status=WidgetStatus.From(state,CarTitle,units,coldRange,outside?.TemperatureF,AppTheme.Derive(Skin.Hex),DateTimeOffset.UtcNow,TimeZoneInfo.Local);
        string key=carModel.Id+"/"+paintHex;
        if(key!=widgetPictureFor&&WriteShared("car.png",WidgetPicture()))widgetPictureFor=key;
        string json=status.Encode();
        if(json!=widgetWritten&&WriteShared("status.json",System.Text.Encoding.UTF8.GetBytes(json)))widgetWritten=json;
    }
    private void ClearWidget()
    {
        WriteShared("status.json",null);WriteShared("car.png",null);
        widgetWritten="";widgetPictureFor="";widgetCleared=true;
    }
    // The car picture in its paint (the same rule as Assets/Paint.gdshader), with everything around the car's
    // shape transparent so the widget's own background shows.
    private byte[] WidgetPicture()
    {
        var image=(Image)CarPicture(carModel).GetImage().Duplicate();
        image.Convert(Image.Format.Rgba8);
        int width=image.GetWidth()/4,height=image.GetHeight()/4;
        image.Resize(width,height,Image.Interpolation.Bilinear);
        var mask=CarMask(carModel).GetImage();mask.Resize(width,height,Image.Interpolation.Bilinear);
        var paint=new Color(paintHex);
        static float Step(float a,float b,float x)=>Mathf.SmoothStep(a,b,x);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            var c=image.GetPixel(x,y);
            float bright=Mathf.Max(c.R,Mathf.Max(c.G,c.B));
            float body=Step(0.065f,0.13f,c.B-c.R)*Step(0.28f,0.48f,bright)*(1-Step(0.05f,0.13f,c.G-c.B));
            var tinted=paint*Mathf.Clamp(bright/0.94f,0,1.15f);
            tinted=tinted.Lerp(new Color(0.92f,0.92f,0.92f),Step(0.91f,1,bright)*0.27f);
            var color=c.Lerp(tinted,body);
            image.SetPixel(x,y,new Color(color.R,color.G,color.B,mask.GetPixel(x,y).R>0.5f?1:0));
        }
        var used=image.GetUsedRect();
        return (used.Size.X>0?image.GetRegion(used):image).SavePngToBuffer();
    }

    // A place shared from another app: opens Send to car with it (after signing in, if the app isn't open yet).
    private void CheckShared()
    {
        string json=heldShare??TakeShared();heldShare=null;
        if(json.Length==0)return;
        if(!liveMode||DisclaimerRequired) {
            if(vault.HasAccount&&!DisclaimerRequired){heldShare=json;return;} // Picked up once the car is open.
            notice="Open your car in nCTRL first, then share the place again.";noticeError=true;Render();return;
        }
        var (place,lookup,name)=Destinations.FromShare(json);
        pendingDestination=null;destinationText="";sentDestination=null;screen="Send";notice="";noticeError=false;
        if(place!=null){pendingDestination=place;Render();_ = LoadDestinationMap(place);}
        else if(lookup.Length>0){destinationText=lookup;Render();_ = FindDestination(lookup,name);}
        else {notice="nCTRL couldn't find a place in what was shared.";noticeError=true;Render();}
    }
    private string TakeShared()
    {
#if DEBUG
        if(smoke){if(!sharedFixture.Remove("share.json",out var bytes))return "";return System.Text.Encoding.UTF8.GetString(bytes);}
#endif
        return NativeVaultBridge.TakeShare();
    }
}
