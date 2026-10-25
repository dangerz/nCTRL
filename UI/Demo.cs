using Godot;
using NGarage.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// The demo: a pretend IONIQ 5 N served inside the app (DemoHyundai), for trying nCTRL without an account
// and for App Review. Nothing is sent to Hyundai and nothing is saved: preferences, saved status, request
// limits, ignored places and the last known location are all put back when the demo ends.
public partial class Main
{
    private bool demo;
    private ConfigFile? demoReturnPrefs;
    private VehicleSnapshot? demoReturnSnapshot;
    private RequestBudget? demoReturnBudget;
    private Func<HyundaiUsClient>? demoReturnFactory;
    // Short cooldowns so the demo can be explored freely; real limits are untouched.
    private static readonly IReadOnlyDictionary<RequestKind,RequestLimit> DemoLimits=RequestBudget.Defaults.ToDictionary(
        k=>k.Key,k=>new RequestLimit(TimeSpan.FromSeconds(k.Key is RequestKind.Location or RequestKind.Camera?30:3),1000));

    private async Task StartDemo()
    {
        if(liveBusy||demo)return;
        EndLiveSession(false);
        demoReturnPrefs=PrefsConfig();demoReturnSnapshot=snapshot;demoReturnBudget=budget;demoReturnFactory=connectionFactory;
        demo=true;snapshot=null;persistedState=null;ignoredPlaces.Clear();ForgetOutside();sharedHistory=Array.Empty<SharedEntry>();
        budget=new RequestBudget(new MemoryBudgetStore(),null,DemoLimits);
        var service=new DemoHyundai(CarPicture(CarModels.Default).GetImage().SavePngToBuffer());
        connectionFactory=()=>new HyundaiUsClient(service,null,budget);
        int version=liveEpoch;
        await RunLive(async()=>{
            var active=connectionFactory();connection=active;
            var vehicles=await active.LoginAsync(DemoHyundai.Email,"demo","0000",liveLifetime.Token);EnsureLive(version);
            accountKey=VehicleLink.AccountKey(DemoHyundai.Email);enrollment=vehicles;selectedIndex=0;
            await ConnectVehicle(active,vehicles[0],version);
            notice="Demo car ready. Everything here is pretend: nothing is sent to Hyundai.";noticeError=false;
        },"Starting the demo…");
    }
    // Called by EndLiveSession: put back everything the demo replaced.
    private void LeaveDemo()
    {
        demo=false;
        if(demoReturnPrefs!=null)ApplyPrefs(demoReturnPrefs);
        snapshot=demoReturnSnapshot;persistedState=null;
        if(demoReturnBudget!=null)budget=demoReturnBudget;
        if(demoReturnFactory!=null)connectionFactory=demoReturnFactory;
        demoReturnPrefs=null;demoReturnSnapshot=null;demoReturnBudget=null;demoReturnFactory=null;
        savedLocation=storedLocation=null;savedFor="";savedLoaded=false;ignoredPlaces.Clear();ForgetOutside();
        _ = LoadPlaces();
        _ = LoadShared();
    }
}
