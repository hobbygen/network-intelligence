# Per-application ETW telemetry proof-of-concept — 2026-09-17

Answers validation-plan experiment 4 and requirements section 7.2. This is a standalone probe result, not an implemented application feature. Nothing here has been wired into `NetworkIntelligence.Infrastructure` or the App/service processes.

## Method

`tools/NetworkIntelligence.TelemetryProbe --etw-seconds N` opens a real-time ETW session on the kernel Network provider (`Microsoft-Windows-Kernel-Network`, `NetworkTCPIP` keyword) via `Microsoft.Diagnostics.Tracing.TraceEvent` (`TraceEventSession`), a Microsoft-maintained ETW consumption library — added only to the isolated probe project pending this validation. No kernel driver, no Windows Filtering Platform, no packet payload. Only event-header fields are read per event: owning PID, transfer size, TCP/UDP, IPv4/IPv6, direction (send/receive), and connect/disconnect. Source: `tools/NetworkIntelligence.TelemetryProbe/EtwProbe.cs`.

## Standard-user result (measured)

This development session runs as a standard user: not an Administrator, not a member of Performance Log Users (`whoami /groups` confirmed). `TraceEventSession.IsElevated()` returned `false`; the probe reported `PermissionDenied` and did not attempt to start a session.

```json
{"Kind":"EtwCapability","Availability":"PermissionDenied","Elevated":false,
 "Detail":"Process is not elevated. Starting a real-time ETW trace session requires
  Administrator or Performance Log Users membership; the session was not attempted."}
```

This is the expected, documented Windows behavior for real-time ETW sessions and confirms Tier 2 (unprivileged, exact byte-level accounting) is not achievable — matching the requirements document's own warning in section 7.1.

## Elevated result (measured)

Run as Administrator via one interactive UAC consent (`Start-Process -Verb RunAs`), 30.25-second window, with ~24 MB of concurrent HTTPS traffic generated in 8 requests plus ordinary background activity (browsers, editor, this CLI session):

| Metric | Value |
|---|---|
| Session start | Succeeded (`EnableKernelProvider(NetworkTCPIP)`) |
| Events lost | 0 |
| TCP send / receive events | 159 / 382 |
| UDP send / receive events | 35 / 282 |
| IPv6-flagged events | 433 |
| Connect / disconnect events | 4 / 7 |
| Distinct processes attributed | 15 |

Sample of per-process attribution (PID, name, received/sent bytes, event count) — real values from `artifacts/etw-elevated.jsonl` (local, git-ignored):

- `claude.exe.old.*` — 607,965 / 31,516 bytes, 234 events
- `python` — 19,987 / 8,111 bytes, 225 events
- `chrome` — 10,580 / 1,992 bytes, 88 events
- `svchost` — 10,052 / 1,902 bytes, 106 events
- `opera`, `DuckDuckGo.WebView`, `Code`, `System` — each attributed distinctly with smaller totals

No packet payload was read at any point — only the header fields listed above.

## Findings against the ten required questions (spec section 7.2)

1. **Traffic attributed to individual processes** — yes, validated, elevated only.
2. **Upload/download bytes measured** — yes, per event, validated.
3. **Consistent across process restarts** — not yet validated; the probe resolves PID → process name only at read time and explicitly does not de-duplicate PID reuse.
4. **Multiple adapters distinguished** — not yet; the captured event fields do not include adapter identity, so this needs a separate investigation (e.g. correlating local socket address to adapter).
5. **Collected without packet payload** — yes, by construction.
6. **Privileges required** — Administrator or Performance Log Users to start the real-time session; a standard user is refused before any session is attempted.
7. **Missing/unreliable telemetry** — per-event adapter attribution, loopback/VPN interaction, retransmit accounting, and UDP endpoint semantics beyond raw size remain unvalidated.
8. **CPU/memory overhead** — not isolated for the ETW session specifically; only whole-probe-process wall time was captured. A dedicated overhead benchmark (validation-plan experiment 6) is still required.
9. **Windows 10 compatibility** — not tested; this machine is Windows 11 build 26200.
10. **Validated against independently known traffic** — not yet; this run used organic and approximate background traffic, not a byte-for-byte controlled comparison (validation-plan experiment 5).

## Remaining work before Tier 3 can be marked complete

- Controlled known-byte transfer comparison at 1 MiB and 100 MiB against an independently logged payload total.
- Process-restart / short-lived-process attribution test.
- Adapter disambiguation.
- Long-running overhead measurement (five-minute idle, ten-minute steady/high traffic, one-hour soak).
- Windows 10 elevated-session behavior.
- IPv6/UDP endpoint semantics review.

## Architecture implication

This validates the ADR-002/ADR-004 tiered strategy: the kernel Network ETW provider, consumed through `TraceEventSession`, is a technically viable Tier 3 mechanism, but it requires elevation and belongs only in the optional, explicitly-installed `NetworkIntelligence.MonitoringService` (section 5.2/10.1) — never in the unelevated main app process. The main WinUI app must remain fully usable without it. No service has been installed by this experiment; the probe starts and stops its own session (`StopOnDispose`) and exits.

Raw evidence: `artifacts/etw-elevated.jsonl` (local, git-ignored, requires an elevated re-run to reproduce).
