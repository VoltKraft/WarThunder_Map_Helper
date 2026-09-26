namespace MapHelper.Core;

public sealed record MapViewport(Vec2 Center, double Scale)
{
    public static MapViewport? Fit(IEnumerable<Vec2> points, double width, double height, double maxScale, double padding = 48)
    {
        var visible = points.Where(p => p.IsFinite).ToArray();
        if (visible.Length == 0 || width <= 2 * padding || height <= 2 * padding || !double.IsFinite(maxScale) || maxScale <= 0) return null;
        var min = new Vec2(visible.Min(p => p.X), visible.Min(p => p.Y));
        var max = new Vec2(visible.Max(p => p.X), visible.Max(p => p.Y));
        var center = (min + max) / 2;
        return new(center, ContainingScale(visible, center, width, height, maxScale, padding));
    }

    internal static double ContainingScale(IReadOnlyList<Vec2> points, Vec2 center, double width, double height, double maxScale, double padding)
    {
        var extentX = points.Max(p => Math.Abs(p.X - center.X));
        var extentY = points.Max(p => Math.Abs(p.Y - center.Y));
        var sx = extentX > 0 ? (width / 2 - padding) / extentX : maxScale;
        var sy = extentY > 0 ? (height / 2 - padding) / extentY : maxScale;
        return Math.Min(maxScale, Math.Min(sx, sy));
    }
}

public sealed class AutomaticViewport
{
    private MapViewport? _view;
    private double _lastTime;
    public void Reset() => _view = null;

    public MapViewport? Update(IEnumerable<Vec2> points, double width, double height, double maxScale, double now, double padding = 48)
    {
        var visible = points.Where(p => p.IsFinite).ToArray();
        var target = MapViewport.Fit(visible, width, height, maxScale, padding);
        if (target == null) { Reset(); return null; }
        if (_view == null) _view = target;
        else
        {
            var elapsed = Math.Clamp(now - _lastTime, 0, 1);
            var center = _view.Center + (target.Center - _view.Center) * (1 - Math.Exp(-elapsed / .25));
            var easedScale = _view.Scale + (target.Scale - _view.Scale) * (1 - Math.Exp(-elapsed / .4));
            // Zoom out immediately when required. Even while panning smoothly, every marker
            // must remain within the viewport, including a newly appearing distant contact.
            var safeScale = MapViewport.ContainingScale(visible, center, width, height, maxScale, padding);
            _view = new(center, Math.Min(easedScale, safeScale));
        }
        _lastTime = now;
        return _view;
    }
}
