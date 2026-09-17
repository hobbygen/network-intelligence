# Security and privacy — implemented 0.2 behavior

Main executable requests `asInvoker`; there is no self-elevation, service, driver or startup task. Native P/Invoke calls read IP Helper and WLAN metadata only. Native buffers are bounded and freed, retry counts are finite. Process start time prevents the UI from presenting a bare PID as a stable process instance, but long-term executable grouping is not implemented.

Passive startup does not send network probes or contact a backend. DNS/ICMP and HTTPS transfer testing require explicit user action. Transfer testing sends generated random data to the displayed endpoint, uses HTTPS without credentials/cookies, rejects redirects and limits payload size and time. It does not call provider result-logging endpoints. HTTP upload rates use the submitted body size, not independent receiver-side measurement; this limit is disclosed in the guide.

Live address and SSID visibility are opt-in. Process names can be hidden. No payload, password, browser content, executable path, remote destination, MAC or BSSID collection is implemented. The database retains adapter IDs/names/types, aggregate counters and state transitions. Diagnostic targets are redacted from stored/exported results unless address collection is enabled; explicitly configured target settings remain stored for convenience.

CSV exports quote cells and prefix formula-like strings to avoid spreadsheet formula execution. SQL values are parameterized. Retention validates bounds, deletes bounded batches and avoids UI-thread work. A newest-only UI snapshot slot prevents an unbounded dispatcher backlog; session totals are accumulated by the collector worker so dropping chart updates does not drop counted bytes.

Warning/error logs are local and rotate at about 1 MiB with one previous file. Current collection/storage logs use exception type rather than network addresses or raw payloads. Startup exception logs can include source paths; review before sharing. SQLite files use normal per-user filesystem protection, not application-level encryption. No stored credentials are required.

No elevated collector or IPC exists in this build. Future service work must pass the approved permission, authenticated-local-client, signing and unauthorized-access gates. Unsigned portable packaging, unavailable dependency auditing, untested public endpoint behavior, UI automation coverage and Windows 10 compatibility remain release limitations.
