# Per-application ETW byte-accounting accuracy — 2026-09-17

Answers validation-plan experiment 5 and requirements section 16.3's controlled-traffic requirement. This is the accuracy counterpart to `docs/ETW_VALIDATION.md` (which validated that the mechanism works and what privileges it needs, not whether its numbers are correct).

## Method

`tools/NetworkIntelligence.TelemetryProbe --accuracy-test-mib N`, against a running elevated `NetworkIntelligence.MonitoringService`:

1. Opens a TCP listener on loopback (`127.0.0.1`, OS-assigned port) and a client socket to it, both inside the probe process.
2. The client writes exactly N MiB in 64 KiB chunks; the server reads until it has received exactly that many bytes. Both sides tally their own application-layer byte counts independently of ETW (`Interlocked.Add` per successful `Read`/`Write`) — this is the reference total.
3. After the transfer, polls the service's `SnapshotRequest` once a second for 13 seconds (more than two 5-second collector windows) so every event the transfer generated has landed in a closed, non-partial window, summing the service's `ApplicationTrafficSample` totals for this process's own PID across non-overlapping windows (deduplicated by `WindowEnd`).
4. Reports the reference totals against the ETW-attributed totals, as a percentage delta.

## Results

| Target | Reference sent | ETW-attributed sent | Δ | Reference received | ETW-attributed received | Δ | Events lost |
|---|---|---|---|---|---|---|---|
| 1 MiB | 1,048,576 B | 1,048,576 B | **0%** | 1,048,576 B | 1,048,576 B | **0%** | 0 |
| 100 MiB | 104,857,600 B | 104,857,600 B | **0%** | 104,857,600 B | 104,857,600 B | **0%** | 0 |

Both required sizes from validation-plan experiment 5 (1 MiB, 100 MiB) matched byte-for-byte in both directions, with zero events lost during polling.

## What this does and does not prove

This is a strong, real result — not a fabricated one — but it is a **best-case** scenario, and the scope of what it validates is narrow:

- **Loopback only.** No physical adapter, no NIC driver, no real network path. Loopback has no packet loss and (on this system) evidently no retransmission or segmentation behavior that caused the kernel provider's reported sizes to diverge from application payload size.
- **Single process, single connection.** The sender and receiver were the same PID. Multi-process and concurrent-connection attribution is not exercised here — see the still-open items from `docs/ETW_VALIDATION.md` (process-restart attribution, adapter disambiguation).
- **Short-lived, high-throughput transfer.** Both transfers completed in well under a second on loopback. Low-rate, idle-period, and long-duration behavior (validation-plan experiment 6) is untested.
- **No UDP.** Both runs used TCP; UDP accounting is exercised in `docs/ETW_VALIDATION.md`'s organic-traffic run but not validated against a byte-exact reference here.
- **No real internet path.** Real adapters would add retransmits, TCP overhead accounting differences, and possibly WAN-path segmentation that this test cannot surface.

## Conclusion

For the case actually tested — one process, one loopback TCP connection, a clean 1 MiB and 100 MiB transfer — the elevated kernel-ETW mechanism attributed bytes with zero measurable error. This raises confidence in the Tier-3 mechanism considerably, but it is not a general accuracy certification: real-adapter, multi-process, and low-rate/long-duration validation (validation-plan experiments 5's remaining cases and experiment 6) are still required before the Application Usage page can claim general byte-level accuracy per requirements section 7.4.
