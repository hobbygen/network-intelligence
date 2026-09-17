# Telemetry proof-of-concept and testing strategy

## Current probe

Sample each adapter independently using monotonic elapsed time. First sample, identity change, decreasing counter and gaps over ten seconds yield unavailable rates. Do not sum virtual and physical interfaces. Read IPv4 TCP owners without resolving private process metadata. Enumerate WLAN interfaces without requesting location access. DNS/ICMP are opt-in; test localhost first, then explicitly selected gateway and internet targets. Failed ICMP is not proof of internet outage.

## Required next experiments

1. Blank WinUI application: restore exact stable packages; build Debug/Release x64, launch, close, package and run outside IDE. Test Windows 10 LTSC and Windows 11 clean VMs.
2. Ethernet/Wi-Fi: compare known sustained transfer with interface counters; unplug, disable/enable, change Wi-Fi, VPN, multiple adapters, sleep/resume. Record driver and OS privately; export redacted summaries. Check WLAN denied/absent/service-stopped behavior.
3. IP Helper: IPv4/IPv6 TCP/UDP owners and process exits/restarts. Validate inaccessible processes without substituting PID-based long-term identity.
4. ETW: review event schemas first; collect only metadata. Attempt standard-user session and record permission result. Elevated experiment requires explicit consent. Capture TCP/UDP send/receive, v4/v6, process lifecycle and event-loss counters. Never parse packet content. **Done — see `docs/ETW_VALIDATION.md`.** Standard user: permission denied, no session attempted. Elevated: kernel Network provider session via TraceEvent, 0 events lost, real per-process TCP/UDP send/receive bytes across 15 processes, IPv4/IPv6, header fields only. Still open: controlled known-byte comparison (#5), overhead benchmark (#6), process-restart attribution, adapter disambiguation, Windows 10.
5. Controlled bytes: local sender and receiver independently record payload totals. Test 1 MiB and 100 MiB upload/download, concurrent processes, restart, short-lived connections, loopback/LAN/remote, virtual adapters, low/high rate and idle. Compare payload, ETW-layer bytes and adapter-layer bytes separately; protocol overhead is not necessarily error. Establish documented tolerances from measured semantics, not arbitrary exactness claims. Emit unattributed bytes separately. **Partially done — see `docs/ETW_ACCURACY.md`.** 1 MiB and 100 MiB loopback TCP, single process: 0% delta both directions, 0 events lost. Still open: concurrent processes, restart, short-lived connections, LAN/remote/real adapters, virtual adapters, low/high rate, idle, UDP against a byte-exact reference.
6. Overhead: warmup, five-minute idle, ten-minute steady/high traffic, one-hour soak and resume cycles. Record CPU seconds/elapsed/logical processors, working set, event loss, queue depth, database latency and UI frame timing. The current short probe does not validate these targets.

## Automated tests by layer

- Domain: rates/reset/identity/gaps, units, aggregation, quality propagation, baselines, learning period, directional anomalies, brief/sustained spikes, cooldown, quiet hours, trust and health coverage.
- Infrastructure: native buffer resizing/errors, collector cancellation, actual adapter discovery; SQLite migrations/rollback/integrity/disk-full/retention; export escaping and missing values; bounded provider success/failure/cancel.
- Application: bounded queues, slow storage, shutdown, pause/resume, service disconnect/recovery and unauthorized IPC.
- UI: navigation, keyboard/screen reader, dark/light/high contrast, chart unavailable states, tray lifecycle and notification activation.
- Release: clean install, update with existing database, uninstall, data preservation/deletion, certificate trust, Windows edition/build matrix and one-year dataset.

## Completion gates

No application-usage completion claim without published direction/protocol coverage, error distribution, event loss, restart handling and overhead. No desktop release without build, tests, clean-machine packaging and signed production artifacts. No false benchmark or simulated telemetry can stand in for hardware tests. Failures and untested cases remain explicit in TEST_REPORT.md.
