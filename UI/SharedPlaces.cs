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

    private async Task LoadShared()
    {
        if(!vault.Present||demo){sharedPersisted=false;return;}
        try {sharedHistory=SharedHistory.Decode(await vault.LoadSharedAsync(liveLifetime.Token));sharedPersisted=true;}
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
        foreach(var r in receipts.Where(r=>!r.Saved))
            _ = RecordShare(new(r.At,r.Source,r.Kinds.Length>0?r.Kinds:"nothing",null,"Nothing in it looked like a place, so nothing was saved",r.Opened));
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
        var place=item.Place;string problem="",lookup=item.Lookup,name=item.Name;
        if(place==null&&item.ShortLink.Length>0) {
            lookingUp=true;notice="Reading the Google Maps link…";Render();
            string? full=null;
            try {full=await ResolveShortLink(item.ShortLink);} catch(Exception) { } finally {lookingUp=false;}
            if(full!=null) {
                var (found,more)=Destinations.FromText(full);
                place=found;
                if(place!=null&&name.Length>0&&place.Name==Destinations.Format(place.Latitude,place.Longitude))place=place with {Name=name};
                if(place==null&&lookup.Length==0)lookup=more;
            } else if(lookup.Length==0)problem="Only a Google Maps short link, and it didn't lead to a place";
        }
        if(place==null&&lookup.Length>0) {
            destinationText=lookup;lookingUp=true;notice="Looking up the address with Apple Maps…";Render();
            try {place=await LookUpAddress(lookup);} finally {lookingUp=false;}
            if(place!=null&&name.Length>0)place=place with {Name=name};
            if(place==null)problem="Apple Maps couldn't find “"+lookup+"”";
        }
        if(place==null&&problem.Length==0)problem="Nothing in it looked like a place";
        if(screen=="Send") {
            if(place!=null){pendingDestination=place;notice="";noticeError=false;}
            else {notice=problem+".";noticeError=true;}
            Render();
        }
        await RecordShare(new(DateTimeOffset.UtcNow,item.Source,item.Kinds,place,place==null?problem:"",opened));
        if(place!=null){_ = LoadDestinationMap(place);await SendShared(place);}
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
