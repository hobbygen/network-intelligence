# Architecture review — 2026-09-17

Status: proposed; owner approval pending. The initial repository contained only the requirements document. The probe is an experiment, not the production architecture or completed Phase 1.

## Requirements summary

Build a local-first x64 Windows desktop application with Ethernet/Wi-Fi/multiple-adapter monitoring, diagnostics, historical analysis, CSV/JSON exports, tray operation and configurable alerts. Distinguish adapter throughput, physical link speed and active internet speed tests. Preserve timestamps, source, units and availability for every metric. One-year retention by default. Learn per-application baselines before explaining sustained anomalies. No payload inspection, external AI dependency, undisclosed cloud telemetry, automatic network changes or mandatory elevation. PDF reports are deferred.

## Technology and Windows compatibility

Propose C#/.NET 10 LTS, WinUI 3/Windows App SDK stable, MVVM, Microsoft.Extensions.DependencyInjection/Logging/Options, Microsoft.Data.Sqlite and versioned SQL migrations, xUnit, MSIX distribution. Evaluate LiveCharts2 WinUI compatibility and accessibility in a separate shell spike before pinning its version. No chart dependency is installed yet. Dependency versions must be explicit and reviewed; prereleases require an ADR. Use nullable C#, asynchronous cancellation-aware I/O, warnings as errors and no sensitive logging.

Windows App SDK's documented API floor is Windows 10 1809, build 17763. This is a **proposed compatibility floor**, not a tested application minimum. .NET 10's current supported Windows list includes selected Windows 10 Enterprise/LTSC releases, not every Windows 10 edition. Release support must be the intersection of OS servicing, .NET and Windows App SDK support. Recommend supported Windows 11 releases and supported Windows 10 LTSC editions at build 17763 or newer. Validate each promised edition on a clean VM before advertising support. This machine is Windows build 26200; it cannot establish Windows 10 compatibility.

Sources checked:
- https://learn.microsoft.com/en-us/windows/apps/get-started/windows-developer-faq
- https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md
- https://dotnet.microsoft.com/en-us/platform/support/policy

## Module hierarchy and dependencies

```text
NetworkIntelligence.App (initial WinUI window implemented; navigation/viewmodels/tray planned)
  -> Application (planned orchestration, use cases, alerts, export)
      -> Domain (implemented measurement/rate foundation; planned baselines/health)
      -> Contracts (planned versioned collector/store/notification interfaces)
  -> Infrastructure (planned Windows collectors, SQLite, settings, export)
      -> Domain + Contracts
MonitoringService (optional, scaffold implemented; Generic Host worker, references Contracts)
tools/TelemetryProbe (implemented experiment; references Domain)
tests/Domain.Tests (implemented rate regressions)
```

Keep P/Invoke out of viewmodels and domain. Promote proven probe collectors into Infrastructure with independent tests; do not reuse the probe entry point as a service. Collect on cancellable workers with bounded channels. Timestamp in UTC; compute elapsed time monotonically. Publish immutable snapshots on the dispatcher. Slow storage must degrade observably without freezing UI. Bound chart buffers; dispose native handles and subscriptions. Pause stops collection without confusing missing data with idle traffic. Resume and adapter replacement invalidate rate baselines.

## Windows API capability matrix

| Capability | API candidate | Privilege/limitation | Local status |
|---|---|---|---|
| Adapter identity/state/counters | NetworkInterface / GetIPStatistics; later GetIfEntry2 | Normally standard user; counters include protocol/virtual adapter effects | Implemented probe |
| Link speed | NetworkInterface.Speed | Negotiated/reported link capacity, not throughput | Implemented probe |
| Configuration | GetIPProperties / GetAdaptersAddresses | Gateway/DNS presence does not prove reachability | Presence only; values withheld |
| Duplex, driver, manufacturer | MIB_IF_ROW2, SetupAPI | Driver-dependent; separate optional metadata | Pending |
| Wi-Fi availability | WlanOpenHandle / WlanEnumInterfaces | WLAN service/device dependent | Implemented capability probe |
| SSID, signal, channel, security | WlanQueryInterface / WlanGetNetworkBssList | Location consent may be required; never bypass | Pending |
| Process connections | GetExtendedTcpTable/GetExtendedUdpTable | Ownership only; no byte counts | IPv4 TCP probe only |
| Per-process network events | Kernel TCP/IP ETW + TraceEvent | Elevated session confirmed required (standard user refused); event loss 0/measured run, protocol semantics/PID-reuse/adapter mapping still need validation | Validated standard-user and elevated probe; see `docs/ETW_VALIDATION.md` |
| TCP extended statistics | Get/SetPerTcpConnectionEStats | Must enable collection; TCP-only; permissions and short connections limit coverage | Pending experiment |
| Performance counters | Adapter counters, process I/O counters | Process I/O includes non-network operations; unsuitable as network byte source | Rejected for per-app bandwidth |
| WFP | Filtering platform classification/accounting | Deployment complexity; no automatic filtering or kernel driver in v1 | Deferred |
| Latency/loss | Ping/ICMP | ICMP may be blocked; short sample is not general packet loss | Optional probe implemented |
| DNS | Dns.GetHostAddressesAsync | OS cache may affect timing | Optional probe implemented |
| Internet reachability | Explicit HTTPS request with configured endpoint | Captive portals/proxies; external request disclosure required | Pending |

References:
- https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcptable_owner_pid
- https://learn.microsoft.com/en-us/windows/win32/etw/tcpip
- https://learn.microsoft.com/en-us/windows/win32/etw/about-event-tracing
- https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes

## Per-application feasibility

Tier 1 is the recommended initial production collector. The implemented probe tests aggregate adapter measurement. TCP ownership can be read independently but cannot supply upload/download byte counts. Never distribute adapter totals among processes using connection counts.

ETW is the validated Tier 3 mechanism (elevated), confirmed by a standard-user and an elevated probe run — see `docs/ETW_VALIDATION.md`. Microsoft warns that event-header PID may not identify the initiating process for network events: decode event-specific process fields and validate event versions. Track event loss and represent incomplete windows as lower quality. Identify process instances by PID plus creation time; group by normalized executable identity only when metadata is available. Missing metadata forms a separate unresolved group, never a guessed match. Do not assume the event stream identifies the physical adapter reliably — adapter identity was not present in the captured event fields and needs separate correlation work.

Answers to the ten required questions, now measured rather than provisional: (1) traffic attributed to individual processes — yes, elevated only; (2) per-direction bytes — yes, measured per event; (3) restarts — process-instance identity by PID+creation time proposed, not yet tested; (4) adapters distinguished for aggregate counters, per-process interface mapping still pending; (5) collection reads size/PID/protocol/direction/family only, confirmed no payload access; (6) elevation (Administrator or Performance Log Users) confirmed required — standard user is refused before any session attempt; (7) UDP/IPv6 endpoint semantics beyond size, loopback, VPN, retransmits remain unresolved; (8) only whole-probe wall time measured (0 events lost in a 30s run), no isolated session CPU/memory benchmark yet; (9) current host (Windows 11 build 26200) only, Windows 10 validation pending; (10) controlled known-byte traffic comparison not yet completed.

ETW requires elevation, confirmed, and the mechanism now ships inside `NetworkIntelligence.MonitoringService` — a scaffold, not a released feature (see ADR-007, `docs/DECISIONS.md`). It is an explicitly installed, optional Generic Host worker with an authenticated local named pipe (`WellKnownSidType.InteractiveSid`/Administrators ACL, anonymous/network denied), versioned bounded messages (fixed `ServiceMessageType` enum, length-capped framing) and no arbitrary commands. The main App remains unelevated and fully usable without the service. Manually validated end to end: elevated service, unelevated client, real per-process data, zero events lost. Still required before release: code signing, a dedicated least-privileged service account, automated unauthorized-client tests, and wiring an actual client into the main App/Infrastructure (today the only client is the telemetry probe's `--service-status` flag).

## Database design (proposal, not an implemented migration)

SQLite under the user's local application-data directory; WAL, foreign keys, single writer, short transactions, busy timeout and transactional numbered migrations. Store integer UTC timestamps and integer byte totals; optional values are NULL with explicit state/source/quality fields.

| Tables | Keys and indexes | Purpose |
|---|---|---|
| SchemaMigrations, Settings | Version; setting key | Versioned upgrades and typed configuration |
| NetworkAdapters, AdapterSnapshots | Adapter identity; (adapter,time) | Stable IDs and changing metadata |
| TrafficSamples, PerformanceSamples | (adapter,time), time | Raw intervals with availability and sample duration |
| ApplicationIdentities, ApplicationTrafficSamples | App identity, process-instance key; (app,time) | Stable groups and quality-bearing traffic |
| ApplicationBaselines | (app,modelVersion,direction) | Learning state, robust statistics, hour-of-week observations |
| AnomalyEvents, AlertEvents | event ID; time; (app,time) | Explanation, evidence and notification disposition |
| ConnectionEvents, SpeedTestResults, DiagnosticResults | ID; time | Auditable actions/results |

One second live chart samples remain bounded in memory. Propose one-minute durable aggregates with min/max/total/coverage plus time-bounded higher resolution anomaly evidence; one-year retention applies to stored history. Do not imply that minute aggregates preserve one-second details. Use independently indexed UTC cutoff deletes in small background batches; observable last run and deleted count. Integrity checks, pre-migration backup, failed-migration rollback, disk-full behavior and export cancellation need integration tests. Data deletion requires an explicit UI action. Schema and aggregation granularity remain reviewable before implementation.

## UI/UX specification

Use a WinUI NavigationView, resizable main content, system/light/dark theme, keyboard navigation, accessible labels and text alternatives for graphs. Show measured/unavailable/permission status next to values; no zero placeholders or invented health score. Source and timestamp appear in details. Avoid adding virtual and physical adapter totals together by default.

| Page | Primary content and actions |
|---|---|
| Dashboard | Selected adapter, independent internet state, RX/TX cards, session totals, uptime, traffic/latency charts, top apps, events/alerts, explained health score |
| Ethernet | Per-adapter state, reported link speed, counters/errors, configuration and events |
| Wi-Fi | Interface selection, connection/security/signal details, permission guidance and unavailable states |
| Network Performance | Target selection, ICMP/DNS history, manual speed test with provider/data warning/cancel |
| Bandwidth & Data Usage | Interval and adapter selection, daily/weekly/monthly/year aggregation, CSV/JSON |
| Application Usage | Socket-ownership search/sort/filter implemented; live per-app bandwidth now wired to the optional MonitoringService (ADR-009), gated behind manual install and clearly marked not accuracy-certified; persisted history, sort/filter on live data, trust/exclude still pending |
| Connection History | Timestamped transitions, adapter context and correlated diagnostic evidence |
| Diagnostics | Explicit bounded tests, progress/cancel, factual results, export; no automatic fixes |
| Settings | Retention, privacy, targets, alerts/quiet hours, sensitivity, exclusions, theme, service status, delete data |

Tray: show, status, pause/resume, diagnostics, settings, exit. Closing-to-tray behavior must be explained on first use. Notifications: native Windows API after packaging validation; open details/trust/snooze/dismiss, cooldown and quiet hours. Future AI uses a versioned explanation interface over consented local summaries; algorithmic core must remain independent.

## Anomaly and health design

Proposed balanced defaults: seven observed days plus at least 60 valid active intervals before learned-baseline alerts; compare upload/download separately using rolling median, MAD and percentile envelope. Require minimum bandwidth and at least 30 seconds of sustained deviation, then cooldown per app/direction. These are starting parameters for false-positive testing, not validated product defaults. Exclude trusted apps before alert emission. Freeze or robustly limit anomalous samples entering baseline. Persist model version and confidence. Missing/low-quality data must not train or trigger. Quiet hours suppress notifications, not evidence storage. Explain rates, baseline, duration and quality; never imply malware.

Health needs a documented weighting model and minimum coverage. Propose no score without a reachability test and sufficient latency/loss observations. Display contributing available factors and omitted inputs; do not silently award perfect points for missing Wi-Fi/errors/disconnect history. Final weights require scenario tests.

## Roadmap and risk register

1. Phase 0: repository/build/tests/docs and blank WinUI window built; packaging spike still pending.
2. Phase 1: complete API experiments, ETW controlled traffic validation, privilege/overhead measurements, Windows compatibility matrix; review decisions with owner.
3. Phases 2–3: approved WinUI shell/tray/settings, isolated collectors and SQLite.
4. Phases 4–5: diagnostics/provider integration, charts/aggregation/exports.
5. Phases 6–7: only validated application accounting, then baselines/explained alerts.
6. Phases 8–10: privacy/accessibility, prolonged tests, signed package and clean install/upgrade/uninstall.

| Risk | Impact | Mitigation / release gate |
|---|---|---|
| ETW undercount/double-count/PID reuse | Misleading alerts | Mechanism validated (0 events lost/measured run); known-traffic comparison and PID-reuse dedup still outstanding before accuracy claims |
| Windows 10 support ambiguity | Unsupported installs | Edition/build matrix plus VM validation |
| WLAN location restrictions | Missing SSID/signal | PermissionDenied state and user-controlled consent |
| NuGet/network restrictions | Cannot build WinUI or audit dependencies | Restore with working network; never replace stack silently |
| Virtual adapters and counter resets | Double-count/huge spikes | Adapter scope, monotonic timing, reset gaps, regression tests |
| Long retention volume | Disk growth/slow UI | Aggregates, bounded writes, retention and one-year load test |
| Speed endpoint terms/reliability | Broken or unauthorized tests | Supported provider contract, bounded transfer, explicit action |
| Elevated service/IPC | Security exposure | Scaffold implemented with restricted ACL, versioned/bounded schema; signing, dedicated service account and unauthorized-client tests remain |
| New-app/brief-spike alerts | Alert fatigue | Learning, duration threshold, quality gate, trust and cooldown |

## Essential review questions

No visual-preference clarification is needed; the brief is specific. Owner checkpoint: approve the proposed architecture and tiered accounting strategy before major features. Confirm supported Windows 10 Enterprise/LTSC scope or specify the required edition/build for compatibility testing. Elevated ETW collection must remain a separately approved experiment if needed. An approved architecture does not waive accuracy or release gates.
