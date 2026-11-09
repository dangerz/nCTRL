using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NGarage.Core;

static class AlertTests
{
    static LiveVehicleState State(bool? locked=true,int? battery=60,bool? plugged=false,bool? charging=false,int? limit=80,int? v12=80,bool? tire=false,
        bool? door=false,bool? window=false,bool? tailgate=false,string report="r1")=>
        new(locked,false,null,battery,plugged,charging,200,limit,limit,report,DateTimeOffset.UtcNow,
            Health:new VehicleHealth(v12,door,false,false,false,tailgate,false,window,false,false,false,tire,false,false,false));

    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Background alerts: "+label);count++;}
        var t0=new DateTimeOffset(2026,11,9,12,0,0,TimeSpan.Zero);
        Func<NoticeKind,bool> all=_=>true;
        (AlertMemory,System.Collections.Generic.IReadOnlyList<Notice>) Eval(AlertMemory? m,LiveVehicleState s,DateTimeOffset at,bool ignored=false,Func<NoticeKind,bool>? allow=null)=>
            BackgroundAlerts.Evaluate(m,s,at,"Flareon",ignored,allow??all);

        // Charging.
        var (m,n)=Eval(null,State(charging:true,plugged:true,battery:50),t0);
        Check(n.Count==0&&m.Seen&&m.Charging,"The first check only remembers");
        var (_,done)=Eval(m,State(charging:false,plugged:true,battery:80,limit:80),t0);
        Check(done.Single().Title=="Flareon finished charging"&&done[0].Body=="Battery at 80%.","Finished at the limit");
        Check(Eval(m,State(charging:false,plugged:true,battery:100,limit:100),t0).Item2.Single().Title=="Flareon is fully charged","Fully charged at 100%");
        var (_,early)=Eval(m,State(charging:false,plugged:true,battery:62,limit:80),t0);
        Check(early.Single().Title=="Flareon stopped charging"&&early[0].Body=="Still plugged in at 62%, below the 80% limit.","Stopped early while plugged in");
        Check(Eval(m,State(charging:false,plugged:false,battery:62,limit:80),t0).Item2.Single().Title=="Flareon was unplugged","Unplugged mid-charge");
        Check(Eval(m,State(charging:null,plugged:null,battery:null),t0).Item2.Count==0,"Unknown charging never alerts");
        var (after,_)=Eval(m,State(charging:false,plugged:true,battery:80),t0);
        Check(Eval(after,State(charging:false,plugged:true,battery:80),t0.AddHours(1)).Item2.Count==0,"Finished charging is announced once");
        Check(Eval(m,State(charging:false,battery:80),t0,allow:k=>k!=NoticeKind.Charge).Item2.Count==0,"A switched-off alert stays quiet");

        // Left unlocked or open, after ten minutes.
        var (o1,n1)=Eval(new AlertMemory(Seen:true),State(locked:false),t0);
        Check(n1.Count==0&&o1.OpenSince==t0,"Unlocked: starts the ten-minute timer");
        Check(Eval(o1,State(locked:false),t0.AddMinutes(9)).Item2.Count==0,"Nothing before ten minutes");
        var (o2,n2)=Eval(o1,State(locked:false,door:true,window:true),t0.AddMinutes(10));
        Check(n2.Single().Title=="Flareon is unlocked"&&n2[0].Body=="Unlocked, a door open, a window open."&&o2.OpenAlerted,"After ten minutes: names what's open");
        Check(Eval(o2,State(locked:false),t0.AddHours(2)).Item2.Count==0,"Announced once while it stays open");
        var (closed,_)=Eval(o2,State(locked:true),t0.AddHours(3));
        Check(closed.OpenSince==null&&!closed.OpenAlerted,"Locking resets it");
        var (home,homeNotices)=Eval(new AlertMemory(Seen:true),State(locked:false),t0,ignored:true);
        Check(homeNotices.Count==0&&home.OpenSince==null&&Eval(home,State(locked:false),t0.AddHours(1),ignored:true).Item2.Count==0,"Unlocked at an ignored place never alerts");
        var (gate,_)=Eval(new AlertMemory(Seen:true),State(locked:false,tailgate:true),t0,ignored:true);
        Check(Eval(gate,State(locked:false,tailgate:true),t0.AddMinutes(11),ignored:true).Item2.Single() is {Title:"Flareon is open",Body:"Tailgate open."},"An open tailgate still alerts at an ignored place");

        // 12-volt battery with a gap.
        var (b1,bn1)=Eval(new AlertMemory(Seen:true),State(v12:39),t0);
        Check(bn1.Single().Title=="Flareon's 12-volt battery is low"&&bn1[0].Body.StartsWith("At 39%")&&b1.Low12V,"Low below 40%");
        var (b2,bn2)=Eval(b1,State(v12:45),t0);var (b3,bn3)=Eval(b2,State(v12:38),t0);
        Check(bn2.Count==0&&bn3.Count==0&&b3.Low12V,"No repeat between 40% and 50%");
        var (b4,_)=Eval(b3,State(v12:51),t0);
        Check(!b4.Low12V&&Eval(b4,State(v12:39),t0).Item2.Count==1,"Can alert again after recovering above 50%");
        Check(Eval(new AlertMemory(Seen:true),State(v12:null),t0).Item2.Count==0,"Unknown 12-volt level never alerts");

        // Tires.
        var (tw,tn)=Eval(new AlertMemory(Seen:true),State(tire:true),t0);
        Check(tn.Single().Title=="Flareon: check tire pressure"&&Eval(tw,State(tire:true),t0).Item2.Count==0,"Tire warning once when it comes on");
        Check(Eval(tw,State(tire:null),t0).Item1.TireWarning,"An unreported tire warning keeps the last known");
        Check(Eval(null,State(locked:false),t0,allow:_=>true).Item2.Count==0&&BackgroundAlerts.Evaluate(null,State(),t0,"",false,all).Item1.Seen,"An empty car name still works");
        Check(Eval(new AlertMemory(Seen:true,Charging:true),State(charging:false,battery:80),t0).Item2.Single().Title.StartsWith("Flareon"),"Notices use the car's name");
        Check(BackgroundAlerts.Evaluate(new AlertMemory(Seen:true,Charging:true),State(charging:false,battery:80),t0,"",false,all).Item2.Single().Title=="Your car finished charging","No name: says your car");

        // The settings file.
        var file=new AlertFile(true,true,false,true,true,"Flareon",o2);
        var back=AlertFile.Decode(file.Encode());
        Check(back==file,"The alerts file round-trips");
        Check(!back.Allows(NoticeKind.Open)&&back.Allows(NoticeKind.Charge)&&!new AlertFile().Allows(NoticeKind.Charge),"Allows follows the switches, and alerts start off");
        Check(AlertFile.Decode("not json")==new AlertFile()&&AlertFile.Decode("")==new AlertFile()&&AlertFile.Decode("{\"version\":9}")==new AlertFile(),"A damaged file reads as alerts off");
        Check(!file.Encode().Contains("@")&&!file.Encode().Contains("lat"),"The file holds no login or location");

        // The background check, against the fake Hyundai server.
        string folder=Path.Combine(Path.GetTempPath(),"nctrl-alerts-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            string account=new SavedAccount(DemoHyundai.Email,"demo","0000").Encode();
            string Request(string? session)=>RequestJson.Object(w=>{
                w.WriteString("account",account);if(session!=null)w.WriteString("session",session);else w.WriteNull("session");
                w.WriteString("places","");w.WriteString("folder",folder);});
            string alerts=Path.Combine(folder,AlertFile.Name);
            var demo=new DemoHyundai(Array.Empty<byte>());
            string off=await AlertCheck.CheckJsonAsync(Request(null),demo,default,t0);
            Check(off.Contains("\"skipped\":\"alerts are off\"")&&demo.Requests==0,"Alerts off: the check sends nothing");
            File.WriteAllText(alerts,new AlertFile(On:true,Car:"Flareon").Encode());
            string owner=SharedSession.OwnerOf(Brand.Hyundai,VehicleLink.AccountKey(DemoHyundai.Email));
            string session;
            using(var app=new HyundaiUsClient(new DemoHyundai(Array.Empty<byte>()),null,new RequestBudget(new MemoryBudgetStore()))) {
                var cars=await app.LoginAsync(DemoHyundai.Email,"demo","0000",default);await app.SelectAsync(cars[0],default);
                session=(app.ExportSession()! with {Owner=owner}).Encode();
            }
            string first=await AlertCheck.CheckJsonAsync(Request(session),demo,default,t0);
            using(var r=JsonDocument.Parse(first))
                Check(r.RootElement.GetProperty("ok").GetBoolean()&&r.RootElement.GetProperty("notices").GetArrayLength()==0&&demo.Requests==1&&
                    SharedSession.Decode(r.RootElement.GetProperty("session").GetString()!)?.BelongsTo(Brand.Hyundai,VehicleLink.AccountKey(DemoHyundai.Email))==true,
                    "With this login's session: one stored-status read, nothing to say yet, the session handed back");
            Check(AlertFile.Decode(File.ReadAllText(alerts)).Memory?.Seen==true,"The check remembers what it saw");
            var other=SharedSession.Decode(session)! with {Owner=SharedSession.OwnerOf(Brand.Hyundai,VehicleLink.AccountKey("someone.else@example.invalid"))};
            File.Delete(Path.Combine(folder,AlertCheck.LimitsFile)); // The read cooldown from the check just above.
            var fresh=new DemoHyundai(Array.Empty<byte>());
            using(var r=JsonDocument.Parse(await AlertCheck.CheckJsonAsync(Request(other.Encode()),fresh,default,t0.AddMinutes(1))))
                Check(r.RootElement.GetProperty("ok").GetBoolean()&&fresh.Requests>1,"Another login's session is ignored: signs in with the saved login");
            // Near today's read limit it keeps the rest for you.
            var budget=new RequestBudget(new PathBudgetStore(Path.Combine(folder,AlertCheck.LimitsFile)));
            int cap=budget.Status(RequestKind.Read).DailyCap;
            File.WriteAllText(Path.Combine(folder,AlertCheck.LimitsFile),"");
            var spent=new RequestBudget(new PathBudgetStore(Path.Combine(folder,AlertCheck.LimitsFile)),new FixedClock(DateTimeOffset.UtcNow));
            for(int i=0;i<cap-AlertCheck.ReadsKeptForYou;i++)spent.Acquire(RequestKind.Read,enforceCooldown:false);
            var quiet=new DemoHyundai(Array.Empty<byte>());
            string held=await AlertCheck.CheckJsonAsync(Request(session),quiet,default,t0);
            Check(held.Contains("keeping the rest")&&quiet.Requests==0,"Near the daily read limit the check skips itself");
            File.Delete(Path.Combine(folder,AlertCheck.LimitsFile));
            Check((await AlertCheck.CheckJsonAsync("garbage",quiet,default,t0)).Contains("\"ok\":false")&&quiet.Requests==0,"A damaged request sends nothing");
            File.WriteAllText(alerts,new AlertFile(On:true).Encode());
            AlertCheck.Remember(folder,State(charging:true),false,"Flareon",t0);
            Check(AlertFile.Decode(File.ReadAllText(alerts)) is {Car:"Flareon",Memory.Charging:true},"The app's reads update the memory silently");
        } finally {Directory.Delete(folder,true);}
        Console.WriteLine($"PASS: {count} background alert checks (fake server, no network).");
    }
    sealed class FixedClock(DateTimeOffset now) : TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
}
