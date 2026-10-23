namespace NGarage.Core;

public enum ActionKind { Lock, Unlock, Climate, Charge, ChargeLimit }
public record VehicleCommand(ActionKind Kind, int Value = 0, bool Enabled = false);
