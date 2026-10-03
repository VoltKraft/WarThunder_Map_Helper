namespace MapHelper.Core;

/// <summary>A north-up screen-space ray ending at the viewport edge; bearings are clockwise from north.</summary>
public sealed record CompassRay(Vec2 End, double Degrees, string? Cardinal);

/// <summary>Geometry for a player-centered compass on an unrotated, north-up map.</summary>
public static class Compass
{
    /// <summary>Clamps to 4–72 rays, rounding down to a multiple of four to retain every cardinal.</summary>
    public static int NormalizeLineCount(int count) => Math.Clamp(count, 4, 72) / 4 * 4;

    /// <summary>Returns clockwise bearings in [0, 360), or null for coincident or nonfinite endpoints.</summary>
    public static double? Bearing(Vec2 start, Vec2 end)
    {
        var delta = end - start;
        if (!start.IsFinite || !end.IsFinite || !delta.IsFinite || delta == default) return null;
        return (Math.Atan2(delta.X, -delta.Y) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>Intersects equally spaced rays from a screen-space player position with the viewport.</summary>
    /// <remarks>Returns no rays for invalid bounds or an offscreen origin: the origin is never clamped.
    /// On an edge, outward rays have zero length. Coordinates and bounds must use the same units.</remarks>
    public static IReadOnlyList<CompassRay> Rays(Vec2 origin, double width, double height, int count)
    {
        if (!origin.IsFinite || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
            || origin.X < 0 || origin.Y < 0 || origin.X > width || origin.Y > height) return [];
        count = NormalizeLineCount(count);
        var rays = new List<CompassRay>(count);
        for (var i = 0; i < count; i++)
        {
            var degrees = i * 360d / count;
            var radians = degrees * Math.PI / 180;
            var dx = Math.Sin(radians); var dy = -Math.Cos(radians);
            // Exact cardinal components avoid near-zero trig residuals at viewport edges.
            if (i % (count / 4) == 0)
                (dx, dy) = (i / (count / 4)) switch { 0 => (0d, -1d), 1 => (1d, 0d), 2 => (0d, 1d), _ => (-1d, 0d) };
            var tx = dx == 0 ? double.PositiveInfinity : (dx > 0 ? width - origin.X : -origin.X) / dx;
            var ty = dy == 0 ? double.PositiveInfinity : (dy > 0 ? height - origin.Y : -origin.Y) / dy;
            var t = Math.Min(tx, ty);
            var end = new Vec2(Math.Clamp(origin.X + dx * t, 0, width), Math.Clamp(origin.Y + dy * t, 0, height));
            var cardinal = i % (count / 4) == 0 ? (i / (count / 4)) switch { 0 => "N", 1 => "E", 2 => "S", _ => "W" } : null;
            rays.Add(new(end, degrees, cardinal));
        }
        return rays;
    }
}
