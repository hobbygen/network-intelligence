# Changelog

## Unreleased

- Validate the ETW kernel Network provider as the Tier-3 per-application telemetry mechanism: standard-user permission-denied path and an elevated capture (0 events lost, real per-process TCP/UDP byte totals, IPv4/IPv6, header-only) — see `docs/ETW_VALIDATION.md`. Not yet wired into the app; controlled accuracy comparison and the elevated service remain outstanding.

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
