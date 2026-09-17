# 0.2.0 — early access

Adds the working WinUI monitoring shell, real adapter telemetry and charts, native WLAN details with availability states, IPv4/IPv6 TCP/UDP ownership, local SQLite minute history, connection events, retention, settings, native tray operation/connection notifications, explicit DNS/ICMP diagnostics, CSV/JSON exports and a manual bounded HTTPS transfer test.

This is an unsigned engineering build, not the complete 1.0 product. Important remaining requirements:

- Per-application byte accounting: the elevated kernel-ETW mechanism is validated (`docs/ETW_VALIDATION.md`), has a working `NetworkIntelligence.MonitoringService` scaffold (`docs/DECISIONS.md` ADR-007) — a Generic Host worker with a versioned, ACL-restricted named-pipe IPC, validated end to end — and a controlled-traffic accuracy test (`docs/ETW_ACCURACY.md`, ADR-008) that matched byte-for-byte at 1 MiB and 100 MiB on loopback. Still missing before this is a certified feature: accuracy testing beyond that narrow (loopback, single-process) case, code signing, a dedicated service account, and wiring an actual client into the main App — the service today is only reachable from the telemetry probe's `--service-status`/`--accuracy-test-mib` flags, not from the WinUI app.
- Learned application baselines, anomaly detection, trust controls, alert action/history workflows and transparent health scoring.
- Full Wi-Fi channel/frequency/BSSID, driver/duplex metadata and continuously sampled latency charts.
- Persisted speed-test history, multi-provider validation and public-endpoint integration testing.
- One-year data-volume tests, long-running CPU/memory/stability tests, Windows 10 clean-machine tests, full accessibility and interactive tray/notification checks.
- Signed MSIX/installer, automated install/upgrade/uninstall tests, dependency audit and release certification.

The build never substitutes connection counts for application bandwidth or fabricates missing metrics. There is no production-readiness claim.

Dependency restore in the development environment used an existing local NuGet cache with vulnerability auditing disabled for that restore command because the network feed was unreachable. CI keeps audit enabled. The self-contained build embeds the available .NET 10.0.11 runtime; servicing updates and a successful dependency audit are required before production distribution.

Implementation references: Microsoft Windows App SDK/WinUI documentation; Microsoft IP Helper and Native Wi-Fi APIs; LiveCharts2 WinUI documentation; Cloudflare's public speedtest repository for `/__down` and `/__up` endpoint conventions: https://github.com/cloudflare/speedtest . The app does not use Cloudflare's result-logging engine.
