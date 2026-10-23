using System;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class Program
{
    static async Task Main()
    {
        var c = new DemoHyundaiClient { LatencyMs = 1 };
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        Task<CommandResult> Send(ActionKind k, int v=0, bool on=false)
            => c.ExecuteAsync(new(k,v,on), CancellationToken.None);
        var before = c.State;
        c.FailNext = true;
        Check(!(await Send(ActionKind.Unlock)).Success && c.State == before, "Failure preserves state");
        c.Offline = true;
        Check(!(await Send(ActionKind.Unlock)).Success && c.State == before, "Offline preserves state");
        c.Offline = false;
        Check((await Send(ActionKind.Unlock)).Success && !c.State.Locked, "Unlock");
        Check((await Send(ActionKind.Lock)).Success && c.State.Locked, "Lock");
        Check(!(await Send(ActionKind.Climate,90,true)).Success && !c.State.ClimateOn, "Invalid temperature");
        Check((await Send(ActionKind.Climate,78,true)).Success && c.State.TargetF == 78, "Climate preset");
        Check((await Send(ActionKind.Climate,78,false)).Success && !c.State.ClimateOn, "Stop climate");
        c.SetPluggedIn(false);
        Check(!(await Send(ActionKind.Charge,0,true)).Success && !c.State.Charging, "Unplugged charge rejected");
        c.SetPluggedIn(true);
        Check((await Send(ActionKind.Charge,0,true)).Success && c.State.Charging, "Start charging");
        Check((await Send(ActionKind.ChargeLimit,70)).Success && !c.State.Charging, "Reached target stops charging");
        Check(!(await Send(ActionKind.Charge,0,true)).Success, "Reached target cannot restart");
        Check(!(await Send(ActionKind.ChargeLimit,85)).Success && c.State.ChargeLimit==70, "Invalid limit");
        await Send(ActionKind.ChargeLimit,90); await Send(ActionKind.Charge,0,true);
        c.SetPluggedIn(false);
        Check(!c.State.Charging, "Unplug stops charging");
        before=c.State;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await c.ExecuteAsync(new(ActionKind.Unlock),cancel.Token); throw new Exception("Cancellation missing"); }
        catch(OperationCanceledException) { Check(c.State==before,"Cancellation preserves state"); }
        Check((await Send(ActionKind.Lock)).Success,"Gate usable after cancellation");
        c.Reset(); Check(c.State==new VehicleState(),"Reset");
        Console.WriteLine($"PASS: {checks} vehicle behavior checks.");
        await ProtocolTests.Run();
        await VaultTests.Run();
    }
}
