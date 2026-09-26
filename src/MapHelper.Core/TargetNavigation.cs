namespace MapHelper.Core;

public enum TargetCategory { Air, Ground, Base, Sea }

[Flags]
public enum TargetValues { None = 0, Distance = 1, Time = 2 }

public sealed record TargetDisplayOptions(
    TargetValues CourseTarget = TargetValues.Distance | TargetValues.Time,
    TargetValues Air = TargetValues.None,
    TargetValues Ground = TargetValues.None,
    TargetValues Bases = TargetValues.None,
    TargetValues Sea = TargetValues.None,
    TargetValues MarkedTarget = TargetValues.Distance | TargetValues.Time)
{
    public TargetValues ForCategory(TargetCategory category) => category switch
    {
        TargetCategory.Air => Air,
        TargetCategory.Ground => Ground,
        TargetCategory.Base => Bases,
        TargetCategory.Sea => Sea,
        _ => TargetValues.None
    };
}

public sealed record TargetReadout(long TrackId, Vec2 Position, TargetCategory Category, double DistanceMeters,
    double? Seconds, bool IsCourseTarget, bool Estimated, TargetValues Values, bool IsMarkedTarget = false);

public static class TargetNavigation
{
    public static TargetCategory? Category(MapObservation observation) => observation.Type.ToLowerInvariant() switch
    {
        "aircraft" or "air_model" => TargetCategory.Air,
        "ship" or "ship_model" => TargetCategory.Sea,
        // The live browser API also reports ships as ground_model.
        "ground_model" when observation.IconClass == "Ship" => TargetCategory.Sea,
        "ground_model" or "tank" => TargetCategory.Ground,
        "bombing_point" or "airfield" => TargetCategory.Base,
        _ => null
    };

    public static Vec2 CourseEnd(Vec2 position, Vec2 direction, MapInfo map)
    {
        if (!position.IsFinite || !direction.IsFinite || direction.Length < 1e-9) return position;
        var tx = direction.X > 0 ? (map.Max.X - position.X) / direction.X
            : direction.X < 0 ? (map.Min.X - position.X) / direction.X : double.PositiveInfinity;
        var ty = direction.Y > 0 ? (map.Max.Y - position.Y) / direction.Y
            : direction.Y < 0 ? (map.Min.Y - position.Y) / direction.Y : double.PositiveInfinity;
        return position + direction * Math.Max(0, Math.Min(tx, ty));
    }

    public static IReadOnlyList<TargetReadout> Calculate(MapScene scene, TargetDisplayOptions options)
    {
        if (!scene.Live || scene.Map is not { IsUsable: true } map) return [];
        var player = scene.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
        if (player == null || !player.Position.IsFinite) return [];
        var direction = player.Direction is { IsFinite: true, Length: > 1e-9 } heading ? heading.Normalized : (Vec2?)null;
        var courseLength = direction is { } d ? (CourseEnd(player.Position, d, map) - player.Position).Length : 0;
        var speed = !player.Ambiguous && player.Velocity is { IsFinite: true } velocity ? velocity.Length : 0;
        var candidates = new List<(Contact Contact, TargetCategory Category, Vec2 Position, double Distance, double SideDistance)>();
        foreach (var contact in scene.Contacts)
        {
            if (contact.Observation.Affiliation != Affiliation.Enemy || contact.Phase is not (ContactPhase.Live or ContactPhase.Predicted)
                || contact.Observation.Destroyed || contact.Phase == ContactPhase.Predicted && contact.Age >= ContactTracker.GhostSeconds
                || Category(contact.Observation) is not { } category) continue;
            var position = contact.Observation.Type == "airfield" && contact.Observation.EndPosition is { IsFinite: true } end
                ? (contact.Position + map.ToWorld(end)) / 2 : contact.Position;
            if (!position.IsFinite) continue;
            var delta = position - player.Position;
            var distance = delta.Length;
            if (!double.IsFinite(distance)) continue;
            var sideDistance = double.PositiveInfinity;
            if (direction is { } course && courseLength > 0)
            {
                var ahead = delta.X * course.X + delta.Y * course.Y;
                // The drawn vector starts at the player and ends at the map edge. Targets behind
                // the player must not win merely because they lie on the infinite extension.
                if (ahead > 0) sideDistance = (delta - course * Math.Min(ahead, courseLength)).Length;
            }
            candidates.Add((contact, category, position, distance, sideDistance));
        }
        var nearest = candidates.Where(c => double.IsFinite(c.SideDistance)).OrderBy(c => c.SideDistance)
            .ThenBy(c => c.Distance).ThenBy(c => c.Contact.TrackId).Select(c => (long?)c.Contact.TrackId).FirstOrDefault();
        var readouts = new List<TargetReadout>();
        foreach (var candidate in candidates)
        {
            var isCourse = candidate.Contact.TrackId == nearest;
            var isMarked = candidate.Contact.Phase == ContactPhase.Live && candidate.Contact.Observation.ApiTargetMarked;
            var values = isMarked ? options.MarkedTarget : isCourse ? options.CourseTarget : options.ForCategory(candidate.Category);
            if ((values & (TargetValues.Distance | TargetValues.Time)) == TargetValues.None) continue;
            var seconds = double.IsFinite(speed) && speed > .5 ? candidate.Distance / speed : (double?)null;
            readouts.Add(new(candidate.Contact.TrackId, candidate.Position, candidate.Category, candidate.Distance,
                seconds is { } time && double.IsFinite(time) ? time : null, isCourse,
                candidate.Contact.Phase == ContactPhase.Predicted, values, isMarked));
        }
        return readouts.OrderByDescending(r => r.IsMarkedTarget).ThenByDescending(r => r.IsCourseTarget).ThenBy(r => r.TrackId).ToArray();
    }
}
