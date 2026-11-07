using System.Runtime.CompilerServices;
using MapHelper.Core;

namespace MapHelper.Telemetry;

/// <summary>The currently polled address and whether a background LAN search is active.</summary>
public sealed record ApiConnectionStatus(Uri Address, bool IsSearching);

/// <summary>Polls the configured API and discovers a LAN fallback when a loopback API is unavailable.</summary>
/// <remarks>
/// Supports one reader. Status callbacks execute on the reader's thread. Discovered addresses are
/// session-only. A healthy fallback remains selected until its object feed fails for three seconds.
/// Source changes advance packet epochs to prevent mixing players, maps, and events across hosts.
/// Cancel and await the reader before disposal, as required by <see cref="ITelemetrySource"/>.
/// </remarks>
public sealed class DiscoveringTelemetrySource : ITelemetrySource
{
    private readonly Uri _configuredAddress;
    private readonly Func<double> _clock;
    private readonly Func<Uri, ITelemetrySource> _createSource;
    private readonly Func<CancellationToken, Task<Uri?>> _discover;
    private readonly NetworkApiDiscovery? _discovery;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Raised when polling changes address or background discovery starts or stops.</summary>
    public event Action<ApiConnectionStatus>? ConnectionChanged;

    /// <summary>The active polling address; reading this reference is safe from other threads.</summary>
    public Uri CurrentAddress { get; private set; }

    /// <param name="address">Configured HTTP(S) API base address. Only loopback addresses enable discovery.</param>
    /// <param name="clock">Thread-safe monotonic clock, in seconds.</param>
    /// <param name="parser">Optional parser preserving affiliation overrides across host changes.</param>
    public DiscoveringTelemetrySource(Uri address, Func<double> clock, TelemetryParser? parser = null)
    {
        _configuredAddress = CurrentAddress = address;
        _clock = clock;
        _createSource = uri => new HttpTelemetrySource(uri, clock, parser);
        _discovery = new NetworkApiDiscovery(address);
        _discover = _discovery.FindAsync;
    }

    internal DiscoveringTelemetrySource(Uri address, Func<double> clock,
        Func<Uri, ITelemetrySource> createSource, Func<CancellationToken, Task<Uri?>> discover)
    {
        _configuredAddress = CurrentAddress = address;
        _clock = clock;
        _createSource = createSource;
        _discover = discover;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TelemetryPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var ct = lifetime.Token;
        long epochOffset = 0, highestEpoch = -1;
        var nextSearch = double.NegativeInfinity;
        while (!ct.IsCancellationRequested)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using var source = _createSource(CurrentAddress);
            await using var reader = source.ReadAsync(session.Token).GetAsyncEnumerator(session.Token);
            CancellationTokenSource? searchCancellation = null;
            Task<Uri?>? search = null;
            Task<bool>? pending = null;
            Uri? nextAddress = null;
            double? failedSince = null;
            ConnectionChanged?.Invoke(new(CurrentAddress, false));
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    pending ??= reader.MoveNextAsync().AsTask();
                    if (search != null) await Task.WhenAny(pending, search);
                    // Prefer fresh local packets over a simultaneous discovery result.
                    if (search == null || pending.IsCompleted)
                    {
                        if (!await pending) yield break;
                        pending = null;
                        var packet = reader.Current;
                        highestEpoch = Math.Max(highestEpoch, packet.Epoch + epochOffset);
                        yield return packet with { Epoch = packet.Epoch + epochOffset };
                        if (packet.Endpoint != Endpoint.Objects) continue;
                        if (packet.Success)
                        {
                            failedSince = null;
                            if (search != null)
                            {
                                await searchCancellation!.CancelAsync();
                                await IgnoreCancellation(search);
                                search = null;
                                searchCancellation.Dispose(); searchCancellation = null;
                                nextSearch = _clock() + 30;
                                ConnectionChanged?.Invoke(new(CurrentAddress, false));
                            }
                            continue;
                        }
                        failedSince ??= _clock();
                        if (!_configuredAddress.IsLoopback) continue;
                        if (CurrentAddress != _configuredAddress)
                        {
                            if (_clock() - failedSince >= 3)
                            {
                                nextAddress = _configuredAddress;
                                break;
                            }
                        }
                        else if (search == null && _clock() >= nextSearch)
                        {
                            searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            ConnectionChanged?.Invoke(new(CurrentAddress, true));
                            // Enumeration of network interfaces must not delay the polling reader.
                            search = Task.Run(() => _discover(searchCancellation.Token), searchCancellation.Token);
                        }
                    }
                    else
                    {
                        nextAddress = await search;
                        search = null;
                        searchCancellation!.Dispose(); searchCancellation = null;
                        nextSearch = _clock() + 30;
                        ConnectionChanged?.Invoke(new(CurrentAddress, false));
                        if (nextAddress != null && nextAddress != CurrentAddress) break;
                    }
                }
            }
            finally
            {
                await session.CancelAsync();
                if (searchCancellation != null)
                {
                    await searchCancellation.CancelAsync();
                    if (search != null) await IgnoreCancellation(search);
                    searchCancellation.Dispose();
                }
                if (pending != null) await IgnoreCancellation(pending);
            }
            ct.ThrowIfCancellationRequested();
            if (nextAddress == null) yield break;
            CurrentAddress = nextAddress;
            epochOffset = highestEpoch + 1;
            // Clear the old host immediately, even if the new host has stopped responding.
            highestEpoch = epochOffset;
            yield return new(Endpoint.MapInfo, _clock(), epochOffset, false, Error: "API connection changed.");
        }
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try { await task; } catch (OperationCanceledException) { }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _discovery?.Dispose();
        _stop.Dispose();
    }
}
