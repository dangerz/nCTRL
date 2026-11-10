using Godot;
using NGarage.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

// Alerts when nCTRL is closed: iOS background refresh runs Core/AlertCheck.cs now and then (Native/iOS/NGarageVault.mm).
// The settings and what the last check saw live in the App Group file alerts.json, shared with that check.
public partial class Main
{
    private string? alertsFolderFixture;
    private string AlertsFolder=>alertsFolderFixture??NativeVaultBridge.GroupPath();
    private AlertFile LoadAlerts()
    {
        try {string path=Path.Combine(AlertsFolder,AlertFile.Name);return AlertsFolder.Length>0&&File.Exists(path)?AlertFile.Decode(File.ReadAllText(path)):new();}
        catch(Exception){return new();}
    }
    private bool SaveAlerts(AlertFile file)
    {
        if(AlertsFolder.Length==0)return false;
        try {
            string path=Path.Combine(AlertsFolder,AlertFile.Name),temporary=path+".tmp";
            File.WriteAllText(temporary,file.Encode());File.Move(temporary,path,true);return true;
        } catch(Exception){return false;}
    }
    // After each read in the app: what you've seen here isn't announced again by a later background check.
    private void RememberAlerts()
    {
        if(demo||!liveMode||CurrentState is not {} state||AlertsFolder.Length==0)return;
        try {AlertCheck.Remember(AlertsFolder,state,AtIgnoredPlace,CarTitle,DateTimeOffset.UtcNow);} catch(Exception) { }
    }
    // After a sign-in with the password: background checks paused by a refused login start again.
    private void ResumeAlerts()
    {
        if(demo||AlertsFolder.Length==0)return;
        try {AlertCheck.Resume(AlertsFolder);} catch(Exception) { }
    }
    private void ForgetAlerts()
    {
        if(AlertsFolder.Length==0)return;
        try {File.Delete(Path.Combine(AlertsFolder,AlertFile.Name));} catch(Exception) { }
    }

    private void AlertsScreen()
    {
        var file=LoadAlerts();
        Title("ALERTS WHEN CLOSED.",$"From {Brand.Maker}'s stored status · never wakes the car");
        bool available=AlertsFolder.Length>0&&vault.Available&&!demo;
        LiveButton(24,196,412,48,"Alerts  /  "+(file.On?"On":"Off"),14,()=>_ = ToggleAlerts(!file.On),!file.On,"AlertsOn").Disabled|=!available;
        var kinds=new (string Label,bool Value,Func<AlertFile,bool,AlertFile> Set,string Name)[] {
            ("Charging finished, stopped early or unplugged",file.Charge,(f,v)=>f with {Charge=v},"AlertCharge"),
            ("Left unlocked or open for 10 minutes",file.Open,(f,v)=>f with {Open=v},"AlertOpen"),
            ("12-volt battery low (below 40%)",file.Battery12V,(f,v)=>f with {Battery12V=v},"AlertBattery"),
            ("Tire pressure warning",file.Tires,(f,v)=>f with {Tires=v},"AlertTires")};
        int y=258;
        foreach(var (label,value,set,name) in kinds) {
            var box=new CheckBox {Name=name,Position=new(24,y),Size=new(412,38),Text=label,ButtonPressed=value,Disabled=!file.On||!available};
            box.AddThemeFontOverride("font",regular);box.AddThemeFontSizeOverride("font_size",13);
            box.Toggled+=on=>{SaveAlerts(set(LoadAlerts(),on));};
            canvas.AddChild(box);y+=42;
        }
        LiveButton(24,y+8,412,44,"Check now",13,()=>_ = CheckAlertsNow(),false,"AlertsCheckNow").Disabled|=!file.On||!available;
        Label(24,y+64,412,190,(available?"":"Alerts work in the iPhone app with a saved login. ")+
            (file.On&&file.Paused?$"Paused: {Brand.Maker} refused the saved login. Checks start again the next time {Brand.App} signs in with your password. ":"")+
            "iOS decides when to check: usually every 15 minutes to a few hours, more often when you use your phone. "+
            "Checks run only while your phone is unlocked, and stop if you swipe nCTRL away in the app switcher. "+
            $"Each check reads the status {Brand.Maker} already has, without waking the car, and counts toward your request limits; "+
            "near today's limit it skips itself. Unlocked at an ignored place, like Home, doesn't alert.",11,Muted,false,true).Name="AlertsHelp";
        Button(24,718,412,46,"Back to settings",13,()=>Navigate("Settings"),false,"AlertsBack");
    }
    private async Task ToggleAlerts(bool on)
    {
        if(!on) {
            SaveAlerts(LoadAlerts() with {On=false,Memory=null});
            notice="Alerts off. Nothing is checked in the background.";noticeError=false;Render();return;
        }
        int version=liveEpoch;
        await RunLive(async()=>{
            bool allowed=await vault.AskNotificationsAsync(liveLifetime.Token);EnsureLive(version);
            if(!allowed){notice=$"Notifications are off for {Brand.App}. Turn them on in iPhone Settings → Notifications → {Brand.App}.";noticeError=true;return;}
            if(!SaveAlerts(LoadAlerts() with {On=true,Car=CarTitle,Memory=null})){notice="Couldn't save the alert settings.";noticeError=true;return;}
            RememberAlerts();
            notice="Alerts on. iOS checks now and then while the app is closed.";noticeError=false;
        },"Asking iOS for notifications…");
    }
    private async Task CheckAlertsNow()
    {
        int version=liveEpoch;
        await RunLive(async()=>{
            string json=await vault.CheckAlertsNowAsync(liveLifetime.Token);EnsureLive(version);
            notice=CheckResult(json);noticeError=!notice.StartsWith("Checked",StringComparison.Ordinal);
        },"Checking…");
    }
    private static string CheckResult(string json)
    {
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            string setup=r.TryGetProperty("registered",out var reg)&&reg.ValueKind==JsonValueKind.False?" Background checks aren't set up in this build, so only Check now works.":"";
            if(r.GetProperty("ok").GetBoolean()) {
                int count=r.TryGetProperty("notices",out var n)&&n.ValueKind==JsonValueKind.Array?n.GetArrayLength():0;
                return (count==0?"Checked: nothing new to tell you.":count==1?"Checked: sent 1 notification.":$"Checked: sent {count} notifications.")+setup;
            }
            return "Skipped: "+(r.TryGetProperty("skipped",out var why)?why.GetString():"unknown reason")+"."+setup;
        } catch(Exception){return "The check didn't answer.";}
    }
}
