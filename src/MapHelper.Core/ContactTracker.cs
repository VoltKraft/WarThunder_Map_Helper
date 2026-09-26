namespace MapHelper.Core;

public sealed class ContactTracker
{
    public const double GhostSeconds = 2;
    public const double DestroyedSeconds = 5;
    private sealed class Track(long id, MapObservation observation, Vec2 position, double time)
    {
        public long Id = id;
        public MapObservation Observation = observation;
        public Vec2 Position = position;
        public Vec2? Velocity;
        public double LastSeen = time;
        public double? DestroyedAt;
        public bool Seen = true;
        public bool Ambiguous;
        public readonly List<(double Time, Vec2 Position)> History = [(time, position)];
    }
    private readonly List<Track> _tracks = [];
    private long _nextId;
    private double _lastFrame = double.NegativeInfinity;
    private bool _offline;
    public void Reset() { _tracks.Clear(); _lastFrame = double.NegativeInfinity; _offline = false; }
    public void Disconnect()
    {
        _offline = true;
        foreach (var t in _tracks) { t.Velocity = null; t.History.Clear(); }
    }

    public void Update(IReadOnlyList<MapObservation> objects, MapInfo map, double time)
    {
        if (time <= _lastFrame || !map.IsUsable) return;
        Expire(time);
        _lastFrame = time;
        _offline = false;
        foreach (var t in _tracks) t.Seen = false;
        var observations = objects.Where(o => o.Position.IsFinite &&
            !(o.SourceId != null && _tracks.Any(t => t.Observation.SourceId == o.SourceId && t.DestroyedAt != null))).ToArray();
        var ids = observations.Where(o => o.SourceId != null).GroupBy(o => o.SourceId!).ToDictionary(g => g.Key, g => g.Count());
        var candidates = new List<(int Index, Track Track, double Distance)>();
        var assigned = new HashSet<int>();

        // Unique source IDs and the own-player marker take precedence over kinematic association.
        for (var i = 0; i < observations.Length; i++)
        {
            var o = observations[i];
            Track? exact = null;
            if (o.Affiliation == Affiliation.Self)
                exact = _tracks.FirstOrDefault(t => !t.Seen && t.Observation.Affiliation == Affiliation.Self && t.DestroyedAt == null);
            else if (o.SourceId is { } id && ids[id] == 1)
                exact = _tracks.FirstOrDefault(t => !t.Seen && t.Observation.SourceId == id && t.DestroyedAt == null);
            if (exact == null) continue;
            Apply(exact, o, map, time, false);
            assigned.Add(i);
        }
        for (var i = 0; i < observations.Length; i++)
        {
            if (assigned.Contains(i)) continue;
            var o = observations[i];
            foreach (var t in _tracks.Where(t => !t.Seen && t.DestroyedAt == null))
            {
                if (!Compatible(t.Observation, o)) continue;
                if (o.SourceId != null && t.Observation.SourceId != null && o.SourceId != t.Observation.SourceId) continue;
                var dt = Math.Max(0, time - t.LastSeen);
                var predicted = t.Position + (t.Velocity ?? default) * dt;
                var distance = (predicted - map.ToWorld(o.Position)).Length;
                if (distance <= Gate(o, dt)) candidates.Add((i, t, distance));
            }
        }
        foreach (var c in candidates.OrderBy(c => c.Distance).ThenBy(c => c.Track.Id).ThenBy(c => c.Index))
        {
            if (assigned.Contains(c.Index) || c.Track.Seen) continue;
            var margin = Math.Max(12, c.Distance * .3);
            var ambiguous = candidates.Any(other => (other.Index == c.Index && other.Track != c.Track ||
                other.Track == c.Track && other.Index != c.Index) && other.Distance <= c.Distance + margin);
            Apply(c.Track, observations[c.Index], map, time, ambiguous);
            assigned.Add(c.Index);
        }
        for (var i = 0; i < observations.Length; i++)
        {
            if (assigned.Contains(i)) continue;
            var o = observations[i];
            var t = new Track(++_nextId, o, map.ToWorld(o.Position), time);
            if (o.Destroyed) t.DestroyedAt = time;
            _tracks.Add(t);
        }
        // Only enemies retain individual lost-contact predictions. Static objectives follow the latest map frame.
        _tracks.RemoveAll(t => !t.Seen && t.DestroyedAt == null && (!t.Observation.IsMobile || t.Observation.Affiliation != Affiliation.Enemy));
    }

    private static bool Compatible(MapObservation a, MapObservation b) =>
        a.Type == b.Type && a.Icon == b.Icon && a.Affiliation == b.Affiliation;
    private static double Gate(MapObservation o, double dt) => o.IsMobile
        ? 15 + (o.IconClass is "Aircraft" or "Player" ? 1500 : o.IconClass == "Ship" ? 100 : 180) * dt
        : 3;
    private static void Apply(Track t, MapObservation o, MapInfo map, double time, bool ambiguous)
    {
        var p = map.ToWorld(o.Position);
        var dt = time - t.LastSeen;
        var delta = p - t.Position;
        if (o.IsMobile && dt > .015 && dt <= GhostSeconds && delta.Length <= Gate(o, dt) && !ambiguous)
        {
            var previous = t.History.Count > 0 ? t.History[^1] : ((double Time, Vec2 Position)?)null;
            t.History.Add((time, p));
            t.History.RemoveAll(s => time - s.Time > .61);
            // Sparse observations still need their previous sample; do not turn a one-second
            // update interval into an unknown velocity merely because the smoothing window is shorter.
            if (t.History.Count == 1 && previous is { } sample && time - sample.Time <= GhostSeconds)
                t.History.Insert(0, sample);
            // The HTTP poll can run faster than the game updates coordinates. Repeated positions
            // are samples, not proof that a moving aircraft stopped between every second request.
            var meanTime = t.History.Average(s => s.Time);
            var denominator = t.History.Sum(s => Math.Pow(s.Time - meanTime, 2));
            if (denominator > .00001)
            {
                var measured = new Vec2(t.History.Sum(s => (s.Time - meanTime) * s.Position.X),
                    t.History.Sum(s => (s.Time - meanTime) * s.Position.Y)) / denominator;
                var alpha = 1 - Math.Exp(-dt / .2);
                t.Velocity = measured.Length < .5 ? default(Vec2) : t.Velocity is { } old ? old * (1 - alpha) + measured * alpha : measured;
            }
            else t.Velocity = null;
        }
        else { t.Velocity = null; t.History.Clear(); t.History.Add((time, p)); }
        t.Ambiguous = ambiguous;
        t.Position = p;
        t.Observation = o;
        t.LastSeen = time;
        t.Seen = true;
        if (o.Destroyed && t.DestroyedAt == null) t.DestroyedAt = time;
    }

    public bool MarkDestroyed(string sourceId, double time)
    {
        var matches = _tracks.Where(t => t.Observation.SourceId == sourceId && t.DestroyedAt == null).ToArray();
        if (matches.Length != 1) return false;
        matches[0].DestroyedAt = time;
        matches[0].Velocity = null;
        return true;
    }
    private void Expire(double now) => _tracks.RemoveAll(t => t.DestroyedAt is { } at
        ? now - at >= DestroyedSeconds
        : t.Observation.IsMobile && now - t.LastSeen >= GhostSeconds);
    public IReadOnlyList<Contact> GetContacts(double now)
    {
        Expire(now);
        return _tracks.Select(t =>
        {
            var age = Math.Max(0, now - t.LastSeen);
            var phase = t.DestroyedAt != null ? ContactPhase.Destroyed : _offline || age > .5
                ? ContactPhase.Stale : !t.Seen ? ContactPhase.Predicted : ContactPhase.Live;
            if (!_offline && !t.Seen && t.DestroyedAt == null) phase = ContactPhase.Predicted;
            var position = phase == ContactPhase.Predicted ? t.Position + (t.Velocity ?? default) * age : t.Position;
            Vec2? direction = t.Velocity is { Length: > .5 } v ? v.Normalized : t.Observation.Direction;
            return new Contact(t.Id, t.Observation, position, t.Velocity, direction, t.LastSeen, age, phase, t.DestroyedAt, t.Ambiguous);
        }).ToArray();
    }
}
