# Network Intelligence

A native Windows desktop application for local network monitoring.

**0.2 early access is implemented and buildable. It is not the complete 1.0 product.** The owner approved the architecture on 2026-09-17. See [remaining requirements](docs/RELEASE_NOTES.md) before treating this as production-ready.

## Run the application

Use the self-contained package in `artifacts/NetworkIntelligence-0.2.0-win-x64.zip`: extract the entire ZIP and run `NetworkIntelligence.App.exe`. No IDE, administrator access or separate .NET runtime is required by that package. The portable build is unsigned.

## Implemented

- WinUI 3 sidebar with nine pages, system/light/dark appearance, native tray and pause/resume.
- Real adapter discovery, counter deltas, directional throughput, bounded LiveCharts graphs and session totals.
- Daily, weekly, monthly, yearly and custom usage reports with per-adapter totals, recorded coverage, daily charts, application breakdown and CSV/JSON export.
- Native Wi-Fi association details with denied/unavailable states.
- IPv4/IPv6 TCP/UDP ownership and process-instance metadata, always available. Live and stored (minute-aggregated) per-app **byte accounting** when the separate optional MonitoringService is installed and running — off by default, not accuracy-certified beyond a narrow loopback test, grouping is by PID+process name rather than a stable app identity (see below).
- Algorithmic per-application anomaly detection: learned baselines, independent download/upload evaluation, a learning period before any alert, sustained-duration + cooldown gating, trust exclusion, Dashboard alerts and a Settings sensitivity control — detection thresholds are provisional, not validated against a real false-positive rate (see below).
- SQLite migrations, minute aggregates, connection events, configurable retention (365 days default), integrity check and explicit history deletion.
- User-initiated DNS/ICMP diagnostics with cancellation; one-click bounded HTTPS speed test with automatic Cloudflare selection, progress, Cancel, HTTP latency/jitter, a saved provider override, and locally persisted history of completed runs.
- Privacy controls, connection notifications with quiet hours/cooldown, CSV and JSON exports.
- Layered Domain/Contracts/Application/Infrastructure/App projects, DI/options/logging, unit/integration tests and CI definition.

## Build

Install .NET SDK 10.0.400 or a newer patch in that feature band, then run from the repository root:

```powershell
dotnet restore -m:1
dotnet build --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test --no-build -m:1
dotnet run --no-build --project src/NetworkIntelligence.App
powershell -NoProfile -File scripts/package.ps1
```

NuGet access is required for dependencies and vulnerability audit. In an offline environment with all dependencies already cached, use `--source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false` for restore, or `scripts/package.ps1 -UsePackageCache`. That is a development workaround, **not a successful dependency audit**. CI retains audit.

The runtime-generated data directory defaults to `%LOCALAPPDATA%\NetworkIntelligence`. Tests can use `--data-dir <absolute-directory>`. An isolated application lifecycle/layout test runs with `--smoke-test --data-dir <test-directory>`: it samples live adapters, queries persisted history, checks integrity, renders a page to PNG and exits. It never starts public network tests. Add `--smoke-page <name>` (e.g. `Applications`) to render a page other than the Dashboard default.

The separate telemetry prototype remains runnable:

```powershell
dotnet run --no-build --project tools/NetworkIntelligence.TelemetryProbe -- --samples 5
dotnet run --no-build --project tools/NetworkIntelligence.TelemetryProbe -- --samples 3 --target localhost
```

## Optional elevated monitoring service

`NetworkIntelligence.MonitoringService` is an optional, explicitly installed collector for per-application byte accounting. It never runs by default, and the main app works fully without it — the Application Usage page's "Live per-application bandwidth" card simply shows "Unavailable" until the service is installed and running. It is validated manually end to end and wired into the App (`docs/DECISIONS.md` ADR-009), but not signed and not accuracy-certified beyond a narrow loopback test (see `docs/ETW_VALIDATION.md`, `docs/ETW_ACCURACY.md`).

```powershell
# Local test run (no install), elevated shell required — stop with Ctrl+C:
powershell -NoProfile -File scripts/service-run-foreground.ps1

# From an ordinary, unelevated shell while the above is running:
dotnet run --no-build --project tools/NetworkIntelligence.TelemetryProbe -- --service-status
dotnet run --no-build --project tools/NetworkIntelligence.TelemetryProbe -- --accuracy-test-mib 100

# Install/uninstall as a Windows Service (Manual start; does not start automatically), elevated:
powershell -NoProfile -File scripts/service-install.ps1
powershell -NoProfile -File scripts/service-uninstall.ps1
```

## Documentation

- [User and installation guide](docs/USER_GUIDE.md)
- [Release notes and remaining scope](docs/RELEASE_NOTES.md)
- [Architecture review and roadmap](docs/ARCHITECTURE_REVIEW.md)
- [Architecture decisions and approval](docs/DECISIONS.md)
- [Implemented database schema](docs/DATABASE_SCHEMA.md)
- [Security and privacy](docs/SECURITY_PRIVACY.md)
- [Validation plan](docs/VALIDATION_PLAN.md)
- [Test report](docs/TEST_REPORT.md)
- [ETW per-application telemetry proof-of-concept](docs/ETW_VALIDATION.md)
- [ETW controlled-traffic accuracy test](docs/ETW_ACCURACY.md)

The original requirements remain unchanged in `NETWORK_INTELLIGENCE.md`. Health scoring, signed installation and complete Windows 10 validation are still outstanding. Missing telemetry is displayed as unavailable.

The elevated kernel-ETW mechanism for per-application byte accounting is validated (`docs/ETW_VALIDATION.md`), has a working `MonitoringService` with secure named-pipe IPC (`docs/DECISIONS.md` ADR-007), passed a controlled-traffic accuracy test at 1 MiB/100 MiB on loopback with 0% delta (`docs/ETW_ACCURACY.md`, ADR-008), is wired into the App's Application Usage page with persisted history (ADR-009/010), and now drives algorithmic anomaly detection (ADR-011) — learned baselines, trust exclusion, Dashboard alerts. Broader accuracy testing (concurrent processes, real adapters, UDP), code signing, a dedicated service account, stable application identity, and real false-positive-rate validation of the anomaly thresholds are still required before any of this is a fully certified feature.


### Internet speed test

Open **Network performance → Start test**. The app selects Cloudflare automatically and network routing chooses the serving edge. Each run requests 25 MB download and 10 MB random upload plus protocol overhead; the provider sees your public IP. Use **Cancel** beside Start test to stop. Tests never run automatically. A completed run is saved locally and listed under **Recent speed tests** (last 10); cancelled, timed-out or failed runs are never saved.

To use your own compatible HTTPS endpoint, open **Settings → Speed test provider**, enter a base URL, and choose **Save provider**. It must implement `GET /__down?bytes=N` (including an empty response for zero bytes) and `POST /__up`. Clear the field and save to restore automatic selection. Redirects are not followed; rate-limited or failed tests are not automatically retried.

Results show bounded HTTPS throughput, average HTTP latency and successive-sample HTTP jitter, not maximum line speed or ICMP ping. Packet loss is unavailable. The system route, including VPN/proxy, is used regardless of the adapter selected for monitoring. Results stay in the current session. See ADR-014 in `docs/DECISIONS.md` for provider sources and measurement limits.
