# Network Intelligence 0.2 — early access

## Start

Extract the whole portable ZIP to a writable folder, then run `NetworkIntelligence.App.exe`. Keep the supporting DLLs and resource folders beside the executable. This x64 self-contained build does not require the IDE or a separately installed .NET runtime. It is unsigned early-access software, not a production-certified installer. Do not bypass organizational security policy to run it.

Monitoring begins when the app starts. No administrator request, Windows startup registration, service, driver, firewall change or external telemetry is performed. The window's close button keeps monitoring in the tray by default; use the tray menu's **Exit** command to stop. Disable close-to-tray in Settings to make closing exit. If the tray icon cannot be created, closing exits normally.

## Read the dashboard

Choose an adapter using the dropdown. The app initially prefers an up adapter with a configured gateway; that is a heuristic, not a declaration that all internet traffic uses it. Download/upload are measured bytes-per-second converted to decimal bits-per-second. Link speed is the adapter's reported capacity, not internet speed. Session totals include valid observed deltas only. First samples and reset/resume gaps are unavailable. The graph holds the last 90 samples (normally three minutes); missing values break the line.

Ethernet includes virtual interfaces. Do not sum virtual and physical totals, as they may represent the same traffic. Wi-Fi shows the driver's native connection information when Windows permits it. Signal is a quality percentage, not dBm. Windows location permissions may prevent association details; the app reports this without changing permissions. Channel, frequency and BSSID are not collected in this build.

## History and exports

Bandwidth and data supports 24-hour, 7-day, 30-day and 365-day ranges. Measured intervals for up interfaces are grouped into minute buckets by sample end time. Coverage hours show the amount of actual observed time, not the full requested range. Pause, shutdown and collection gaps are not counted as zero. Connection history contains the latest 200 observed transitions; initial adapter discovery is not a transition.

The Export button saves CSV or JSON aggregates for the selected historical range. On Diagnostics, Export saves the most recent diagnostic result as JSON. Exports include measurement source and units. Address privacy controls redact the diagnostic target in exported/stored results. Settings intentionally retain the diagnostic target you explicitly configure.

## Diagnostics and transfer testing

Choose a host name or IP in Diagnostics, then run the test. It resolves DNS and sends ten ICMP requests with bounded timeouts. Localhost is useful for a basic smoke test. DNS timing may include an OS cache hit. ICMP nonresponses are target-specific and do not prove an internet outage; local probe errors make loss unavailable. Jitter uses consecutive successful responses. Cancel stops ongoing probes.

The Network performance page can explicitly run an HTTPS transfer test. The default provider is Cloudflare's documented speed endpoints; its network chooses the serving edge. Compatible custom HTTPS base endpoints can be entered for the current session. Each test downloads up to 25 MB and uploads up to 10 MB of generated random data, plus protocol overhead. Confirm the endpoint and data use before starting. The provider sees the public IP, as with any internet request. No application/user files or result-logging requests are uploaded. Overall timeout is 60 seconds. The Diagnostics cancel button also cancels a transfer test.

These rates describe individual HTTP transfers including request/server overhead; they are not certified maximum internet bandwidth. The upload calculation uses the submitted request-body size after a successful HTTP response, not independently verified server byte accounting. HTTP response time is not ICMP latency. Speed jitter/loss is unavailable. Results are currently shown only for the session and are not retained. Public-provider behavior has not been live-validated in this build.

## Applications and alerts

Application usage always shows IPv4/IPv6 TCP/UDP socket ownership, PID and process start time where accessible. Socket counts do not measure bytes or bandwidth. Names can be hidden in Settings. Process exits/access restrictions have explicit states. No application history is stored.

Live per-application download/upload rates, and a stored per-application history below them, are also shown, but only when the separate, optional `NetworkIntelligence.MonitoringService` is installed and running (see the README) — it is off by default, requires an administrator to install it, and the app works fully without it. When the service isn't running, those sections of the page simply read "Unavailable" / show no history. Their numbers have only been validated against a controlled, single-process, loopback test (`docs/ETW_ACCURACY.md`); treat them as indicative, not certified for accuracy in general use yet. Each live application card has a "Trust this app" button, which excludes it from anomaly detection.

Application anomaly detection compares each application's current download/upload rate against a locally learned baseline (Settings > "Application anomaly detection"). A new application is never flagged — it needs at least 7 days and 60 samples of history before any alert is possible, and the deviation must persist for at least 30 seconds before an alert fires, with a 15-minute cooldown per app/direction after that. Sensitivity is adjustable in Settings. Detected anomalies appear in the Dashboard's "Recent alerts" card and describe the rate, baseline, and how far it deviated — bandwidth only, never a claim that an application is malware or otherwise malicious. These thresholds are provisional defaults, not validated against real long-running usage; expect them to need tuning.

Connection alerts use Windows native tray notifications for observed adapter disconnects, and anomaly alerts use the same native notifications when they fire. Settings include quiet hours and alert enablement for both; cooldown is five minutes per adapter for connection alerts and fifteen minutes per app/direction for anomaly alerts. Equal start/end quiet hours mean all-day quiet. Quiet hours suppress the notification only — an anomaly is still recorded and visible in "Recent alerts" when quiet hours are active. Notification delivery also depends on Windows notification settings.

## Privacy, data and retention

Data is stored in `%LOCALAPPDATA%\NetworkIntelligence\network-intelligence.db`. Retention defaults to 365 days and accepts 1–3650 days. Background cleanup runs hourly (or sooner when catching up), removing bounded batches. Minute traffic buckets, adapter names/IDs/types and connection transitions are stored. IP/MAC/BSSID/SSID/path/remote destination metadata and packet payloads are not stored. Opt-in address/SSID display is live-only; process names are also live-only. No external AI is used.

Delete stored history removes aggregates, connection events and diagnostics while retaining settings. Monitoring may immediately record new intervals. SQLite page storage is not a secure-erasure guarantee. For a backup, exit via the tray first and copy the database. Do not copy only the database while its WAL writer is active.

## Update and uninstall

Exit the running app, extract the new release into a new directory and launch it. The default data directory is shared across builds; back it up before upgrading. Newer schema versions are rejected by older builds instead of silently modified. To uninstall the portable build, exit and remove its extracted directory. Remove the local data directory separately only if you want to delete history/settings. No system registration requires cleanup.

## Troubleshooting

- No rate initially: allow two collection cycles. Check selected adapter and pause state.
- History unavailable: check disk space/write access. Use the database integrity button. Do not delete an existing database as a first response; exit and back it up.
- Wi-Fi details denied: review Windows permissions yourself; core adapter monitoring still works.
- No ICMP reply: the host/network may block ping. Check DNS and try a target you control.
- Transfer test failed: endpoint availability, proxy/TLS policy or provider response may be responsible. Incomplete results are discarded.
- App fails at startup: `startup-error.log` in the local data directory records startup exceptions. Review it before sharing; stack traces can include local paths.
- Windows 10: API target floor is build 17763, but only current Windows 11 build 26200 was exercised. Windows 10 LTSC compatibility still needs clean-machine validation.
