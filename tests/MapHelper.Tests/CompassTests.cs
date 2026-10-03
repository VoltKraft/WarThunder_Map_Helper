using MapHelper.Core;
using Xunit;

namespace MapHelper.Tests;

public class CompassTests
{
    [Fact]
    public void CardinalsEndOnViewportEdgesFromActualPlayerPosition()
    {
        var rays = Compass.Rays(new(120, 90), 800, 500, 4);
        Assert.Collection(rays,
            n => { Assert.Equal("N", n.Cardinal); Assert.Equal(new Vec2(120, 0), n.End); Assert.Equal(0, n.Degrees); },
            e => { Assert.Equal("E", e.Cardinal); Assert.Equal(new Vec2(800, 90), e.End); Assert.Equal(90, e.Degrees); },
            s => { Assert.Equal("S", s.Cardinal); Assert.Equal(new Vec2(120, 500), s.End); Assert.Equal(180, s.Degrees); },
            w => { Assert.Equal("W", w.Cardinal); Assert.Equal(new Vec2(0, 90), w.End); Assert.Equal(270, w.Degrees); });
    }

    [Fact]
    public void EverySupportedCountRetainsCardinalsAndEquallySpacedBearings()
    {
        for (var count = 4; count <= 72; count += 4)
        {
            var origin = new Vec2(147, 230);
            var rays = Compass.Rays(origin, 900, 600, count);
            Assert.Equal(count, rays.Count);
            Assert.Equal(new[] { "N", "E", "S", "W" }, rays.Where(r => r.Cardinal != null).Select(r => r.Cardinal));
            for (var i = 0; i < count; i++)
            {
                var ray = rays[i];
                Assert.True(ray.End.IsFinite);
                Assert.InRange(ray.End.X, 0, 900);
                Assert.InRange(ray.End.Y, 0, 600);
                Assert.True(ray.End.X < 1e-9 || ray.End.X > 900 - 1e-9 || ray.End.Y < 1e-9 || ray.End.Y > 600 - 1e-9);
                Assert.Equal(i * 360d / count, ray.Degrees, 9);
                Assert.Equal(ray.Degrees, Compass.Bearing(origin, ray.End)!.Value, 9);
            }
        }
    }

    [Theory]
    [InlineData(int.MinValue, 4)]
    [InlineData(0, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 8)]
    [InlineData(15, 12)]
    [InlineData(73, 72)]
    [InlineData(int.MaxValue, 72)]
    public void UntrustedCountsAreBounded(int input, int expected)
    {
        Assert.Equal(expected, Compass.NormalizeLineCount(input));
        Assert.Equal(expected, Compass.Rays(new(20, 20), 40, 40, input).Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(800, 0)]
    [InlineData(0, 500)]
    [InlineData(800, 500)]
    [InlineData(0, 250)]
    public void BoundaryOriginKeepsAllCardinalsWithoutInvalidIntersections(double x, double y)
    {
        var rays = Compass.Rays(new(x, y), 800, 500, 72);
        Assert.Equal(72, rays.Count);
        Assert.All(rays, ray =>
        {
            Assert.True(ray.End.IsFinite);
            Assert.InRange(ray.End.X, 0, 800);
            Assert.InRange(ray.End.Y, 0, 500);
        });
    }

    [Theory]
    [InlineData(-1, 100, 800, 500)]
    [InlineData(801, 100, 800, 500)]
    [InlineData(100, -1, 800, 500)]
    [InlineData(100, 501, 800, 500)]
    [InlineData(double.NaN, 100, 800, 500)]
    [InlineData(100, double.PositiveInfinity, 800, 500)]
    [InlineData(0, 0, 0, 500)]
    [InlineData(0, 0, 800, -1)]
    [InlineData(0, 0, double.PositiveInfinity, 500)]
    [InlineData(0, 0, 800, double.NaN)]
    public void InvalidViewportOrOffscreenOriginProducesNoFabricatedCompass(double x, double y, double width, double height)
        => Assert.Empty(Compass.Rays(new(x, y), width, height, 4));

    [Theory]
    [InlineData(0, -1, 0)]
    [InlineData(1, -1, 45)]
    [InlineData(1, 0, 90)]
    [InlineData(1, 1, 135)]
    [InlineData(0, 1, 180)]
    [InlineData(-1, 1, 225)]
    [InlineData(-1, 0, 270)]
    [InlineData(-1, -1, 315)]
    public void MeasurementsUseClockwiseStartToEndBearings(double x, double y, double expected)
    {
        var start = new Vec2(1234, -4567);
        var end = start + new Vec2(x, y);
        Assert.Equal(expected, new MeasuredSegment(start, end).BearingDegrees!.Value, 9);
        Assert.Equal((expected + 180) % 360, new MeasuredSegment(end, start).BearingDegrees!.Value, 9);
    }

    [Fact]
    public void CoincidentAndNonfiniteMeasurementsHaveNoBearing()
    {
        Assert.Null(new MeasuredSegment(new(1, 2), new(1, 2)).BearingDegrees);
        Assert.Null(new MeasuredSegment(new(double.NaN, 2), default).BearingDegrees);
        Assert.Null(new MeasuredSegment(default, new(1, double.PositiveInfinity)).BearingDegrees);
        Assert.Null(new MeasuredSegment(new(-double.MaxValue, 0), new(double.MaxValue, 0)).BearingDegrees);
    }
}
