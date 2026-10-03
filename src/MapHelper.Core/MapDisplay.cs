namespace MapHelper.Core;

/// <summary>Persisted map overlays; compass bearings use north as 0 degrees, increasing clockwise.</summary>
public sealed record MapDisplayOptions(bool ShowAllies = true, bool SquadVectors = true, bool TargetVector = true,
    bool ShowCompass = true, int CompassLineCount = 4, bool ShowCompassDegrees = false);
public sealed record ContactVector(long TrackId, Vec2 Start, Vec2 End, bool IsTarget, bool Estimated);
public sealed record NavigationFocus(long TrackId, Vec2 Position, bool Estimated, bool ApiMarked);

public static class MapDisplay
{
    /// <summary>True only for valid tank indicators and a live own ground marker.</summary>
    public static bool IsGroundVehicleContext(MapScene scene) => scene.Live
        && scene.Player is { Valid: true, Army: "tank" }
        && scene.Contacts.Any(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live
            && c.Observation.Type is "ground_model" or "tank");

    // Retain invisible tank spawn anchors independently of the allied-unit display filter.
    public static MapScene ApplyVisibility(MapScene scene, MapDisplayOptions options) => options.ShowAllies ? scene
        : scene with
        {
            Contacts = scene.Contacts.Where(c => c.Observation.Affiliation != Affiliation.Ally
            || c.Observation.Type == "respawn_base_tank").ToArray()
        };

    /// <summary>Returns drawable contacts without changing the source scene used for camera planning.</summary>
    /// <remarks>Tank spawns identify the ground battlefield even while the own vehicle is unavailable.</remarks>
    public static MapScene ApplyMarkerVisibility(MapScene scene) => IsGroundVehicleContext(scene)
        || scene.Contacts.Any(c => c.Observation.Type == "respawn_base_tank")
        ? scene with
        {
            Contacts = scene.Contacts.Where(c => c.Observation.Affiliation == Affiliation.Self
            || !c.Observation.Type.StartsWith("respawn_base_", StringComparison.Ordinal)).ToArray()
        }
        : scene;

    public static NavigationFocus? FocusTarget(MapScene scene)
    {
        if (!scene.Live) return null;
        var marked = scene.Contacts.Where(c => c.Observation.Affiliation == Affiliation.Enemy && c.Observation.ApiTargetMarked
            && c.Phase == ContactPhase.Live && !c.Observation.Destroyed && c.Position.IsFinite).Take(2).ToArray();
        // Multiple API-marked objects are not a unique selection. Retain all their visual
        // markers and fall back to the deterministic course-nearest target for navigation.
        if (marked.Length == 1) return new(marked[0].TrackId, marked[0].Position, false, true);
        var course = TargetNavigation.Calculate(scene, new()).FirstOrDefault(r => r.IsCourseTarget);
        return course == null ? null : new(course.TrackId, course.Position, course.Estimated, false);
    }

    public static IReadOnlyList<ContactVector> Vectors(MapScene scene, MapDisplayOptions options)
    {
        if (!scene.Live || scene.Map is not { IsUsable: true } map) return [];
        var targetId = options.TargetVector ? FocusTarget(scene)?.TrackId : null;
        return scene.Contacts.Where(c => c.Observation.IsMobile && !c.Observation.Destroyed
                && (c.Phase == ContactPhase.Live || c.Phase == ContactPhase.Predicted && c.Age < ContactTracker.GhostSeconds)
                && c.Direction is { IsFinite: true, Length: > 1e-9 } && c.Position.IsFinite
                && (options.SquadVectors && c.Observation.Affiliation == Affiliation.Squad || c.TrackId == targetId))
            .Select(c => new ContactVector(c.TrackId, c.Position, TargetNavigation.CourseEnd(c.Position, c.Direction!.Value, map),
                c.TrackId == targetId, c.Phase == ContactPhase.Predicted)).ToArray();
    }
}
