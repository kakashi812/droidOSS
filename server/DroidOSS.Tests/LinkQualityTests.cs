using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class LinkQualityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_meter_has_no_rate()
    {
        var meter = new RateMeter();
        meter.Update(0, T0);
        Assert.Null(meter.Rate);
    }

    [Fact]
    public void A_rate_appears_after_one_second()
    {
        var meter = new RateMeter();
        meter.Update(1_000, T0);
        meter.Update(1_060, T0.AddMilliseconds(500));
        Assert.Null(meter.Rate);

        meter.Update(1_125, T0.AddSeconds(1));
        Assert.Equal(125, meter.Rate);
    }

    [Fact]
    public void Fast_ticks_accumulate_into_one_steady_measurement()
    {
        var meter = new RateMeter();
        meter.Update(0, T0);

        // A 30 fps display, with the phone sending at exactly 120 Hz.
        for (var tick = 1; tick <= 30; tick++)
            meter.Update(tick * 4, T0.AddSeconds(tick / 30.0));

        Assert.NotNull(meter.Rate);
        Assert.Equal(120, meter.Rate!.Value, precision: 6);
    }

    [Fact]
    public void A_falling_total_means_a_new_session_and_starts_over()
    {
        var meter = new RateMeter();
        meter.Update(0, T0);
        meter.Update(125, T0.AddSeconds(1));
        Assert.Equal(125, meter.Rate);

        meter.Update(3, T0.AddSeconds(2));
        Assert.Null(meter.Rate);
    }

    [Theory]
    [InlineData(null, LinkQuality.Unknown)]
    [InlineData(125.0, LinkQuality.Good)]
    [InlineData(100.0, LinkQuality.Good)]
    [InlineData(99.0, LinkQuality.Fair)]
    [InlineData(62.5, LinkQuality.Fair)]
    [InlineData(62.0, LinkQuality.Poor)]
    [InlineData(0.0, LinkQuality.Poor)]
    public void Rates_map_to_quality(double? rate, LinkQuality expected) =>
        Assert.Equal(expected, LinkQualityRating.FromRate(rate));
}
