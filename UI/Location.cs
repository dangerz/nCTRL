using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Main
{
    private readonly List<(MapTile Tile,Texture2D Texture)> mapTiles=new();
    private VehicleLocation? mapLocation;
    private string mapStatus="";
    private async Task OpenVehicleLocation()
    {
        if(liveBusy||!liveMode)return;
        Navigate("Locate");
        await UpdateVehicleLocation(connection?.Location==null||connection.CanRequestLocation);
    }
    private Task UpdateVehicleLocation(bool request=true)=>RunLive(async()=>{
        int version=liveEpoch;var active=connection!;var token=liveLifetime.Token;
        if(request)await active.LocateAsync(token);
        EnsureLive(version);
        var location=active.Location;
        if(location==null)return;
        if(mapLocation!=location){mapTiles.Clear();mapLocation=location;}
        mapStatus="Loading map…";notice="Location received. Loading map…";Render();
        // Smoke tests never contact the map service.
        if(smoke){mapStatus="Map network disabled in offline tests.";notice="Location fixture loaded.";return;}
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
        notice="Showing Hyundai’s reported location; it may lag the vehicle.";
    },"Requesting vehicle location…",()=>UpdateVehicleLocation(true));

    private void LiveLocation()
    {
        Title("FIND YOUR HYUNDAI.","Last reported position · tap the map to open Maps");
        var loc=connection?.Location;
        if(loc==null) {
            Label(24,240,412,90,liveBusy?"Requesting vehicle location…":"Location unavailable. Tap Update Location to request it.",16,White,false,true);
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
        LiveButton(24,677,412,47,"Update Location",14,()=>_ = UpdateVehicleLocation(),true,"UpdateLocation");
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
