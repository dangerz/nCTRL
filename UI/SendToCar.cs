using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Send to car: look up an address (Apple's geocoder on the phone) or read coordinates or a maps link,
// confirm the place on a small map, then send it to the car's navigation through Hyundai (sendPoi).
public partial class Main
{
    private string destinationText="";
    private Destination? pendingDestination,sentDestination;
    private bool lookingUp;
    // After a place is sent, offer to start climate (Warm up, or Cool down when it's hot). Settings turns it off or on.
    private bool climateOffer=true;
    private bool ClimateOffered=>climateOffer&&connection?.Selected?.Electric==true;
    private readonly List<(MapTile Tile,Texture2D Texture)> destinationTiles=new();
    private Destination? destinationTilesFor;
#if DEBUG
    // Smoke tests answer address lookups themselves.
    private Func<string,Destination?>? lookupFixture;
#endif

    private void SendScreen()
    {
        Title("SEND TO CAR.","Look up an address, or paste coordinates or a maps link.");
        var input=new LineEdit {Name="DestinationInput",Position=new(24,196),Size=new(296,44),Text=destinationText,
            PlaceholderText="Address, coordinates or maps link",Editable=!lookingUp&&!liveBusy,MaxLength=Destinations.MaxText};
        input.AddThemeFontOverride("font",regular);input.AddThemeFontSizeOverride("font_size",14);
        input.TextChanged+=text=>destinationText=text;
        input.TextSubmitted+=text=>_ = FindDestination(text);
        canvas.AddChild(input);
        Button(330,196,106,44,lookingUp?"Finding…":"Find",13,()=>_ = FindDestination(input.Text),false,"DestinationFind").Disabled=lookingUp||liveBusy;
        if(pendingDestination is {} place) {
            var map=new Panel {Name="DestinationMap",Position=new(24,252),Size=new(412,200),ClipContents=true,MouseFilter=MouseFilterEnum.Ignore};
            map.AddThemeStyleboxOverride("panel",Style(Skin.Hero,Skin.PanelEdge));canvas.AddChild(map);
            if(destinationTilesFor==place)foreach(var (tile,texture) in destinationTiles)
                map.AddChild(new TextureRect {Position=new((float)tile.Left,(float)tile.Top),Size=new(256,256),Texture=texture,MouseFilter=MouseFilterEnum.Ignore});
            map.AddChild(new PlacePin {Name="DestinationPin",Position=new(194,62),Size=new(24,38)});
            Label(24,460,412,26,place.Name,17,White,true).Name="DestinationName";
            Label(24,488,412,44,place.Address.Length>0&&place.Address!=place.Name?place.Address:"No street address",12,Muted,false,true).Name="DestinationAddress";
            Label(24,532,412,20,Destinations.Format(place.Latitude,place.Longitude),11,Muted);
            RemoteButton(24,566,264,52,"Send to car",14,()=>_ = SendDestination(),true,"DestinationSend");
            Button(300,566,136,52,"Cancel",13,()=>{pendingDestination=null;Render();},false,"DestinationCancel");
        } else Label(24,262,412,130,"Type an address and tap Find; nCTRL looks it up with Apple Maps on this phone. You can also paste coordinates (38.8977, -77.0365) or a link shared from Apple or Google Maps.",13,Muted,false,true);
        Label(24,630,412,58,"Hyundai sends the place to your car's navigation, which needs Bluelink's Guidance features. Addresses are looked up by Apple; the place you send goes to Hyundai.",10,Muted,false,true);
        Button(24,694,412,44,"Back to map",13,()=>Navigate("Locate"),false,"DestinationBack");
    }
    // name: the shared place's own name, kept over the looked-up one.
    private async Task FindDestination(string text,string name="")
    {
        if(lookingUp)return;
        destinationText=text;
        var (place,lookup)=Destinations.FromText(text);
        if(place==null&&lookup.Length==0){notice="Type an address, coordinates or a maps link.";noticeError=true;Render();return;}
        if(place==null) {
            lookingUp=true;pendingDestination=null;notice="Looking up the address with Apple Maps…";noticeError=false;Render();
            try {place=await LookUpAddress(lookup);} finally {lookingUp=false;}
            if(screen!="Send")return;
            if(place==null) {
                notice=NativeVaultBridge.CanGeocode||smoke?"Apple Maps couldn't find that address. Try adding the city, or paste coordinates.":
                    "Looking up addresses works on iPhone. Paste coordinates or a maps link instead.";
                noticeError=true;Render();return;
            }
        }
        if(name.Length>0)place=place with {Name=name};
        pendingDestination=place;notice="";noticeError=false;Render();
        await LoadDestinationMap(place);
    }
    private async Task<Destination?> LookUpAddress(string query)
    {
#if DEBUG
        if(smoke)return lookupFixture?.Invoke(query);
#endif
        if(!NativeVaultBridge.CanGeocode)return null;
        NativeVaultBridge.Geocode(query);
        for(int i=0;i<60;i++) {
            await ToSignal(GetTree().CreateTimer(0.25),SceneTreeTimer.SignalName.Timeout);
            var (state,found)=NativeVaultBridge.GeocodeResult();
            if(state==2)return Destinations.FromLookup(found);
            if(state>=3)return null;
        }
        return null;
    }
    private async Task LoadDestinationMap(Destination place)
    {
        destinationTiles.Clear();destinationTilesFor=null;
        if(smoke)return;
        int version=liveEpoch;
        try {
            using var cache=new MapTileCache(ProjectSettings.GlobalizePath("user://map-cache"));
            var loaded=new List<(MapTile Tile,Texture2D Texture)>();
            foreach(var tile in VehicleMap.TilesFor(new MapFrame(place.Latitude,place.Longitude,VehicleMap.CloseZoom,412,200))) {
                if(screen!="Send"||pendingDestination!=place)return;
                var bytes=await cache.LoadAsync(tile,liveLifetime.Token);EnsureLive(version);
                using var image=new Image();
                if(image.LoadPngFromBuffer(bytes)!=Error.Ok)return;
                loaded.Add((tile,ImageTexture.CreateFromImage(image)));
            }
            destinationTiles.AddRange(loaded);destinationTilesFor=place;
            if(screen=="Send"&&!liveBusy)Render();
        } catch(Exception) { } // The pin and address still show without a map.
    }
    private Task SendDestination()=>RunLive(async()=>{
        int version=liveEpoch;
        if(pendingDestination is not {} place)return;
        var (active,_)=await AcquireSession(version);
        if(active==null)return;
        string? tracking=await active.SendDestinationAsync(place,liveLifetime.Token);EnsureLive(version);
        pendingDestination=null;destinationText="";sentDestination=place;notice="";
        history.Add(("Sent to car: "+place.Name,DateTime.Now.ToString("h:mm tt")+(tracking!=null?" · Hyundai tracking ID received":""),true));
    },"Sending to your car…",SendDestination);
    // Your car in its paint, cut out (the widget's picture), for the Sent To Car dialog.
    private Texture2D? sentCar;private string sentCarFor="";
    private Texture2D SentCar()
    {
        string key=carModel.Id+"/"+paintHex;
        if(sentCar==null||sentCarFor!=key) {
            var image=new Image();
            image.LoadPngFromBuffer(WidgetPicture());
            sentCar=ImageTexture.CreateFromImage(image);sentCarFor=key;
        }
        return sentCar;
    }
    private void SentDialog()
    {
        var place=sentDestination!;bool offer=ClimateOffered;
        canvas.AddChild(new ColorRect {Name="SentShade",Size=new(460,920),Color=new(0,0,0,.72f),MouseFilter=MouseFilterEnum.Stop});
        Panel(24,180,412,offer?410:330,Skin.Card);
        canvas.AddChild(new TextureRect {Name="SentCar",Position=new(54,192),Size=new(352,104),Texture=SentCar(),
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});
        Label(44,302,372,34,"SENT TO CAR",22,Blue,true).Name="SentTitle";
        Label(44,344,372,26,place.Name,16,White,true);
        string at=Destinations.Format(place.Latitude,place.Longitude);
        Label(44,372,372,44,place.Address.Length>0&&place.Address!=place.Name?place.Address:place.Name==at?"":at,12,Muted,false,true).VerticalAlignment=VerticalAlignment.Top;
        Label(44,416,372,24,"Shared with your "+CarTitle+"'s navigation.",12,Muted,false,true).Name="SentCarName";
        if(!offer){Button(44,452,372,48,"OK",14,()=>{sentDestination=null;Navigate("Locate");},true,"SentOk");return;}
        // Get the car ready: your Warm up preset, or Cool down when it's hot outside.
        var preset=ClimatePreset.ForOutside(outside?.TemperatureF,warmPreset,coolPreset);
        Label(44,450,372,22,"GET THE CAR READY?",11,Muted,true);
        RemoteButton(44,476,372,48,"Start "+preset.Name+"  ·  "+Units.Setpoint(preset.TemperatureF,units),14,
            ()=>{sentDestination=null;Navigate("Car");StartClimatePreset(preset);},true,"SentClimate");
        Button(44,534,180,44,"Not now",13,()=>{sentDestination=null;Navigate("Locate");},false,"SentOk");
        Button(236,534,180,44,"Disable this",13,()=>{
            climateOffer=false;SavePrefs();sentDestination=null;screen="Locate";
            notice="nCTRL won't offer climate after sending a place. Turn it back on in Settings.";noticeError=false;Render();
        },false,"SentDisable");
    }
}
