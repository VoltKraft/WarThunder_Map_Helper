using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapHelper.Core;

public readonly record struct Vec2(double X, double Y)
{
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    [JsonIgnore] public Vec2 Normalized => Length > 1e-9 ? this / Length : default;
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);
}

public enum Affiliation { Unknown, Ally, Enemy, Self, Squad }
public enum ContactPhase { Live, Predicted, Stale, Destroyed }
public enum Endpoint { MapInfo, Objects, State, Indicators, Mission, Events, MapImage }

/// <summary>Map bounds and grid metadata supplied by the game; world distances are in meters.</summary>
/// <remarks>Check <see cref="IsUsable"/> before converting normalized positions into physical distances.</remarks>
public sealed record MapInfo(string Key, int Generation, Vec2 Min, Vec2 Max, Vec2 GridStep, Vec2 GridZero)
{
    [JsonIgnore] public Vec2 Size => Max - Min;
    [JsonIgnore] public bool IsUsable => Min.IsFinite && Max.IsFinite && Size.X > 0 && Size.Y > 0;
    public Vec2 ToWorld(Vec2 normalized) => new(Min.X + normalized.X * Size.X, Min.Y + normalized.Y * Size.Y);
    public Vec2 ToNormalized(Vec2 world) => new((world.X - Min.X) / Size.X, (world.Y - Min.Y) / Size.Y);
}

public sealed record MapObservation(
    string? SourceId, string Type, string Icon, Affiliation Affiliation, string Color,
    Vec2 Position, Vec2? Direction = null, Vec2? EndPosition = null, bool? IsHuman = null,
    bool Destroyed = false, IReadOnlyDictionary<string, JsonElement>? Raw = null, string? IdentityBasis = null,
    string? BackgroundIcon = null)
{
    // A directly supplied target-style background is a map cue. It does not prove a radar
    // lock or the user's personal selection; never infer either from blink or team color.
    [JsonIgnore]
    public bool ApiTargetMarked => !string.IsNullOrWhiteSpace(Icon) && Icon != "none"
        && string.Equals(BackgroundIcon, Icon + "Target", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsMobile => Type is "aircraft" or "air_model" or "ground_model" or "tank" or "ship" or "ship_model" || Affiliation == Affiliation.Self;
    [JsonIgnore]
    public string IconClass => Affiliation == Affiliation.Self ? "Player" : Type switch
    {
        "aircraft" or "air_model" => "Aircraft",
        "ground_model" when Icon.ToLowerInvariant() is "boat" or "frigate" or "missiledestroyer" or "ship"
            or "missilecorvette" or "missilelightcruiser" or "aircraftcarrier" => "Ship",
        "ground_model" or "tank" => "Ground",
        "ship" or "ship_model" => "Ship",
        _ => "Objective"
    };
}

public sealed record Contact(long TrackId, MapObservation Observation, Vec2 Position, Vec2? Velocity,
    Vec2? Direction, double LastSeen, double Age, ContactPhase Phase, double? DestroyedAt, bool Ambiguous);
public sealed record WeaponTelemetry(IReadOnlyDictionary<string, double> Ammunition, IReadOnlyDictionary<string, JsonElement> Raw)
{
    public static WeaponTelemetry Empty { get; } = new(new Dictionary<string, double>(), new Dictionary<string, JsonElement>());
}
public sealed record PlayerTelemetry(bool Valid, string? Vehicle, double? FuelKg, double? TrueAirspeedKmh,
    double? AltitudeM, double? VerifiedConsumptionKgS, WeaponTelemetry Weapons,
    IReadOnlyDictionary<string, JsonElement>? Raw = null);
public sealed record CombatEvent(string Id, string Text, string? TargetId = null, bool ConfirmedDestruction = false);

/// <summary>A received endpoint update associated with one map epoch.</summary>
/// <remarks>
/// Time is the monotonic receipt time in seconds, never wall-clock time. Consumers must discard
/// obsolete epochs. Missing optional fields remain unknown; a failed packet is not an empty success.
/// Raw JSON and game-provided text are untrusted, and may retain the game's original language.
/// </remarks>
public sealed record TelemetryPacket(Endpoint Endpoint, double Time, long Epoch, bool Success = true,
    MapInfo? Map = null, IReadOnlyList<MapObservation>? Objects = null, PlayerTelemetry? Player = null,
    IReadOnlyList<CombatEvent>? Events = null, byte[]? Image = null, string? MissionStatus = null,
    string? Error = null, string? RawJson = null);

/// <summary>Owns a single asynchronous stream of live or explicitly synthetic telemetry.</summary>
/// <remarks>
/// Use one active enumeration per instance. The caller cancels and awaits that enumeration before
/// disposing the source. Implementations release their resources on disposal; consumers marshal
/// packets to their owning thread rather than sharing a mutable map engine between polling tasks.
/// </remarks>
public interface ITelemetrySource : IAsyncDisposable
{
    /// <summary>Yields endpoint updates until the caller cancels or the source stops.</summary>
    /// <param name="cancellationToken">Cancels polling and pending reads.</param>
    /// <returns>A stream whose receipt times use a common monotonic clock.</returns>
    /// <exception cref="OperationCanceledException">The caller or source cancels the stream.</exception>
    IAsyncEnumerable<TelemetryPacket> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record RangeEstimate(double Seconds, double Meters, double ConsumptionKgS, double WindowSeconds, bool Extended);
public sealed record MapScene(MapInfo? Map, IReadOnlyList<Contact> Contacts, RangeEstimate? Range,
    PlayerTelemetry? Player, string Status, bool Live, double Time, IReadOnlyList<string> Events, long SessionId = 0);
