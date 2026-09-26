using MapHelper.Core;
using Xunit;

namespace MapHelper.Tests;

public class TrackingTests
{
    internal static readonly MapInfo Map = new("test", 1, new(-1000, -2000), new(9000, 18000), new(1000, 1000), default);
    internal static MapObservation Enemy(double x, double y = .5, string? id = "e") => new(id, "aircraft", "Fighter", Affiliation.Enemy, "#fa3200", new(x, y));
    [Fact]
    public void RectangularMapUsesBothWorldDimensions()
    {
        var world = Map.ToWorld(new(.5, .5)); Assert.Equal(new Vec2(4000, 8000), world);
        Assert.Equal(new Vec2(.5, .5), Map.ToNormalized(world));
    }
    [Fact]
    public void GhostMovesForTwoSecondsMeasuredFromLastSighting()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4)], Map, 0); tracker.Update([Enemy(.41)], Map, 1);
        tracker.Update([], Map, 1.1);
        var ghost = Assert.Single(tracker.GetContacts(2));
        Assert.Equal(ContactPhase.Predicted, ghost.Phase); Assert.Equal(3200, ghost.Position.X, 3);
        Assert.Single(tracker.GetContacts(2.999)); Assert.Empty(tracker.GetContacts(3));
    }
    [Fact]
    public void StationaryGhostDoesNotMove()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4)], Map, 0); tracker.Update([Enemy(.4)], Map, .1); tracker.Update([], Map, .2);
        Assert.Equal(Map.ToWorld(new(.4, .5)), Assert.Single(tracker.GetContacts(1)).Position);
    }
    [Fact]
    public void ReacquisitionDoesNotDuplicateContact()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4, id: null)], Map, 0); tracker.Update([Enemy(.41, id: null)], Map, 1);
        var id = Assert.Single(tracker.GetContacts(1)).TrackId; tracker.Update([], Map, 1.1); tracker.Update([Enemy(.42, id: null)], Map, 2);
        Assert.Equal(id, Assert.Single(tracker.GetContacts(2)).TrackId);
    }
    [Fact]
    public void UniqueIdsSurviveCrossingAndReorderedArrays()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4, id: "a"), Enemy(.6, id: "b")], Map, 0);
        var ids = tracker.GetContacts(0).ToDictionary(c => c.Observation.SourceId!, c => c.TrackId);
        tracker.Update([Enemy(.49, id: "b"), Enemy(.51, id: "a")], Map, 1);
        Assert.All(tracker.GetContacts(1), c => Assert.Equal(ids[c.Observation.SourceId!], c.TrackId));
    }
    [Fact]
    public void AmbiguousCrossingDiscardsVelocity()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.499, id: null), Enemy(.501, id: null)], Map, 0);
        tracker.Update([Enemy(.5, id: null), Enemy(.5001, id: null)], Map, .1);
        Assert.All(tracker.GetContacts(.1), c => { Assert.True(c.Ambiguous); Assert.Null(c.Velocity); });
    }
    [Fact]
    public void DisconnectFreezesAndExpiresWithoutDeath()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4)], Map, 0); tracker.Update([Enemy(.41)], Map, 1); tracker.Disconnect();
        var contact = Assert.Single(tracker.GetContacts(2)); Assert.Equal(ContactPhase.Stale, contact.Phase);
        Assert.Equal(3100, contact.Position.X, 3); Assert.Null(contact.DestroyedAt); Assert.Empty(tracker.GetContacts(3));
    }
    [Fact]
    public void DestructionPersistsFiveSecondsAndIsNotRepeatedByStaleObjects()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4)], Map, 0);
        Assert.True(tracker.MarkDestroyed("e", .5)); tracker.Update([Enemy(.4)], Map, 1);
        Assert.Equal(ContactPhase.Destroyed, Assert.Single(tracker.GetContacts(1)).Phase);
        Assert.Single(tracker.GetContacts(5.499)); Assert.Empty(tracker.GetContacts(5.5));
    }
    [Fact]
    public void UnknownDestructionCannotInventPosition()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4, id: null)], Map, 0);
        Assert.False(tracker.MarkDestroyed("not-known", .1)); Assert.Equal(ContactPhase.Live, Assert.Single(tracker.GetContacts(.1)).Phase);
    }
    [Fact]
    public void OldFrameCannotRewindPosition()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4)], Map, 2); tracker.Update([Enemy(.1)], Map, 1);
        Assert.Equal(3000, Assert.Single(tracker.GetContacts(2)).Position.X, 3);
    }
    [Fact]
    public void MissingStaticEnemyIsRemoved()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.4) with { Type = "bombing_point" }], Map, 0); tracker.Update([], Map, .1);
        Assert.Empty(tracker.GetContacts(10));
    }
    [Fact]
    public void TeleportDoesNotBecomeVelocity()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.1)], Map, 0); tracker.Update([Enemy(.9)], Map, .1);
        Assert.Null(Assert.Single(tracker.GetContacts(.1)).Velocity);
    }
    [Fact]
    public void DuplicateSourceIdsCannotReceiveConfirmedKill()
    {
        var tracker = new ContactTracker(); tracker.Update([Enemy(.1), Enemy(.9)], Map, 0);
        Assert.False(tracker.MarkDestroyed("e", .1));
    }
    [Fact]
    public void PollingFasterThanGameUpdatesDoesNotAlternateWithZeroSpeed()
    {
        var tracker = new ContactTracker();
        for (var i = 0; i <= 30; i++)
        {
            tracker.Update([Enemy(.4 + (i / 2) * .002)], Map, i * .1);
            if (i > 10) Assert.InRange(Assert.Single(tracker.GetContacts(i * .1)).Velocity!.Value.Length, 85, 115);
        }
        for (var i = 31; i <= 40; i++) tracker.Update([Enemy(.43)], Map, i * .1);
        Assert.Equal(0, Assert.Single(tracker.GetContacts(4)).Velocity!.Value.Length, 3);
    }
}
