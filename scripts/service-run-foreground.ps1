<#
Runs the MonitoringService as an ordinary elevated console process (not installed as a Windows Service) —
the fastest way to test the ETW collector and pipe server locally. Requires elevation (the process checks
its own token and exits with a clear message if not elevated, rather than failing inside the ETW session).
Stop with Ctrl+C. Logs still go to %ProgramData%\NetworkIntelligence\Service\service.log.

While this is running, verify the pipe end to end from an ordinary (non-elevated) shell with:
  dotnet run --no-build --project tools/NetworkIntelligence.TelemetryProbe -- --service-status
#>
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Error 'Run this script elevated (Run as Administrator) — the kernel network ETW provider requires it.'
    exit 1
}

& dotnet run --project (Join-Path $projectRoot 'src\NetworkIntelligence.MonitoringService')
