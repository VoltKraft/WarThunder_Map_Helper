using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapHelper.Core;

namespace MapHelper.Desktop;

public sealed class MapControl(IconRepository icons) : Control
{
    private MapScene? _scene;
    private Bitmap? _image;
    private string? _mapKey;
    private long _session = -1;
    private Vec2 _center;
    private double _zoom = 1;
    private Point? _drag;
    private bool _follow;
    private bool _autoFrame = true;
    private readonly AutomaticViewport _automatic = new();
    private readonly CameraFocusPlanner _focusPlanner = new();
    private CameraFocus _focus = new([], null);
    private readonly DistanceMeasurement _measurement = new();
    private Point? _hoverPoint;
    private double _lastHoverUpdate;
    private const double MaxZoom = 64;
    public bool Follow { get => _follow; set { _follow = value; InvalidateVisual(); } }
    public bool AutoFrame { get => _autoFrame; set { if (value != _autoFrame) _automatic.Reset(); _autoFrame = value; InvalidateVisual(); } }
    public TargetDisplayOptions TargetDisplay { get; set; } = new();
    public CameraOptions Camera { get; set; } = new();
    public MapDisplayOptions Display { get; set; } = new();
    public event Action? ManualNavigation;
    public event Action? NewSession;
    public event Action<long?, Point>? HoverContactChanged;
    private static readonly IBrush Muted = Brush("#8194A8");
    private static readonly IBrush Light = Brush("#E8EFF5");
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    public void SetScene(MapScene scene, Bitmap? image)
    {
        _scene = MapDisplay.ApplyMarkerVisibility(scene); _image = image;
        if (scene.Map?.Key != _mapKey || scene.SessionId != _session)
        {
            _mapKey = scene.Map?.Key; _session = scene.SessionId;
            _focusPlanner.Reset(); _measurement.Reset(); _autoFrame = true; _follow = false;
            Fit(); NewSession?.Invoke(); HoverContactChanged?.Invoke(null, default);
        }
        _focus = _focusPlanner.Update(scene, Camera);
        if (_autoFrame && !_measurement.IsPressed && scene.Map != null)
        {
            if (_automatic.Update(_focus.Points, Bounds.Width, Bounds.Height, BaseScale * MaxZoom, scene.Time) is { } view)
                SetViewport(view);
            else Fit();
        }
        else if (_follow && !_measurement.IsPressed && scene.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live) is { } player) _center = player.Position;
        if (_hoverPoint is { } point && scene.Time - _lastHoverUpdate >= .25) { _lastHoverUpdate = scene.Time; UpdateHover(point); }
        InvalidateVisual();
    }
    public void Fit()
    {
        _zoom = 1;
        _automatic.Reset();
        if (_scene?.Map is { } m) _center = (m.Min + m.Max) / 2;
        InvalidateVisual();
    }
    private void SetViewport(MapViewport view) { _center = view.Center; _zoom = view.Scale / BaseScale; }
    public bool FitRange()
    {
        var player = _scene?.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
        if (_scene?.Live != true || _scene.Range is not { Meters: > 1 } range || player == null) return false;
        var radius = new Vec2(range.Meters, range.Meters);
        var view = MapViewport.Fit([player.Position - radius, player.Position + radius], Bounds.Width, Bounds.Height, BaseScale * MaxZoom);
        if (view == null) return false;
        SetViewport(view); InvalidateVisual(); return true;
    }
    private double BaseScale => _scene?.Map is { } map ? Math.Min(Math.Max(1, Bounds.Width - 52) / map.Size.X, Math.Max(1, Bounds.Height - 72) / map.Size.Y) : 1;
    private double Scale => BaseScale * _zoom;
    private Point Project(Vec2 point) => new((point.X - _center.X) * Scale + Bounds.Width / 2, (point.Y - _center.Y) * Scale + Bounds.Height / 2);
    private Vec2 Unproject(Point p) => new((p.X - Bounds.Width / 2) / Scale + _center.X, (p.Y - Bounds.Height / 2) / Scale + _center.Y);
    private static void Label(DrawingContext c, string text, double x, double y, double size = 12, IBrush? color = null) =>
        c.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, color ?? Muted), new Point(x, y));
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var clip = context.PushClip(new Rect(Bounds.Size));
        context.DrawRectangle(Brush("#101B29"), null, new Rect(Bounds.Size));
        if (_scene?.Map is not { } map)
        {
            for (var x = 0; x < Bounds.Width; x += 48) context.DrawLine(new Pen(Brush("#172535")), new(x, 0), new(x, Bounds.Height));
            for (var y = 0; y < Bounds.Height; y += 48) context.DrawLine(new Pen(Brush("#172535")), new(0, y), new(Bounds.Width, y));
            var cx = Bounds.Width / 2; var cy = Bounds.Height / 2;
            context.DrawEllipse(null, new Pen(Brush("#355165"), 1), new(cx, cy - 45), 32, 32);
            context.DrawLine(new Pen(Brush("#6FD8C2"), 2), new(cx, cy - 65), new(cx, cy - 25));
            context.DrawLine(new Pen(Brush("#6FD8C2"), 2), new(cx - 20, cy - 45), new(cx + 20, cy - 45));
            Label(context, "Ready for your next mission", Math.Max(24, cx - 174), cy + 10, 21, Light);
            Label(context, "Start War Thunder and join a battle or test flight.", Math.Max(24, cx - 195), cy + 48, 12);
            Label(context, "Select “Demo” to explore the map features.", Math.Max(24, cx - 182), cy + 72, 12);
            return;
        }
        var p0 = Project(map.Min); var p1 = Project(map.Max);
        var rect = new Rect(p0, p1);
        using (context.PushClip(rect))
        {
            if (_image != null) context.DrawImage(_image, new Rect(_image.Size), rect);
            else if (map.Key == "demo") DrawDemoTerrain(context, rect);
            else context.DrawRectangle(Brush("#152637"), null, rect);
            DrawGrid(context, map, rect);
        }
        context.DrawRectangle(null, new Pen(Brush("#354453"), 1), rect);
        // Range is a physical distance and can extend beyond the original map image.
        // Clip navigation aids to the viewport, never to the geographic map boundary.
        var player = _scene.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
        if (player != null && _scene.Live)
        {
            var p = Project(player.Position);
            if (_scene.Range is { Meters: > 1 } range)
            {
                var radius = range.Meters * Scale;
                if (radius < 100_000)
                {
                    context.DrawEllipse(Brush("#0DEBCD81"), new Pen(Brush("#DDEBCD81"), 1.4, new DashStyle([6, 5], 0)), p, radius, radius);
                    if (p.Y - radius > 14 && p.Y - radius < Bounds.Height - 30 && p.X >= 0 && p.X < Bounds.Width - 100)
                        Label(context, FormattableString.Invariant($"≈ {range.Meters / 1000:0.#} km"), p.X + 10, p.Y - radius - 20, 12, Brush("#F8D781"));
                }
            }
            if (player.Direction is { Length: > 0 } direction)
            {
                var edge = TargetNavigation.CourseEnd(player.Position, direction, map);
                context.DrawLine(new Pen(Brush("#DDF5D982"), 1), p, Project(edge));
            }
        }
        foreach (var vector in MapDisplay.Vectors(_scene, Display))
        {
            var color = vector.IsTarget ? "#DDF8D781" : "#CC74E79C";
            using var opacity = context.PushOpacity(vector.Estimated ? .5 : 1);
            context.DrawLine(new Pen(Brush(color), 1, vector.Estimated ? new DashStyle([5, 5], 0) : null),
                Project(vector.Start), Project(vector.End));
        }
        foreach (var c in _scene.Contacts.OrderBy(c => c.Observation.Affiliation == Affiliation.Self ? 1 : 0)) DrawContact(context, c, map);
        if (Display.TargetVector && MapDisplay.FocusTarget(_scene) is { ApiMarked: false } focusTarget)
            context.DrawEllipse(null, new Pen(Brush("#F8D781"), 1.2,
                focusTarget.Estimated ? new DashStyle([3, 3], 0) : null), Project(focusTarget.Position), 18, 18);
        DrawTargetReadouts(context);
        if (_autoFrame && _focus.Fallback is { } fallback)
        {
            var point = Project(fallback.Position);
            context.DrawEllipse(null, new Pen(Muted, 1, new DashStyle([3, 3], 0)), point, 10, 10);
            Label(context, "Last enemy position", Math.Clamp(point.X + 15, 8, Bounds.Width - 160), Math.Clamp(point.Y - 8, 8, Bounds.Height - 25), 11);
        }
        DrawMeasurement(context);
        Label(context, "N", Bounds.Width - 34, 19, 13, Light);
        context.DrawLine(new Pen(Brush("#B2C5D8"), 1.5), new(Bounds.Width - 29, 57), new(Bounds.Width - 29, 40));
        context.DrawLine(new Pen(Brush("#B2C5D8"), 1.5), new(Bounds.Width - 34, 45), new(Bounds.Width - 29, 40));
        context.DrawLine(new Pen(Brush("#B2C5D8"), 1.5), new(Bounds.Width - 24, 45), new(Bounds.Width - 29, 40));
        var meters = NiceDistance(100 / Scale); var length = meters * Scale;
        var sy = Bounds.Height - 28;
        context.DrawLine(new Pen(Light, 2), new(24, sy), new(24 + length, sy));
        Label(context, ContactInformation.Distance(meters), 24, sy - 22, 11, Light);
        Label(context, map.Key == "demo" ? "DEMO / SYNTHETIC MAP" : _image == null ? "LOADING MAP IMAGE" : "WAR THUNDER / TACTICAL MAP", Bounds.Width - 253, Bounds.Height - 28, 10);
    }
    private static double NiceDistance(double x)
    {
        var unit = Math.Pow(10, Math.Floor(Math.Log10(x)));
        return (x / unit >= 5 ? 5 : x / unit >= 2 ? 2 : 1) * unit;
    }
    private void DrawGrid(DrawingContext context, MapInfo map, Rect rect)
    {
        var pen = new Pen(Brush("#263FA8B8"), .7);
        var stepX = map.GridStep.X > 0 ? map.GridStep.X : map.Size.X / 8;
        var stepY = map.GridStep.Y > 0 ? map.GridStep.Y : map.Size.Y / 8;
        // Guard malformed metadata from creating unbounded drawing loops.
        if (map.Size.X / stepX > 100) stepX = map.Size.X / 8;
        if (map.Size.Y / stepY > 100) stepY = map.Size.Y / 8;
        for (var x = map.GridZero.X + Math.Ceiling((map.Min.X - map.GridZero.X) / stepX) * stepX; x <= map.Max.X; x += stepX)
        { var px = Project(new(x, 0)).X; context.DrawLine(pen, new(px, rect.Top), new(px, rect.Bottom)); }
        for (var y = map.GridZero.Y + Math.Ceiling((map.Min.Y - map.GridZero.Y) / stepY) * stepY; y <= map.Max.Y; y += stepY)
        { var py = Project(new(0, y)).Y; context.DrawLine(pen, new(rect.Left, py), new(rect.Right, py)); }
    }
    private void DrawContact(DrawingContext ctx, Contact c, MapInfo map)
    {
        var p = Project(c.Position);
        var color = c.Observation.Affiliation switch { Affiliation.Self => "#F8D781", Affiliation.Squad => "#74E79C", Affiliation.Ally => "#68C4F5", Affiliation.Enemy => "#FF7E82", _ => "#B8C4D1" };
        var brush = Brush(color);
        if (c.Phase == ContactPhase.Destroyed)
        {
            var age = _scene!.Time - c.DestroyedAt!.Value;
            if (age < .4) ctx.DrawEllipse(Brush("#55FFE7AC"), new Pen(Brush("#FFE7AC"), 2), p, 12 + age * 60, 12 + age * 60);
            using var fade = ctx.PushOpacity(Math.Min(1, 5 - age));
            ctx.DrawLine(new Pen(brush, 2), p + new Vector(-6, -6), p + new Vector(6, 6));
            ctx.DrawLine(new Pen(brush, 2), p + new Vector(6, -6), p + new Vector(-6, 6));
            return;
        }
        if (c.Observation.Type == "airfield" && c.Observation.EndPosition is { } end)
        {
            ctx.DrawLine(new Pen(brush, 5), p, Project(map.ToWorld(end)));
            ctx.DrawLine(new Pen(Brush("#192635"), 1, new DashStyle([3, 3], 0)), p, Project(map.ToWorld(end)));
            return;
        }
        var old = c.Phase is ContactPhase.Predicted or ContactPhase.Stale;
        using var opacity = ctx.PushOpacity(old ? .38 + .24 * (.5 + .5 * Math.Sin(_scene!.Time * Math.PI * 4)) : 1);
        if (old) ctx.DrawEllipse(null, new Pen(brush, 1, new DashStyle([2, 3], 0)), p, 17, 17);
        if (c.Observation.Affiliation == Affiliation.Self) ctx.DrawEllipse(Brush("#24262925"), new Pen(Brush("#60F8D781"), 1), p, 21, 21);
        if (c.Observation.Affiliation == Affiliation.Squad) ctx.DrawEllipse(null, new Pen(brush, 1.5), p, 17, 17);
        if (c.Observation.ApiTargetMarked && c.Observation.Affiliation == Affiliation.Enemy && c.Phase == ContactPhase.Live)
        {
            var pen = new Pen(Brush("#FFF5D2"), 2);
            foreach (var x in new[] { -1, 1 }) foreach (var y in new[] { -1, 1 })
            {
                var corner = p + new Vector(x * 20, y * 20);
                ctx.DrawLine(pen, corner, corner - new Vector(x * 8, 0));
                ctx.DrawLine(pen, corner, corner - new Vector(0, y * 8));
            }
        }
        var angle = c.Observation.IsMobile && c.Direction is { } d ? Math.Atan2(d.X, -d.Y) : 0;
        using (ctx.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(p.X, p.Y)))
        {
            var icon = icons.Get(c.Observation.Icon, c.Observation.IconClass, color);
            var size = c.Observation.Affiliation == Affiliation.Self ? 28 : c.Observation.IsMobile ? 24 : 20;
            ctx.DrawEllipse(Brush("#99101B29"), null, new(0, 0), size / 2 + 2, size / 2 + 2);
            if (icon != null) ctx.DrawImage(icon, new Rect(icon.Size), new Rect(-size / 2, -size / 2, size, size));
            if (c.Observation.IsMobile && c.Direction != null)
            {
                ctx.DrawLine(new Pen(brush, 1.5), new(-3, -size / 2 - 3), new(0, -size / 2 - 7));
                ctx.DrawLine(new Pen(brush, 1.5), new(0, -size / 2 - 7), new(3, -size / 2 - 3));
            }
        }
        if (c.Observation.IsHuman == false && c.Observation.IsMobile)
        {
            ctx.DrawRectangle(Brush("#E8101B29"), null, new Rect(p.X - 9, p.Y + 11, 20, 14), 2, 2);
            Label(ctx, "AI", p.X - 6, p.Y + 11, 10, brush);
        }
    }
    private void DrawMeasurement(DrawingContext context)
    {
        if (_measurement.Segment is not { } segment) return;
        var start = Project(segment.Start); var end = Project(segment.End);
        var color = Brush("#89F2D6");
        context.DrawLine(new Pen(Brush("#DA0B1320"), 4), start, end);
        context.DrawLine(new Pen(color, 1.5), start, end);
        context.DrawEllipse(color, new Pen(Brush("#101B29"), 1), start, 4, 4);
        context.DrawEllipse(color, new Pen(Brush("#101B29"), 1), end, 4, 4);
        var middle = Project((segment.Start + segment.End) / 2);
        var text = new FormattedText(ContactInformation.Distance(segment.Meters), English, FlowDirection.LeftToRight, Typeface.Default, 13, color);
        var box = new Rect(Math.Clamp(middle.X + 12, 8, Math.Max(8, Bounds.Width - text.Width - 24)),
            Math.Clamp(middle.Y + 12, 8, Math.Max(8, Bounds.Height - text.Height - 24)), text.Width + 16, text.Height + 10);
        context.DrawRectangle(Brush("#F0101B29"), new Pen(color, 1), box, 4, 4);
        context.DrawText(text, box.TopLeft + new Vector(8, 5));
    }
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static string ReadoutText(TargetReadout readout)
    {
        var parts = new List<string>();
        if (readout.Values.HasFlag(TargetValues.Distance))
            parts.Add((readout.Estimated ? "≈ " : "") + (readout.DistanceMeters < 1000
                ? readout.DistanceMeters.ToString("0", English) + " m"
                : (readout.DistanceMeters / 1000).ToString("0.0", English) + " km"));
        if (readout.Values.HasFlag(TargetValues.Time))
        {
            parts.Add(readout.Seconds is { } seconds ? "≈ " + ContactInformation.Duration(seconds) : "Time —");
        }
        return string.Join(" · ", parts);
    }
    private void DrawTargetReadouts(DrawingContext context)
    {
        if (_scene == null || Bounds.Width < 200 || Bounds.Height < 150) return;
        var occupied = _scene.Contacts.Select(c => Project(c.Position))
            .Select(p => new Rect(p.X - 19, p.Y - 19, 38, 38)).ToList();
        // Keep the compass and scale readable as well as the unit symbols.
        occupied.Add(new Rect(Bounds.Width - 54, 8, 46, 60));
        occupied.Add(new Rect(16, Bounds.Height - 56, 160, 48));
        occupied.Add(new Rect(Bounds.Width - 263, Bounds.Height - 38, 255, 30));
        foreach (var readout in TargetNavigation.Calculate(_scene, TargetDisplay))
        {
            var point = Project(readout.Position);
            if (!new Rect(Bounds.Size).Contains(point)) continue;
            var color = Brush(readout.IsMarkedTarget ? "#FFF5D2" : readout.IsCourseTarget ? "#F8D781" : "#F3D4D7");
            var text = new FormattedText(ReadoutText(readout), English, FlowDirection.LeftToRight, Typeface.Default, 12, color);
            var width = text.Width + 14; var height = text.Height + 8;
            var box = PlaceReadout(point, width, height, occupied);
            occupied.Add(box.Inflate(3));
            using var opacity = context.PushOpacity(readout.Estimated ? .62 : 1);
            var edge = new Point(Math.Clamp(point.X, box.Left, box.Right), Math.Clamp(point.Y, box.Top, box.Bottom));
            var offset = new Vector(edge.X - point.X, edge.Y - point.Y);
            if (offset.Length > 20) context.DrawLine(new Pen(color, .8), point + offset / offset.Length * 18, edge);
            if (readout.IsCourseTarget) context.DrawEllipse(null, new Pen(color, 1.2), point, 18, 18);
            context.DrawRectangle(Brush("#ED101B29"), new Pen(Brush(readout.IsMarkedTarget ? "#B8FFF5D2" : readout.IsCourseTarget ? "#B8F8D781" : "#606B4650"),
                1, readout.Estimated ? new DashStyle([3, 3], 0) : null), box, 4, 4);
            context.DrawText(text, box.TopLeft + new Vector(7, 4));
        }
    }
    private Rect PlaceReadout(Point point, double width, double height, IReadOnlyList<Rect> occupied)
    {
        var candidates = new List<Rect>();
        for (var row = 0; row < 7; row++)
            foreach (var offset in row == 0 ? new[] { 0d } : new[] { -row * (height + 5), row * (height + 5) })
            {
                candidates.Add(new(point.X + 22, point.Y - height / 2 + offset, width, height));
                candidates.Add(new(point.X - width - 22, point.Y - height / 2 + offset, width, height));
            }
        return candidates.Select(r => new Rect(Math.Clamp(r.X, 8, Math.Max(8, Bounds.Width - width - 8)),
                Math.Clamp(r.Y, 8, Math.Max(8, Bounds.Height - height - 8)), width, height))
            .MinBy(r => occupied.Sum(o => { var overlap = r.Intersect(o); return overlap.Width * overlap.Height; }) * 100
                + new Vec2(r.Center.X - point.X, r.Center.Y - point.Y).Length);
    }
    private static void DrawDemoTerrain(DrawingContext ctx, Rect r)
    {
        ctx.DrawRectangle(Brush("#183342"), null, r);
        var coast = new (double, double)[] { (0, .50), (.10, .45), (.14, .36), (.21, .35), (.28, .44), (.37, .38), (.43, .32), (.49, .37), (.50, .49), (.60, .50), (.64, .40), (.62, .30), (.69, .27), (.74, .31), (.83, .26), (.87, .14), (1, .10), (1, 1), (0, 1) };
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new(r.X + coast[0].Item1 * r.Width, r.Y + coast[0].Item2 * r.Height), true);
            foreach (var (x, y) in coast.Skip(1)) g.LineTo(new(r.X + x * r.Width, r.Y + y * r.Height));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Brush("#30463D"), new Pen(Brush("#5B7160"), 1.5), geometry);
        for (var i = 0; i < 18; i++)
        {
            var x = r.X + ((i * .173) % .85 + .08) * r.Width;
            var y = r.Y + (.57 + (i * .091) % .32) * r.Height;
            ctx.DrawEllipse(Brush("#223C5641"), null, new(x, y), r.Width * .035, r.Height * .045);
        }
        var road = new Pen(Brush("#557F8367"), 2);
        ctx.DrawLine(road, new(r.X + r.Width * .15, r.Y + r.Height * .81), new(r.X + r.Width * .48, r.Y + r.Height * .63));
        ctx.DrawLine(road, new(r.X + r.Width * .48, r.Y + r.Height * .63), new(r.X + r.Width * .90, r.Y + r.Height * .48));
        Label(ctx, "N O R T H  B A Y", r.X + r.Width * .23, r.Y + r.Height * .16, 13, Brush("#68828D"));
        Label(ctx, "TRAINING AREA", r.X + r.Width * .60, r.Y + r.Height * .83, 11, Brush("#90A18A"));
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (_scene?.Map == null) return;
        if (_measurement.IsPressed) { e.Handled = true; return; }
        _autoFrame = false; _automatic.Reset(); _follow = false; ManualNavigation?.Invoke();
        var p = e.GetPosition(this); var before = Unproject(p);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.18, e.Delta.Y), .001, MaxZoom);
        _center += before - Unproject(p); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_scene?.Map == null) return;
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            var point = e.GetPosition(this);
            _measurement.Begin(Unproject(point), new(point.X, point.Y));
            _hoverPoint = null; HoverContactChanged?.Invoke(null, point);
            e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true; return;
        }
        if (!properties.IsLeftButtonPressed || _measurement.IsPressed) return;
        _drag = e.GetPosition(this); e.Pointer.Capture(this); _follow = false; _autoFrame = false; _automatic.Reset(); ManualNavigation?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_measurement.IsPressed)
        {
            _measurement.Move(Unproject(p), new(p.X, p.Y)); InvalidateVisual(); e.Handled = true;
        }
        else if (_drag is { } previous)
        {
            _center -= new Vec2(p.X - previous.X, p.Y - previous.Y) / Scale;
            _drag = p; InvalidateVisual();
        }
        else { _hoverPoint = p; UpdateHover(p); }
    }
    private void UpdateHover(Point point)
    {
        if (_scene?.Map is not { } map) return;
        var selected = _scene.Contacts.Select(c =>
        {
            var start = Project(c.Position);
            var distance = new Vec2(start.X - point.X, start.Y - point.Y).Length;
            if (c.Observation.Type == "airfield" && c.Observation.EndPosition is { } end)
            {
                var finish = Project(map.ToWorld(end)); var delta = new Vec2(finish.X - start.X, finish.Y - start.Y);
                var offset = new Vec2(point.X - start.X, point.Y - start.Y);
                var lengthSquared = delta.X * delta.X + delta.Y * delta.Y;
                if (lengthSquared > 0) distance = (offset - delta * Math.Clamp((offset.X * delta.X + offset.Y * delta.Y) / lengthSquared, 0, 1)).Length;
            }
            return (Contact: c, Distance: distance);
        }).Where(c => c.Distance < 18).OrderBy(c => c.Distance).FirstOrDefault().Contact;
        HoverContactChanged?.Invoke(selected?.TrackId, point);
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { _hoverPoint = null; HoverContactChanged?.Invoke(null, e.GetPosition(this)); base.OnPointerExited(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_measurement.IsPressed && e.InitialPressMouseButton == MouseButton.Right)
        { var p = e.GetPosition(this); _measurement.End(Unproject(p), new(p.X, p.Y)); InvalidateVisual(); e.Handled = true; }
        _drag = null; e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { _drag = null; _measurement.Finish(); }
    internal void PreviewMeasurement(Vec2 start, Vec2 end)
    { _measurement.Begin(start, default); _measurement.End(end, new(100, 100)); InvalidateVisual(); }
    internal MeasuredSegment? Measurement => _measurement.Segment;
}
