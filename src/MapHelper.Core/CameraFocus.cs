namespace MapHelper.Core;

public sealed record CameraOptions(bool IncludeAi = true, bool IncludeBases = true);
public sealed record EnemyPosition(Vec2 Position, double LastSeen, long TrackId);
public sealed record CameraFocus(IReadOnlyList<Vec2> Points, EnemyPosition? Fallback);

public sealed class CameraFocusPlanner
{
    private EnemyPosition? _lastEnemy;
    private (string? Map, long Session)? _session;
    private bool _groundFocus;
    public void Reset() { _session = null; _lastEnemy = null; }

    public static bool Included(MapObservation observation, CameraOptions options) => observation.Affiliation == Affiliation.Self
        || (options.IncludeAi || observation.IsHuman != false) && (options.IncludeBases || observation.IsMobile);

    public CameraFocus Update(MapScene scene, CameraOptions options)
    {
        var session = (scene.Map?.Key, scene.SessionId);
        if (_session != session) { _session = session; _lastEnemy = null; }
        if (scene.Map is not { IsUsable: true } map) return new([], null);
        if (!scene.Live) _lastEnemy = null; // An outage is not evidence that the enemy disappeared.
        var player = scene.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
        // Army identifies the current vehicle, not the mission rules. Require a matching
        // live own marker so stale tank indicators cannot constrain an aircraft respawn.
        var groundFocus = MapDisplay.IsGroundVehicleContext(scene);
        if (groundFocus != _groundFocus) { _groundFocus = groundFocus; _lastEnemy = null; }
        bool InFocus(Contact contact) => !groundFocus || InGroundFocus(contact);
        var eligibleEnemies = scene.Contacts.Where(c => c.Observation.Affiliation == Affiliation.Enemy
            && c.Observation.IsMobile && c.Observation.IsHuman != false && !c.Observation.Destroyed && c.Position.IsFinite && InFocus(c)).ToArray();
        if (_lastEnemy != null && scene.Contacts.Any(c => c.TrackId == _lastEnemy.TrackId && !InFocus(c))) _lastEnemy = null;
        if (scene.Live)
        {
            var latest = eligibleEnemies.Where(c => c.Phase == ContactPhase.Live).OrderByDescending(c => c.LastSeen)
                .ThenBy(c => c.TrackId == _lastEnemy?.TrackId ? 0 : 1)
                .ThenBy(c => player == null ? 0 : (c.Position - player.Position).Length).ThenBy(c => c.TrackId).FirstOrDefault();
            if (latest != null) _lastEnemy = new(latest.Position, latest.LastSeen, latest.TrackId);
        }
        // Tank spawns define the battlefield extent even when mission markers are disabled.
        var points = scene.Contacts.Where(c => (Included(c.Observation, options)
                || groundFocus && c.Observation.Type == "respawn_base_tank") && InFocus(c))
            .SelectMany(c => c.Observation.EndPosition is { } end ? new[] { c.Position, map.ToWorld(end) } : new[] { c.Position })
            .Where(p => p.IsFinite).ToList();
        // The existing two-second prediction remains visible first. Afterwards use the actual
        // last sighting as a camera anchor, never as a fabricated or indefinitely moving contact.
        var fallback = !options.IncludeAi && !options.IncludeBases && scene.Live && player != null
            && !eligibleEnemies.Any(c => c.Phase is ContactPhase.Live or ContactPhase.Predicted) ? _lastEnemy : null;
        if (fallback != null) points.Add(fallback.Position);
        return new(points, fallback);
    }

    private static bool InGroundFocus(Contact contact)
    {
        var observation = contact.Observation;
        if (observation.Affiliation == Affiliation.Self) return true;
        return observation.Type is not ("aircraft" or "air_model" or "airfield" or "respawn_base_fighter" or "respawn_base_bomber");
    }
}
