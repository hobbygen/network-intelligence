param([switch]$UsePackageCache)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'src\NetworkIntelligence.App\NetworkIntelligence.App.csproj'
$output = Join-Path $projectRoot 'artifacts\NetworkIntelligence-0.2.0-win-x64'
$restoreArguments = @('restore', $project, '-m:1', '-r', 'win-x64', '-p:SelfContained=true')
if ($UsePackageCache) { $restoreArguments += @('--source', (Join-Path $env:USERPROFILE '.nuget\packages'), '-p:NuGetAudit=false') }
& dotnet @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -m:1 -p:UseSharedCompilation=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $output 'USER_GUIDE.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\RELEASE_NOTES.md') -Destination (Join-Path $output 'RELEASE_NOTES.md')
$archive = Join-Path $projectRoot 'artifacts\NetworkIntelligence-0.2.0-win-x64.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
Write-Output $archive
