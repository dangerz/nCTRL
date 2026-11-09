using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>What a car service offers, so screens can hide what it can't do. Names are shown to the user.</summary>
/// <param name="ServiceName">The service as people know it, e.g. "Kia Connect".</param>
/// <param name="Maker">The car maker, e.g. "Kia".</param>
/// <param name="NeedsPin">Sign-in asks for a four-digit service PIN.</param>
/// <param name="OneTimeCode">Sign-in may ask for a one-time code (see <see cref="IOneTimeCodeSignIn"/>).</param>
public sealed record ServiceInfo(string ServiceName,string Maker,bool NeedsPin,bool OneTimeCode,
    bool LightsAndHorn,bool SurroundView,bool SendToCar,bool FrontSeats,bool RequestLocation,bool Heading);

/// <summary>A connection to one car maker's US service. The UI talks to this, never to a concrete client, so more
/// than one service can live in the app. Failures are <see cref="BluelinkException"/> (the historical name; it is the
/// shared failure type for every service).</summary>
public interface IVehicleConnection : IDisposable
{
    ServiceInfo Service { get; }
    bool IsAuthenticated { get; }
    bool SessionValid { get; }
    EnrolledVehicle? Selected { get; }
    LiveVehicleState? State { get; }
    JsonElement? RawStatus { get; }
    RemoteReceipt? Pending { get; }
    VehicleLocation? Location { get; }
    SurroundView? SurroundView { get; }
    bool CanRequestLocation { get; }
    RequestBudget Budget { get; }

    SharedSession? ExportSession();
    void ResumeSession(string email,string secret,string servicePin,SharedSession session);
    void UseVehicle(EnrolledVehicle vehicle);
    Task<IReadOnlyList<EnrolledVehicle>> LoginAsync(string email,string secret,string servicePin,CancellationToken ct,bool implicitSignIn=false);
    Task SelectAsync(EnrolledVehicle vehicle,CancellationToken ct);
    Task<LiveVehicleState> ReadAsync(bool wakeVehicle,CancellationToken ct);
    Task<RemoteReceipt> ExecuteAsync(VehicleCommand command,int? dcLimit,CancellationToken ct);
    Task<string?> SendDestinationAsync(Destination place,CancellationToken ct);
    Task<RemoteReceipt> CheckCommandAsync(CancellationToken ct);
    Task MonitorCommandAsync(VehicleCommand command,int? dcLimit,LiveVehicleState? before,Action updated,CancellationToken ct);
    Task<SurroundView> CaptureSurroundViewAsync(Action<int>? checkedCount,CancellationToken ct);
    Task<VehicleLocation> LocateAsync(CancellationToken ct);
}

public enum CodeChannel { Email, Text }
/// <summary>Where the service can send a one-time code. Addresses are masked by the service.</summary>
public sealed record CodeDestinations(string? Email,string? Phone)
{
    public bool HasEmail=>!string.IsNullOrEmpty(Email);
    public bool HasPhone=>!string.IsNullOrEmpty(Phone);
}
/// <summary>Sign-in stopped because the service wants a one-time code. Nothing else was sent.</summary>
public sealed class OneTimeCodeRequiredException(CodeDestinations destinations)
    : BluelinkException($"{Brand.Maker} wants to confirm it's you: enter the one-time code it sends.")
{
    public CodeDestinations Destinations { get; }=destinations;
}

/// <summary>Services whose sign-in can ask for a one-time code. After <see cref="IVehicleConnection.LoginAsync"/>
/// throws <see cref="OneTimeCodeRequiredException"/>, send the code and verify it; verifying finishes the sign-in.
/// A remember token then lets later sign-ins skip the code; keep it with the saved login.</summary>
public interface IOneTimeCodeSignIn
{
    /// <summary>The token that lets this device skip the code, or "" without one. Secret.</summary>
    string RememberToken { get; set; }
    /// <summary>The device id the remember token belongs to. Not secret, but must stay with the token.</summary>
    string DeviceId { get; set; }
    Task SendCodeAsync(CodeChannel channel,CancellationToken ct);
    Task<IReadOnlyList<EnrolledVehicle>> VerifyCodeAsync(string code,CancellationToken ct);
}
