using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Ignored places: where an unlocked car is not flagged. Stored only in the device Keychain
// (memory only on desktop builds). Only the unlocked-doors alert is hidden there.
public partial class Main
{
    private List<IgnoredPlace> ignoredPlaces=new();
    private bool placesPersisted;
    // The car location being named in the "Ignore here" dialog.
    private VehicleLocation? pendingPlaceAt;

    // The car's location for the current report, only when fresh enough to hide an alert.
    private VehicleLocation? FreshCarLocation=>Offline?null:IgnoredPlaces.FreshLocation(connection?.State,connection?.Location);
    private IgnoredPlace? IgnoredHere=>IgnoredPlaces.Match(ignoredPlaces,FreshCarLocation);
    // From saved status the coordinates are gone; only the saved yes/no flag is known.
    private bool AtIgnoredPlace=>Offline?snapshot?.AtIgnoredPlace==true:IgnoredHere!=null;

    private async Task LoadPlaces()
    {
        if(!vault.Present){placesPersisted=false;return;}
        try {ignoredPlaces=IgnoredPlaces.Decode(await vault.LoadPlacesAsync(liveLifetime.Token)).ToList();placesPersisted=true;}
        catch(Exception ex) when(ex is VaultException or OperationCanceledException) {placesPersisted=false;}
        if(IsInsideTree())Render();
    }
    private async Task StorePlaces(List<IgnoredPlace> updated,string done)
    {
        await RunLive(async()=>{
            int version=liveEpoch;
            if(placesPersisted&&!demo)await vault.SavePlacesAsync(IgnoredPlaces.Encode(updated),liveLifetime.Token);
            EnsureLive(version);
            ignoredPlaces=updated;
            // Re-save the status so its ignored-place flag matches.
            persistedState=null;PersistSnapshot();
            notice=demo?done+" Kept for this demo only.":placesPersisted?done:done+" Saved for this session only on this device.";noticeError=false;
        },"Saving ignored places…");
    }
    private void StartIgnoreHere()
    {
        if(FreshCarLocation is not {} at||liveBusy)return;
        pendingPlaceAt=at;Render();
    }
    private void PlaceNameDialog()
    {
        canvas.AddChild(new ColorRect {Name="PlaceShade",Size=new(460,920),Color=new(0,0,0,.72f),MouseFilter=MouseFilterEnum.Stop});
        Panel(24,200,412,330,Skin.Card);
        Label(44,220,372,30,"IGNORE THIS PLACE",18,Amber,true);
        Label(44,256,372,80,$"Doors left unlocked within {Units.Radius(IgnoredPlaces.DefaultRadius,units)} of the car's current location won't be flagged. Other alerts still show.",12,White,false,true)
            .VerticalAlignment=VerticalAlignment.Top;
        Label(44,340,372,22,"NAME",11,Muted,true);
        var name=AccountInput(44,364,372,"Name",false);name.Name="PlaceName";name.MaxLength=IgnoredPlaces.MaxName;
        name.Text=ignoredPlaces.Any(p=>p.Name=="Home")?"Place "+(ignoredPlaces.Count+1):"Home";
        LiveButton(44,452,174,52,"Cancel",13,()=>{pendingPlaceAt=null;Render();},false,"PlaceCancel");
        LiveButton(242,452,174,52,"Save",13,()=>{
            var at=pendingPlaceAt;pendingPlaceAt=null;
            if(at==null)return;
            var updated=new List<IgnoredPlace>(ignoredPlaces){new(IgnoredPlaces.CleanName(name.Text),at.Latitude,at.Longitude)};
            _ = StorePlaces(updated.Take(IgnoredPlaces.MaxPlaces).ToList(),"Unlocked doors won't be flagged here.");
        },true,"PlaceSave");
    }
    private void PlacesScreen()
    {
        Title("IGNORED PLACES.","Unlocked doors aren't flagged here");
        if(ignoredPlaces.Count==0)Label(24,204,412,60,"No ignored places yet. Use Ignore here in Check your car while the car is somewhere you don't mind it unlocked.",13,White,false,true);
        int y=200;
        for(int i=0;i<ignoredPlaces.Count&&y<600;i++) {
            var place=ignoredPlaces[i];
            Panel(24,y,412,54,Skin.Card,false);Rect(24,y,3,54,Amber);
            Label(42,y+4,250,24,place.Name,14,White,true);
            Label(42,y+28,250,20,"Within "+Units.Radius(place.RadiusMeters,units),11,Muted);
            LiveButton(316,y+7,110,40,"Delete",13,()=>_ = StorePlaces(ignoredPlaces.Where(p=>p!=place).ToList(),"Removed "+place.Name+"."),false,"PlaceDelete"+i);
            y+=62;
        }
        Label(24,612,412,90,"Only the unlocked-doors alert is hidden at these places; open doors, windows and other alerts still show. "+
            (placesPersisted?"Places stay in this iPhone's Keychain and are deleted by Sign out & forget.":"This build keeps places for the current session only."),11,Muted,false,true);
        Button(24,714,412,46,"Back to settings",13,()=>Navigate("Settings"),false,"PlacesBack");
    }
}
