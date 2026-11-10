using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// Surround View: one composite picture from the car's cameras. The last 10 are kept in the app's own storage
// (Core/LookHistory.cs), re-encoded so no metadata rides along, until you delete them or sign out; a picture reaches
// Photos only when you tap Save.
public partial class Main
{
    private SurroundView? lookView;
    private Texture2D? lookTexture;
    private int lookChecks;
    private bool lookCapturing;
    // Swappable so the smoke test can save without the iOS photo library.
    private Func<byte[],Task<string>> savePhoto=SaveToPhotos;

    private IReadOnlyList<LookEntry> lookHistory=Array.Empty<LookEntry>();
    private bool lookHistoryLoaded;
    private string lookShown="";
    private readonly Dictionary<string,Texture2D> lookThumbs=new();
    private string? lookFolderFixture;
    private string LookFolder=>lookFolderFixture??ProjectSettings.GlobalizePath("user://look");

    // The app's own files stay out of iCloud and computer backups: Look around pictures show where the car is parked and the
    // map cache the areas you looked at. The pictures also can't be read while the phone is locked.
    private bool filesExcludedFromBackup;
    private void KeepFilesPrivate()
    {
        filesExcludedFromBackup=NativeVaultBridge.FilePrivacy(ProjectSettings.GlobalizePath("user://"),false);
        if(Directory.Exists(LookFolder))NativeVaultBridge.FilePrivacy(LookFolder,true);
        MapTileCache.Prune(MapFolder,DateTimeOffset.UtcNow);
    }
    // Map pictures from OpenStreetMap, kept about a week (pruned at launch) and deleted by Sign out & forget.
    private string? mapFolderFixture;
    private string MapFolder=>mapFolderFixture??ProjectSettings.GlobalizePath("user://map-cache");
    private void ForgetMapPictures()=>MapTileCache.Clear(MapFolder);
    private void LoadLookHistory()
    {
        if(lookHistoryLoaded)return;
        lookHistoryLoaded=true;
        try {
            string index=Path.Combine(LookFolder,LookHistory.IndexFile);
            lookHistory=File.Exists(index)?LookHistory.Decode(File.ReadAllText(index)).Where(e=>File.Exists(Path.Combine(LookFolder,e.File))).ToList():Array.Empty<LookEntry>();
        } catch(Exception){lookHistory=Array.Empty<LookEntry>();}
    }
    private void WriteLookIndex()
    {
        try {
            string index=Path.Combine(LookFolder,LookHistory.IndexFile),temporary=index+".tmp";
            File.WriteAllText(temporary,LookHistory.Encode(lookHistory));File.Move(temporary,index,true);
        } catch(Exception) { }
    }
    // Keeps a new capture, re-encoded as JPEG so nothing but the picture is stored. The demo keeps nothing.
    private void KeepLook(SurroundView view,Texture2D texture)
    {
        if(demo)return;
        LoadLookHistory();
        try {
            Directory.CreateDirectory(LookFolder);
            if(lookFolderFixture==null)NativeVaultBridge.FilePrivacy(LookFolder,true);
            string file=LookHistory.NewFile();
            File.WriteAllBytes(Path.Combine(LookFolder,file),texture.GetImage().SaveJpgToBuffer(0.9f));
            var (list,removed)=LookHistory.Add(lookHistory,new LookEntry(file,view.CapturedAt,DateTimeOffset.UtcNow));
            foreach(string old in removed){File.Delete(Path.Combine(LookFolder,old));lookThumbs.Remove(old);}
            lookHistory=list;lookShown=file;WriteLookIndex();
        } catch(Exception) { } // Still shown; just not kept.
    }
    private void ShowLook(LookEntry entry)
    {
        try {
            byte[] bytes=File.ReadAllBytes(Path.Combine(LookFolder,entry.File));
            if(PictureTexture(bytes) is not {} texture)return;
            lookView=new SurroundView(bytes,entry.CapturedAt,null,null,null,entry.SavedAt);lookTexture=texture;lookShown=entry.File;
        } catch(Exception) { }
    }
    private Texture2D? LookThumb(LookEntry entry)
    {
        if(lookThumbs.TryGetValue(entry.File,out var known))return known;
        try {
            var image=new Image();
            if(image.LoadJpgFromBuffer(File.ReadAllBytes(Path.Combine(LookFolder,entry.File)))!=Error.Ok)return null;
            image.Resize(72,Math.Max(1,72*image.GetHeight()/Math.Max(1,image.GetWidth())),Image.Interpolation.Bilinear);
            return lookThumbs[entry.File]=ImageTexture.CreateFromImage(image);
        } catch(Exception){return null;}
    }
    private void DeleteLook()
    {
        if(lookShown.Length==0)return;
        try {File.Delete(Path.Combine(LookFolder,lookShown));} catch(Exception) { }
        lookThumbs.Remove(lookShown);lookHistory=LookHistory.Remove(lookHistory,lookShown);WriteLookIndex();
        lookView=null;lookTexture=null;lookShown="";
        if(lookHistory.Count>0)ShowLook(lookHistory[0]);
        notice="Picture deleted from this iPhone.";noticeError=false;Render();
    }
    private void ForgetLookHistory()
    {
        try {if(Directory.Exists(LookFolder))Directory.Delete(LookFolder,true);} catch(Exception) { }
        lookHistory=Array.Empty<LookEntry>();lookThumbs.Clear();lookShown="";lookView=null;lookTexture=null;
    }

    private void LookAround()
    {
        Title("LOOK AROUND.","Surround View from the car's cameras");
        if(!demo)LoadLookHistory();
        if(!lookCapturing&&lookTexture==null&&lookHistory.Count>0)ShowLook(lookHistory[0]);
        if(lookCapturing) {
            Label(24,220,412,90,$"Waiting for the car's cameras… check {lookChecks} of {HyundaiUsClient.SurroundViewChecks}. This can take up to two minutes.",15,White,false,true).Name="LookProgress";
        } else if(lookTexture!=null&&lookView!=null) {
            Panel(20,192,420,320,Skin.Hero,false);
            canvas.AddChild(new TextureRect {Name="LookPicture",Position=new(24,196),Size=new(412,312),Texture=lookTexture,
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});
            Label(24,514,412,22,"Captured: "+ReportedTime(lookView.CapturedAt),11,Muted);
            // The pictures kept on this phone, newest first: tap one to show it.
            for(int i=0;i<lookHistory.Count;i++) {
                var entry=lookHistory[i];
                var thumb=Button(24+i*41,536,37,40,"",10,()=>{ShowLook(entry);Render();},false,"LookThumb"+i);
                if(LookThumb(entry) is {} t)thumb.AddChild(new TextureRect {Position=new(2,2),Size=new(33,36),Texture=t,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode=TextureRect.StretchModeEnum.KeepAspectCovered,MouseFilter=MouseFilterEnum.Ignore});
                if(entry.File==lookShown)thumb.AddThemeStyleboxOverride("normal",Style(Skin.Selected,Blue,2));
            }
            LiveButton(24,584,132,46,NativeVaultBridge.CanSavePhotos||smoke?"Save to Photos":"Save (iPhone)",13,()=>_ = SaveLookPicture(),true,"LookSave")
                .Disabled|=!NativeVaultBridge.CanSavePhotos&&!smoke;
            var delete=Button(168,584,124,46,"Delete",13,DeleteLook,false,"LookDelete");delete.Disabled=lookShown.Length==0||liveBusy;
            LiveButton(304,584,132,46,"Capture again",13,ConfirmLook,false,"LookCapture");
        } else {
            Label(24,206,412,120,"Ask the car to take a Surround View picture of where it's parked. It wakes the car and runs its cameras, and Hyundai can take up to two minutes to send the picture.",14,White,false,true);
            LiveButton(24,340,412,52,"Capture",15,ConfirmLook,true,"LookCapture");
        }
        bool kept=lookHistory.Count>0;
        Label(24,640,kept?280:412,70,(demo?"Demo pictures aren't kept. ":"The last 10 pictures stay on this iPhone, inside nCTRL only, until you delete them or sign out. ")+
            "One capture every 5 minutes, 6 a day. Surround View may not be available for every vehicle or account.",10,Muted,false,true).Name="LookHelp";
        if(kept)Button(316,646,120,38,"Delete all",12,()=>{ForgetLookHistory();notice="All Look around pictures deleted from this iPhone.";noticeError=false;Render();},false,"LookDeleteAll");
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
            lookView=view;lookTexture=texture;lookShown="";KeepLook(view,texture);notice="Surround View captured.";noticeError=false;
        }
        catch(BluelinkException ex) when(ex is not RequestLimitException&&ex.HttpStatus is >=400 and <500&&ex.HttpStatus is not (401 or 403 or 429)) {
            throw new BluelinkException(ex.Message+" Surround View may not be available for this vehicle or account.",ex.HttpStatus,ex.ApiErrorCode,ex.ApiSubCode);
        }
        finally {if(version==liveEpoch)lookCapturing=false;}
    },"Asking the car for a Surround View picture…",CaptureLook);
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
