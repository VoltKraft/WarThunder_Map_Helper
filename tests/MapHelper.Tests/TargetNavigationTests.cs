using System.Text.Json;
using System.Text.Json.Serialization;
using MapHelper.Core;
using Xunit;

namespace MapHelper.Tests;

public sealed class TargetNavigationTests
{
    private static readonly MapInfo Map = new("targets", 1, new(0, 0), new(10000, 20000), default, default);
    private const TargetValues Both = TargetValues.Distance | TargetValues.Time;
    private static readonly TargetDisplayOptions All = new(Both, Both, Both, Both, Both);
    private static Contact Unit(long id, double x, double y, string type = "aircraft", string icon = "Fighter", Affiliation team = Affiliation.Enemy)
        => new(id, new(id.ToString(), type, icon, team, "#fa3200", Map.ToNormalized(new(x, y))),
            new(x, y), new Vec2(100, 0), new Vec2(1, 0), 1, 0, ContactPhase.Live, null, false);
    private static Contact Player => Unit(1, 1000, 1000, icon: "Player", team: Affiliation.Self);
    private static MapScene Scene(params Contact[] contacts) => new(Map, contacts, null, null, "Live", true, 1, []);
    private static Contact Mark(Contact contact) => contact with { Observation = contact.Observation with { BackgroundIcon = contact.Observation.Icon + "Target" } };

    [Theory]
    [InlineData("aircraft", "Fighter")]
    [InlineData("ground_model", "MediumTank")]
    [InlineData("bombing_point", "bombing_point")]
    [InlineData("ship", "Ship")]
    public void MarkedTargetShowsBothValuesByDefaultEvenBehindPlayerWithOtherReadoutsOff(string type, string icon)
    {
        var target = Mark(Unit(2, 700, 1400, type, icon));
        var readout = Assert.Single(TargetNavigation.Calculate(Scene(Player, target), new(CourseTarget: TargetValues.None)));
        Assert.True(readout.IsMarkedTarget); Assert.False(readout.IsCourseTarget);
        Assert.Equal(Both, readout.Values); Assert.Equal(500, readout.DistanceMeters); Assert.Equal(5d, readout.Seconds);
    }
    [Fact]
    public void MarkedAndCourseTargetsHaveIndependentReadoutsWithoutDuplicateLabels()
    {
        var target = Mark(Unit(2, 2000, 1000));
        var scene = Scene(Player, target, Unit(3, 3000, 1200));
        var options = new TargetDisplayOptions(CourseTarget: TargetValues.Time, Air: TargetValues.Distance);
        var result = TargetNavigation.Calculate(scene, options);
        Assert.Equal(2, result.Count); var marked = result[0];
        Assert.Equal(2, marked.TrackId); Assert.True(marked.IsMarkedTarget); Assert.True(marked.IsCourseTarget);
        Assert.Equal(Both, marked.Values); Assert.Equal(TargetValues.Distance, result[1].Values);
        Assert.Equal(TargetValues.Time, TargetNavigation.Calculate(scene, options with { MarkedTarget = TargetValues.Time })[0].Values);
        Assert.Equal(3, Assert.Single(TargetNavigation.Calculate(scene, options with { MarkedTarget = TargetValues.None })).TrackId);
    }
    [Fact]
    public void MarkedLabelMovesWithSelectionAndNeverTreatsLostOrStaleMarkersAsCurrent()
    {
        var first = Unit(2, 700, 1400); var second = Unit(3, 600, 1300);
        var options = new TargetDisplayOptions(CourseTarget: TargetValues.None);
        Assert.Equal(2, Assert.Single(TargetNavigation.Calculate(Scene(Player, Mark(first), second), options)).TrackId);
        Assert.Equal(3, Assert.Single(TargetNavigation.Calculate(Scene(Player, first, Mark(second)), options)).TrackId);
        Assert.Empty(TargetNavigation.Calculate(Scene(Player, first, second), options));
        foreach (var phase in new[] { ContactPhase.Predicted, ContactPhase.Stale, ContactPhase.Destroyed })
            Assert.Empty(TargetNavigation.Calculate(Scene(Player, Mark(first) with { Phase = phase, Age = .1 }), options));
        Assert.Empty(TargetNavigation.Calculate(Scene(Player, Mark(first)) with { Live = false }, options));
    }
    [Fact]
    public void MarkedTargetDoesNotNeedHeadingAndUnavailableSpeedDoesNotInventArrivalTime()
    {
        var target = Mark(Unit(2, 700, 1400));
        var readout = Assert.Single(TargetNavigation.Calculate(Scene(Player with { Direction = null, Velocity = null }, target), new()));
        Assert.Equal(Both, readout.Values); Assert.Equal(500, readout.DistanceMeters); Assert.Null(readout.Seconds);
    }

    [Fact]
    public void CourseTargetUsesSmallestLateralDistanceNotNearestDistanceToPlayer()
    {
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player, Unit(2, 1300, 1020), Unit(3, 9000, 1005)), new()));
        Assert.Equal(3, result.TrackId); Assert.True(result.IsCourseTarget);
    }
    [Fact]
    public void EnemyBehindPlayerCannotBeCourseTarget()
    {
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player, Unit(2, 800, 1000), Unit(3, 3000, 1040)), new()));
        Assert.Equal(3, result.TrackId);
    }
    [Fact]
    public void TiedCourseTargetsPreferCloserTargetThenStableTrackId()
    {
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player, Unit(4, 4000, 1000), Unit(3, 2000, 1000), Unit(2, 2000, 1000)), new()));
        Assert.Equal(2, result.TrackId);
    }
    [Fact]
    public void DirectionComesFromPlayerCourseAndNeedNotBeUnitLength()
    {
        var player = Player with { Direction = new Vec2(0, 8), Velocity = new Vec2(0, 100) };
        var result = Assert.Single(TargetNavigation.Calculate(Scene(player, Unit(2, 4000, 1010), Unit(3, 1005, 4000)), new()));
        Assert.Equal(3, result.TrackId);
    }
    [Fact]
    public void TimeUsesDirectMapDistanceAndOwnSpeedRatherThanTargetSpeedOrAirspeed()
    {
        var scene = Scene(Player, Unit(2, 1300, 1400) with { Velocity = new Vec2(-500, 200) }) with
        { Player = new PlayerTelemetry(true, "jet", 50, 1800, 3000, null, WeaponTelemetry.Empty) };
        var result = Assert.Single(TargetNavigation.Calculate(scene, new()));
        Assert.Equal(500, result.DistanceMeters, 8); Assert.Equal(5, result.Seconds!.Value, 8);
    }
    [Fact]
    public void GroundSpeedChangesUpdateTimeImmediately()
    {
        var target = Unit(2, 2000, 1000);
        Assert.Equal(10d, Assert.Single(TargetNavigation.Calculate(Scene(Player, target), new())).Seconds);
        Assert.Equal(5d, Assert.Single(TargetNavigation.Calculate(Scene(Player with { Velocity = new Vec2(200, 0) }, target), new())).Seconds);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void StoppedOrInvalidSpeedKeepsDistanceWithoutInventingTime(double speed)
    {
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player with { Velocity = new Vec2(speed, 0) }, Unit(2, 2000, 1000)), new()));
        Assert.Equal(1000, result.DistanceMeters); Assert.Null(result.Seconds);
    }
    [Fact]
    public void MissingOrAmbiguousSpeedDoesNotProduceTime()
    {
        foreach (var player in new[] { Player with { Velocity = null }, Player with { Ambiguous = true } })
            Assert.Null(Assert.Single(TargetNavigation.Calculate(Scene(player, Unit(2, 2000, 1000)), new())).Seconds);
    }
    [Fact]
    public void UnknownDirectionAllowsCategoryLabelsButNoCourseTarget()
    {
        var scene = Scene(Player with { Direction = null }, Unit(2, 2000, 1000));
        Assert.Empty(TargetNavigation.Calculate(scene, new()));
        Assert.False(Assert.Single(TargetNavigation.Calculate(scene, new(Air: Both))).IsCourseTarget);
    }
    [Fact]
    public void MissingPlayerOrConnectionSuppressesNavigation()
    {
        Assert.Empty(TargetNavigation.Calculate(Scene(Unit(2, 2000, 1000)), All));
        Assert.Empty(TargetNavigation.Calculate(Scene(Player, Unit(2, 2000, 1000)) with { Live = false }, All));
        Assert.Empty(TargetNavigation.Calculate(Scene(Player with { Phase = ContactPhase.Stale }, Unit(2, 2000, 1000)), All));
    }
    [Fact]
    public void AlliesUnknownObjectsDestroyedAndStaleContactsAreExcluded()
    {
        var result = TargetNavigation.Calculate(Scene(Player,
            Unit(2, 2000, 1000, team: Affiliation.Ally), Unit(3, 2000, 1000, team: Affiliation.Unknown),
            Unit(4, 2000, 1000, "point_of_interest"), Unit(5, 2000, 1000) with { Phase = ContactPhase.Destroyed },
            Unit(6, 2000, 1000) with { Phase = ContactPhase.Stale }), All);
        Assert.Empty(result);
    }
    [Fact]
    public void LostContactIsMarkedAsEstimatedAndExpiresAtTwoSeconds()
    {
        var target = Unit(2, 2000, 1000) with { Phase = ContactPhase.Predicted, Age = 1.999 };
        Assert.True(Assert.Single(TargetNavigation.Calculate(Scene(Player, target), new())).Estimated);
        Assert.Empty(TargetNavigation.Calculate(Scene(Player, target with { Age = 2 }), All));
    }
    [Theory]
    [InlineData("aircraft", "Fighter", TargetCategory.Air)]
    [InlineData("air_model", "Bomber", TargetCategory.Air)]
    [InlineData("ground_model", "SAM", TargetCategory.Ground)]
    [InlineData("tank", "MediumTank", TargetCategory.Ground)]
    [InlineData("bombing_point", "bombing_point", TargetCategory.Base)]
    [InlineData("airfield", "none", TargetCategory.Base)]
    [InlineData("ground_model", "MissileDestroyer", TargetCategory.Sea)]
    [InlineData("ground_model", "Frigate", TargetCategory.Sea)]
    [InlineData("ground_model", "Boat", TargetCategory.Sea)]
    [InlineData("ship", "Ship", TargetCategory.Sea)]
    public void CategoriesDistinguishAircraftGroundBasesAndBrowserApiShips(string type, string icon, TargetCategory category)
        => Assert.Equal(category, TargetNavigation.Category(Unit(2, 2000, 1000, type, icon).Observation));
    [Fact]
    public void DistanceAndTimeCanBeSwitchedIndependentlyForEachCategory()
    {
        var settings = new TargetDisplayOptions(CourseTarget: TargetValues.None, Air: TargetValues.Distance, Ground: TargetValues.Time, Bases: Both);
        var result = TargetNavigation.Calculate(Scene(Player with { Direction = null }, Unit(2, 2000, 1000),
            Unit(3, 3000, 1000, "ground_model"), Unit(4, 4000, 1000, "bombing_point"), Unit(5, 5000, 1000, "ship")), settings);
        Assert.Equal(3, result.Count);
        Assert.Equal(TargetValues.Distance, result.Single(r => r.Category == TargetCategory.Air).Values);
        Assert.Equal(TargetValues.Time, result.Single(r => r.Category == TargetCategory.Ground).Values);
        Assert.Equal(Both, result.Single(r => r.Category == TargetCategory.Base).Values);
    }
    [Fact]
    public void CourseTargetHasIndependentSwitchesAndNeverGetsDuplicateLabels()
    {
        var scene = Scene(Player, Unit(2, 2000, 1000), Unit(3, 3000, 1200));
        var result = TargetNavigation.Calculate(scene, new(CourseTarget: TargetValues.Time, Air: Both));
        Assert.Equal(2, result.Count); Assert.Single(result, r => r.IsCourseTarget);
        Assert.Equal(TargetValues.Time, result.Single(r => r.IsCourseTarget).Values);
        var disabled = Assert.Single(TargetNavigation.Calculate(scene, new(CourseTarget: TargetValues.None, Air: Both)));
        Assert.Equal(3, disabled.TrackId);
        Assert.Empty(TargetNavigation.Calculate(scene, new(CourseTarget: TargetValues.None)));
    }
    [Fact]
    public void AirfieldDistanceUsesRunwayCenter()
    {
        var field = Unit(2, 3000, 1000, "airfield", "none");
        field = field with { Observation = field.Observation with { EndPosition = Map.ToNormalized(new(5000, 1000)) } };
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player, field), new()));
        Assert.Equal(new Vec2(4000, 1000), result.Position); Assert.Equal(3000, result.DistanceMeters);
    }
    [Fact]
    public void CourseSelectionUsesTheDrawnSegmentUpToMapEdge()
    {
        var result = Assert.Single(TargetNavigation.Calculate(Scene(Player, Unit(2, 15000, 1000), Unit(3, 9000, 1100)), new()));
        Assert.Equal(3, result.TrackId);
        Assert.Equal(new Vec2(10000, 1000), TargetNavigation.CourseEnd(Player.Position, new(1, 0), Map));
    }
    [Fact]
    public void TargetSettingsRoundTripAndOldProfilesGetUsefulDefaults()
    {
        var jsonOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var options = new TargetDisplayOptions(TargetValues.Time, TargetValues.Distance, Both, TargetValues.None, Both);
        Assert.Equal(options, JsonSerializer.Deserialize<TargetDisplayOptions>(JsonSerializer.Serialize(options, jsonOptions), jsonOptions));
        Assert.Equal(new TargetDisplayOptions(), JsonSerializer.Deserialize<TargetDisplayOptions>("{}", jsonOptions));
    }
}
