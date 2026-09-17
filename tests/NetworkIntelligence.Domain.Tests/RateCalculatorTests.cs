using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Domain.Tests;
public class RateCalculatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    [Fact] public void FirstSampleIsUnavailable() => Assert.Null(RateCalculator.Calculate(null, new("a", 100, 200, 1), Now).Download.Value);
    [Fact] public void CalculatesBytesPerSecond()
    {
        var rate = RateCalculator.Calculate(new("a", 100, 200, 1), new("a", 500, 400, 3), Now);
        Assert.Equal(200d, rate.Download.Value); Assert.Equal(100d, rate.Upload.Value);
        Assert.Equal(Availability.Measured, rate.Download.Availability);
    }
    [Fact] public void ResetDoesNotCreateNegativeTraffic()
    {
        var rate = RateCalculator.Calculate(new("a", 500, 200, 1), new("a", 1, 400, 2), Now);
        Assert.Null(rate.Download.Value); Assert.Equal(200d, rate.Upload.Value);
    }
    [Fact] public void DifferentAdapterCannotReuseBaseline() => Assert.Null(RateCalculator.Calculate(new("a", 1, 1, 1), new("b", 100, 100, 2), Now).Download.Value);
    [Theory] [InlineData(1)] [InlineData(0)] [InlineData(20)] [InlineData(double.NaN)]
    public void InvalidOrResumeIntervalIsUnavailable(double time) => Assert.Null(RateCalculator.Calculate(new("a", 1, 1, 1), new("a", 100, 100, time), Now).Download.Value);
    [Fact] public void GenuineZeroIsMeasured() => Assert.Equal(0d, RateCalculator.Calculate(new("a", 1, 1, 1), new("a", 1, 1, 2), Now).Download.Value);
}
