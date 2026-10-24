using Godot;
using NGarage.Core;
using System;
using System.Globalization;
using System.Threading.Tasks;

public partial class Main
{
    private RequestBudget budget=new(new MemoryBudgetStore());
    private const string LimitsFile="user://request-limits.json";
    // Counts and timestamps only; no account data.
    private sealed class FileBudgetStore(string path) : IRequestBudgetStore
    {
        public string Load(){using var file=FileAccess.Open(path,FileAccess.ModeFlags.Read);return file?.GetAsText()??"";}
        public void Save(string data){using var file=FileAccess.Open(path,FileAccess.ModeFlags.Write);file?.StoreString(data);}
    }
    // A user-initiated request our own limit refused. Retry may hold credentials in memory until
    // the prompt is answered; EndLiveSession clears it.
    private sealed class PendingOverride(RequestLimitException refusal,Func<Task> retry)
    {
        public RequestKind Kind=>refusal.Kind;public string Message=>refusal.Message;public Func<Task> Retry=>retry;
        public override string ToString()=>"PendingOverride [redacted]";
    }
    private PendingOverride? pendingOverride;

    private void OfferOverride(RequestLimitException refusal,Func<Task>? retry)
    {
        notice=refusal.Message;noticeError=true;
        if(refusal.Overridable&&retry!=null)pendingOverride=new(refusal,retry);
    }
    private void OverrideDialog()
    {
        var p=pendingOverride!;
        canvas.AddChild(new ColorRect {Name="OverrideShade",Size=new(460,920),Color=new(0,0,0,.72f),MouseFilter=MouseFilterEnum.Stop});
        Panel(24,236,412,400,new("#0e2031"));
        Label(44,256,372,30,"LIMIT REACHED",18,Red,true);
        Label(44,294,372,128,p.Message,13,White,false,true);
        Label(44,428,372,112,"Going past nCTRL's limits raises the chance that Hyundai rate-limits or bans your account. "+
            "Sending anyway covers only this one request and is recorded in Activity.",12,Muted,false,true);
        LiveButton(44,560,174,52,"Cancel",13,()=>{pendingOverride=null;notice="Nothing was sent.";noticeError=false;Render();},false,"OverrideCancel");
        LiveButton(242,560,174,52,deviceCheckRequired?"Send with "+vault.BiometricName:"Send anyway",13,()=>_ = ConfirmOverride(),true,"OverrideConfirm");
    }
    private async Task ConfirmOverride()
    {
        var pending=pendingOverride;
        if(pending==null||liveBusy)return;
        int version=liveEpoch;bool confirmed=false;
        await RunLive(async()=>{
            if(deviceCheckRequired) {
                try {await vault.AuthenticateAsync("Confirm sending one request beyond nCTRL's limit",liveLifetime.Token);}
                catch(VaultException) {
                    EnsureLive(version);pendingOverride=null;
                    notice="Override not confirmed. Nothing was sent.";noticeError=true;return;
                }
            }
            EnsureLive(version);
            pendingOverride=null;budget.AllowOnce(pending.Kind);confirmed=true;
            history.Add(("Limit override confirmed for one "+RequestBudget.Singular(pending.Kind)+".",DateTime.Now.ToString("h:mm tt")+" · Override",false));
        },"Waiting for confirmation…");
        if(confirmed&&version==liveEpoch)await pending.Retry();
    }
    private void LimitsScreen()
    {
        Title("REQUEST LIMITS.","nCTRL's own limits · Hyundai publishes no US quotas");
        int y=196;
        foreach(var kind in Enum.GetValues<RequestKind>())
        {
            var s=budget.Status(kind);int left=Math.Max(0,s.DailyCap-s.UsedToday);
            string name=RequestBudget.Label(kind);
            Panel(24,y,412,62,new("#0e2031"),false);Rect(24,y,3,62,s.Factor>1||left==0?Red:Blue);
            Label(42,y+8,200,24,char.ToUpperInvariant(name[0])+name[1..],14,White,true);
            Label(236,y+8,190,24,$"{left} of {s.DailyCap} left today",12,left==0?Red:Blue,true).HorizontalAlignment=HorizontalAlignment.Right;
            Label(42,y+34,384,24,(s.Cooldown==TimeSpan.Zero?"No cooldown":"One every "+RequestBudget.Duration(s.Cooldown))+
                (s.Factor>1?$" · tightened ×{s.Factor} until "+s.TightenedUntil.ToLocalTime().ToString("t",CultureInfo.CurrentCulture):""),11,Muted,false,true);
            y+=70;
        }
        var blocked=budget.HyundaiBlockedUntil;
        if(blocked>DateTimeOffset.UtcNow)
            Label(24,612,412,32,"Hyundai is rate limiting. Requests are paused until "+blocked.ToLocalTime().ToString("t",CultureInfo.CurrentCulture)+".",12,Red,true,true);
        Label(24,648,412,68,"Shared by every account on this device; daily counts reset at midnight. Hyundai rate limiting doubles that request's limits "+
            "for 24 hours, up to ×8. Overrides cover one request"+(deviceCheckRequired?" and need "+vault.BiometricName:"")+".",11,Muted,false,true);
        Button(24,722,412,46,"Back to settings",13,()=>Navigate("Settings"),false,"LimitsBack");
    }
}
