using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace MapHelper.Telemetry;

/// <summary>Finds a game API on directly connected private IPv4 subnets.</summary>
/// <remarks>
/// Call sequentially, with at least 30 seconds between passes. Each pass probes at most 1,024
/// addresses across eight subnets, using 32 workers, a one-second deadline per host, and a
/// 35-second overall deadline. Larger subnets are visited in rotating pages. HTTP redirects,
/// proxies, public addresses, IPv6, loopback and tunnel interfaces are excluded. A result is
/// the first host with matching game endpoint schemas, not a verified player identity.
/// Dispose only after awaiting an active search. The instance owns its HTTP handler.
/// </remarks>
public sealed class NetworkApiDiscovery : IDisposable
{
    private const int MaximumCandidates = 1024;
    private const int MaximumBodyBytes = 256 * 1024;
    private readonly Uri _configuredAddress;
    private readonly HttpClient _http;
    private readonly Func<IReadOnlyList<Subnet>> _subnets;
    private readonly Dictionary<(uint Network, uint Broadcast), uint> _offsets = [];

    /// <summary>Creates a discovery client retaining the configured scheme, port and base path.</summary>
    /// <param name="configuredAddress">An absolute HTTP(S) address without credentials, query or fragment.</param>
    /// <exception cref="ArgumentException">The base address is not accepted.</exception>
    public NetworkApiDiscovery(Uri configuredAddress)
        : this(configuredAddress, new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }, GetSubnets) { }

    internal NetworkApiDiscovery(Uri configuredAddress, HttpMessageHandler handler, Func<IReadOnlyList<Subnet>> subnets)
    {
        if (!configuredAddress.IsAbsoluteUri || configuredAddress.Scheme is not ("http" or "https")
            || configuredAddress.UserInfo.Length != 0 || configuredAddress.Query.Length != 0 || configuredAddress.Fragment.Length != 0)
            throw new ArgumentException("Provide an HTTP or HTTPS base address without credentials, query or fragment.", nameof(configuredAddress));
        _configuredAddress = configuredAddress;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _subnets = subnets;
    }

    /// <summary>Returns the first matching API, or null when this bounded pass finds none.</summary>
    /// <param name="cancellationToken">Cancels all pending requests and the search.</param>
    /// <returns>A base address with the configured scheme, port and path and the discovered IP.</returns>
    /// <exception cref="OperationCanceledException">The caller cancelled the search.</exception>
    public async Task<Uri?> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(35));
        Uri? found = null;
        try
        {
            await Parallel.ForEachAsync(GetCandidates(), new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = stop.Token },
                async (address, ct) =>
                {
                    var candidate = new UriBuilder(_configuredAddress) { Host = address.ToString() }.Uri;
                    if (await ProbeAsync(candidate, ct) && Interlocked.CompareExchange(ref found, candidate, null) == null)
                        await stop.CancelAsync();
                });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        cancellationToken.ThrowIfCancellationRequested();
        return found;
    }

    internal IReadOnlyList<IPAddress> GetCandidates()
    {
        var ranges = _subnets().Select(ToRange).Where(r => r.HasValue).Select(r => r!.Value)
            .DistinctBy(r => (r.First, r.Last)).Take(8).ToArray();
        var positions = ranges.Select(r => _offsets.GetValueOrDefault((r.First, r.Last), r.Start - r.First)).ToArray();
        var visited = new uint[ranges.Length];
        var result = new List<IPAddress>();
        var seen = new HashSet<uint>();
        while (result.Count < MaximumCandidates)
        {
            var advanced = false;
            for (var i = 0; i < ranges.Length && result.Count < MaximumCandidates; i++)
            {
                var range = ranges[i];
                var count = range.Last - range.First + 1;
                if (visited[i] >= count) continue;
                advanced = true;
                var address = range.First + positions[i];
                visited[i]++;
                positions[i] = (positions[i] + 1) % count;
                if (seen.Add(address)) result.Add(FromNumber(address));
            }
            if (!advanced) break;
        }
        for (var i = 0; i < ranges.Length; i++) _offsets[(ranges[i].First, ranges[i].Last)] = positions[i];
        return result;
    }

    private async Task<bool> ProbeAsync(Uri address, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            using var map = await GetJsonAsync(new Uri(address, "map_info.json"), deadline.Token);
            var root = map.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("valid", out var valid)
                || valid.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (valid.GetBoolean() && new TelemetryParser().ParseMap(root.GetRawText()) == null) return false;
            using var objects = await GetJsonAsync(new Uri(address, "map_obj.json"), deadline.Token);
            if (objects.RootElement.ValueKind != JsonValueKind.Array) return false;
            foreach (var item in objects.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || TelemetryParser.Text(item, "type") is not { } type
                    || TelemetryParser.Number(item, type == "airfield" ? "sx" : "x") == null
                    || TelemetryParser.Number(item, type == "airfield" ? "sy" : "y") == null) return false;
            }
            if (valid.GetBoolean()) return true;
            if (objects.RootElement.GetArrayLength() != 0) return false;
            using var state = await GetJsonAsync(new Uri(address, "state"), deadline.Token);
            return state.RootElement.ValueKind == JsonValueKind.Object
                && state.RootElement.TryGetProperty("valid", out var stateValid) && stateValid.ValueKind == JsonValueKind.False;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri address, CancellationToken ct)
    {
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBodyBytes) throw new InvalidDataException("Discovery response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + read > MaximumBodyBytes) throw new InvalidDataException("Discovery response is too large.");
            body.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(body.ToArray());
    }

    private static IReadOnlyList<Subnet> GetSubnets()
    {
        var result = new List<Subnet>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(new(address.Address, address.PrefixLength));
            }
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return result;
    }

    private static (uint First, uint Last, uint Start)? ToRange(Subnet subnet)
    {
        if (subnet.Address.AddressFamily != AddressFamily.InterNetwork || subnet.PrefixLength is < 0 or > 32) return null;
        var address = ToNumber(subnet.Address);
        var privatePrefix = (address >> 24) == 10 ? 8
            : (address >> 20) == 0xAC1 ? 12 : (address >> 16) == 0xC0A8 ? 16 : 0;
        if (privatePrefix == 0) return null;
        // Clamp unusually broad interface masks to their private block, never probing public IPs.
        var prefix = Math.Max(subnet.PrefixLength, privatePrefix);
        var mask = uint.MaxValue << (32 - prefix);
        var first = address & mask;
        var last = first | ~mask;
        if (prefix < 31) { first++; last--; }
        var start = Math.Clamp(address & 0xFFFFFF00, first, last);
        return (first, last, start);
    }

    private static uint ToNumber(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromNumber(uint address) => new(new byte[]
        { (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address });

    /// <summary>Releases the owned HTTP client after pending searches have completed.</summary>
    public void Dispose() => _http.Dispose();

    internal sealed record Subnet(IPAddress Address, int PrefixLength);
}
