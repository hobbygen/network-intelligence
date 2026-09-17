# Architecture decision records

## ADR-001 — Native WinUI / .NET 10 (approved)

Keep the requested native stack. Installed SDK is 10.0.400. Windows App SDK 1.8.260804001 restored from cache and passed Debug/Release build and blank-window launch checks. Security audit and release support review remain pending. A WinForms or browser UI is not an implicit substitute. Proposed API floor: Windows 10 1809; supported editions and tested minimum remain separate release conditions.

## ADR-002 — Tiered, quality-bearing telemetry (approved)

Adapter counters are the first tier. Connection ownership is metadata, never a byte proxy. ETW must pass controlled traffic validation before per-application usage is enabled. Decreased counters or long gaps produce unavailable intervals, not guessed rollover values. Process identity includes creation time. Elevated service is optional and deferred until technically justified.

## ADR-003 — Local SQLite and bounded retention (approved)

Use explicit transactional migrations and a single background writer. Default one-year retained aggregates. Keep live high-frequency samples bounded. Export source/state alongside values. Privacy controls act before persistence and export, not just on UI rendering.

## ADR-004 — Phase gate (required by project brief)

Sections 4.1, 15 and 21 require architecture/telemetry validation. The owner explicitly approved the proposed architecture and proceeding on 2026-09-17. Core adapter monitoring has proceeded with validated APIs. Per-application byte accounting remains gated on accuracy evidence; owner approval does not substitute for measurement validation. No production-readiness claim.

## ADR-005 — Portable engineering distribution (accepted for early access)

Publish a self-contained x64 folder/ZIP for the first usable build. This can run outside the IDE without installing a privileged component. It is not a signed production installer; MSIX/signing and clean-machine deployment tests remain required for 1.0.

## ADR-006 — ETW kernel Network provider validated as the Tier-3 mechanism (accepted)

Standard-user and elevated experiments both ran (see `docs/ETW_VALIDATION.md`). Standard user: `TraceEventSession.IsElevated()` false, no session attempted, `PermissionDenied` — confirms Tier 2 exact byte accounting is unavailable without elevation. Elevated: a real-time session on `Microsoft-Windows-Kernel-Network` (`NetworkTCPIP` keyword), consumed via `Microsoft.Diagnostics.Tracing.TraceEvent`, ran 30 seconds with zero events lost and produced real per-process send/receive byte totals across 15 processes, IPv4 and IPv6, reading only event headers — never payload. This is the validated Tier-3 mechanism. It is not yet an accuracy-certified feature: controlled known-byte comparison, process-restart attribution, adapter disambiguation, and overhead benchmarking (validation-plan experiments 5–6) remain outstanding before the Application Usage page can claim byte-level accounting. The mechanism must ship only inside the optional, explicitly-installed, least-privileged `MonitoringService` (section 5.2) with authenticated local IPC to the unelevated main app — never by elevating the main app itself. `Microsoft.Diagnostics.Tracing.TraceEvent` is added only to the isolated `tools/NetworkIntelligence.TelemetryProbe` project pending this validation; it must be re-evaluated as a production dependency (signing, servicing, size) before it is referenced from the service.

## ADR-007 — MonitoringService scaffold: Generic Host worker, named pipe IPC (accepted)

`NetworkIntelligence.MonitoringService` is a .NET Generic Host worker (`Microsoft.Extensions.Hosting.WindowsServices`), so the same binary runs as an ordinary elevated console process for local testing (`scripts/service-run-foreground.ps1`) or as a registered Windows Service (`scripts/service-install.ps1`/`service-uninstall.ps1`, `StartupType Manual`, never installed or started automatically). `EtwCollector` promotes the validated ADR-006 mechanism into a continuous `BackgroundService`: a persistent kernel Network ETW session aggregated into fixed 5-second windows, exposed as the latest immutable `ServiceSnapshot`. `PipeServer` serves that snapshot over a named pipe (`NetworkIntelligence.MonitoringService.v1`) to unelevated local clients.

IPC design, per section 13.4: a `PipeSecurity` ACL (Administrators + `WellKnownSidType.InteractiveSid`, explicit deny on `AnonymousSid`/`NetworkSid`) is the actual authorization boundary, enforced by the OS before the server code runs — never a network-reachable pipe. Messages are a fixed, versioned envelope (`ServiceMessageType` enum: Hello/HelloAck/SnapshotRequest/SnapshotResponse/Error) with length-prefixed JSON framing and a hard size cap (`ServiceWireFormat`, `NetworkIntelligence.Contracts`) — never a free-form command string. Client identity is resolved via `GetImpersonationUserName()` after the first read (required by the Windows API) purely for audit logging; a failure to resolve it does not itself deny an already-ACL-authorized connection. All of this was validated end to end manually: an elevated service instance served real per-process byte data, zero events lost, to a client running as the interactively logged-on standard user, with the client's identity correctly logged.

Deferred to a later ADR: code signing, a dedicated least-privileged service account (the scaffold runs under whatever elevated account starts it), and wiring a client into the main App/Infrastructure (the scaffold's only client today is `tools/NetworkIntelligence.TelemetryProbe --service-status`, kept deliberately separate so the WinUI app's Tier 1/2 behavior is untouched by this work).

## ADR-008 — Controlled-traffic accuracy: exact match on loopback, still narrow (accepted)

`tools/NetworkIntelligence.TelemetryProbe --accuracy-test-mib N` sent an exact, independently-tallied N MiB over a loopback TCP socket and compared it against the MonitoringService's ETW-attributed total for the same PID (see `docs/ETW_ACCURACY.md`). Both required sizes (1 MiB, 100 MiB) matched byte-for-byte in both directions, zero events lost. This is a real, strong result but a narrow one: loopback only, single process, single short-lived high-throughput TCP connection. It raises confidence in the mechanism without being a general accuracy certification — concurrent processes, process restart, real/physical adapters, UDP-against-reference, and low-rate/long-duration behavior (the rest of validation-plan experiments 5–6) remain open before requirements section 7.4's gate is satisfied.

## ADR-009 — Wire the MonitoringService client into the App, gated and degrading (accepted)

Adds `IApplicationTrafficClient`/`MonitoringServiceClient` (`NetworkIntelligence.Infrastructure`) — a short-lived, reconnect-per-call named-pipe client, matching the probe's already-validated protocol usage. `NetworkIntelligence.Application.MonitoringService` polls it on its own independent 4-second `PeriodicTimer`, deliberately decoupled from the 2-second adapter-collection loop, so a slow or unreachable optional service can never delay adapter monitoring or history writes; failures surface as an `Unavailable` `ServiceSnapshot`, never an exception. The Application Usage page gained a "Live per-application bandwidth (optional)" card bound to this data, with the existing socket-ownership list kept unchanged below it — the InfoBar was reworded from "ETW validation is required" (no longer true) to explain the feature is off by default, requires manually installing the separate service, and is only accuracy-validated for a single loopback connection (ADR-008), not certified for general use.

Verified visually end to end via the app's `--smoke-test --smoke-page <name>` flag (extended in this change to pick which page renders, since Application Usage isn't the default landing page): with no service running, the card renders "Unavailable · Monitoring service not reachable" and the rest of the page works normally; with the service running elevated, the card renders real live per-process rates (e.g. curl.exe traffic at ~3.2 Mbps, `0 events lost`, correct window timestamps) sourced through the same pipe validated in ADR-007/ADR-008. This is UI wiring, not a claim of general accuracy or a promotion of Application Usage to "fully implemented" per requirements section 7.4 — persisted history, baselines, anomaly detection, and trust/exclusion controls (spec sections 10.2–10.6) remain unbuilt.
