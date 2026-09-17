<#
Installs the optional, elevated NetworkIntelligence.MonitoringService as a Windows Service.
Must be run elevated (right-click PowerShell -> Run as Administrator, or Start-Process -Verb RunAs).
The service is registered with StartupType Manual: installing it does not start it, and it never
starts automatically with Windows. The main app runs fully without this service; installing it only
unlocks per-application byte accounting (see docs/ETW_VALIDATION.md for what that mechanism currently
does and does not validate).
#>
[CmdletBinding()]
param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\NetworkIntelligence.MonitoringService\bin\Release\net10.0-windows\win-x64\publish\NetworkIntelligence.MonitoringService.exe')
)
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Error 'Run this script as Administrator.'
    exit 1
}

$ExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found at $ExePath. Publish it first: dotnet publish -c Release -r win-x64 src/NetworkIntelligence.MonitoringService"
    exit 1
}

$serviceName = 'NetworkIntelligenceMonitoring'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Error "$serviceName is already installed. Run service-uninstall.ps1 first to reinstall."
    exit 1
}

New-Service -Name $serviceName -BinaryPathName "`"$ExePath`"" `
    -DisplayName 'Network Intelligence Monitoring' `
    -Description 'Optional per-application network telemetry collector for Network Intelligence. Reads TCP/UDP send/receive byte counts by process via the kernel network ETW provider; never inspects packet payloads. The desktop app works fully without this service.' `
    -StartupType Manual | Out-Null

Write-Output "$serviceName installed (Manual start; does not run automatically). Start it with: Start-Service $serviceName"
Write-Output 'Logs: %ProgramData%\NetworkIntelligence\Service\service.log'
