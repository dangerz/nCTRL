using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public partial class Main
{
    private readonly List<(MapTile Tile,Texture2D Texture)> mapTiles=new();
    private VehicleLocation? mapLocation;
    private string mapStatus="";
    // The last known location, from this session or the device Keychain, for the vehicle it belongs to.
    // Shown without Face ID, marked old, whenever no new location was just received.
    private VehicleLocation? savedLocation,storedLocation;
    private string savedFor="";private bool savedLoaded;
    // What the map shows, and whether it is an old location rather than one just requested.
    private VehicleLocation? shownLocation;private bool shownOld;
    private string VehicleKey=>connection?.Selected is {} v&&accountKey.Length>0?VehicleLink.Fingerprint(accountKey,v.Vin):Offline?snapshot?.VehicleFingerprint??"":"";

    private async Task OpenVehicleLocation()
    {
        if(liveBusy||!liveMode)return;
        Navigate("Locate");
        await ShowOldLocation();
        // Request a new location when the limits allow it, or when there is nothing to show.
        if(budget.CanAcquire(RequestKind.Location)||shownLocation==null)await UpdateVehicleLocation();
    }
    private Task ShowOldLocation()=>RunLive(async()=>{
        int version=liveEpoch;
        if(await OldLocation(version) is {} old)await ShowMap(old,true,version,liveLifetime.Token);
    },"Loading the last known location…");
    private async Task<VehicleLocation?> OldLocation(int version)
    {
        string key=VehicleKey;
        if(key.Length==0)return null;
        if(savedFor!=key){savedFor=key;savedLocation=null;storedLocation=null;savedLoaded=false;}
        if(!savedLoaded&&vault.Present&&!demo) {
            try {
                var loaded=LastLocation.Decode(await vault.LoadLocationAsync(liveLifetime.Token),key,DateTimeOffset.UtcNow);
                EnsureLive(version);storedLocation=loaded;savedLocation=LastLocation.Newest(savedLocation,loaded);
            } catch(VaultException) { }
            savedLoaded=true;
        }
        return LastLocation.Newest(connection?.Location,connection?.State?.StatusLocation,savedLocation);
    }
    // After each successful request, keep the newest location (from a location request or a status report).
    private async Task RememberLocation(int version)
    {
        string key=VehicleKey;
        if(connection==null||key.Length==0)return;
        var newest=LastLocation.Newest(connection.Location,connection.State?.StatusLocation);
        if(newest==null)return;
        if(savedFor!=key){savedFor=key;savedLocation=null;storedLocation=null;savedLoaded=false;}
        savedLocation=LastLocation.Newest(newest,savedLocation);
        if(!vault.Present||demo||LastLocation.Same(savedLocation,storedLocation))return;
        try {await vault.SaveLocationAsync(LastLocation.Encode(key,savedLocation!),liveLifetime.Token);EnsureLive(version);storedLocation=savedLocation;}
        catch(VaultException) { } // The location is still shown this session.
    }
    private Task UpdateVehicleLocation()=>RunLive(async()=>{
        int version=liveEpoch;var token=liveLifetime.Token;
        // A new location request is a request like any other: the first one of a session asks for Face ID.
        var active=(await AcquireSession(version)).Client;
        if(active==null)return;
        try {await active.LocateAsync(token);}
        catch(Exception ex) when(ex is BluelinkException or HttpRequestException) {
            // Keep a map up: the last known location, marked old. Then report why.
            EnsureLive(version);
            if(await OldLocation(version) is {} old)await ShowMap(old,true,version,token);
            throw;
        }
        EnsureLive(version);
        await ShowMap(active.Location!,false,version,token);
    },NeedsFaceId?"Waiting for "+vault.BiometricName+"…":"Requesting vehicle location…",UpdateVehicleLocation);
    private async Task ShowMap(VehicleLocation location,bool old,int version,CancellationToken token)
    {
        shownLocation=location;shownOld=old;
        if(mapLocation!=location){mapTiles.Clear();mapLocation=location;}
        mapStatus="Loading map…";notice=old?"Showing the last known location. Loading map…":"Location received. Loading map…";Render();
        // Smoke tests never contact the map service.
        if(smoke){mapStatus="Map network disabled in offline tests.";notice=old?"Old location fixture loaded.":"Location fixture loaded.";return;}
        try {
            using var cache=new MapTileCache(ProjectSettings.GlobalizePath("user://map-cache"));
            var loaded=new List<(MapTile Tile,Texture2D Texture)>();
            foreach(var tile in VehicleMap.Tiles(location)) {
                token.ThrowIfCancellationRequested();
                if(screen!="Locate")break; // No prefetch after leaving the map.
                var bytes=await cache.LoadAsync(tile,token);EnsureLive(version);
                using var image=new Image();
                if(image.LoadPngFromBuffer(bytes)!=Error.Ok)throw new InvalidOperationException();
                loaded.Add((tile,ImageTexture.CreateFromImage(image)));
            }
            EnsureLive(version);mapTiles.Clear();mapTiles.AddRange(loaded);
            mapStatus=loaded.Count>0?"Tap the map to open your maps app.":"Tap to open your maps app.";
        } catch(OperationCanceledException){throw;}
        catch(Exception) {EnsureLive(version);mapStatus="Map unavailable. Tap to open your maps app.";}
        notice=old?"Showing the last known location. The car may have moved since.":"Showing Hyundai’s reported location; it may lag the vehicle.";
    }

    private void LiveLocation()
    {
        Title("FIND YOUR HYUNDAI.","Last reported position · tap the map to open Maps");
        var loc=shownLocation;
        if(shownOld&&loc!=null)
            Label(24,186,412,24,"OLD LOCATION · received "+LastLocation.Age(loc.RetrievedAt,DateTimeOffset.UtcNow),11,Amber,true).Name="LocationOld";
        if(loc==null) {
            Label(24,240,412,90,liveBusy?"Requesting vehicle location…":"No location yet. Tap Update Location to request it.",16,White,false,true);
        } else {
            var map=Button(24,214,412,320,"",14,()=>OpenMaps(loc),false,"VehicleMap");
            map.ClipContents=true;
            if(mapLocation==loc)foreach(var (tile,texture) in mapTiles)
                map.AddChild(new TextureRect{Position=new((float)tile.Left,(float)tile.Top),Size=new(256,256),Texture=texture,MouseFilter=MouseFilterEnum.Ignore});
            var marker=new Label{Position=new(184,138),Size=new(44,44),Text="N",HorizontalAlignment=HorizontalAlignment.Center,
                VerticalAlignment=VerticalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            marker.AddThemeStyleboxOverride("normal",Style(Red,White));
            marker.AddThemeColorOverride("font_color",White);marker.AddThemeFontOverride("font",bold);map.AddChild(marker);
            Button(24,538,412,28,"© OpenStreetMap contributors",11,()=>OS.ShellOpen("https://www.openstreetmap.org/copyright"));
            Label(24,578,412,44,mapStatus,12,White,false,true);
            Label(24,624,412,35,"Reported: "+ReportedTime(loc.ReportedAt),11,Muted,false,true);
        }
        LiveButton(24,677,198,47,"Update Location",14,()=>_ = UpdateVehicleLocation(),true,"UpdateLocation");
        Button(238,677,198,47,"Look around  ›",13,()=>Navigate("Look"),false,"LookAround");
        Label(24,731,412,40,"Hyundai location requests: once per hour. Map tiles share the viewed area with OpenStreetMap.",10,Muted,false,true);
    }
    private void OpenMaps(VehicleLocation location)
    {
        if(NativeVaultBridge.OpenVehicleMap(location.Latitude,location.Longitude))return;
        if(OS.ShellOpen(VehicleMap.ExternalUrl(location,OS.GetName()))!=Error.Ok) {
            notice="No maps application could open this location.";noticeError=true;Render();
        }
    }
}
