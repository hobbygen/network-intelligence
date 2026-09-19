namespace NetworkIntelligence.Domain;

/// <summary>Attributes a stream of per-pid traffic events to a window, closing the one remaining gap docs/DECISIONS.md
/// ADR-012 documented: the Windows kernel can reuse a pid for a new, unrelated process, and if that handoff happens
/// inside one collection window, naively summing traffic by pid alone would silently merge the old and new
/// processes' bytes into a single sample tagged with whichever name is current at flush time. The caller (an ETW
/// collector) calls <see cref="SplitOnIdentityChange"/> the moment it observes a process-start event for a pid it
/// already had a resolved identity for, detaching and preserving whatever traffic had already accumulated under
/// the old identity before it is overwritten. Pure and clock-free — knows nothing about process names, ETW, or
/// time; the caller supplies event ordering and decides window boundaries.</summary>
public sealed class ProcessTrafficAccumulator
{
    public sealed record Sample(int Pid, long ReceivedBytes, long SentBytes, int Events);

    private readonly Dictionary<int, (long Received, long Sent, int Events)> live = [];

    public void RecordTraffic(int pid, int size, bool isSend)
    {
        live.TryGetValue(pid, out var totals);
        live[pid] = isSend
            ? (totals.Received, totals.Sent + size, totals.Events + 1)
            : (totals.Received + size, totals.Sent, totals.Events + 1);
    }

    /// <summary>Detaches and returns whatever traffic is currently pending for <paramref name="pid"/>, resetting
    /// it to empty so subsequent traffic starts a fresh bucket under the new identity. Returns <c>null</c> if
    /// nothing was pending — the common case, since most process starts are not a mid-window pid handoff.</summary>
    public Sample? SplitOnIdentityChange(int pid)
    {
        if (!live.Remove(pid, out var totals)) return null;
        return new Sample(pid, totals.Received, totals.Sent, totals.Events);
    }

    /// <summary>Ends the current window: returns every pid's accumulated traffic and clears all state.</summary>
    public IReadOnlyList<Sample> Flush()
    {
        var result = new List<Sample>(live.Count);
        foreach (var (pid, totals) in live) result.Add(new Sample(pid, totals.Received, totals.Sent, totals.Events));
        live.Clear();
        return result;
    }
}
