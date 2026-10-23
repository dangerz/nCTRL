using Godot;
using NGarage.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

public partial class Main
{
    private sealed record PendingConfirm(string Title,string Message,string Confirm,Func<Task> Action);
    private PendingConfirm? pendingConfirm;
    private static readonly Color Amber=new("#e0a43a");

    // Car screen line under the range: "All closed and locked", or the most important alert as a strip that opens Check your car.
    private void HealthStrip(LiveVehicleState? s)
    {
        var alerts=HealthAlerts.From(s);
        if(alerts.Count==0) {
            Label(24,482,412,24,s?.Locked==null?"Door status unknown":s.Health==null?"Doors locked":"All closed and locked",13,White).Name="HealthStatus";
            return;
        }
        bool critical=alerts.Any(a=>a.Critical);
        var strip=Button(24,478,412,30,"⚠  "+alerts[0].Text+(alerts.Count>1?$"  ·  +{alerts.Count-1} more":"")+"  ›",12,()=>Navigate("Alerts"),false,"AlertStrip");
        foreach(string state in new[]{"normal","hover","pressed"})
            strip.AddThemeStyleboxOverride(state,Style(critical?new("#47232a"):new("#3a3020"),critical?Red:Amber));
        strip.Alignment=HorizontalAlignment.Left;
    }
    private void LiveAlerts()
    {
        var s=CurrentState;var alerts=HealthAlerts.From(s);
        Title("CHECK YOUR CAR.",Updated(s?.RetrievedAt)+" · last reported state");
        if(alerts.Count==0)Label(24,206,412,60,"Nothing needs attention.",16,White,true,true);
        int y=200;
        foreach(var alert in alerts) {
            if(y>650)break;
            Panel(24,y,412,54,new("#0e2031"),false);Rect(24,y,3,54,alert.Critical?Red:Amber);
            Label(42,y+4,alert.Kind==AlertKind.Unlocked?250:380,46,alert.Text,14,White,true,true);
            if(alert.Kind==AlertKind.Unlocked)
                CommandFeedback(RemoteButton(316,y+7,110,40,"Lock",13,()=>_ = SendLive(new(ActionKind.Lock)),true,"AlertLock"),ActionKind.Lock,false);
            y+=62;
        }
        Label(24,666,412,40,"Alerts come from Hyundai's last report and may lag the car. Unknown items never raise an alert.",11,Muted,false,true);
        Button(24,714,412,46,"Back to car",13,()=>Navigate("Car"),false,"AlertsBack");
    }
    // A weak 12-volt battery can leave the car unable to start, and waking the car uses it.
    private void WakeWithCheck()
    {
        if(HealthAlerts.Low12V(CurrentState)) {
            pendingConfirm=new("WAKE THE CAR?",$"The 12-volt battery is at {CurrentState!.Health!.Battery12V}%. Waking the car uses it, "+
                "and a weak 12-volt battery can stop the car from starting.","Wake anyway",()=>RefreshLive(true));
            Render();return;
        }
        _ = RefreshLive(true);
    }
    private void ConfirmDialog()
    {
        var p=pendingConfirm!;
        canvas.AddChild(new ColorRect {Name="ConfirmShade",Size=new(460,920),Color=new(0,0,0,.72f),MouseFilter=MouseFilterEnum.Stop});
        Panel(24,260,412,330,new("#0e2031"));
        Label(44,280,372,30,p.Title,18,Amber,true);
        Label(44,322,372,170,p.Message,13,White,false,true).VerticalAlignment=VerticalAlignment.Top;
        LiveButton(44,514,174,52,"Cancel",13,()=>{pendingConfirm=null;Render();},false,"ConfirmCancel");
        LiveButton(242,514,174,52,p.Confirm,13,()=>{var action=p.Action;pendingConfirm=null;Render();_ = action();},true,"ConfirmGo");
    }
}
