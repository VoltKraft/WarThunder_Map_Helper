namespace MapHelper.Core;

/// <summary>Combines endpoint packets into a map scene while isolating mission epochs.</summary>
/// <remarks>
/// This mutable engine is not thread-safe. The desktop application serializes updates and scene
/// reads on the UI thread. Failed or absent endpoints must not be replaced with synthetic data.
/// </remarks>
public sealed class MapEngine
{
    private readonly ContactTracker _tracker = new();
    private readonly RangeEstimator _rangeEstimator = new();
    private readonly Dictionary<Endpoint, double> _lastPacket = [];
    private readonly HashSet<string> _eventIds = [];
    private readonly Queue<string> _events = [];
    private long _epoch = -1;
    private long _navigationSession;
    private MapInfo? _map;
    private PlayerTelemetry? _player;
    private RangeEstimate? _range;
    private double _lastObjects = double.NegativeInfinity;
    private double _lastState = double.NegativeInfinity;
    private bool _connected;
    private bool _hadSelf;
    private bool _missionRunning = true;
    public byte[]? MapImage { get; private set; }
    public int ImageVersion { get; private set; }
    public string? LastError { get; private set; }

    public void Accept(TelemetryPacket packet)
    {
        if (packet.Epoch < _epoch) return;
        if (packet.Epoch > _epoch)
        {
            _epoch = packet.Epoch;
            _navigationSession++;
            _tracker.Reset(); _rangeEstimator.Reset(); _lastPacket.Clear();
            _range = null; _player = null; _hadSelf = false; _map = null;
            _lastObjects = _lastState = double.NegativeInfinity;
            _connected = false; _missionRunning = true;
            MapImage = null; ImageVersion++;
            _eventIds.Clear(); _events.Clear();
        }
        if (_lastPacket.TryGetValue(packet.Endpoint, out var last) && packet.Time <= last) return;
        _lastPacket[packet.Endpoint] = packet.Time;
        if (!packet.Success)
        {
            LastError = packet.Error;
            if (packet.Endpoint == Endpoint.Objects) Disconnect();
            if (packet.Endpoint is Endpoint.State or Endpoint.Indicators) { _range = null; _rangeEstimator.Reset(); }
            return;
        }
        switch (packet.Endpoint)
        {
            case Endpoint.MapInfo:
                if (packet.Map?.IsUsable == true) _map = packet.Map;
                else { _map = null; _tracker.Reset(); Disconnect(); }
                break;
            case Endpoint.MapImage:
                if (packet.Image != null) { MapImage = packet.Image; ImageVersion++; }
                break;
            case Endpoint.Objects:
                if (_map == null || packet.Objects == null || !_missionRunning) break;
                var hasSelf = packet.Objects.Any(o => o.Affiliation == Affiliation.Self);
                if (hasSelf != _hadSelf) { _range = null; _rangeEstimator.Reset(); _navigationSession++; }
                _hadSelf = hasSelf;
                _tracker.Update(packet.Objects, _map, packet.Time);
                _lastObjects = packet.Time; _connected = true; LastError = null;
                break;
            case Endpoint.State:
            case Endpoint.Indicators:
                if (packet.Player == null) break;
                if (_player?.Vehicle != null && packet.Player.Vehicle != null && _player.Vehicle != packet.Player.Vehicle)
                { _rangeEstimator.Reset(); _range = null; _tracker.Disconnect(); _navigationSession++; }
                _player = packet.Player;
                if (!_player.Valid) { _range = null; _rangeEstimator.Reset(); break; }
                if (packet.Endpoint == Endpoint.State)
                {
                    _lastState = packet.Time;
                    var self = _tracker.GetContacts(packet.Time).FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
                    _range = _connected && _missionRunning && self != null ? _rangeEstimator.Update(packet.Time, _player.Vehicle,
                        _player.FuelKg, _player.VerifiedConsumptionKgS, self.Velocity?.Length) : null;
                }
                break;
            case Endpoint.Mission:
                var running = packet.MissionStatus is "running" or null;
                if (running != _missionRunning)
                { _tracker.Reset(); _rangeEstimator.Reset(); _range = null; _player = null; _hadSelf = false; _connected = false; _navigationSession++; }
                _missionRunning = running;
                break;
            case Endpoint.Events:
                foreach (var e in packet.Events ?? [])
                {
                    if (!_eventIds.Add(e.Id)) continue;
                    if (e.ConfirmedDestruction && e.TargetId != null) _tracker.MarkDestroyed(e.TargetId, packet.Time);
                    _events.Enqueue(e.Text);
                    while (_events.Count > 5) _events.Dequeue();
                }
                break;
        }
    }
    private void Disconnect() { _connected = false; _tracker.Disconnect(); _rangeEstimator.Reset(); _range = null; }
    public MapScene Scene(double now)
    {
        if (now - _lastObjects > .5 && _connected) Disconnect();
        var live = _connected && now - _lastObjects <= .5;
        var contacts = _tracker.GetContacts(now);
        var status = !_missionRunning ? "Waiting for the next mission" : live
            ? contacts.Any(c => c.Observation.Affiliation == Affiliation.Self) ? "Connected live" : "Map connected · own player unavailable"
            : _map == null ? "Waiting for War Thunder" : "Disconnected · data is stale";
        return new(_map, contacts, live && now - _lastState < 1 ? _range : null,
            now - _lastState < 2 ? _player : null, status, live, now, _events.ToArray(), _navigationSession);
    }
}
