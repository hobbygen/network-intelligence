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
