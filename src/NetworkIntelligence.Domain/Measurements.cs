namespace NetworkIntelligence.Domain;

public enum Availability { Measured, Estimated, Cached, Unavailable, PermissionDenied, Error }
public sealed record Measurement<T>(T? Value, string Unit, string Source,
    DateTimeOffset Timestamp, Availability Availability, string? Detail = null) where T : struct;
public sealed record CounterSample(string AdapterId, long Received, long Sent, double MonotonicSeconds);
public sealed record TrafficRate(Measurement<double> Download, Measurement<double> Upload);
public static class RateCalculator
{
    public static TrafficRate Calculate(CounterSample? previous, CounterSample current, DateTimeOffset timestamp)
    {
        const string source = "NetworkInterface.GetIPStatistics / monotonic elapsed time";
        Measurement<double> Missing(string reason) => new(null, "bytes/s", source, timestamp, Availability.Unavailable, reason);
        if (previous is null || previous.AdapterId != current.AdapterId)
            return new(Missing("Waiting for a second sample for this adapter."), Missing("Waiting for a second sample for this adapter."));
        double elapsed = current.MonotonicSeconds - previous.MonotonicSeconds;
        if (!double.IsFinite(elapsed) || elapsed <= 0 || elapsed > 10)
            return new(Missing("Invalid interval or collection gap; baseline reset."), Missing("Invalid interval or collection gap; baseline reset."));
        Measurement<double> Direction(long before, long after) => before < 0 || after < before
            ? Missing("Counter decreased or reset; wrap cannot be distinguished safely.")
            : new((after - before) / elapsed, "bytes/s", source, timestamp, Availability.Measured);
        return new(Direction(previous.Received, current.Received), Direction(previous.Sent, current.Sent));
    }
}
