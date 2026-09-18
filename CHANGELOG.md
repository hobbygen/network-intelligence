# Changelog

## Unreleased

- Validate the ETW kernel Network provider as the Tier-3 per-application telemetry mechanism: standard-user permission-denied path and an elevated capture (0 events lost, real per-process TCP/UDP byte totals, IPv4/IPv6, header-only) — see `docs/ETW_VALIDATION.md`.
- Add the `NetworkIntelligence.MonitoringService` scaffold: a Generic Host worker (runnable as a plain elevated console process or installed as a Windows Service, `StartupType Manual`) hosting a continuous kernel-ETW collector and a named-pipe IPC server with a restrictive ACL, versioned/bounded message framing, and audit-logged client identity. Validated end to end manually. Add install/uninstall/local-run scripts and 6 wire-format unit tests.
- Add a controlled-traffic accuracy test (`--accuracy-test-mib`): sends an exact, independently-tallied 1 MiB / 100 MiB over a loopback socket and compares against the service's ETW attribution — 0% delta both directions, both sizes, 0 events lost (see `docs/ETW_ACCURACY.md`). Narrow scope (loopback, single process).
- Wire the MonitoringService client into the app (`docs/DECISIONS.md` ADR-009): a new `IApplicationTrafficClient`/`MonitoringServiceClient` polled on its own independent 4-second timer, and a "Live per-application bandwidth (optional)" card on the Application Usage page that shows real data when the service is running and degrades to "Unavailable" (never fake data) otherwise. Verified visually via an extended `--smoke-test --smoke-page <name>` flag in both states.
- Persist per-application traffic as minute aggregates, database schema version 2 (`docs/DECISIONS.md` ADR-010): a new `ApplicationTrafficMinutes` table following the same aggregation pattern as adapter history, wired into the existing retention/deletion/export paths, with a "Stored per-application history" card and CSV/JSON export on the Application Usage page. 4 new tests (32 total).
- Add algorithmic anomaly detection, database schema version 3 (`docs/DECISIONS.md` ADR-011): `BaselineCalculator` (Welford mean/stddev with outlier trimming) and `AnomalyEvaluator` (pure, per-direction) in Domain; `AnomalyTracker` (sustained-duration + cooldown state machine) and `AnomalyDetectionService` (orchestration, downstream of the live application-traffic poll) in Application. Learns per-process-name baselines from stored history, requires a 60-sample/7-day learning period before any alert, evaluates download/upload independently, excludes trusted apps, persists fired anomalies to a new `AnomalyEvents` table, and notifies via tray (gated by quiet hours/alerts-enabled, which never gates storage). Adds a "Trust this app" button, a Dashboard "Recent alerts" card, and a Settings sensitivity control. 29 new tests (61 total) covering every required scenario from requirements section 16.4; caught and fixed a real double-counting bug in multi-process baseline rate computation along the way. Detection thresholds are provisional, not validated against a real false-positive rate; stable application identity, alert snooze/history browsing and health scoring remain outstanding.
- Fix restart-attribution gap in per-application identity (`docs/DECISIONS.md` ADR-012): `EtwCollector` now resolves process names from kernel `ProcessStart`/`ProcessDCStart` rundown events, captured at/near a process's actual start, instead of a live `Process.GetProcessById` lookup at window-flush time (up to 5s later) — previously any process that had already exited by then fell back to a PID-embedded placeholder that differed on every restart, so restart-prone short-lived processes could never accumulate the baseline learning period under one stable name and stayed permanently exempt from anomaly detection. Stopped-process entries are retained 2 minutes past their stop event then pruned, bounding memory under process churn. Solution builds clean and the existing 61-test suite is unaffected (no test project covers `MonitoringService`). Live-validated elevated via `scripts/service-run-foreground.ps1`: kernel session started cleanly with the added Process provider, 0 events lost; 15 concurrent short-lived `curl.exe` processes (each ~1-2s) were correctly attributed by name over the pipe, not as PID-exited placeholders — the specific failure mode this fixes.
- Add untrust to the Settings trusted-applications list (`docs/DECISIONS.md` ADR-011 follow-up): the trust list was previously a view-only comma-joined line. It's now an `ItemsControl` with a per-row "Untrust" button that removes the app case-insensitively and saves through the same settings round-trip trust/save already use, taking effect on the next detection cycle without an app restart. Visually verified via `--smoke-test --smoke-page Settings` against a seeded trusted app.

## 0.2.0 early access — 2026-09-17

- Owner approved architecture and tiered telemetry approach.
- Build the native monitoring shell with nine pages, live adapter selection and bounded charts.
- Add real Ethernet/WLAN and IPv4/IPv6 TCP/UDP ownership collectors.
- Add SQLite migrations, minute aggregates, retention, connection history and integrity checks.
- Add privacy/appearance settings, native tray and connection notifications.
- Add cancellable DNS/ICMP diagnostics and user-confirmed bounded HTTPS transfer testing.
- Add CSV/JSON export and local bounded error logging.
- Expand automated coverage to storage, retention, exports, privacy and live local diagnostics.
- Produce an unsigned self-contained portable Windows x64 engineering package.
- Keep per-application byte accounting and anomaly functionality unavailable pending validation.

## Initial checkpoint — 2026-09-17

- Initialize solution, Git repository, CI and architecture documents.
- Build WinUI preview and real telemetry console prototype.
- Validate rate calculations with nine unit tests.
