using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

// Places shared to nCTRL from other apps (the Share extension saves them in the App Group): each opens Send to car
// and goes to the car right away (places typed in Send to car still wait for the button). A Google Maps short link is opened once to read the place (ShortLinks). Every share,
// including ones that couldn't be read, is listed under Activity → Shared (the last 20, in the device Keychain).
public partial class Main
{
    private string? heldShare;
    private IReadOnlyList<SharedEntry> sharedHistory=Array.Empty<SharedEntry>();
    private bool sharedPersisted;
    private string activityTab="Requests";
#if DEBUG
    // Smoke tests answer Google short links themselves.
    private HttpMessageHandler? shortLinkFixture;
#endif

    // Hyundai's session, shared with the Share extension after each request so it can send without signing in.
    private string sessionShared="";
    private async Task ShareSession()
    {
        if(demo||!AppService.SendToCar||!vault.Present||!vault.HasAccount||connection?.ExportSession() is not {} session)return;
        string json=session.Encode();
        if(json==sessionShared)return;
        try {await vault.SaveSessionAsync(json,liveLifetime.Token);sessionShared=json;}
        catch(Exception ex) when(ex is VaultException or OperationCanceledException) { } // The extension signs in instead.
    }
    private async Task LoadShared()
    {
        if(!vault.Present||demo){sharedPersisted=false;return;}
        try {
            var saved=SharedHistory.Decode(await vault.LoadSharedAsync(liveLifetime.Token));
            // Shares recorded while the list was loading (CheckShared runs right after launch) go on top, not lost.
            var added=sharedHistory;sharedHistory=saved;sharedPersisted=true;
            foreach(var entry in added.Reverse())sharedHistory=SharedHistory.Add(sharedHistory,entry);
            if(added.Count>0)await vault.SaveSharedAsync(SharedHistory.Encode(sharedHistory),liveLifetime.Token);
        }
        catch(Exception ex) when(ex is VaultException or OperationCanceledException) {sharedPersisted=false;}
    }
    private async Task RecordShare(SharedEntry entry)
    {
        sharedHistory=SharedHistory.Add(sharedHistory,entry);
        if(sharedPersisted&&!demo)
            try {await vault.SaveSharedAsync(SharedHistory.Encode(sharedHistory),liveLifetime.Token);}
            catch(Exception ex) when(ex is VaultException or OperationCanceledException) { } // Still listed this session.
        if(IsInsideTree()&&screen=="Activity"&&!liveBusy)Render();
    }
    private string TakeGroup(string name)
    {
#if DEBUG
        if(smoke){if(!sharedFixture.Remove(name,out var bytes))return "";return System.Text.Encoding.UTF8.GetString(bytes);}
#endif
        return NativeVaultBridge.GroupTake(name);
    }

    // On opening the app: what the Share extension left. A share waits for the car to be open (after signing in).
    private void CheckShared()
    {
        var receipts=SharedHistory.Receipts(TakeGroup("share-log.json"));
        // Shares the extension couldn't save still belong in the list, so you can see they happened.
        foreach(var r in receipts.Where(r=>!r.Saved&&!r.Sent))
            _ = RecordShare(new(r.At,r.Source,r.Kinds.Length>0?r.Kinds:"nothing",null,r.Reason,r.Opened));
        // Places the Share sheet sent to the car itself, while nCTRL was closed.
        foreach(var r in receipts.Where(r=>r.Sent&&r.Place!=null)) {
            _ = RecordShare(new(r.At,r.Source,r.Kinds,r.Place,""));
            history.Add(("Sent to car: "+r.Place!.Name,r.At.ToLocalTime().ToString("h:mm tt")+" · from the Share sheet",true));
        }
        string json=heldShare??TakeGroup("share.json");heldShare=null;
        if(json.Length==0)return;
        if(!liveMode||DisclaimerRequired) {
            if(vault.HasAccount&&!DisclaimerRequired){heldShare=json;return;} // Picked up once the car is open.
            notice="Open your car in nCTRL first, then share the place again.";noticeError=true;Render();return;
        }
        var item=Destinations.FromShare(json);
        _ = ProcessShare(item,receipts.FirstOrDefault(r=>r.Id.Length>0&&r.Id==item.Id)?.Opened);
    }
    private async Task ProcessShare(SharedItem item,bool? opened)
    {
        pendingDestination=null;destinationText="";sentDestination=null;screen="Send";notice="";noticeError=false;
        if(item.Place==null&&item.ShortLink.Length>0){lookingUp=true;notice="Reading the Google Maps link…";Render();}
        ShareResult read;
        try {read=await ShareSender.ReadAsync(item,ResolveShortLink);} finally {lookingUp=false;}
        var place=read.Place;string problem=read.Problem,lookup=read.Lookup,name=read.Name;
        if(place==null&&lookup.Length>0) {
            destinationText=lookup;lookingUp=true;notice="Looking up the address with Apple Maps…";Render();
            try {place=await LookUpAddress(lookup);} finally {lookingUp=false;}
            if(place!=null&&name.Length>0)place=place with {Name=name};
            if(place==null)problem="Apple Maps couldn't find “"+lookup+"”";
        }
        if(screen=="Send") {
            if(place!=null){pendingDestination=place;notice="";noticeError=false;}
            else {notice=problem+".";noticeError=true;}
            Render();
        }
        await RecordShare(new(DateTimeOffset.UtcNow,item.Source,item.Kinds,place,place==null?problem:"",opened));
        if(place==null)return;
        _ = LoadDestinationMap(place);
        // The Share sheet already tried and Hyundai refused, or it couldn't reach Hyundai: don't try again by itself.
        if(item.Unsent.Length>0){notice="The Share sheet couldn't send it: "+item.Unsent+" Tap Send to car to try again.";noticeError=true;Render();return;}
        await SendShared(place);
    }
    // Waits for any request already running (such as the read when the app opens), then sends. If you've moved on,
    // or something is still running after 30 seconds, the place stays on Send to car for you to send.
    private async Task SendShared(Destination place)
    {
        for(int i=0;i<300&&(liveBusy||monitoringCommand);i++)await ToSignal(GetTree().CreateTimer(0.1),SceneTreeTimer.SignalName.Timeout);
        if(screen!="Send"||pendingDestination!=place||liveBusy||monitoringCommand)return;
        await SendDestination();
    }
    private Task<string?> ResolveShortLink(string link)
    {
#if DEBUG
        if(smoke)return shortLinkFixture==null?Task.FromResult<string?>(null):ShortLinks.ResolveAsync(link,shortLinkFixture,liveLifetime.Token);
#endif
        return ShortLinks.ResolveAsync(link,null,liveLifetime.Token);
    }

    // Activity → Shared: newest first. A place opens again in Send to car.
    private void SharedTab()
    {
        if(sharedHistory.Count==0) {
            Label(24,250,412,90,"Nothing shared yet. In Apple Maps, Google Maps or another app, tap Share, then nCTRL, to send a place to your car.",13,Muted,false,true);
            return;
        }
        var scroll=new ScrollContainer {Name="SharedScroll",Position=new(24,246),Size=new(412,440),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        var list=new VBoxContainer {CustomMinimumSize=new(404,0)};list.AddThemeConstantOverride("separation",8);
        for(int i=0;i<sharedHistory.Count;i++) {
            var entry=sharedHistory[i];var place=entry.Place;
            var row=new Button {Name="Shared"+i,CustomMinimumSize=new(404,84),Disabled=place==null,FocusMode=FocusModeEnum.None,MouseDefaultCursorShape=CursorShape.PointingHand};
            foreach(string state in new[]{"normal","hover","pressed","disabled","focus"})row.AddThemeStyleboxOverride(state,Style(state=="hover"?Skin.ButtonHover:Skin.Card,Skin.Card));
            row.AddChild(new ColorRect {Size=new(3,84),Color=place!=null?Blue:Red,MouseFilter=MouseFilterEnum.Ignore});
            string when=entry.At.ToLocalTime().ToString(entry.At.ToLocalTime().Date==DateTime.Today?"h:mm tt":"MMM d, h:mm tt");
            row.AddChild(RowText(6,when+"  ·  "+entry.Source+(entry.Kinds.Length>0?"  ·  "+entry.Kinds:""),10,Muted,false));
            row.AddChild(RowText(24,place?.Name??"Couldn't read it",14,White,true));
            row.AddChild(RowText(46,place!=null?(place.Address.Length>0&&place.Address!=place.Name?place.Address:Destinations.Format(place.Latitude,place.Longitude)):entry.Problem,11,Muted,false));
            if(entry.Opened==false&&place!=null)row.AddChild(RowText(64,"nCTRL didn't open from the share; it was picked up later.",10,Amber,false));
            if(place!=null)row.Pressed+=()=>{pendingDestination=place;destinationText="";notice="";Navigate("Send");_ = LoadDestinationMap(place);};
            list.AddChild(row);
        }
        scroll.AddChild(list);canvas.AddChild(scroll);
        Button(24,696,412,40,"Clear shared places",12,()=>_ = ClearShared(),false,"SharedClear");
    }
    private Label RowText(float y,string text,int size,Color color,bool strong)
    {
        var label=new Label {Position=new(14,y),Size=new(384,18),Text=text,ClipText=true,MouseFilter=MouseFilterEnum.Ignore};
        label.AddThemeFontOverride("font",strong?bold:regular);label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color);
        return label;
    }
    private async Task ClearShared()
    {
        sharedHistory=Array.Empty<SharedEntry>();
        if(sharedPersisted&&!demo)
            try {await vault.ForgetSharedAsync(liveLifetime.Token);} catch(Exception ex) when(ex is VaultException or OperationCanceledException) { }
        notice="Shared places cleared.";noticeError=false;Render();
    }
}
