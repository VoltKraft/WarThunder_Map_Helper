namespace MapHelper.Core;

/// <summary>Estimates remaining time and one-way ground range from measured fuel consumption.</summary>
/// <remarks>Mutable and not thread-safe; serialize calls to <see cref="Update"/> and <see cref="Reset"/>.</remarks>
public sealed class RangeEstimator
{
    private readonly List<(double Time, double Fuel)> _samples = [];
    private string? _vehicle;
    private double? _smoothedFlow;
    private double _lastTime = double.NegativeInfinity;
    public void Reset() { _samples.Clear(); _smoothedFlow = null; _vehicle = null; _lastTime = double.NegativeInfinity; }

    /// <summary>Incorporates one sample, resetting estimates after refueling, vehicle changes, or gaps.</summary>
    /// <param name="now">Strictly increasing monotonic time in seconds.</param>
    /// <param name="vehicle">Vehicle identity, when supplied by the API.</param>
    /// <param name="fuelKg">Measured fuel mass in kilograms, or null when unavailable.</param>
    /// <param name="verifiedKgS">Consumption in kilograms per second only when its unit is verified; otherwise null.</param>
    /// <param name="speedMps">Current ground speed in meters per second, or null when unavailable.</param>
    /// <returns>An estimate at the current burn rate and speed, or null when the measurements are insufficient.</returns>
    /// <remarks>The usual five-second window expands up to thirty seconds for coarse fuel gauges. No return reserve is included.</remarks>
    public RangeEstimate? Update(double now, string? vehicle, double? fuelKg, double? verifiedKgS, double? speedMps)
    {
        if (vehicle != _vehicle) { Reset(); _vehicle = vehicle; }
        if (now <= _lastTime) return null;
        if (verifiedKgS == 0) { Reset(); return null; }
        if (fuelKg is not { } fuel || !double.IsFinite(fuel) || fuel < 0) { Reset(); return null; }
        var dt = now - _lastTime;
        if (_samples.Count > 0 && (fuel > _samples[^1].Fuel + .05 || dt > 2 ||
            _samples[^1].Fuel - fuel > Math.Max(20, _samples[^1].Fuel * .08)))
        { Reset(); _vehicle = vehicle; dt = double.PositiveInfinity; }
        _lastTime = now;
        _samples.Add((now, fuel));
        _samples.RemoveAll(s => now - s.Time > 30.01);
        double? flow = verifiedKgS is > 0 && double.IsFinite(verifiedKgS.Value) ? verifiedKgS : null;
        var span = 5d;
        if (flow == null)
        {
            var window = _samples.Where(s => now - s.Time <= 5.01).ToArray();
            // Integer fuel gauges need several changes; widen only when the short window lacks resolution.
            if (window.Select(s => s.Fuel).Distinct().Count() < 3) window = _samples.ToArray();
            span = window.Length > 1 ? window[^1].Time - window[0].Time : 0;
            if (span < 4.9 || window[0].Fuel <= window[^1].Fuel || window.Select(s => s.Fuel).Distinct().Count() < 3) return null;
            var mt = window.Average(s => s.Time);
            var mf = window.Average(s => s.Fuel);
            var denominator = window.Sum(s => Math.Pow(s.Time - mt, 2));
            flow = -window.Sum(s => (s.Time - mt) * (s.Fuel - mf)) / denominator;
            // Do not extrapolate an old burn rate indefinitely after shutdown or loss of gauge resolution.
            var lastDecrease = _samples.LastOrDefault(s => s.Fuel > fuel).Time;
            if (now - lastDecrease > Math.Max(5, span / 2)) return null;
        }
        if (flow is not > .000001 || !double.IsFinite(flow.Value)) return null;
        var alpha = double.IsFinite(dt) ? 1 - Math.Exp(-dt / 1.67) : 1;
        _smoothedFlow = _smoothedFlow is { } old ? old + alpha * (flow.Value - old) : flow;
        if (speedMps is not >= 0 || !double.IsFinite(speedMps.Value)) return null;
        var seconds = fuel / _smoothedFlow!.Value;
        return new(seconds, seconds * speedMps.Value, _smoothedFlow.Value, span, span > 5.1);
    }
}
