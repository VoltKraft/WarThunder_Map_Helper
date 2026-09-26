using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MapHelper.Core;

namespace MapHelper.Telemetry;

/// <summary>Polls the optional game API with independent, bounded endpoint requests.</summary>
/// <remarks>
/// Supports one active reader. Request failures are emitted as failed telemetry packets, allowing
/// the application to start offline. Cancellation ends all polling loops; disposal owns the HTTP
/// client and any supplied handler. Map epochs prevent late responses crossing mission boundaries.
/// </remarks>
public sealed class HttpTelemetrySource : ITelemetrySource
{
    private readonly HttpClient _http;
    private readonly Func<double> _clock;
    private readonly TelemetryParser _parser;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _sync = new();
    private long _epoch;
    private MapInfo? _map;
    private JsonElement? _state, _indicators;
    private double _stateAt = -100, _indicatorsAt = -100;
    private long _lastEvent, _lastDamage;
    private bool _eventsPrimed;
    private string? _mission;
    private string? _imageKey;
    private double? _lastObjectsSuccess;
    private bool _objectsFailed;

    /// <param name="address">Trusted HTTP(S) API base address without embedded credentials.</param>
    /// <param name="clock">Thread-safe clock returning monotonic seconds for all endpoint workers.</param>
    /// <param name="parser">Optional parser with caller-selected affiliation overrides.</param>
    /// <param name="handler">Optional HTTP handler; the source takes ownership and disposes it.</param>
    /// <exception cref="ArgumentException">The address scheme or credentials are not accepted.</exception>
    public HttpTelemetrySource(Uri address, Func<double> clock, TelemetryParser? parser = null, HttpMessageHandler? handler = null)
    {
        if (address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo)) throw new ArgumentException("Provide an HTTP or HTTPS address without credentials.");
        _http = handler == null ? new HttpClient(new HttpClientHandler { UseProxy = false }) : new HttpClient(handler);
        _http.BaseAddress = address;
        _http.Timeout = TimeSpan.FromSeconds(1);
        _clock = clock; _parser = parser ?? new();
    }
    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetryPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var ct = linked.Token;
        var channel = Channel.CreateBounded<TelemetryPacket>(new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var loops = new[]
        {
            Poll(Endpoint.MapInfo, "map_info.json", 1000, channel.Writer, ct),
            Poll(Endpoint.Objects, "map_obj.json", 100, channel.Writer, ct),
            Poll(Endpoint.State, "state", 100, channel.Writer, ct),
            Poll(Endpoint.Indicators, "indicators", 100, channel.Writer, ct),
            Poll(Endpoint.Mission, "mission.json", 1000, channel.Writer, ct),
            Poll(Endpoint.Events, "hudmsg", 500, channel.Writer, ct)
        };
        try
        {
            await foreach (var packet in channel.Reader.ReadAllAsync(ct)) yield return packet;
        }
        finally
        {
            await linked.CancelAsync();
            try { await Task.WhenAll(loops); } catch (OperationCanceledException) { }
            channel.Writer.TryComplete();
        }
    }
    private async Task Poll(Endpoint endpoint, string path, int milliseconds, ChannelWriter<TelemetryPacket> writer, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(milliseconds));
        do
        {
            var epoch = Interlocked.Read(ref _epoch);
            var url = endpoint == Endpoint.Events ? $"hudmsg?lastEvt={_lastEvent}&lastDmg={_lastDamage}" : path;
            try
            {
                var data = await GetBytes(url, 4 * 1024 * 1024, ct);
                var json = Encoding.UTF8.GetString(data);
                var now = _clock();
                TelemetryPacket packet;
                TelemetryPacket? metadata = null;
                lock (_sync)
                {
                    if (epoch != _epoch) continue;
                    packet = new(endpoint, now, _epoch, RawJson: json);
                    switch (endpoint)
                    {
                        case Endpoint.MapInfo:
                            var map = _parser.ParseMap(json);
                            if (map?.Key != _map?.Key)
                            {
                                _epoch++; _state = _indicators = null; _imageKey = null;
                                _eventsPrimed = false; _lastEvent = _lastDamage = 0;
                                _lastObjectsSuccess = null; _objectsFailed = false;
                            }
                            _map = map;
                            packet = packet with { Epoch = _epoch, Map = map };
                            break;
                        case Endpoint.Objects:
                            var objects = _parser.ParseObjects(json);
                            if (_objectsFailed && _lastObjectsSuccess is { } lastObjects && now - lastObjects >= 2)
                            {
                                _epoch++; _state = _indicators = null; _imageKey = null;
                                _eventsPrimed = false; _lastEvent = _lastDamage = 0;
                                metadata = new(Endpoint.MapInfo, now, _epoch, Map: _map);
                            }
                            _lastObjectsSuccess = now; _objectsFailed = false;
                            packet = packet with { Epoch = _epoch, Objects = objects };
                            break;
                        case Endpoint.State:
                        case Endpoint.Indicators:
                            using (var doc = JsonDocument.Parse(json))
                            {
                                if (endpoint == Endpoint.State) { _state = doc.RootElement.Clone(); _stateAt = now; }
                                else { _indicators = doc.RootElement.Clone(); _indicatorsAt = now; }
                            }
                            packet = packet with { Player = _parser.ParsePlayer(now - _stateAt <= 1 ? _state : null, now - _indicatorsAt <= 1 ? _indicators : null) };
                            break;
                        case Endpoint.Mission:
                            using (var doc = JsonDocument.Parse(json))
                            {
                                var status = TelemetryParser.Text(doc.RootElement, "status");
                                if (_mission != status && _mission != null)
                                {
                                    _epoch++; _state = _indicators = null; _imageKey = null;
                                    _eventsPrimed = false; _lastEvent = _lastDamage = 0;
                                    _lastObjectsSuccess = null; _objectsFailed = false;
                                    metadata = new(Endpoint.MapInfo, now, _epoch, Map: _map);
                                }
                                _mission = status;
                                packet = packet with { Epoch = _epoch, MissionStatus = status };
                            }
                            break;
                        case Endpoint.Events:
                            var events = TelemetryParser.ParseEvents(json);
                            _lastEvent = Math.Max(_lastEvent, events.LastEvent); _lastDamage = Math.Max(_lastDamage, events.LastDamage);
                            packet = packet with { Events = _eventsPrimed ? events.Events : [] };
                            _eventsPrimed = true;
                            break;
                    }
                }
                epoch = packet.Epoch;
                if (metadata != null) await writer.WriteAsync(metadata, ct);
                await writer.WriteAsync(packet, ct);
                if (endpoint == Endpoint.MapInfo && packet.Map is { } imageMap && _imageKey != imageMap.Key)
                {
                    var image = await GetBytes($"map.img?gen={imageMap.Generation}", 20 * 1024 * 1024, ct);
                    if (packet.Epoch == Interlocked.Read(ref _epoch))
                    {
                        await writer.WriteAsync(new(Endpoint.MapImage, _clock(), packet.Epoch, Image: image), ct);
                        _imageKey = imageMap.Key;
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or IOException or JsonException or TaskCanceledException or InvalidOperationException or ArgumentException or OverflowException)
            {
                // A timeout from an earlier map must not reset the new session's estimates.
                if (epoch != Interlocked.Read(ref _epoch)) continue;
                // Failed image downloads must not invalidate otherwise correct map metadata.
                if (endpoint == Endpoint.Objects) lock (_sync) _objectsFailed = true;
                await writer.WriteAsync(new(endpoint, _clock(), epoch, false, Error: ex.Message), ct);
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
    private async Task<byte[]> GetBytes(string path, int limit, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("API response is too large.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var result = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (result.Length + read > limit) throw new InvalidDataException("API response is too large.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }
    public async ValueTask DisposeAsync() { await _stop.CancelAsync(); _http.Dispose(); _stop.Dispose(); }
}
