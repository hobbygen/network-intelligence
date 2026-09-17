# Changelog

## Unreleased

- Validate the ETW kernel Network provider as the Tier-3 per-application telemetry mechanism: standard-user permission-denied path and an elevated capture (0 events lost, real per-process TCP/UDP byte totals, IPv4/IPv6, header-only) — see `docs/ETW_VALIDATION.md`.
- Add the `NetworkIntelligence.MonitoringService` scaffold: a Generic Host worker (runnable as a plain elevated console process or installed as a Windows Service, `StartupType Manual`) hosting a continuous kernel-ETW collector and a named-pipe IPC server with a restrictive ACL, versioned/bounded message framing, and audit-logged client identity. Validated end to end manually. Add install/uninstall/local-run scripts and 6 wire-format unit tests.
- Add a controlled-traffic accuracy test (`--accuracy-test-mib`): sends an exact, independently-tallied 1 MiB / 100 MiB over a loopback socket and compares against the service's ETW attribution — 0% delta both directions, both sizes, 0 events lost (see `docs/ETW_ACCURACY.md`). Narrow scope (loopback, single process).
- Wire the MonitoringService client into the app (`docs/DECISIONS.md` ADR-009): a new `IApplicationTrafficClient`/`MonitoringServiceClient` polled on its own independent 4-second timer, and a "Live per-application bandwidth (optional)" card on the Application Usage page that shows real data when the service is running and degrades to "Unavailable" (never fake data) otherwise. Verified visually via an extended `--smoke-test --smoke-page <name>` flag in both states. Broader accuracy testing, code signing, a dedicated service account, persisted per-app history and trust/exclusion controls remain outstanding.

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
