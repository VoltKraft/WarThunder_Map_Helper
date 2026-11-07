using System.Net;
using MapHelper.Telemetry;
using Xunit;

namespace MapHelper.Tests;

public class NetworkApiDiscoveryTests
{
    private const string Map = """{"valid":true,"map_generation":1,"map_min":[0,0],"map_max":[10000,20000]}""";

    private static NetworkApiDiscovery Create(HttpMessageHandler handler, params NetworkApiDiscovery.Subnet[] subnets) =>
        new(new Uri("http://localhost:8111/"), handler, () => subnets.Length == 0 ? [new(IPAddress.Parse("192.168.1.7"), 32)] : subnets);

    [Fact]
    public async Task FindsApiAndPreservesConfiguredPortAndPath()
    {
        var requests = new List<Uri>();
        using var discovery = new NetworkApiDiscovery(new("http://localhost:8123/game/"), new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(Reply(request.RequestUri!.AbsolutePath.EndsWith("map_info.json", StringComparison.Ordinal) ? Map : "[]"));
        }), () => [new(IPAddress.Parse("192.168.1.7"), 32)]);
        Assert.Equal(new Uri("http://192.168.1.7:8123/game/"), await discovery.FindAsync());
        Assert.Equal(new[] { "/game/map_info.json", "/game/map_obj.json" }, requests.Select(r => r.AbsolutePath));
    }

    [Fact]
    public async Task RecognizesInactiveApiWithoutInventingLiveData()
    {
        using var discovery = Create(new Handler((request, _) => Task.FromResult(Reply(
            request.RequestUri!.AbsolutePath == "/map_obj.json" ? "[]" : "{\"valid\":false}"))));
        Assert.NotNull(await discovery.FindAsync());
    }

    [Theory]
    [InlineData("<html>Not a game</html>", "[]", "{}")]
    [InlineData("{}", "[]", "{}")]
    [InlineData("{\"valid\":true}", "[]", "{}")]
    [InlineData(Map, "{}", "{}")]
    [InlineData(Map, "[{}]", "{}")]
    [InlineData("{\"valid\":false}", "[]", "{\"status\":\"ok\"}")]
    public async Task RejectsNonGameResponses(string map, string objects, string state)
    {
        using var discovery = Create(new Handler((request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath switch
        {
            "/map_info.json" => map,
            "/map_obj.json" => objects,
            _ => state
        }))));
        Assert.Null(await discovery.FindAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task RejectsNonSuccessStatuses(HttpStatusCode status)
    {
        using var discovery = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(Map) })));
        Assert.Null(await discovery.FindAsync());
    }

    [Fact]
    public async Task RejectsOversizedResponses()
    {
        using var discovery = Create(new Handler((_, _) => Task.FromResult(Reply(new string(' ', 256 * 1024 + 1) + Map))));
        Assert.Null(await discovery.FindAsync());
    }

    [Fact]
    public async Task BoundsBodiesWithoutContentLength()
    {
        using var discovery = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new string(' ', 256 * 1024 + 1) + Map)
        })));
        Assert.Null(await discovery.FindAsync());
    }

    [Fact]
    public async Task FindingOneHostCancelsAndAwaitsOtherProbes()
    {
        var otherStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherStopped = false;
        using var discovery = Create(new Handler(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "192.168.1.2")
            {
                otherStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { otherStopped = true; }
            }
            await otherStarted.Task.WaitAsync(ct);
            return Reply(request.RequestUri.AbsolutePath == "/map_info.json" ? Map : "[]");
        }), new NetworkApiDiscovery.Subnet(IPAddress.Parse("192.168.1.1"), 30));
        Assert.Equal("192.168.1.1", (await discovery.FindAsync())?.Host);
        Assert.True(otherStopped);
    }

    [Fact]
    public async Task CancellationStopsAllConcurrentProbes()
    {
        var active = 0;
        var maximum = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery = Create(new Handler(async (_, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref maximum, Math.Max(count, Volatile.Read(ref maximum)));
            if (count == 32) started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply(Map); }
            finally { Interlocked.Decrement(ref active); }
        }), new NetworkApiDiscovery.Subnet(IPAddress.Parse("192.168.1.7"), 24));
        using var stop = new CancellationTokenSource();
        var search = discovery.FindAsync(stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        Assert.InRange(maximum, 1, 32);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task SlowHostHasAnOverallDeadline()
    {
        var cancelled = false;
        using var discovery = Create(new Handler(async (_, ct) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Reply(Map); }
            finally { cancelled = ct.IsCancellationRequested; }
        }));
        Assert.Null(await discovery.FindAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(cancelled);
    }

    [Fact]
    public async Task SlowBodyAfterHeadersIsCancelledByHostDeadline()
    {
        var body = new SlowStream();
        using var discovery = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamedContent(body)
        })));
        Assert.Null(await discovery.FindAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(body.Cancelled);
    }

    [Fact]
    public void EnumeratesOnlyPrivateUsableHostsAndDeduplicatesSubnets()
    {
        using var discovery = Create(new Handler((_, _) => throw new InvalidOperationException("No requests expected")),
            new(IPAddress.Parse("192.168.1.5"), 30), new(IPAddress.Parse("192.168.1.6"), 30),
            new(IPAddress.Parse("127.0.0.1"), 8), new(IPAddress.Parse("8.8.8.8"), 24),
            new(IPAddress.Parse("169.254.1.2"), 16), new(IPAddress.IPv6Loopback, 128));
        Assert.Equal(new[] { "192.168.1.5", "192.168.1.6" }, discovery.GetCandidates().Select(a => a.ToString()));
    }

    [Fact]
    public void BoundedPagesRotateAcrossLargeSubnetsAndShareBudget()
    {
        using var discovery = Create(new Handler((_, _) => throw new InvalidOperationException("No requests expected")),
            new(IPAddress.Parse("10.4.5.6"), 16), new(IPAddress.Parse("192.168.2.7"), 24));
        var first = discovery.GetCandidates().Select(a => a.ToString()).ToHashSet();
        var second = discovery.GetCandidates().Select(a => a.ToString()).ToHashSet();
        Assert.Equal(1024, first.Count);
        Assert.Equal(1024, second.Count);
        Assert.Contains("10.4.5.0", first);
        Assert.Contains("192.168.2.254", first);
        Assert.DoesNotContain("10.4.5.0", second);
        Assert.Contains(second, a => a.StartsWith("10.4.8.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("10.1.2.3", "10.")]
    [InlineData("172.16.2.3", "172.16.")]
    [InlineData("192.168.2.3", "192.168.")]
    public void BroadMasksCannotEscapePrivateRanges(string address, string expectedPrefix)
    {
        using var discovery = Create(new Handler((_, _) => throw new InvalidOperationException("No requests expected")), new NetworkApiDiscovery.Subnet(IPAddress.Parse(address), 0));
        Assert.All(discovery.GetCandidates(), candidate => Assert.StartsWith(expectedPrefix, candidate.ToString()));
    }

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class UnknownLengthContent(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class StreamedContent(Stream body) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(body);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class SlowStream : Stream
    {
        public bool Cancelled { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
            finally { Cancelled = cancellationToken.IsCancellationRequested; }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
