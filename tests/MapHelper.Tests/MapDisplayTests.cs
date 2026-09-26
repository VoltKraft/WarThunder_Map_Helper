using MapHelper.Core;
using MapHelper.Telemetry;
using Xunit;

namespace MapHelper.Tests;

public sealed class MapDisplayTests
{
    [Fact]
    public void ObservedBattlePalettesKeepSquadGreenAndAlliedShipsClassified()
    {
        var objects = new TelemetryParser().ParseObjects(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "battle-su30sm2-map_obj.json")));
        Assert.Single(objects.Where(o => o.Color == "#39D921" && o.Affiliation == Affiliation.Squad));
        Assert.Single(objects.Where(o => o.Color == "#134AFF" && o.Affiliation == Affiliation.Ally));
        Assert.Single(objects.Where(o => o.Affiliation == Affiliation.Self));
        foreach (var icon in new[] { "MissileCorvette", "MissileLightCruiser", "AircraftCarrier" })
        {
            var ship = Assert.Single(objects.Where(o => o.Icon == icon));
            Assert.Equal("Ship", ship.IconClass); Assert.Equal(TargetCategory.Sea, TargetNavigation.Category(ship));
        }
        Assert.All(objects.Where(o => o.Icon == "Wheeled"), o => Assert.False(o.IsHuman));
        Assert.All(objects.Where(o => o.Icon == "Fighter"), o => Assert.Null(o.IsHuman));
    }
    private static Contact Unit(long id, Affiliation team, Vec2 position, Vec2? direction = null) => new(id,
        new(id.ToString(), "aircraft", "Fighter", team, "#000000", position / 10000), position, new Vec2(100, 0), direction, 1, 0, ContactPhase.Live, null, false);
    private static MapScene Scene(params Contact[] contacts) => new(new("map", 1, default, new(10000, 10000), default, default),
        contacts, null, null, "Live", true, 1, []);
    private static Contact Self => Unit(1, Affiliation.Self, new(1000, 1000), new(1, 0));

    [Fact]
    public void HidingAlliesKeepsSelfSquadAndEnemiesAndRemovesThemFromCamera()
    {
        var source = Scene(Self, Unit(2, Affiliation.Ally, new(9000, 9000)), Unit(3, Affiliation.Squad, new(2000, 2000)), Unit(4, Affiliation.Enemy, new(3000, 3000)));
        Assert.Equal(4, MapDisplay.ApplyVisibility(source, new()).Contacts.Count);
        var displayed = MapDisplay.ApplyVisibility(source, new(false));
        Assert.Equal(new long[] { 1, 3, 4 }, displayed.Contacts.Select(c => c.TrackId));
        Assert.Equal(3, new CameraFocusPlanner().Update(displayed, new()).Points.Count);
        Assert.Equal(4, source.Contacts.Count);
    }
    [Fact]
    public void VectorOptionsAreIndependentAndUseCourseNearestEnemy()
    {
        var squad = Unit(2, Affiliation.Squad, new(2000, 2000), new(0, -1));
        var near = Unit(3, Affiliation.Enemy, new(8000, 1100), new(-1, 0));
        var close = Unit(4, Affiliation.Enemy, new(1500, 4000), new(0, 1));
        var scene = Scene(Self, squad, near, close);
        Assert.Equal(2, MapDisplay.Vectors(scene, new()).Count);
        Assert.Empty(MapDisplay.Vectors(scene, new(SquadVectors: false, TargetVector: false)));
        var squadLine = Assert.Single(MapDisplay.Vectors(scene, new(SquadVectors: true, TargetVector: false)));
        Assert.Equal(2, squadLine.TrackId); Assert.Equal(new Vec2(2000, 0), squadLine.End); Assert.False(squadLine.IsTarget);
        var enemyLine = Assert.Single(MapDisplay.Vectors(scene, new(SquadVectors: false, TargetVector: true)));
        Assert.Equal(3, enemyLine.TrackId); Assert.Equal(new Vec2(0, 1100), enemyLine.End); Assert.True(enemyLine.IsTarget);
        Assert.Equal(2, MapDisplay.Vectors(scene, new(SquadVectors: true, TargetVector: true)).Count);
    }
    [Fact]
    public void UnknownDirectionDoesNotInventVectorButTargetRemainsIdentifiable()
    {
        var scene = Scene(Self, Unit(2, Affiliation.Enemy, new(8000, 1100)));
        Assert.Equal(2, MapDisplay.FocusTarget(scene)!.TrackId);
        Assert.Empty(MapDisplay.Vectors(scene, new(TargetVector: true)));
    }
    [Fact]
    public void StaleDestroyedOrExpiredContactsNeverProduceVectors()
    {
        foreach (var phase in new[] { ContactPhase.Stale, ContactPhase.Destroyed, ContactPhase.Predicted })
        {
            var scene = Scene(Self, Unit(2, Affiliation.Squad, new(8000, 1100), new(1, 0)) with { Phase = phase, Age = 2 });
            Assert.Empty(MapDisplay.Vectors(scene, new(SquadVectors: true)));
        }
        Assert.Empty(MapDisplay.Vectors(Scene(Self, Unit(2, Affiliation.Squad, new(8000, 1100), new(1, 0))) with { Live = false }, new(SquadVectors: true)));
    }
    [Fact]
    public void PredictedEnemyLineIsMarkedAsEstimatedAndStopsAtTwoSeconds()
    {
        var ghost = Unit(2, Affiliation.Enemy, new(8000, 1100), new(1, 0)) with { Phase = ContactPhase.Predicted, Age = 1.9 };
        Assert.True(Assert.Single(MapDisplay.Vectors(Scene(Self, ghost), new(TargetVector: true))).Estimated);
        Assert.Empty(MapDisplay.Vectors(Scene(Self, ghost with { Age = 2 }), new(TargetVector: true)));
    }
    [Fact]
    public void BlinkAnimationDoesNotOverrideCourseTargetSelection()
    {
        var parser = new TelemetryParser();
        var obj = Assert.Single(parser.ParseObjects("[{\"type\":\"aircraft\",\"icon\":\"Fighter\",\"color\":\"#FA3200\",\"x\":0.2,\"y\":0.9,\"blink\":2}]"));
        var blinking = Unit(2, Affiliation.Enemy, new(2000, 9000)) with { Observation = obj };
        Assert.Equal(3, MapDisplay.FocusTarget(Scene(Self, blinking, Unit(3, Affiliation.Enemy, new(8000, 1100))))!.TrackId);
    }
    [Fact]
    public void ExplicitApiTargetBackgroundTakesPriorityAndClearingItRestoresCourseFallback()
    {
        var marked = Unit(2, Affiliation.Enemy, new(2000, 9000), new(1, 0));
        marked = marked with { Observation = marked.Observation with { BackgroundIcon = "FighterTarget" } };
        var nearest = Unit(3, Affiliation.Enemy, new(8000, 1100), new(0, 1));
        var scene = Scene(Self, marked, nearest);
        Assert.True(MapDisplay.FocusTarget(scene)!.ApiMarked); Assert.Equal(2, MapDisplay.FocusTarget(scene)!.TrackId);
        Assert.Equal(2, Assert.Single(MapDisplay.Vectors(scene, new(TargetVector: true))).TrackId);
        Assert.Contains("API target marker", ContactInformation.Build(scene, marked).Summary);
        var cleared = marked with { Observation = marked.Observation with { BackgroundIcon = "none" } };
        Assert.Equal(3, MapDisplay.FocusTarget(Scene(Self, cleared, nearest))!.TrackId);
        Assert.False(MapDisplay.FocusTarget(Scene(Self, cleared, nearest))!.ApiMarked);
        Assert.Null(MapDisplay.FocusTarget(scene with { Live = false }));
    }
    [Fact]
    public void AmbiguousOrNoLongerLiveApiMarkersCannotBecomeTheSelectedNavigationTarget()
    {
        var a = Unit(2, Affiliation.Enemy, new(2000, 9000)); a = a with { Observation = a.Observation with { BackgroundIcon = "FighterTarget" } };
        var b = Unit(3, Affiliation.Enemy, new(8000, 1100));
        var c = a with { TrackId = 4, Position = new(1000, 9000) };
        Assert.Equal(3, MapDisplay.FocusTarget(Scene(Self, a, b, c))!.TrackId);
        Assert.False(MapDisplay.FocusTarget(Scene(Self, a with { Phase = ContactPhase.Predicted, Age = .5 }, b))!.ApiMarked);
        Assert.Equal(3, MapDisplay.FocusTarget(Scene(Self, a with { Phase = ContactPhase.Destroyed }, b))!.TrackId);
        Assert.Equal(3, MapDisplay.FocusTarget(Scene(Self, a with { Observation = a.Observation with { Affiliation = Affiliation.Ally } }, b))!.TrackId);
    }
    [Fact]
    public void LiveTankTargetMarkerIsParsedButUnrelatedBackgroundNamesAreNotSelections()
    {
        var objects = new TelemetryParser().ParseObjects(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "battle-su30sm2-map_obj.json")));
        var marked = Assert.Single(objects.Where(o => o.BackgroundIcon == "MediumTankTarget"));
        Assert.True(marked.ApiTargetMarked);
        Assert.False((marked with { BackgroundIcon = "none" }).ApiTargetMarked);
        Assert.False((marked with { BackgroundIcon = "FighterTarget" }).ApiTargetMarked);
        Assert.False((marked with { BackgroundIcon = "Target" }).ApiTargetMarked);
    }
}
