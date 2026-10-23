using System;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

public record VehicleState(
    bool Locked = true, bool ClimateOn = false, int TargetF = 72,
    bool PluggedIn = true, bool Charging = false, int Battery = 78,
    int ChargeLimit = 80, int RangeMiles = 184, bool Defrost = false);

public enum ActionKind { Lock, Unlock, Climate, Charge, ChargeLimit, Refresh }
public record VehicleCommand(ActionKind Kind, int Value = 0, bool Enabled = false);
public record CommandResult(bool Success, string Message, VehicleState State);

public interface IHyundaiClient
{
    Task<VehicleState> ReadAsync(CancellationToken token);
    Task<CommandResult> ExecuteAsync(VehicleCommand command, CancellationToken token);
}

/// <summary>
/// Deterministic simulated vehicle. No network calls, credentials or vehicle access.
/// Replace this implementation with a verified direct C# Hyundai US adapter.
/// </summary>
public sealed class DemoHyundaiClient : IHyundaiClient
{
    public VehicleState State { get; private set; } = new();
    public bool Offline { get; set; }
    public bool FailNext { get; set; }
    public int LatencyMs { get; set; } = 1100;
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<VehicleState> ReadAsync(CancellationToken token)
    {
        await Task.Delay(LatencyMs, token);
        if (Offline) throw new InvalidOperationException("Offline — showing the last saved demo state.");
        return State;
    }

    public void SetPluggedIn(bool value)
        => State = State with { PluggedIn = value, Charging = value && State.Charging };

    public void Reset() { State = new(); Offline = false; FailNext = false; }

    public async Task<CommandResult> ExecuteAsync(VehicleCommand command, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            await Task.Delay(LatencyMs, token);
            if (Offline) return new(false, "Offline. No command was sent.", State);
            if (FailNext) { FailNext = false; return new(false, "Demo request failed. Vehicle state is unchanged.", State); }
            switch (command.Kind)
            {
                case ActionKind.Lock: State = State with { Locked = true }; break;
                case ActionKind.Unlock: State = State with { Locked = false }; break;
                case ActionKind.Climate:
                    if (command.Value < 62 || command.Value > 82)
                        return new(false, "Choose a temperature between 62 and 82°F.", State);
                    State = State with { ClimateOn = command.Enabled, TargetF = command.Value }; break;
                case ActionKind.Charge:
                    if (command.Enabled && !State.PluggedIn) return new(false, "Plug in the demo car before charging.", State);
                    if (command.Enabled && State.Battery >= State.ChargeLimit) return new(false, "Charge target already reached.", State);
                    State = State with { Charging = command.Enabled }; break;
                case ActionKind.ChargeLimit:
                    if (command.Value < 50 || command.Value > 100 || command.Value % 10 != 0)
                        return new(false, "Charge target must be 50–100% in 10% steps.", State);
                    State = State with { ChargeLimit = command.Value,
                        Charging = State.Charging && State.Battery < command.Value }; break;
                case ActionKind.Refresh: break;
            }
            return new(true, command.Kind switch
            {
                ActionKind.Lock => "Doors locked",
                ActionKind.Unlock => "Doors unlocked",
                ActionKind.Climate => State.ClimateOn ? $"Climate set to {State.TargetF}°F" : "Climate stopped",
                ActionKind.Charge => State.Charging ? "Charging started" : "Charging paused",
                ActionKind.ChargeLimit => $"Charge target saved · {State.ChargeLimit}%",
                _ => "Demo state refreshed"
            }, State);
        }
        finally { gate.Release(); }
    }
}
