using System.Text.Json;
using MapHelper.Core;
using MapHelper.Telemetry;
using Xunit;

namespace MapHelper.Tests;

public sealed class InteractionLogicTests
{
    private static readonly MapInfo Map = new("test", 1, default, new(10000, 20000), default, default);
    private static Contact Unit(long id, double x, Affiliation team = Affiliation.Enemy, bool? human = null, string type = "aircraft")
        => new(id, new(id.ToString(), type, team == Affiliation.Self ? "Player" : "Fighter", team, "#fa3200", new(x / 10000, .1), IsHuman: human),
            new(x, 2000), new Vec2(100, 0), new Vec2(1, 0), 1, 0, ContactPhase.Live, null, false);
    private static Contact Own => Unit(1, 1000, Affiliation.Self, true);
    private static MapScene Scene(params Contact[] units) => new(Map, units, null, null, "Live", true, 1, []);

    [Fact]
    public void ZoomDefaultsIncludeAiAndBasesAndFiltersNeverHideSelfOrUnknownIdentity()
    {
        var planner = new CameraFocusPlanner();
        var scene = Scene(Own, Unit(2, 3000, human: false), Unit(3, 6000, type: "bombing_point"), Unit(4, 4000));
        Assert.Equal(4, planner.Update(scene, new()).Points.Count);
        Assert.Equal(new[] { Own.Position, new Vec2(4000, 2000) }, planner.Update(scene, new(false, false)).Points);
        Assert.Equal(3, planner.Update(scene, new(false, true)).Points.Count);
        Assert.Equal(3, planner.Update(scene, new(true, false)).Points.Count);
    }
    [Fact]
    public void AirfieldsUseBothEndsOnlyWhenBasesIncluded()
    {
        var field = Unit(2, 2000, type: "airfield");
        field = field with { Observation = field.Observation with { EndPosition = new(.7, .4) } };
        var planner = new CameraFocusPlanner();
        Assert.Contains(new Vec2(7000, 8000), planner.Update(Scene(Own, field), new()).Points);
        Assert.Single(planner.Update(Scene(Own, field), new(true, false)).Points);
    }
    [Fact]
    public void LastEnemyPositionRemainsAfterGhostExpiresWithoutInventingAContact()
    {
        var planner = new CameraFocusPlanner(); var enemy = Unit(2, 5000);
        planner.Update(Scene(Own, enemy), new(false, false));
        var ghost = enemy with { Phase = ContactPhase.Predicted, Age = 1, Position = new(5100, 2000) };
        Assert.Null(planner.Update(Scene(Own, ghost), new(false, false)).Fallback);
        var scene = Scene(Own) with { Time = 4 };
        var result = planner.Update(scene, new(false, false));
        Assert.Equal(enemy.Position, result.Fallback!.Position);
        Assert.Contains(Own.Position, result.Points); Assert.Contains(enemy.Position, result.Points);
        Assert.Single(scene.Contacts);
    }
    [Fact]
    public void FallbackUsesLastRemainingEnemyAndNewSightingReplacesIt()
    {
        var planner = new CameraFocusPlanner();
        planner.Update(Scene(Own, Unit(2, 3000), Unit(3, 6000)), new(false, false));
        planner.Update(Scene(Own, Unit(3, 7000) with { LastSeen = 2 }), new(false, false));
        Assert.Equal(new Vec2(7000, 2000), planner.Update(Scene(Own), new(false, false)).Fallback!.Position);
        Assert.Null(planner.Update(Scene(Own, Unit(4, 9000) with { LastSeen = 3 }), new(false, false)).Fallback);
        Assert.Equal(4, planner.Update(Scene(Own), new(false, false)).Fallback!.TrackId);
    }
    [Fact]
    public void ExcludedAiOrBasesNeverBecomeLastEnemyAnchor()
    {
        var planner = new CameraFocusPlanner();
        planner.Update(Scene(Own, Unit(2, 3000, human: false), Unit(3, 9000, type: "bombing_point")), new());
        Assert.Null(planner.Update(Scene(Own), new(false, false)).Fallback);
    }
    [Fact]
    public void FallbackRequiresBothFiltersOffAndLiveOwnPosition()
    {
        var planner = new CameraFocusPlanner(); planner.Update(Scene(Own, Unit(2, 3000)), new());
        Assert.Null(planner.Update(Scene(Own), new(true, false)).Fallback);
        Assert.Null(planner.Update(Scene(Own), new(false, true)).Fallback);
        Assert.Null(planner.Update(Scene(), new(false, false)).Fallback);
        Assert.NotNull(planner.Update(Scene(Own), new(false, false)).Fallback);
    }
    [Fact]
    public void MapSessionAndConnectionChangesDiscardTheFallback()
    {
        foreach (var scene in new[] { Scene(Own) with { SessionId = 2 }, Scene(Own) with { Map = Map with { Key = "new" } }, Scene(Own) with { Live = false } })
        {
            var planner = new CameraFocusPlanner(); planner.Update(Scene(Own, Unit(2, 4000)), new(false, false));
            Assert.Null(planner.Update(scene, new(false, false)).Fallback);
            Assert.Null(planner.Update(scene with { Live = true }, new(false, false)).Fallback);
        }
    }
    [Fact]
    public void EngineChangesNavigationSessionOnRespawnAndMissionTransitions()
    {
        var engine = new MapEngine(); engine.Accept(new(Endpoint.MapInfo, 0, 1, Map: Map));
        engine.Accept(new(Endpoint.Objects, .1, 1, Objects: [Own.Observation])); var first = engine.Scene(.1).SessionId;
        engine.Accept(new(Endpoint.Objects, .2, 1, Objects: [])); Assert.NotEqual(first, engine.Scene(.2).SessionId);
        engine.Accept(new(Endpoint.Objects, .3, 1, Objects: [Own.Observation])); var respawn = engine.Scene(.3).SessionId;
        engine.Accept(new(Endpoint.Mission, .4, 1, MissionStatus: "success")); Assert.NotEqual(respawn, engine.Scene(.4).SessionId);
    }
    [Fact]
    public void RulerMeasuresContinuouslyAndPersistsAfterRelease()
    {
        var ruler = new DistanceMeasurement(); ruler.Begin(new(1000, 2000), new(20, 20));
        ruler.Move(new(1300, 2400), new(50, 60)); Assert.Equal(500, ruler.Segment!.Meters);
        ruler.Move(new(1600, 2800), new(80, 100)); Assert.Equal(1000, ruler.Segment!.Meters);
        ruler.End(new(1900, 3200), new(110, 140)); Assert.Equal(1500, ruler.Segment!.Meters); Assert.False(ruler.IsPressed);
        ruler.Move(new(9000, 5000), new(200, 300)); Assert.Equal(1500, ruler.Segment.Meters);
    }
    [Fact]
    public void RightClickClearsOldMeasurementAndDragStartsANewOne()
    {
        var ruler = new DistanceMeasurement(); ruler.Begin(default, default); ruler.End(new(300, 400), new(30, 40));
        ruler.Begin(new(500, 500), new(10, 10)); Assert.Null(ruler.Segment);
        ruler.End(new(501, 501), new(11, 11)); Assert.Null(ruler.Segment);
        ruler.Begin(default, default); ruler.End(new(600, 800), new(30, 40)); Assert.Equal(1000, ruler.Segment!.Meters);
        ruler.Reset(); Assert.Null(ruler.Segment);
    }
    [Fact]
    public void PointerCaptureLossEndsMeasurementAndInvalidCoordinatesAreIgnored()
    {
        var ruler = new DistanceMeasurement(); ruler.Begin(default, default); ruler.Move(new(300, 400), new(30, 40));
        ruler.Move(new(double.NaN, 0), new(50, 50)); ruler.Finish();
        Assert.False(ruler.IsPressed); Assert.Equal(500, ruler.Segment!.Meters);
    }
    [Theory]
    [InlineData("#00FF00")]
    [InlineData("#00C800")]
    [InlineData("#00fa00")]
    public void GreenSquadPalettesStaySeparateFromBlueAllies(string color)
    {
        var parser = new TelemetryParser(); Assert.Equal(Affiliation.Squad, parser.Classify("Fighter", color));
        Assert.Equal(Affiliation.Self, parser.Classify("Player", color));
        parser.ColorOverrides["#123456"] = Affiliation.Squad; Assert.Equal(Affiliation.Squad, parser.Classify("Fighter", "#123456"));
    }
    [Theory]
    [InlineData("aircraft", "Fighter", "\"is_ai\":true", false)]
    [InlineData("aircraft", "Fighter", "\"is_human\":true", true)]
    [InlineData("aircraft", "Fighter", "\"is_bot\":true", false)]
    [InlineData("ground_model", "Wheeled", "\"blink\":0", false)]
    [InlineData("ground_model", "Tracked", "\"blink\":0", false)]
    public void AiIdentityUsesExplicitFlagsOrDocumentedAiOnlyIcons(string type, string icon, string field, bool human)
    {
        var item = Assert.Single(new TelemetryParser().ParseObjects("[{\"type\":\"" + type + "\",\"icon\":\"" + icon + "\",\"x\":0.2,\"y\":0.3," + field + "}]"));
        Assert.Equal(human, item.IsHuman); Assert.NotNull(item.IdentityBasis);
    }
    [Theory]
    [InlineData("\"is_ai\":true,\"is_human\":true")]
    [InlineData("\"blink\":2")]
    [InlineData("\"is_ai\":\"true\"")]
    [InlineData("\"is_player\":false")]
    public void AmbiguousOrAbsentAiIdentityRemainsUnknown(string fields)
    {
        var item = Assert.Single(new TelemetryParser().ParseObjects("[{\"type\":\"aircraft\",\"icon\":\"Fighter\",\"x\":0.2,\"y\":0.3," + fields + "}]"));
        Assert.Null(item.IsHuman);
    }
    [Fact]
    public void HoverShowsHiddenDistanceAndTimeForAnyUnitWithoutDisplaySwitches()
    {
        var details = ContactInformation.Build(Scene(Own, Unit(2, 3000, Affiliation.Squad, true)), Unit(2, 3000, Affiliation.Squad, true));
        Assert.Contains("Own squad", details.Summary); Assert.Contains("Distance: 2.00 km", details.Summary);
        Assert.Contains("≈ 20 s", details.Summary); Assert.Contains("360 km/h", details.Summary);
    }
    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void DisplayUsesEnglishNumbersRegardlessOfHostCulture(string cultureName)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal("999.5 m", ContactInformation.Distance(999.5));
            Assert.Equal("1.23 km", ContactInformation.Distance(1234.5));
            Assert.Equal("1:01 min", ContactInformation.Duration(60.5));
            Assert.Equal("1 h 01 min", ContactInformation.Duration(3660));
            var details = ContactInformation.Build(Scene(Own), Own);
            Assert.Contains("Own player", details.Summary);
            Assert.Contains("Map position: X 1,000 m", details.Summary);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void OwnHoverIncludesFuelRangeAltitudeAmmoRawFieldsAndHudMessagesWithoutLeakingThemToEnemies()
    {
        using var raw = JsonDocument.Parse("{\"weapon2\":0}");
        var values = TelemetryParser.Raw(raw.RootElement);
        var scene = Scene(Own, Unit(2, 2000)) with
        {
            Player = new(true, "Demojet", 100, 720, 3500, null, new(new Dictionary<string, double> { ["ammo_counter1"] = 120 }, values), values),
            Range = new(200, 20000, .5, 5, false),
            Events = ["Test message"]
        };
        var own = ContactInformation.Build(scene, Own); var enemy = ContactInformation.Build(scene, Unit(2, 2000));
        foreach (var text in new[] { "Demojet", "100 kg", "3500 m", "20.00 km", "30 kg/min", "ammo_counter1", "Test message" }) Assert.Contains(text, own.Summary);
        Assert.Contains("weapon2: 0", own.RawValues);
        Assert.DoesNotContain("Fuel", enemy.Summary); Assert.DoesNotContain("weapon2", enemy.RawValues); Assert.DoesNotContain("Test message", enemy.Summary);
    }
}
