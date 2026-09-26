using System.Diagnostics;
using System.Net;
using System.Text.Json;
using MapHelper.Core;
using MapHelper.Telemetry;
using Xunit;

namespace MapHelper.Tests;

public class TelemetryTests
{
    [Theory]
    [InlineData("#f00C00", Affiliation.Enemy)]
    [InlineData("#F00C00", Affiliation.Enemy)]
    [InlineData("#fa0C00", Affiliation.Enemy)]
    [InlineData("#174DFF", Affiliation.Ally)]
    [InlineData("#043FFF", Affiliation.Ally)]
    public void LiveTestFlightPaletteUsesCorrectTeams(string color, Affiliation expected) =>
        Assert.Equal(expected, new TelemetryParser().Classify("Fighter", color));

    [Fact]
    public void CapturedEnemyAircraftAreEnemiesAndOwnMarkerIsSelf()
    {
        var objects = new TelemetryParser().ParseObjects(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testflight-f16xl-map_obj.json")));
        Assert.Single(objects, o => o.Affiliation == Affiliation.Self);
        var aircraft = objects.Where(o => o.Type == "aircraft" && o.Icon != "Player").ToArray();
        Assert.NotEmpty(aircraft); Assert.All(aircraft, o => Assert.Equal(Affiliation.Enemy, o.Affiliation));
    }
    [Fact]
    public void CapturedF16xlIndicatorsContainOnlyUnidentifiedWeaponValues()
    {
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testflight-f16xl-state.json")));
        using var indicators = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "testflight-f16xl-indicators.json")));
        var player = new TelemetryParser().ParsePlayer(state.RootElement, indicators.RootElement);
        Assert.Equal("f_16xl", player.Vehicle); Assert.True(player.FuelKg > 0);
        Assert.Contains("weapon2", player.Weapons.Raw.Keys); Assert.Contains("weapon4", player.Weapons.Raw.Keys);
        Assert.Empty(player.Weapons.Ammunition);
    }
    [Fact]
    public void RawFieldsArePreservedAndMissingValuesStayNull()
    {
        using var doc = JsonDocument.Parse("""{"valid":true,"type":"test","weapon2":0,"ammo_counter1":12,"fuel_consume":42,"new_field":3}""");
        var player = new TelemetryParser().ParsePlayer(null, doc.RootElement);
        Assert.Null(player.FuelKg); Assert.Null(player.VerifiedConsumptionKgS); Assert.Equal(12, player.Weapons.Ammunition["ammo_counter1"]);
        Assert.True(player.Raw!.ContainsKey("indicators.new_field")); Assert.True(player.Weapons.Raw.ContainsKey("weapon2"));
    }
    [Fact]
    public void ObjectsNeverInferDeathOrHumanFromBlink()
    {
        var objects = new TelemetryParser().ParseObjects("""[{"type":"aircraft","icon":"Fighter","color":"#fa3200","blink":2,"x":0.2,"y":0.3,"dx":1,"dy":0}]""");
        var o = Assert.Single(objects); Assert.False(o.Destroyed); Assert.Null(o.IsHuman); Assert.Null(o.SourceId); Assert.Equal(Affiliation.Enemy, o.Affiliation);
    }
    [Fact]
    public void CustomColorsCanBeMappedAndUnknownStaysUnknown()
    {
        var p = new TelemetryParser(); Assert.Equal(Affiliation.Unknown, p.Classify("Fighter", "#123456"));
        p.ColorOverrides["#123456"] = Affiliation.Ally; Assert.Equal(Affiliation.Ally, p.Classify("Fighter", "#123456"));
        Assert.Equal(Affiliation.Self, p.Classify("Player", "#123456"));
    }
    [Fact]
    public void HudTextCannotLocateAnEnemy()
    {
        var result = TelemetryParser.ParseEvents("""{"events":[],"damage":[{"id":1,"msg":"Enemy has crashed","enemy":false}]}""");
        var e = Assert.Single(result.Events); Assert.Null(e.TargetId); Assert.False(e.ConfirmedDestruction);
    }
    [Fact]
    public void PacketSerializationRoundTripsWithoutComputedPropertyRecursion()
    {
        var p = new TelemetryPacket(Endpoint.Objects, 1, 1, Map: TrackingTests.Map, Objects: [TrackingTests.Enemy(.3)]);
        var restored = JsonSerializer.Deserialize<TelemetryPacket>(JsonSerializer.Serialize(p));
        Assert.NotNull(restored); Assert.Equal(p.Map, restored.Map); Assert.Equal(.3, Assert.Single(restored.Objects!).Position.X);
    }
    [Fact]
    public void MapEpochAndTimestampRejectOldResponses()
    {
        var e = new MapEngine(); e.Accept(new(Endpoint.MapInfo, 0, 2, Map: TrackingTests.Map));
        e.Accept(new(Endpoint.Objects, 1, 2, Objects: [TrackingTests.Enemy(.4)]));
        e.Accept(new(Endpoint.Objects, 3, 1, Objects: [TrackingTests.Enemy(.9)]));
        e.Accept(new(Endpoint.Objects, .5, 2, Objects: [TrackingTests.Enemy(.1)]));
        Assert.Equal(3000, Assert.Single(e.Scene(1).Contacts).Position.X, 3);
    }
    [Fact]
    public void NewMapAndMissionEndClearContacts()
    {
        var e = new MapEngine(); e.Accept(new(Endpoint.MapInfo, 0, 1, Map: TrackingTests.Map)); e.Accept(new(Endpoint.Objects, .1, 1, Objects: [TrackingTests.Enemy(.4)]));
        e.Accept(new(Endpoint.MapInfo, .2, 2, Map: TrackingTests.Map with { Key = "other" })); Assert.Empty(e.Scene(.2).Contacts);
        e.Accept(new(Endpoint.Objects, .3, 2, Objects: [TrackingTests.Enemy(.4)])); e.Accept(new(Endpoint.Mission, .4, 2, MissionStatus: "success"));
        Assert.Empty(e.Scene(.4).Contacts);
    }
    [Fact]
    public void DuplicatedEventsAreDisplayedOnce()
    {
        var e = new MapEngine(); var events = new[] { new CombatEvent("a", "Test") };
        e.Accept(new(Endpoint.Events, 0, 1, Events: events)); e.Accept(new(Endpoint.Events, .1, 1, Events: events));
        Assert.Single(e.Scene(.1).Events);
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public bool DelayFailureUntilNextMap;
        private int _mapCalls, _stateCalls;
        public readonly Dictionary<string, int> Active = [];
        public bool Overlap;
        public int ObjectCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Active) { Active[path] = Active.GetValueOrDefault(path) + 1; if (Active[path] > 1) Overlap = true; }
            try
            {
                if (path == "/state") await Task.Delay(220, ct);
                if (path == "/state" && DelayFailureUntilNextMap && Interlocked.Increment(ref _stateCalls) == 1)
                {
                    while (Volatile.Read(ref _mapCalls) < 2) await Task.Delay(10, ct);
                    await Task.Delay(50, ct);
                    throw new HttpRequestException("Previous map request failed late");
                }
                if (path == "/map_obj.json" && Interlocked.Increment(ref ObjectCalls) == 1) throw new HttpRequestException("simulated offline");
                var body = path switch
                {
                    "/map_info.json" => "{\"valid\":true,\"map_generation\":" + (DelayFailureUntilNextMap ? Interlocked.Increment(ref _mapCalls) : 1) + ",\"map_min\":[0,0],\"map_max\":[10000,20000]}",
                    "/map_obj.json" => """[{"type":"aircraft","icon":"Player","x":0.5,"y":0.5}]""",
                    "/mission.json" => """{"status":"running"}""",
                    "/hudmsg" => """{"events":[],"damage":[]}""",
                    "/state" => """{"valid":true,"Mfuel, kg":100}""",
                    "/indicators" => """{"valid":true,"type":"jet"}""",
                    _ => "image"
                };
                return new(HttpStatusCode.OK) { Content = new StringContent(body) };
            }
            finally { lock (Active) Active[path]--; }
        }
    }
    [Fact]
    public async Task DelayedFailureFromPreviousMapDoesNotInvalidateNewSession()
    {
        var handler = new FakeHandler { DelayFailureUntilNextMap = true }; var clock = Stopwatch.StartNew();
        await using var source = new HttpTelemetrySource(new("http://localhost:8111/"), () => clock.Elapsed.TotalSeconds, handler: handler);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var packets = new List<TelemetryPacket>();
        await foreach (var p in source.ReadAsync(ct.Token))
        {
            packets.Add(p);
            if (clock.Elapsed.TotalSeconds > 1.5 && packets.Any(x => x.Endpoint == Endpoint.State && x.Success && x.Epoch == 2)) break;
        }
        Assert.Contains(packets, p => p.Map?.Generation == 2);
        Assert.DoesNotContain(packets, p => !p.Success && p.Error == "Previous map request failed late");
        Assert.Contains(packets, p => p.Endpoint == Endpoint.State && p.Success && p.Epoch == 2);
    }
    [Fact]
    public async Task IndependentPollingRecoversAndDoesNotOverlapSlowEndpoints()
    {
        var handler = new FakeHandler(); var clock = Stopwatch.StartNew();
        await using var source = new HttpTelemetrySource(new("http://localhost:8111/"), () => clock.Elapsed.TotalSeconds, handler: handler);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var packets = new List<TelemetryPacket>();
        await foreach (var p in source.ReadAsync(ct.Token))
        {
            packets.Add(p);
            if (clock.Elapsed.TotalSeconds > 1.2 && packets.Count(x => x.Endpoint == Endpoint.Objects && x.Success) >= 5) break;
        }
        Assert.False(handler.Overlap);
        Assert.Contains(packets, p => p.Endpoint == Endpoint.Objects && !p.Success);
        Assert.True(packets.Count(p => p.Endpoint == Endpoint.Objects && p.Success) > packets.Count(p => p.Endpoint == Endpoint.State && p.Success));
        Assert.All(handler.Active, p => Assert.Equal(0, p.Value));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[false]")]
    public void MalformedObjectResponsesAreHandled(string json)
    {
        if (json == "[false]") Assert.Empty(new TelemetryParser().ParseObjects(json));
        else Assert.Throws<InvalidDataException>(() => new TelemetryParser().ParseObjects(json));
    }
}
