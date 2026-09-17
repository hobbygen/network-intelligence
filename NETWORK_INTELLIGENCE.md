# NETWORK INTELLIGENCE
## Enterprise Windows Network Monitoring & Intelligence
### Final AI IDE Development Prompt — v1.0.0

---

# 1. EXECUTIVE DIRECTIVE

You are an enterprise software engineering team operating inside an AI-powered IDE.

Your mission is to design, architect, implement, test, document, and package a production-quality Windows desktop application named **Network Intelligence**.

The application monitors Ethernet, Wi-Fi, network adapters, internet performance, bandwidth, connection reliability, diagnostics, and per-application network activity.

Its differentiating capability is **Application Network Intelligence**: detecting unusually high network usage by individual applications, comparing activity against learned baselines, and notifying the user through configurable Windows desktop alerts.

Build a real application, not a visual prototype.

All core features must have real data sources, functioning implementations, error handling, tests, and documentation.

---

# 2. FINAL PRODUCT REQUIREMENTS

## 2.1 Platform

- Windows 10 and Windows 11.
- Windows 11 optimized user interface.
- 64-bit primary target.
- Confirm and document the minimum supported Windows 10 build.
- Native Windows desktop application.
- System tray operation.
- Optional elevated monitoring component.
- No mandatory cloud service.
- No mandatory external AI API.

## 2.2 User preferences

Apply the same design and engineering preferences as the Battery Intelligence App:

- Modern Fluent-inspired interface.
- Professional monitoring dashboard.
- Sidebar navigation with separate pages.
- Configurable data retention.
- Default retention: one year.
- Measured values whenever available.
- Clearly labeled estimation or unavailable states.
- CSV and JSON export.
- Professional PDF reporting deferred to a future release.
- Configurable alerts.
- Background monitoring.
- Algorithmic intelligence in version 1.0.
- Extensible future AI analysis layer.

## 2.3 Final decisions

| Requirement | Decision |
|---|---|
| Network monitoring | Ethernet and Wi-Fi, including multiple adapters |
| Application traffic | Best practical validated accuracy |
| Speed testing | Automatically select a reliable supported provider or endpoint |
| Network destinations | Local application and traffic monitoring; no packet payload collection |
| Anomaly sensitivity | Balanced |
| Administrator privileges | Optional, only when technically justified |
| Retention | One year by default |
| Diagnostics | Advanced |
| Alerts | Enabled and configurable |
| Export | CSV and JSON |
| Tray | Required |
| AI layer | Future-ready, not mandatory for core operation |

---

# 3. PRODUCT OBJECTIVES

The application must answer:

1. Is the computer connected to a network?
2. Is the internet reachable?
3. Which adapter is active?
4. Is Ethernet or Wi-Fi currently in use?
5. What are the current download and upload rates?
6. How much data has been transferred?
7. What is the current latency, jitter, and packet loss?
8. Which applications are consuming bandwidth?
9. Is an application using unusually high network traffic?
10. Has the network been unreliable over time?
11. What happened during a connection failure?
12. Can the user diagnose and export network problems?

---

# 4. ARCHITECTURAL PRINCIPLES

## 4.1 Architecture before implementation

Before building major features:

- Produce the architecture.
- Validate Windows APIs.
- Identify technical limitations.
- Create architecture decision records.
- Build a telemetry proof of concept.
- Confirm the application traffic strategy.
- Confirm Windows 10 compatibility.
- Obtain approval from the project owner before proceeding.

## 4.2 No fabricated telemetry

Never display fake values.

Every metric must include:

- Measurement source.
- Timestamp.
- Unit.
- Availability.
- Confidence or quality where relevant.

Use explicit states:

- Measured.
- Estimated.
- Cached.
- Unavailable.
- Permission denied.
- Error.

Do not silently convert missing values into zero.

## 4.3 Privacy-first

The application must monitor traffic metadata, not network content.

Do not implement:

- Packet payload capture.
- Password collection.
- Browser content inspection.
- Credential collection.
- Undisclosed remote telemetry.
- Automatic firewall modifications.
- Automatic DNS changes.
- Malware claims based only on bandwidth.

---

# 5. TECHNOLOGY STACK

Use the following stack unless the architecture review identifies a documented compatibility issue.

## 5.1 Primary stack

- Language: C#.
- Runtime: modern supported .NET version compatible with Windows 10 and Windows 11.
- UI: WinUI 3 with Windows App SDK, subject to validated Windows 10 support.
- Architecture: MVVM.
- Dependency injection: Microsoft.Extensions.DependencyInjection.
- Configuration: strongly typed options.
- Logging: Microsoft.Extensions.Logging.
- Database: SQLite.
- Data access: EF Core or a lightweight, well-designed data access layer.
- Charts: maintained charting library compatible with the chosen UI framework.
- Unit tests: xUnit or NUnit.
- Integration tests: selected test framework.
- Packaging: MSIX and/or a documented installer strategy.
- CI/CD: automated build and test pipeline.

## 5.2 Optional monitoring service

A separate Windows service may be implemented if the application traffic proof of concept demonstrates that higher-fidelity accounting requires elevated access.

The service must be:

- Optional.
- Explicitly installed.
- Least-privileged.
- Independently testable.
- Signed for production distribution.
- Clearly documented.
- Removable without breaking the main application.
- Restricted to the required telemetry.
- Free of packet payload collection.

Do not implement a kernel driver in version 1.0 unless an architecture review demonstrates that it is essential, technically justified, and acceptable from a security and deployment perspective.

---

# 6. SYSTEM ARCHITECTURE

## 6.1 Component overview

Create the following logical components:

### NetworkIntelligence.App

Responsibilities:

- Windows application shell.
- Navigation.
- Views.
- ViewModels.
- Theme.
- System tray.
- User interaction.
- Notifications.

### NetworkIntelligence.Application

Responsibilities:

- Use cases.
- Application services.
- Monitoring orchestration.
- Alert coordination.
- Configuration.
- Export workflows.

### NetworkIntelligence.Domain

Responsibilities:

- Domain models.
- Measurement definitions.
- Adapter identity.
- Application identity.
- Baselines.
- Anomalies.
- Alert rules.
- Network health calculations.

### NetworkIntelligence.Infrastructure

Responsibilities:

- Windows API integrations.
- Network collectors.
- Database.
- Logging.
- Configuration persistence.
- Notifications.
- Export.

### NetworkIntelligence.Contracts

Responsibilities:

- Shared DTOs.
- Collector contracts.
- Service communication contracts.
- Versioned interfaces.

### Optional NetworkIntelligence.MonitoringService

Responsibilities:

- Enhanced per-application telemetry, if required.
- Restricted background collection.
- IPC with the main application.
- Service lifecycle.
- Permission-aware operation.

### Test projects

- Unit tests.
- Infrastructure integration tests.
- Database tests.
- Service tests.
- UI tests.
- Performance tests.

---

# 7. CRITICAL TECHNICAL RISK: PER-APPLICATION NETWORK ACCOUNTING

This is the highest-priority technical risk.

## 7.1 Problem statement

Windows interface counters can provide reliable aggregate adapter traffic, but they do not automatically provide exact per-process byte counts.

Do not assume that:

- Standard adapter counters identify individual applications.
- A process connection list provides byte counts.
- Windows performance counters always expose exact per-process network traffic.
- A user-mode API can account for every byte transmitted and received by every process.

Validate the actual Windows capabilities.

## 7.2 Required proof of concept

Before implementing the full application usage feature, build a telemetry prototype that investigates:

- Windows IP Helper APIs.
- Windows performance counters.
- Windows Filtering Platform, where appropriate.
- ETW networking providers.
- Process and connection APIs.
- Supported Windows networking telemetry.
- Optional elevated service architecture.

The proof of concept must answer:

1. Can traffic be attributed to individual processes?
2. Can upload and download bytes be measured?
3. Can traffic be attributed consistently across process restarts?
4. Can multiple network adapters be distinguished?
5. Can traffic be collected without packet payloads?
6. What privileges are required?
7. What telemetry is missing or unreliable?
8. What is the CPU and memory overhead?
9. Is the approach compatible with Windows 10 and Windows 11?
10. Can the results be validated against known traffic?

## 7.3 Tiered implementation strategy

### Tier 1 — Adapter-level telemetry

Must work without administrator privileges wherever possible.

Collect:

- Interface bytes received.
- Interface bytes transmitted.
- Packets.
- Errors.
- Discards.
- Link state.
- Adapter identity.

### Tier 2 — Per-application telemetry

Use the best validated user-mode telemetry available.

Expose measurement quality:

- High.
- Medium.
- Low.
- Unavailable.

Do not claim exact byte-level accounting unless validated.

### Tier 3 — Enhanced collector

If the proof of concept demonstrates a need for elevated access:

- Implement a separate optional service.
- Use explicit user consent.
- Restrict access to required telemetry.
- Secure IPC.
- Provide installation and removal.
- Test service failure and recovery.
- Provide a user-visible status indicator.

If exact accounting cannot be achieved reliably, deliver the best validated approximation and clearly disclose the limitation.

## 7.4 Acceptance requirement

The Application Usage page must not be marked fully implemented until its measurement accuracy, limitations, and validation results are documented.

---

# 8. NETWORK DATA COLLECTION

Implement independent collectors.

## 8.1 AdapterCollector

Collect:

- Adapter name.
- Description.
- Interface index.
- Status.
- MAC address, subject to privacy settings.
- IPv4.
- IPv6.
- Gateway.
- DNS.
- DHCP.
- Interface type.
- Link speed where available.
- Manufacturer and driver information where available.

## 8.2 EthernetCollector

Collect:

- Ethernet state.
- Physical link status.
- Negotiated link speed.
- Duplex information where available.
- Traffic counters.
- Errors.
- Discards.
- Connection duration.
- Adapter events.

## 8.3 WifiCollector

Collect where available:

- SSID.
- BSSID, subject to privacy settings.
- Signal strength.
- Signal quality.
- Channel.
- Frequency.
- Frequency band.
- Receive link speed.
- Transmit link speed.
- Authentication and security type.
- Wi-Fi connection events.

## 8.4 InterfaceTrafficCollector

Collect:

- Bytes received.
- Bytes transmitted.
- Packets.
- Errors.
- Discards.
- Current rate.
- Peak rate.
- Session totals.

Handle:

- Counter reset.
- Counter rollover.
- Adapter replacement.
- Sleep and resume.
- Windows restart.
- Adapter disable/enable.
- Missing counters.

## 8.5 ConnectivityCollector

Test independently:

1. Adapter availability.
2. IP configuration.
3. Default gateway.
4. DNS resolution.
5. Internet connectivity.

## 8.6 PerformanceCollector

Collect:

- Ping.
- Minimum latency.
- Average latency.
- Maximum latency.
- Jitter.
- Packet loss.
- DNS response time.
- Timeouts.

Use configurable targets.

---

# 9. INTERNET SPEED TESTING

Implement an automatic provider/endpoint selection strategy.

## 9.1 Requirements

- Use a reliable supported provider or endpoint.
- Make the provider configurable.
- Prefer HTTPS where applicable.
- Provide an explicit user-initiated test.
- Automatic testing disabled by default.
- Display bandwidth consumption warning.
- Display test endpoint.
- Display test timestamp.
- Display test duration.
- Display download speed.
- Display upload speed.
- Display latency.
- Display jitter and packet loss where available.
- Handle provider failure gracefully.

## 9.2 Important distinction

Clearly separate:

- Adapter traffic speed.
- Internet speed test result.
- Link speed.
- Historical average throughput.

These are different measurements.

Do not run frequent automatic tests that consume significant bandwidth.

---

# 10. APPLICATION NETWORK INTELLIGENCE

## 10.1 Application identity

Implement stable application identity resolution.

Support:

- Executable name.
- Process ID.
- Executable path where permitted.
- Process start time.
- Process restarts.
- Windows services.
- System processes.
- Multiple processes belonging to one application.
- Missing process metadata.
- Permission restrictions.

Document grouping behavior.

## 10.2 Application usage page

Display:

- Application name.
- Executable name.
- Download bytes.
- Upload bytes.
- Total bytes.
- Current download rate.
- Current upload rate.
- Peak rate.
- Observation time.
- Measurement source.
- Confidence.
- Anomaly status.

Provide:

- Search.
- Sort.
- Filter.
- Application details.
- Historical usage.
- Export.
- Exclusion/trust controls.

## 10.3 Anomaly detection requirements

Implement balanced algorithmic detection.

Detect:

- Sustained high download activity.
- Sustained high upload activity.
- Unusual upload spikes.
- Unusual download spikes.
- Activity outside normal periods.
- Usage significantly above an application's learned baseline.

Do not trigger alerts for every short-lived spike.

## 10.4 Baseline learning

Maintain local per-application baselines.

Track:

- Rolling average.
- Robust deviation.
- Percentiles.
- Typical active periods.
- Daily usage.
- Sample count.
- Observation duration.
- Confidence.
- Model version.

New applications must have a learning period.

Do not label new applications anomalous solely because they consume bandwidth.

## 10.5 Detection pipeline

Implement:

1. Collect application traffic.
2. Validate sample quality.
3. Resolve application identity.
4. Retrieve baseline.
5. Check minimum observation history.
6. Check minimum bandwidth threshold.
7. Check minimum duration.
8. Compare with baseline.
9. Evaluate upload/download independently.
10. Apply sensitivity.
11. Apply trusted application exclusions.
12. Apply cooldown.
13. Calculate severity.
14. Generate explanation.
15. Store anomaly event.
16. Trigger notification if enabled.

## 10.6 Alert example

Title:

"Unusual network activity detected"

Body:

"Chrome is using 85 Mbps download bandwidth, significantly above its recent baseline. Review active downloads or streaming activity."

The alert must show:

- Application.
- Current rate.
- Baseline comparison.
- Direction.
- Duration.
- Severity.
- Timestamp.
- Explanation.
- Open details.
- Mark as expected/trusted.
- Snooze or dismiss.

Never state that the application is malicious based only on bandwidth.

---

# 11. DATABASE

Use SQLite with versioned migrations.

## 11.1 Suggested entities

- NetworkAdapters.
- AdapterSnapshots.
- TrafficSamples.
- PerformanceSamples.
- ApplicationIdentities.
- ApplicationTrafficSamples.
- ApplicationBaselines.
- AnomalyEvents.
- AlertEvents.
- ConnectionEvents.
- SpeedTestResults.
- DiagnosticResults.
- Settings.
- SchemaMigrations.

## 11.2 Requirements

- Indexed timestamps.
- Efficient aggregation.
- Transactional writes.
- Retention cleanup.
- Database integrity checks.
- Migration support.
- Backup/export.
- Graceful database error handling.
- One-year default retention.
- No unbounded in-memory history.

## 11.3 Retention

Default:

- One year.

Provide configurable retention settings.

Retention cleanup must be:

- Background.
- Observable.
- Safe.
- Testable.
- Non-blocking.

---

# 12. USER INTERFACE

## 12.1 Design

Use:

- Fluent-inspired Windows design.
- Professional monitoring dashboard.
- Sidebar navigation.
- Separate pages.
- Light and dark themes.
- System theme support.
- Clear charts.
- Accessible contrast.
- Consistent spacing.
- Responsive window layout.
- Clear unavailable states.
- Restrained animation.

Avoid visual clutter and misleading health indicators.

## 12.2 Navigation

- Dashboard.
- Ethernet.
- Wi-Fi.
- Network Performance.
- Bandwidth & Data Usage.
- Application Usage.
- Connection History.
- Diagnostics.
- Settings.

## 12.3 Dashboard

Display:

- Connection status.
- Active adapter.
- Internet availability.
- Download speed.
- Upload speed.
- Latency.
- Packet loss.
- Current session usage.
- Connection uptime.
- Traffic chart.
- Latency chart.
- Top applications.
- Recent alerts.
- Recent connection events.
- Network health score.

## 12.4 Network health score

Create a transparent score based on available measurements:

- Connectivity.
- Latency.
- Packet loss.
- Jitter.
- DNS response.
- Wi-Fi signal.
- Adapter errors.
- Recent disconnects.

Do not calculate a score when insufficient data exists.

Display contributing factors.

## 12.5 System tray

Support:

- Show application.
- Connection status.
- Quick network summary.
- Pause monitoring.
- Run diagnostics.
- Settings.
- Exit.

## 12.6 Alerts

Use native Windows notifications where supported.

Provide:

- Severity.
- Title.
- Explanation.
- Measured values.
- Context.
- Cooldown.
- Quiet hours.
- Alert history.
- User controls.

---

# 13. SECURITY AND PRIVACY

## 13.1 Privileges

- Main application runs without elevation where possible.
- Optional monitoring service only when justified.
- No hidden elevation.
- No driver installation without explicit approval.
- No firewall modification.
- No security-control bypass.

## 13.2 Privacy controls

Allow users to control collection of:

- SSIDs.
- BSSIDs.
- IP addresses.
- MAC addresses.
- Executable paths.
- Process names.
- Application history.
- Remote destination metadata, if later added.

The initial product must not collect packet payloads.

## 13.3 External services

- No required cloud backend.
- No external AI for core monitoring.
- Speed tests require user-visible disclosure.
- No telemetry sent to third parties without explicit consent.
- No stored credentials.
- No unnecessary sensitive logs.

## 13.4 IPC security

If an elevated service is implemented:

- Authenticate the client.
- Restrict access to authorized local users.
- Validate messages.
- Avoid arbitrary command execution.
- Use versioned contracts.
- Secure service installation.
- Secure service shutdown.
- Test unauthorized access.

---

# 14. PERFORMANCE

Measure:

- Startup time.
- Idle CPU.
- Monitoring CPU.
- Memory usage.
- Database write overhead.
- Chart rendering.
- Background service overhead.
- Shutdown time.
- Recovery time.

Requirements:

- No UI blocking.
- Bounded queues.
- Controlled polling.
- Efficient aggregation.
- Graceful shutdown.
- No memory leaks.
- Long-running stability.
- High-bandwidth testing.

Establish measured performance targets during implementation. Do not invent benchmark results.

---

# 15. IMPLEMENTATION PHASES

## Phase 0 — Project initialization

Deliver:

- Solution.
- Repository.
- Build configuration.
- Coding standards.
- Dependency policy.
- README.
- Initial CI.
- Project documentation.

Verify the blank application builds.

## Phase 1 — Architecture and telemetry proof of concept

Must be completed before major feature development.

Deliver:

- Windows API capability matrix.
- Ethernet prototype.
- Wi-Fi prototype.
- Adapter traffic prototype.
- Connectivity prototype.
- Performance prototype.
- Per-application traffic prototype.
- Privilege analysis.
- Performance measurements.
- Architecture decision records.
- Recommended implementation tier.

## Phase 2 — Application shell

Implement:

- Main window.
- Sidebar.
- Navigation.
- Theme.
- Settings framework.
- Tray.
- Shared controls.
- Loading/error states.

## Phase 3 — Core monitoring

Implement:

- Adapter discovery.
- Ethernet.
- Wi-Fi.
- Traffic.
- IP configuration.
- Gateway.
- DNS.
- Background collection.
- SQLite persistence.

## Phase 4 — Performance and diagnostics

Implement:

- Ping.
- Latency.
- Jitter.
- Packet loss.
- DNS tests.
- Connectivity tests.
- Speed testing.
- Diagnostics.

## Phase 5 — Analytics

Implement:

- Live charts.
- Historical aggregation.
- Session statistics.
- Daily/weekly/monthly/yearly reports.
- Adapter comparison.
- CSV export.
- JSON export.

## Phase 6 — Application monitoring

Implement the validated telemetry approach.

Do not claim exact per-process accounting unless the proof of concept validates it.

## Phase 7 — Anomaly detection

Implement:

- Baselines.
- Learning period.
- Statistical detection.
- Sensitivity.
- Minimum duration.
- Cooldown.
- Exclusions.
- Severity.
- Explanations.
- Notifications.
- Alert history.

## Phase 8 — Polish and privacy

Implement:

- Full settings.
- Retention.
- Data deletion.
- Privacy controls.
- Accessibility.
- Localization-ready strings.
- Error handling.
- Performance optimization.

## Phase 9 — Testing and hardening

Run all test categories.

## Phase 10 — Release

Deliver the application, package, documentation, and test report.

---

# 16. TESTING

## 16.1 Unit tests

Test:

- Rate calculations.
- Unit conversion.
- Counter rollover.
- Aggregation.
- Retention.
- Baselines.
- Anomaly detection.
- Severity.
- Health scoring.
- Cooldown.
- Settings.

## 16.2 Integration tests

Test:

- Adapter discovery.
- Ethernet.
- Wi-Fi.
- Database.
- Migrations.
- Background monitoring.
- Notifications.
- Export.
- Speed tests.
- Optional service.

## 16.3 Application traffic validation

Use controlled test traffic.

Validate:

- Known download.
- Known upload.
- Multiple applications.
- Multiple adapters.
- Process restart.
- Service restart.
- Permission denial.
- High traffic.
- Low traffic.
- Idle periods.
- Counter consistency.

Compare collected data against an independently validated reference where practical.

Document accuracy, missing data, and limitations.

## 16.4 Anomaly testing

Test:

- Normal traffic.
- Brief spike.
- Sustained spike.
- High upload.
- High download.
- New application.
- Trusted application.
- Cooldown.
- Quiet hours.
- Baseline changes.
- Missing data.
- False-positive scenarios.

## 16.5 Acceptance scenarios

Test:

- Ethernet connection.
- Wi-Fi connection.
- Switching adapters.
- No internet.
- Gateway failure.
- DNS failure.
- High latency.
- Packet loss.
- Speed test failure.
- Multiple applications.
- Unusual application bandwidth.
- Windows restart.
- Sleep/resume.
- One year of history.
- Database migration.
- Clean installation.
- Upgrade.
- Uninstallation.

---

# 17. BUILD AND RELEASE

Provide complete instructions for:

- Installing prerequisites.
- Installing the .NET SDK.
- Restoring dependencies.
- Building Debug.
- Running locally.
- Running tests.
- Building Release.
- Packaging.
- Installing on Windows 10.
- Installing on Windows 11.
- Upgrading.
- Uninstalling.
- Exporting diagnostics.
- Collecting logs.

The application must run independently of the AI IDE after installation.

---

# 18. DOCUMENTATION

Maintain:

- Product Requirements Document.
- Architecture Document.
- Windows API Capability Matrix.
- Telemetry Proof-of-Concept Report.
- Per-Application Accuracy Report.
- Database Schema.
- Anomaly Detection Specification.
- Security and Privacy Document.
- UI/UX Specification.
- Test Plan.
- Test Report.
- Installation Guide.
- User Guide.
- Troubleshooting Guide.
- Release Checklist.
- Changelog.
- Architecture Decision Records.

---

# 19. AI IDE EXECUTION RULES

Before coding:

1. Inspect the repository.
2. Confirm project status.
3. Create a development plan.
4. Identify risks.
5. Validate APIs.
6. Build the telemetry proof of concept.
7. Present architecture decisions.

During coding:

- Implement incrementally.
- Compile frequently.
- Run tests.
- Fix errors immediately.
- Do not fabricate functionality.
- Do not silently change requirements.
- Maintain documentation.
- Keep the UI responsive.
- Avoid unnecessary dependencies.

When blocked:

- Explain the cause.
- Identify alternatives.
- Document limitations.
- Implement the safest practical fallback.
- Do not claim success without validation.

---

# 20. FINAL DELIVERABLES

Provide:

1. Complete source code.
2. Buildable solution.
3. Release package.
4. Database migrations.
5. Automated tests.
6. Architecture documentation.
7. Telemetry validation report.
8. Per-application measurement limitations.
9. Anomaly detection specification.
10. Installation guide.
11. User guide.
12. Troubleshooting guide.
13. Security and privacy documentation.
14. Test results.
15. Release notes.
16. Known limitations.
17. Future AI layer integration plan.

---

# 21. START COMMAND

Begin with the following deliverables:

1. Requirements summary.
2. Architecture proposal.
3. Module hierarchy.
4. Technology selection.
5. Windows API capability matrix.
6. Per-application traffic accounting feasibility report.
7. Telemetry proof-of-concept plan.
8. Database design.
9. UI/UX screen specification.
10. Implementation roadmap.
11. Testing strategy.
12. Risk register.
13. Essential clarification questions.

Do not begin full application implementation until the critical telemetry and architecture decisions have been validated.

Project name: Network Intelligence

Version: 1.0.0

Platform: Windows 10 and Windows 11

Primary objective: Reliable network monitoring, diagnostics, historical analytics, and explainable application-level network anomaly detection.