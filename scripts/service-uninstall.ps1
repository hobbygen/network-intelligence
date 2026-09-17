<#
Removes the optional NetworkIntelligence.MonitoringService Windows Service. The main app is unaffected;
it degrades to Tier 1/2 telemetry (no per-application byte accounting) exactly as it does when the
service was never installed. Must be run elevated.
#>
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Error 'Run this script as Administrator.'
    exit 1
}

$serviceName = 'NetworkIntelligenceMonitoring'
if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    Write-Output "$serviceName is not installed."
    exit 0
}

Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
sc.exe delete $serviceName | Out-Null
Write-Output "$serviceName removed. Logs under %ProgramData%\NetworkIntelligence\Service were left in place; delete that folder manually if you want them gone too."
