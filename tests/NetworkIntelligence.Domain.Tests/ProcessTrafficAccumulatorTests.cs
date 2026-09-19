using NetworkIntelligence.Domain;
namespace NetworkIntelligence.Domain.Tests;

public class ProcessTrafficAccumulatorTests
{
    [Fact] public void FlushReturnsAccumulatedTotalsForOnePid()
    {
        var accumulator = new ProcessTrafficAccumulator();
        accumulator.RecordTraffic(100, 500, isSend: false);
        accumulator.RecordTraffic(100, 200, isSend: false);
        accumulator.RecordTraffic(100, 300, isSend: true);
        var sample = Assert.Single(accumulator.Flush());
        Assert.Equal(100, sample.Pid); Assert.Equal(700, sample.ReceivedBytes); Assert.Equal(300, sample.SentBytes); Assert.Equal(3, sample.Events);
    }
    [Fact] public void MultiplePidsAccumulateIndependently()
    {
        var accumulator = new ProcessTrafficAccumulator();
        accumulator.RecordTraffic(1, 100, isSend: false);
        accumulator.RecordTraffic(2, 50, isSend: true);
        var samples = accumulator.Flush();
        Assert.Equal(2, samples.Count);
        Assert.Equal(100, samples.Single(s => s.Pid == 1).ReceivedBytes);
        Assert.Equal(50, samples.Single(s => s.Pid == 2).SentBytes);
    }
    [Fact] public void SplitOnIdentityChangeDetachesPendingTrafficAndStartsFreshBucket()
    {
        var accumulator = new ProcessTrafficAccumulator();
        accumulator.RecordTraffic(100, 1000, isSend: false); // old process's traffic before the handoff
        var carried = accumulator.SplitOnIdentityChange(100);
        Assert.NotNull(carried);
        Assert.Equal(1000, carried!.ReceivedBytes); Assert.Equal(1, carried.Events);

        accumulator.RecordTraffic(100, 250, isSend: false); // new process's traffic after the handoff, same pid
        var fresh = Assert.Single(accumulator.Flush());
        Assert.Equal(250, fresh.ReceivedBytes); Assert.Equal(1, fresh.Events); // old traffic not double-counted
    }
    [Fact] public void SplitOnIdentityChangeWithNoPendingTrafficReturnsNull()
    {
        var accumulator = new ProcessTrafficAccumulator();
        Assert.Null(accumulator.SplitOnIdentityChange(999));
    }
    [Fact] public void FlushClearsState()
    {
        var accumulator = new ProcessTrafficAccumulator();
        accumulator.RecordTraffic(1, 100, isSend: false);
        accumulator.Flush();
        Assert.Empty(accumulator.Flush());
    }
    [Fact] public void MultipleHandoffsForTheSamePidWithinOneWindowEachSplitCorrectly()
    {
        var accumulator = new ProcessTrafficAccumulator();
        accumulator.RecordTraffic(100, 111, isSend: false);
        var first = accumulator.SplitOnIdentityChange(100);
        accumulator.RecordTraffic(100, 222, isSend: false);
        var second = accumulator.SplitOnIdentityChange(100);
        accumulator.RecordTraffic(100, 333, isSend: false);
        var third = Assert.Single(accumulator.Flush());

        Assert.Equal(111, first!.ReceivedBytes); Assert.Equal(222, second!.ReceivedBytes); Assert.Equal(333, third.ReceivedBytes);
    }
}
