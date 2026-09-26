namespace MapHelper.Core;

public sealed record MeasuredSegment(Vec2 Start, Vec2 End)
{
    public double Meters => (End - Start).Length;
}

public sealed class DistanceMeasurement
{
    private Vec2 _start, _screenStart;
    private bool _dragged;
    public bool IsPressed { get; private set; }
    public MeasuredSegment? Segment { get; private set; }
    public void Begin(Vec2 world, Vec2 screen)
    {
        if (!world.IsFinite || !screen.IsFinite) return;
        Segment = null; _start = world; _screenStart = screen; _dragged = false; IsPressed = true;
    }
    public void Move(Vec2 world, Vec2 screen)
    {
        if (!IsPressed || !world.IsFinite || !screen.IsFinite) return;
        _dragged |= (screen - _screenStart).Length >= 4;
        if (_dragged) Segment = new(_start, world);
    }
    public void End(Vec2 world, Vec2 screen) { Move(world, screen); Finish(); }
    public void Finish()
    {
        IsPressed = false;
        if (!_dragged || Segment is not { Meters: > .01 }) Segment = null;
    }
    public void Reset() { IsPressed = false; _dragged = false; Segment = null; }
}
