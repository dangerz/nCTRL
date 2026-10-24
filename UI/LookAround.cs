using Godot;
using NGarage.Core;
using System;
using System.Threading.Tasks;

// Surround View: one composite picture from the car's cameras. Kept in memory only and cleared when
// the app goes to the background; it reaches Photos only when the user taps Save.
public partial class Main
{
    private SurroundView? lookView;
    private Texture2D? lookTexture;
    private int lookChecks;
    private bool lookCapturing;
    // Swappable so the smoke test can save without the iOS photo library.
    private Func<byte[],Task<string>> savePhoto=SaveToPhotos;

    private void LookAround()
    {
        Title("LOOK AROUND.","Surround View from the car's cameras");
        if(lookCapturing) {
            Label(24,220,412,90,$"Waiting for the car's cameras… check {lookChecks} of {HyundaiUsClient.SurroundViewChecks}. This can take up to two minutes.",15,White,false,true).Name="LookProgress";
        } else if(lookTexture!=null&&lookView!=null) {
            Panel(20,192,420,320,new("#101820"),false);
            canvas.AddChild(new TextureRect {Name="LookPicture",Position=new(24,196),Size=new(412,312),Texture=lookTexture,
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});
            Label(24,520,412,22,"Captured: "+ReportedTime(lookView.CapturedAt),11,Muted);
            LiveButton(24,552,198,46,NativeVaultBridge.CanSavePhotos||smoke?"Save to Photos":"Save (iPhone only)",13,()=>_ = SaveLookPicture(),true,"LookSave")
                .Disabled|=!NativeVaultBridge.CanSavePhotos&&!smoke;
            LiveButton(238,552,198,46,"Capture again",13,ConfirmLook,false,"LookCapture");
        } else {
            Label(24,206,412,120,"Ask the car to take a Surround View picture of where it's parked. It wakes the car and runs its cameras, and Hyundai can take up to two minutes to send the picture.",14,White,false,true);
            LiveButton(24,340,412,52,"Capture",15,ConfirmLook,true,"LookCapture");
        }
        Label(24,612,412,90,"Pictures stay in this app's memory until you save one, and are cleared when you leave the app. "+
            "Limited to one capture every 5 minutes and 6 a day. Surround View may not be available for every vehicle or account.",11,Muted,false,true);
        Button(24,714,412,46,"Back to location",13,()=>Navigate("Locate"),false,"LookBack");
    }
    private void ConfirmLook()
    {
        if(liveBusy)return;
        string battery=HealthAlerts.Low12V(CurrentState)?$" The 12-volt battery is at {CurrentState!.Health!.Battery12V}%, and a weak 12-volt battery can stop the car from starting.":"";
        pendingConfirm=new("LOOK AROUND?","This wakes the car and uses its 12-volt battery to run the cameras. The pictures may show people and license plates nearby."+battery,
            "Capture",CaptureLook);
        Render();
    }
    private Task CaptureLook()=>RunLive(async()=>{
        int version=liveEpoch;
        var (active,_)=await AcquireSession(version);
        if(active==null)return;
        lookChecks=0;lookCapturing=true;Render();
        try {
            var view=await active.CaptureSurroundViewAsync(n=>{if(version!=liveEpoch)return;lookChecks=n;if(IsInsideTree())Render();},liveLifetime.Token);
            EnsureLive(version);
            var texture=PictureTexture(view.Image);
            if(texture==null)throw new BluelinkException("Surround View: the picture arrived in a format nCTRL can't show.");
            lookView=view;lookTexture=texture;notice="Surround View captured.";noticeError=false;
        }
        catch(BluelinkException ex) when(ex is not RequestLimitException&&ex.HttpStatus is >=400 and <500&&ex.HttpStatus is not (401 or 403 or 429)) {
            throw new BluelinkException(ex.Message+" Surround View may not be available for this vehicle or account.",ex.HttpStatus,ex.ApiErrorCode,ex.ApiSubCode);
        }
        finally {if(version==liveEpoch)lookCapturing=false;}
    },NeedsFaceId?"Waiting for "+vault.BiometricName+"…":"Asking the car for a Surround View picture…",CaptureLook);
    private static Texture2D? PictureTexture(byte[] bytes)
    {
        if(bytes.Length<4)return null;
        var image=new Image();
        var result=bytes[0]==0xFF&&bytes[1]==0xD8?image.LoadJpgFromBuffer(bytes):
            bytes[0]==0x89&&bytes[1]==(byte)'P'?image.LoadPngFromBuffer(bytes):
            bytes[0]==(byte)'R'&&bytes[1]==(byte)'I'?image.LoadWebpFromBuffer(bytes):Error.FileUnrecognized;
        return result==Error.Ok?ImageTexture.CreateFromImage(image):null;
    }
    private Task SaveLookPicture()=>RunLive(async()=>{
        if(lookView==null)return;
        int version=liveEpoch;string result=await savePhoto(lookView.Image);EnsureLive(version);
        notice=result;noticeError=!result.StartsWith("Saved",StringComparison.Ordinal);
    },"Saving to Photos…");
    private static async Task<string> SaveToPhotos(byte[] image)
    {
        if(!NativeVaultBridge.SavePhoto(image))return "Saving pictures is available on iPhone.";
        // The first save waits for the add-only permission prompt.
        for(int i=0;i<1200&&NativeVaultBridge.PhotoStatus()==1;i++)await Task.Delay(100);
        return NativeVaultBridge.PhotoStatus() switch {
            2=>"Saved to Photos.",
            3=>"nCTRL isn't allowed to add photos. Allow it in Settings → Privacy & Security → Photos.",
            _=>"Couldn't save the picture to Photos."};
    }
}
