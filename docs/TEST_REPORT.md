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

## Remaining acceptance work

Controlled known-byte-traffic comparison for concurrent processes, real/physical adapters, UDP against a byte-exact reference, and low-rate/long-duration traffic; adapter disambiguation for per-process events; process-restart attribution; ETW session CPU/memory overhead benchmark; Wi-Fi SSID/signal/channel/security; internet/gateway reachability; adapter switching/sleep; MonitoringService code signing, dedicated least-privileged service account, automated unauthorized-client access tests, and a real client wired into the main App/Infrastructure; Windows 10 and clean Windows 11 installs; storage/migrations/retention; speed provider integration; UI behavior/accessibility; anomaly detection; long-duration/high-throughput benchmarks; signed release packaging.

The Application Usage feature is not implemented in the app; the ETW mechanism and the elevated service that serves it have only been validated manually and standalone, never wired into the WinUI App. Phase 0/1 are not fully complete and no production-readiness claim is made.
