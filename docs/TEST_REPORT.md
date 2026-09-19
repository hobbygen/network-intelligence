# Validation report — 2026-09-17

This is an initial engineering checkpoint, not release acceptance.

## Environment

Windows x64 build 26200; .NET SDK 10.0.400 and runtime 10.0.11. No service, driver, firewall rule or DNS configuration was installed or modified. No elevation was requested. Windows 10 VM testing has not occurred.

## Completed validation

| Check | Result |
|---|---|
| Domain/probe/test Debug build | Passed; NuGet vulnerability-feed warning NU1900 |
| Domain/probe/test Release build | Passed; same audit warning |
| xUnit Debug | 9 passed, 0 failed, 0 skipped |
| Live probe, three samples, localhost diagnostics | Exit 0; valid JSON lines |
| Adapter enumeration | 57 interface entries per pass, 171 snapshots (includes virtual/tunnel interfaces) |
| WLAN enumeration | 1 interface, native return code 0 |
| IPv4 TCP ownership | Measured PID/connection counts; byte accounting unavailable |
| Localhost DNS | 20.4712 ms measured resolver duration; cache may apply |
| Five localhost ICMP attempts | All successful; 0 ms reported RTT, 0% nonresponses, 0 ms successive RTT difference |

The zero RTT is the Ping API's millisecond resolution on loopback, not proof of physically zero latency. This does not validate internet reachability, internet speed or public DNS performance. Adapter counts include down/virtual/tunnel entries and are not counts of physical NICs.

The three-pass Debug probe reported elapsed 2.2980971 seconds, CPU 0.359375 seconds and working set 42,438,656 bytes. These include enumeration, JSON serialization and localhost diagnostics; they are one short observation, not a monitoring CPU target, idle benchmark or performance guarantee. Raw evidence is local and ignored by Git: `artifacts/telemetry-probe.jsonl`; xUnit TRX is under `artifacts/test-results/`.

Unit coverage: first sample unavailable; directional bytes/second; counter reset; different-adapter baseline rejection; duplicate/reversed/long/non-finite intervals; measured zero traffic. Counter decrease is conservatively treated as reset, not assumed rollover. Hardware resets/resume were not exercised.

## Build environment limitations

Initial parallel build failed without useful diagnostics. Single-node build succeeded. Shared compiler connection timed out; `-p:UseSharedCompilation=false` avoids that dependency. NuGet vulnerability audit could not reach api.nuget.org. Tests restored using locally available packages; vulnerability review is not complete. Keep CI audit enabled; do not treat the local cache as security validation.

WinUI uses locally available exact versions Microsoft.WindowsAppSDK 1.8.260804001 and Microsoft.Windows.SDK.BuildTools 10.0.26100.4654. Network restore failed, then restore from the local NuGet package cache succeeded with audit disabled for that explicit command only.

Native WinUI Debug build passed with zero warnings/errors. The full solution Release build passed with the existing test-project NU1900 audit warning. The Debug executable launched, created a nonzero window handle with title `Network Intelligence — Architecture Preview`, and reported Responding=true. This is a process/window lifecycle smoke check, not screenshot-based visual or accessibility validation. The initial input-idle signal arrived before the window existed, so a subsequent process inspection was required.

## ETW per-application telemetry proof-of-concept (2026-09-17, see `docs/ETW_VALIDATION.md`)

Standard-user run: `TraceEventSession.IsElevated()` false, no session attempted, `PermissionDenied` reported. Elevated run (one interactive UAC consent, kernel Network provider via `Microsoft.Diagnostics.Tracing.TraceEvent`, 30.25s window, ~24 MB concurrent HTTPS background traffic): 0 events lost; 159 TCP send / 382 TCP receive / 35 UDP send / 282 UDP receive events; 433 IPv6-flagged events; 15 processes attributed with real per-PID received/sent byte totals; only event headers read, no payload. This validates the Tier-3 mechanism and the elevation requirement; it is not yet an accuracy-certified feature — see remaining acceptance work below.

## MonitoringService scaffold end-to-end validation (2026-09-17, see `docs/DECISIONS.md` ADR-007)

Manual test: `scripts/service-run-foreground.ps1`-equivalent elevated run of `NetworkIntelligence.MonitoringService.exe` (one interactive UAC consent), then `tools/NetworkIntelligence.TelemetryProbe --service-status` run as the ordinary standard user in a separate, unelevated shell.

| Check | Result |
|---|---|
| Service starts, enables kernel provider | Succeeded; logged "Kernel Network ETW session started." |
| Standard-user client connects through the pipe ACL | Succeeded (Administrators + `InteractiveSid` allowed; `AnonymousSid`/`NetworkSid` denied) |
| Hello/HelloAck handshake | Succeeded, protocol version 1 |
| SnapshotRequest → live per-process data | Succeeded across two consecutive 5-second windows, 0 events lost both times |
| Client identity audit logging | Initially broken (`GetImpersonationUserName()` needs a prior read — fixed same session); confirmed working, logged real identity `hp` |
| Live-traffic attribution | Curl-generated downloads (~2 MB each) visible by PID within the window; PID had already exited by snapshot time in one run, correctly labeled "(exited)" — a live instance of the documented PID-reuse caveat |
| Wire-format unit tests (no elevation needed) | 6 new tests added (`ServiceProtocolTests.cs`): round-trip, truncated-stream, oversized-declared-length, empty-disconnect, unavailable-snapshot — all passing |

This validates the IPC and continuous-collection design end to end. It is not an accuracy or security certification — see remaining acceptance work below.

## Controlled-traffic accuracy test (2026-09-17, see `docs/ETW_ACCURACY.md`)

`--accuracy-test-mib` sent exact, independently-tallied 1 MiB and 100 MiB transfers over a loopback TCP socket and compared them against the MonitoringService's ETW-attributed totals for the same PID: **0% delta in both directions at both sizes, 0 events lost.** Narrow scope — loopback, single process, single connection, high throughput; concurrent processes, real adapters, UDP-against-reference and low-rate/long-duration cases remain open.

## App-wired MonitoringService client, visual verification (2026-09-17, see `docs/DECISIONS.md` ADR-009)

Extended `--smoke-test` with `--smoke-page <name>` to render a chosen page (previously Dashboard only) and captured the Application Usage page in both states:

| Scenario | Result |
|---|---|
| Service not running (default state) | No crash; "Live per-application bandwidth" card correctly shows "Unavailable · Monitoring service not reachable — it may not be installed or not running."; socket-ownership list below it unaffected |
| Service running elevated | Card shows real live data: 12 processes, `0 events lost this window`, correct 5s window timestamps; curl-generated traffic visible by PID at ~3.2 Mbps; service log confirms repeated authorized `hp` connections at the expected ~4s poll cadence |

Both renders produced via `dotnet run --project src/NetworkIntelligence.App -- --smoke-test --data-dir <dir> --smoke-page Applications`, screenshots reviewed directly. This is UI-wiring verification, not a UI automation test suite (spec section 16.2 still open) and not an accuracy claim beyond ADR-008's narrow loopback result.

## Per-application history persistence (2026-09-17, see `docs/DECISIONS.md` ADR-010)

`ApplicationTrafficMinutes` (schema version 2) stores minute aggregates of `MonitoringService` snapshots, following the same pattern as adapter `TrafficMinutes`. 4 new xUnit tests: aggregation across multiple saves into one minute bucket, an `Unavailable` snapshot writes nothing, retention/deletion behave the same as adapter history, JSON/CSV export round-trips. All passing (32/32 total).

Also verified against a real elevated service run: launched the app fresh against a clean database with the service running and live traffic present, then queried the resulting `.db` file directly (a throwaway `Microsoft.Data.Sqlite` reader, not the app). Confirmed both migrations applied in one pass (`SchemaMigrations` rows for version 1 and 2, same `AppliedUtc`, as expected for a fresh database) and real aggregated rows, including one process (PID 27684) with `Windows=2` — direct proof that two separate 5-second service windows were correctly summed into a single minute bucket rather than overwritten. Not yet covered: long-duration/high-row-count behavior.

## Algorithmic anomaly detection (2026-09-17, see `docs/DECISIONS.md` ADR-011)

29 new unit tests (61 total, all passing): `BaselineCalculator` (Welford mean/stddev, outlier trimming, partial-coverage rate math), `AnomalyEvaluator` (all section 16.4-required scenarios — normal, sustained high download/upload, new application, insufficient history, trusted, below-bandwidth-floor, no assertive malice claim in explanation text), `AnomalyTracker` (brief spike never fires, sustained spike fires once, cooldown, sustained-timer reset on dip, independent per-direction/per-process tracking), `QuietHours` (overnight wrap, same-day range, always-quiet), plus storage tests (multi-PID minute-series grouping — caught and fixed a real double-counting bug, see below — anomaly persistence/ordering, retention/deletion parity, sensitivity validation bounds).

**Bug caught by the storage test, not by inspection**: `GetApplicationMinuteSeriesAsync` originally summed `Windows` across all PIDs sharing a process name to compute covered seconds. Two concurrent PIDs each contributing one 5-second window in the same minute produced `SUM(Windows)*5 = 10s`, but the actual wall-clock coverage was still only 5s (the two windows overlap in time, they don't add up serially) — this would have silently understated every multi-process baseline's rate. Fixed to `MAX(Windows)*5` (a conservative approximation of actual elapsed coverage), verified by the test that exposed it.

Verified against a real elevated `MonitoringService` run (fresh database, live traffic including curl-generated downloads): the ingestion pipeline processed several live snapshots without any warning/error in the application log, correctly wrote 0 rows to `AnomalyEvents` (no baseline exists yet for a fresh database — exactly the intended safe "new application" behavior) while `ApplicationTrafficMinutes` continued accumulating normally (15 rows). Schema migrated 1→2→3 correctly in one pass on a fresh database, confirmed by direct inspection.

Not done: real false-positive-rate validation against live, varied, multi-day usage (needs time this session cannot provide — tracked in `docs/VALIDATION_PLAN.md`); an actual fired-alert visual/tray-notification check (requires either 7 days of real history or a way to seed history, neither exercised here); UI automation for the new Settings/Dashboard/trust-button controls.

## Process identity, untrust, snooze/dismiss, network health score (2026-09-18, see `docs/DECISIONS.md` ADR-012/013)

`EtwCollector` process-name resolution moved from a post-hoc `Process.GetProcessById` lookup (which lost the name of anything already exited) to kernel `ProcessStart`/`ProcessDCStart` events — live-validated elevated: 15 concurrent short-lived `curl.exe` processes correctly attributed by name, 0 events lost. Untrust added to the Settings trust list; snooze (1 hour, distinct from the automatic cooldown) and dismiss added to the Dashboard "Recent alerts" card, both view-only actions that never touch stored `AnomalyEvent` evidence. The network health score (a new, pure `HealthScoreCalculator` in Domain, 12 new unit tests) closes the last entirely-unbuilt Dashboard element — a weighted average over only currently-available factors, never a fabricated composite when data is missing. All four visually verified via `--smoke-test --smoke-page <Settings|Dashboard>` against seeded/real state; 14 new unit tests total across the four changes (75 total, all passing). One real bug caught by visual verification (not by a unit test): WLAN interface GUIDs come back brace-wrapped while `NetworkInterface.Id` doesn't, so the Wi-Fi signal health factor silently never matched a real Wi-Fi adapter until normalized.

## One-click speed testing (2026-09-18, ADR-014)

- `dotnet build NetworkIntelligence.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`: passed, zero warnings/errors.
- `dotnet test tests/NetworkIntelligence.Domain.Tests --no-build --no-restore -m:1 -nr:false`: **99/99 passed**. 24 added cases cover automatic/custom provider resolution, invalid endpoints with no network activity, exact request sequence/payload limits, monotonic progress, redirects/403/429/503 without retries, HTML/truncated response rejection, upload failure, cancellation before a run/during requests/during download bodies/during upload, deadline distinction, saved-provider persistence/reset, and old-settings compatibility. These HTTP tests use an injected handler, not public network traffic.
- One live bounded run against the documented Cloudflare endpoint via a throwaway console referencing the actual Infrastructure project completed at **2026-09-18 10:54:22 UTC**: 25,000,000 download bytes; 10,000,000 upload bytes; 77.39 Mbps down; 17.34 Mbps up; five HTTP latency samples averaging 169.63 ms; 9.07 ms successive-sample jitter; 8.71 seconds total. A separate zero-byte preflight returned HTTP 200 / application/octet-stream / zero payload. This verifies present endpoint compatibility, not throughput accuracy against an independent line-speed reference or future provider availability.
- WinUI `--smoke-test --smoke-page Performance` and `Settings` rendered successfully against isolated data directories. Screenshots inspected: warning, provider, Start/Cancel, metric labels, provider field, links and Save provider layout. The local ignored evidence is under `artifacts/speed-ui-performance/` and `artifacts/speed-ui-settings-final/`; database integrity was `ok`. No user's normal settings/history were changed.
- Native button interaction, completed-result rendering, keyboard/screen-reader behavior and real-network cancellation were not automated in this pass; cancellation/failure handling was exercised at the transport layer. Multi-provider discovery and adaptive line-capacity estimation are not implemented.

## Persisted speed-test history (2026-09-19, ADR-016)

- `dotnet build NetworkIntelligence.slnx`: passed, zero warnings/errors.
- `dotnet test tests/NetworkIntelligence.Domain.Tests`: **132/132 passed**. 3 added `StorageTests` cases cover newest-first ordering, the `GetSpeedTestsAsync` limit parameter, and retention/deletion parity with the other history tables (`AnomalyEvents`, `Diagnostics`, etc.).
- Seeded an isolated SQLite database with two real `SpeedTestRecord` rows through the same `SqliteHistoryStore` the app uses (a throwaway console project referencing `NetworkIntelligence.Infrastructure`, consistent with the project's established verification pattern — no `sqlite3` CLI is installed in this environment), then rendered `--smoke-test --smoke-page Performance --smoke-speed-history` against it. The "Recent speed tests" card correctly showed both seeded runs with formatted download/upload/latency/jitter/duration; database integrity was `ok`. Evidence: `artifacts/speed-history-ui/smoke-preview.png` (local, ignored).
- The `--smoke-speed-history` flag was added to the smoke-test harness (mirroring the existing `--smoke-report-details` flag for `ReportDailyBreakdown`) because WinUI's `Expander` still runs its expand animation even when `IsExpanded` is set programmatically before the render capture, so the flag applies it early enough to settle before the screenshot.
- Native button/keyboard/screen-reader interaction with the new card was not automated in this pass, consistent with the rest of `NetworkIntelligence.App` (no test project covers WinUI code-behind).

## Dedicated alert history page (2026-09-19, ADR-017)

- No Domain/Infrastructure code changed (UI-only, reuses the existing `AnomalyEvents` table and `GetAnomalyEventsAsync`), so `dotnet test tests/NetworkIntelligence.Domain.Tests`: **132/132 passed** (unchanged from the speed-test-history pass above), confirming no regression.
- Seeded an isolated SQLite database with three real `AnomalyEvent` rows (varying severity, direction and process name) through the same `SqliteHistoryStore` the app uses, then rendered `--smoke-test --smoke-page Alerts`. The page showed all three entries newest-first with correct timestamp/severity/process/rate-vs-baseline/explanation formatting, the search box, severity/direction filter dropdowns, and a working "Showing 3 of 3 stored alerts" status line. Database integrity was `ok`. Evidence: `artifacts/alert-history-ui/smoke-preview.png` (local, ignored).
- Filter interaction itself (typing in the search box, changing the dropdowns) was not exercised by the smoke-test capture, since it renders one static frame — the underlying filter logic mirrors the already-live `FilterApplications` client-side substring filter, not a new mechanism. Native keyboard/screen-reader behavior for the new page was not automated, consistent with the rest of `NetworkIntelligence.App`.

## Pid reuse within one collection window (2026-09-19, ADR-018)

- `dotnet build NetworkIntelligence.slnx`: passed, zero warnings/errors.
- `dotnet test tests/NetworkIntelligence.Domain.Tests`: **138/138 passed**. 6 new `ProcessTrafficAccumulatorTests` cases cover the core split-on-identity-change scenario (traffic accumulated, split detaches it and starts a fresh bucket, a subsequent flush contains only the fresh traffic — no double-counting), single/multi-pid accumulation, a no-op split on an empty pid, flush clearing state, and multiple handoffs for the same pid within one window.
- This specific scenario — the Windows kernel reusing a pid for an unrelated new process within one 5-second window — cannot be reliably forced live; unlike ADR-012's short-lived-process scenario (forced with concurrent `curl.exe` runs), pid reuse timing is entirely kernel-controlled and not something an external test can schedule. The unit tests above are therefore the primary and most rigorous verification available for this fix, by design (see ADR-018's "why this could only be validated by unit test" note).
- Not performed as part of this change: a live elevated re-run of `scripts/service-run-foreground.ps1` to confirm the service still runs and reports live per-app data normally end-to-end post-refactor (this would only confirm the wiring didn't regress normal operation, not the pid-reuse fix itself — this environment's shell is non-elevated, see the project's elevation-constraint note). Left to the user to run if wanted.

## Remaining acceptance work

Controlled known-byte-traffic comparison for concurrent processes, real/physical adapters, UDP against a byte-exact reference, and low-rate/long-duration traffic; adapter disambiguation for per-process events; ETW session CPU/memory overhead benchmark; Wi-Fi SSID/signal/channel/security; internet/gateway reachability; adapter switching/sleep; MonitoringService code signing and dedicated least-privileged service account; automated unauthorized-client access tests; real false-positive-rate anomaly validation against live multi-day usage; stable application identity beyond process-name grouping (ADR-012 and ADR-018 each narrowed this gap — restart name-loss and mid-window pid reuse are fixed — but full executable-path identity, distinguishing two different applications that share a process name, remains open); network health score weights/curves validated against real user-perceived quality; Windows 10 and clean Windows 11 installs; broader speed-provider accuracy/availability testing; UI automation/accessibility; long-duration/high-throughput benchmarks; signed release packaging.

The ETW mechanism, the elevated service that serves it, and the anomaly-detection pipeline built on top of it have all been validated manually (standalone probe runs, and live elevated-service runs wired through the actual WinUI App) but not through an automated test harness exercising the full stack end to end. Phase 0/1 are not fully complete and no production-readiness claim is made.

## Usage reports (2026-09-19, ADR-015)

- Solution build with `--no-restore -m:1 -p:UseSharedCompilation=false`: passed with zero warnings/errors.
- Domain/Application/Infrastructure suite: **129/129 passed**, including 30 report cases covering calendar boundaries, missing data, storage aggregation, cancellation, export and deletion. Five added completion cases cover yearly presets (ordinary year, leap year, January 1), yearly storage boundaries and deletion of both report sources.
- WinUI `--smoke-test --smoke-page Usage --smoke-report-period ThisYear` against `artifacts/report-ui-yearly-final` completed; database integrity was `ok`. Visually inspected the yearly preset, January-to-current-date range, separate adapter selection, totals, approximate coverage and daily chart. Fixture data is synthetic and isolated from normal user data. Screenshot: `artifacts/report-ui-yearly-final/smoke-preview.png` (ignored local evidence).
- Native export-picker interaction, keyboard/screen-reader behavior and long-duration/high-volume report performance were not tested in this pass. CSV/JSON payloads are covered by automated tests. Existing unsigned portable package has not been rebuilt.
