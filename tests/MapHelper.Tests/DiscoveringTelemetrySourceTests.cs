using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MapHelper.Core;
using MapHelper.Telemetry;
using Xunit;

namespace MapHelper.Tests;

public class DiscoveringTelemetrySourceTests
{
    private static readonly Uri Local = new("http://127.0.0.1:8111/");
    private static readonly Uri Remote = new("http://192.168.1.20:8111/");

    [Fact]
    public async Task LocalRecoveryCancelsBackgroundSearchAndDelaysRetryForThirtySeconds()
    {
        var searches = Channel.CreateUnbounded<int>();
        var searchCount = 0;
        var stopped = Signal();
        await using var harness = new Harness(Local, async ct =>
        {
            searches.Writer.TryWrite(Interlocked.Increment(ref searchCount));
            try { await Task.Delay(Timeout.Infinite, ct); return null; }
            finally { stopped.TrySetResult(); }
        });
        var local = await harness.Created.Reader.ReadAsync(harness.Token);
        await harness.SendAndRead(local, new(Endpoint.Objects, 1, 0, false));
        Assert.Equal(1, await searches.Reader.ReadAsync(harness.Token));
        await harness.SendAndRead(local, new(Endpoint.State, 2, 0, false));
        await harness.SendAndRead(local, new(Endpoint.Objects, 3, 0, Objects: []));
        await stopped.Task.WaitAsync(harness.Token);
        await harness.WaitForSearchCompletion();
        Assert.Equal(Local, harness.Source.CurrentAddress);
        Assert.False(local.Disposed);
        Assert.Equal(1, harness.CreatedCount);
        harness.Now = 29;
        await harness.SendAndRead(local, new(Endpoint.Objects, 29, 0, false));
        await harness.SendAndRead(local, new(Endpoint.State, 29.1, 0, false));
        Assert.Equal(1, Volatile.Read(ref searchCount));
        harness.Now = 30;
        await harness.SendAndRead(local, new(Endpoint.Objects, 30, 0, false));
        Assert.Equal(2, await searches.Reader.ReadAsync(harness.Token));
    }

    [Fact]
    public async Task ExplicitRemoteAddressDoesNotStartDiscovery()
    {
        var searches = 0;
        await using var harness = new Harness(Remote, _ =>
        {
            Interlocked.Increment(ref searches);
            return Task.FromResult<Uri?>(Local);
        });
        var remote = await harness.Created.Reader.ReadAsync(harness.Token);
        await harness.SendAndRead(remote, new(Endpoint.Objects, 1, 0, false));
        harness.Now = 100;
        await harness.SendAndRead(remote, new(Endpoint.Objects, 100, 0, false));
        await harness.SendAndRead(remote, new(Endpoint.State, 101, 0, false));
        Assert.Equal(0, Volatile.Read(ref searches));
        Assert.Equal(Remote, harness.Source.CurrentAddress);
        Assert.Equal(1, harness.CreatedCount);
    }

    [Fact]
    public async Task HostSwitchCancelsOldReaderAndClearsItsMapAndContactsWithNewEpoch()
    {
        var discovery = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = Signal();
        await using var harness = new Harness(Local, ct =>
        {
            started.TrySetResult();
            return discovery.Task.WaitAsync(ct);
        });
        var local = await harness.Created.Reader.ReadAsync(harness.Token);
        var engine = new MapEngine();
        engine.Accept(await harness.SendAndRead(local, new(Endpoint.MapInfo, 1, 7, Map: TrackingTests.Map)));
        engine.Accept(await harness.SendAndRead(local, new(Endpoint.Objects, 2, 7, Objects: [TrackingTests.Enemy(.4)])));
        Assert.Single(engine.Scene(2).Contacts);
        engine.Accept(await harness.SendAndRead(local, new(Endpoint.Objects, 3, 7, false)));
        await started.Task.WaitAsync(harness.Token);
        harness.Now = 4;
        discovery.SetResult(Remote);
        var reset = await harness.Packets.Reader.ReadAsync(harness.Token);
        Assert.Equal(Endpoint.MapInfo, reset.Endpoint);
        Assert.False(reset.Success);
        Assert.True(reset.Epoch > 7);
        engine.Accept(reset);
        Assert.Null(engine.Scene(4).Map);
        Assert.Empty(engine.Scene(4).Contacts);
        Assert.True(local.ReaderStopped);
        var remote = await harness.Created.Reader.ReadAsync(harness.Token);
        Assert.True(local.Disposed);
        Assert.Equal(Remote, remote.Address);
        var remotePacket = await harness.SendAndRead(remote, new(Endpoint.MapInfo, 5, 0, Map: TrackingTests.Map));
        Assert.True(remotePacket.Epoch > 7);
        engine.Accept(remotePacket);
        Assert.NotNull(engine.Scene(5).Map);
        Assert.Empty(engine.Scene(5).Contacts);
    }

    [Fact]
    public async Task FailedFallbackReturnsToConfiguredAddressAfterThreeSeconds()
    {
        await using var harness = new Harness(Local, _ => Task.FromResult<Uri?>(Remote));
        var local = await harness.Created.Reader.ReadAsync(harness.Token);
        await harness.SendAndRead(local, new(Endpoint.Objects, 1, 0, false));
        var firstReset = await harness.Packets.Reader.ReadAsync(harness.Token);
        var remote = await harness.Created.Reader.ReadAsync(harness.Token);
        harness.Now = 10;
        await harness.SendAndRead(remote, new(Endpoint.Objects, 10, 0, false));
        await harness.SendAndRead(remote, new(Endpoint.State, 10.1, 0, false));
        harness.Now = 12.9;
        await harness.SendAndRead(remote, new(Endpoint.Objects, 12.9, 0, false));
        await harness.SendAndRead(remote, new(Endpoint.State, 12.95, 0, false));
        Assert.Equal(Remote, harness.Source.CurrentAddress);
        harness.Now = 13;
        await harness.SendAndRead(remote, new(Endpoint.Objects, 13, 0, false));
        var secondReset = await harness.Packets.Reader.ReadAsync(harness.Token);
        Assert.True(secondReset.Epoch > firstReset.Epoch);
        var restored = await harness.Created.Reader.ReadAsync(harness.Token);
        Assert.Equal(Local, restored.Address);
        Assert.True(remote.ReaderStopped);
        Assert.True(remote.Disposed);
    }

    [Fact]
    public async Task UnsuccessfulSearchWaitsThirtySecondsBeforeRetrying()
    {
        var searches = Channel.CreateUnbounded<int>();
        var count = 0;
        await using var harness = new Harness(Local, _ =>
        {
            searches.Writer.TryWrite(Interlocked.Increment(ref count));
            return Task.FromResult<Uri?>(null);
        });
        var local = await harness.Created.Reader.ReadAsync(harness.Token);
        await harness.SendAndRead(local, new(Endpoint.Objects, 1, 0, false));
        Assert.Equal(1, await searches.Reader.ReadAsync(harness.Token));
        await harness.WaitForSearchCompletion();
        harness.Now = 29;
        await harness.SendAndRead(local, new(Endpoint.Objects, 29, 0, false));
        await harness.SendAndRead(local, new(Endpoint.State, 29.1, 0, false));
        Assert.Equal(1, Volatile.Read(ref count));
        harness.Now = 30;
        await harness.SendAndRead(local, new(Endpoint.Objects, 30, 0, false));
        Assert.Equal(2, await searches.Reader.ReadAsync(harness.Token));
    }

    [Fact]
    public async Task CancellationAwaitsBothPendingPollingAndDiscovery()
    {
        var started = Signal();
        var stopped = Signal();
        await using var harness = new Harness(Local, async ct =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); return null; }
            finally { stopped.TrySetResult(); }
        });
        var local = await harness.Created.Reader.ReadAsync(harness.Token);
        await harness.SendAndRead(local, new(Endpoint.Objects, 1, 0, false));
        await started.Task.WaitAsync(harness.Token);
        await harness.Stop();
        Assert.True(stopped.Task.IsCompletedSuccessfully);
        Assert.True(local.ReaderStopped);
        Assert.True(local.Disposed);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _reader;
        private readonly Channel<ApiConnectionStatus> _statuses = Channel.CreateUnbounded<ApiConnectionStatus>();
        private double _now;
        private int _createdCount;
        public double Now { set => Volatile.Write(ref _now, value); }
        public int CreatedCount => Volatile.Read(ref _createdCount);
        public CancellationToken Token => _timeout.Token;
        public DiscoveringTelemetrySource Source { get; }
        public Channel<FakeSource> Created { get; } = Channel.CreateUnbounded<FakeSource>();
        public Channel<TelemetryPacket> Packets { get; } = Channel.CreateUnbounded<TelemetryPacket>();

        public Harness(Uri address, Func<CancellationToken, Task<Uri?>> discover)
        {
            Source = new(address, () => Volatile.Read(ref _now), uri =>
            {
                var source = new FakeSource(uri);
                Interlocked.Increment(ref _createdCount);
                Created.Writer.TryWrite(source);
                return source;
            }, discover);
            Source.ConnectionChanged += status => _statuses.Writer.TryWrite(status);
            _reader = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, Token);
                try
                {
                    await foreach (var packet in Source.ReadAsync(linked.Token)) Packets.Writer.TryWrite(packet);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
                finally { Packets.Writer.TryComplete(); }
            });
        }

        public async Task<TelemetryPacket> SendAndRead(FakeSource source, TelemetryPacket packet)
        {
            await source.Input.Writer.WriteAsync(packet, Token);
            var observed = await Packets.Reader.ReadAsync(Token);
            Assert.Equal(packet.Endpoint, observed.Endpoint);
            Assert.Equal(packet.Time, observed.Time);
            return observed;
        }

        public async Task WaitForSearchCompletion()
        {
            while (!(await _statuses.Reader.ReadAsync(Token)).IsSearching) { }
            Assert.False((await _statuses.Reader.ReadAsync(Token)).IsSearching);
        }

        public async Task Stop()
        {
            await _stop.CancelAsync();
            await _reader.WaitAsync(Token);
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            await Source.DisposeAsync();
            _stop.Dispose();
            _timeout.Dispose();
        }
    }

    private sealed class FakeSource(Uri address) : ITelemetrySource
    {
        public Uri Address { get; } = address;
        public Channel<TelemetryPacket> Input { get; } = Channel.CreateUnbounded<TelemetryPacket>();
        public bool ReaderStopped { get; private set; }
        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<TelemetryPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                await foreach (var packet in Input.Reader.ReadAllAsync(cancellationToken)) yield return packet;
            }
            finally { ReaderStopped = true; }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
