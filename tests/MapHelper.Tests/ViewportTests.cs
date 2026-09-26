using MapHelper.Core;
using Xunit;

namespace MapHelper.Tests;

public class ViewportTests
{
    private static void AllVisible(MapViewport view, IEnumerable<Vec2> points, double width = 1000, double height = 700)
    {
        foreach (var point in points)
        {
            Assert.InRange((point.X - view.Center.X) * view.Scale + width / 2, 48 - 1e-6, width - 48 + 1e-6);
            Assert.InRange((point.Y - view.Center.Y) * view.Scale + height / 2, 48 - 1e-6, height - 48 + 1e-6);
        }
    }
    [Fact]
    public void FitUsesMaximumZoomThatContainsAllMarkers()
    {
        Vec2[] points = [new(0, 0), new(10000, 2000)];
        var view = MapViewport.Fit(points, 1000, 700, 10)!;
        Assert.Equal(new Vec2(5000, 1000), view.Center);
        Assert.Equal(904d / 10000, view.Scale, 8); AllVisible(view, points);
    }
    [Fact]
    public void ClusterInMapCornerMovesViewportToThatCorner()
    {
        Vec2[] points = [new(-49000, 32000), new(-47000, 34000), new(-48500, 33500)];
        var view = new AutomaticViewport().Update(points, 1000, 700, 10, 0)!;
        Assert.Equal(new Vec2(-48000, 33000), view.Center); AllVisible(view, points);
    }
    [Fact]
    public void OutwardMovementImmediatelyZoomsOutWithoutCropping()
    {
        var camera = new AutomaticViewport();
        var before = camera.Update([new(0, 0), new(100, 100)], 1000, 700, 10, 0)!;
        Vec2[] moved = [new(0, 0), new(10000, -4000)];
        var after = camera.Update(moved, 1000, 700, 10, .1)!;
        Assert.True(after.Scale < before.Scale); AllVisible(after, moved);
    }
    [Fact]
    public void ApproachingOutermostContactZoomsBackIn()
    {
        var camera = new AutomaticViewport();
        var before = camera.Update([new(0, 0), new(10000, 0)], 1000, 700, 10, 0)!;
        Vec2[] close = [new(0, 0), new(500, 0)];
        var after = before;
        for (var i = 1; i <= 40; i++) { after = camera.Update(close, 1000, 700, 10, i * .1)!; AllVisible(after, close); }
        Assert.True(after.Scale > before.Scale * 10);
        Assert.InRange(after.Center.X, 249, 251);
    }
    [Fact]
    public void RemovedContactNoLongerDeterminesTheZoom()
    {
        var camera = new AutomaticViewport();
        var before = camera.Update([new(0, 0), new(20000, 0)], 1000, 700, 1, 0)!;
        var after = before;
        for (var i = 1; i <= 40; i++) after = camera.Update([new(0, 0)], 1000, 700, 1, i * .1)!;
        Assert.True(after.Scale > before.Scale * 10); AllVisible(after, [new(0, 0)]);
    }
    [Fact]
    public void SingleUnitUsesFiniteMaximumZoom()
    {
        var view = MapViewport.Fit([new(123, 456)], 1000, 700, .5)!;
        Assert.Equal(.5, view.Scale); Assert.Equal(new Vec2(123, 456), view.Center);
    }
    [Fact]
    public void EmptyInvalidOrTooSmallViewportHasNoFit()
    {
        Assert.Null(MapViewport.Fit([], 1000, 700, 1));
        Assert.Null(MapViewport.Fit([new(double.NaN, 1)], 1000, 700, 1));
        Assert.Null(MapViewport.Fit([new(0, 0)], 50, 50, 1));
    }
    [Fact]
    public void TallWindowAndResizePreserveAllContacts()
    {
        var camera = new AutomaticViewport(); Vec2[] points = [new(-300, -500), new(300, 500)];
        camera.Update(points, 1000, 700, 10, 0);
        var resized = camera.Update(points, 300, 1000, 10, .01)!;
        AllVisible(resized, points, 300, 1000);
    }
    [Fact]
    public void FuelCircleCanExtendBeyondOriginalMapBounds()
    {
        var center = new Vec2(13000, 22000); var radius = new Vec2(350000, 350000);
        var view = MapViewport.Fit([center - radius, center + radius], 1000, 700, 1)!;
        Assert.Equal(center, view.Center); Assert.Equal(604d / 700000, view.Scale, 9);
        AllVisible(view, [center - radius, center + radius]);
    }
}
