using MapHelper.Core;
using Xunit;

namespace MapHelper.Tests;

public class RangeTests
{
    [Fact]
    public void KnownFlowProducesCorrectMetricRange()
    {
        var r = new RangeEstimator().Update(0, "jet", 100, .5, 200);
        Assert.NotNull(r); Assert.Equal(200, r.Seconds, 3); Assert.Equal(40000, r.Meters, 3);
    }
    [Fact]
    public void DerivesFlowFromFuelHistory()
    {
        var estimator = new RangeEstimator(); RangeEstimate? result = null;
        for (var i = 0; i <= 100; i++) result = estimator.Update(i / 10d, "jet", 100 - i / 10d, null, 200);
        Assert.NotNull(result); Assert.Equal(1, result.ConsumptionKgS, 3); Assert.Equal(18000, result.Meters, 2);
    }
    [Fact]
    public void CoarseGaugeExtendsWindowInsteadOfInfiniteRange()
    {
        var e = new RangeEstimator(); RangeEstimate? r = null;
        for (var i = 0; i <= 200; i++) r = e.Update(i / 10d, "prop", Math.Floor(100 - i / 10d * .1), null, 80);
        Assert.NotNull(r); Assert.True(r.Extended); Assert.InRange(r.ConsumptionKgS, .075, .13);
    }
    [Fact]
    public void UnchangingFuelDoesNotImplyInfiniteRange()
    {
        var e = new RangeEstimator();
        for (var i = 0; i < 400; i++) Assert.Null(e.Update(i / 10d, "jet", 100, null, 100));
    }
    [Theory]
    [InlineData(null, 100d)]
    [InlineData(100d, null)]
    [InlineData(-1d, 100d)]
    public void MissingAndInvalidValuesAreUnavailable(double? fuel, double? speed) => Assert.Null(new RangeEstimator().Update(0, "x", fuel, 1, speed));
    [Fact]
    public void StationaryMeansZeroDistance()
    {
        var r = new RangeEstimator().Update(0, "x", 100, 1, 0); Assert.NotNull(r); Assert.Equal(0, r.Meters);
    }
    [Fact]
    public void RefuelingRestartsDerivedCalculation()
    {
        var e = new RangeEstimator(); for (var i = 0; i <= 60; i++) e.Update(i * .1, "x", 100 - i * .1, null, 100);
        Assert.Null(e.Update(6.1, "x", 200, null, 100));
    }
    [Fact]
    public void VehicleSwitchAndLongGapDiscardHistory()
    {
        var e = new RangeEstimator(); for (var i = 0; i <= 60; i++) e.Update(i * .1, "x", 100 - i * .1, null, 100);
        Assert.Null(e.Update(6.1, "y", 93, null, 100)); Assert.Null(e.Update(20, "y", 80, null, 100));
    }
    [Fact] public void VerifiedZeroConsumptionHidesCircle() => Assert.Null(new RangeEstimator().Update(0, "x", 100, 0, 100));
    [Fact]
    public void AbruptFlowChangeIsSmoothedThenConverges()
    {
        var e = new RangeEstimator(); e.Update(0, "x", 100, 1, 100);
        var initial = e.Update(.1, "x", 99.5, 5, 100); Assert.NotNull(initial); Assert.InRange(initial.ConsumptionKgS, 1, 2);
        RangeEstimate? final = null;
        for (var i = 2; i <= 60; i++) final = e.Update(i * .1, "x", 100 - i * .5, 5, 100);
        Assert.NotNull(final); Assert.InRange(final.ConsumptionKgS, 4.8, 5);
    }
    [Fact]
    public void LargeFuelDiscontinuityResetsEstimate()
    {
        var e = new RangeEstimator(); for (var i = 0; i <= 60; i++) e.Update(i * .1, "x", 1000 - i * .1, null, 100);
        Assert.Null(e.Update(6.1, "x", 600, null, 100));
    }
}
