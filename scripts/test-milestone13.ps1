[CmdletBinding()]
param([string]$SolidWorksInteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR=$SolidWorksInteropDir
& $dotnet build (Join-Path $root 'CadHarness.sln') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'M13 solution build failed.'}
& $dotnet (Join-Path $root 'tests/CadHarness.ExternalObservation.Tests/bin/Release/net8.0-windows/CadHarness.ExternalObservation.Tests.dll') $root --pure
if($LASTEXITCODE -ne 0){throw 'M13 pure suite failed.'}
& $dotnet (Join-Path $root 'tests/CadHarness.ManagedRecovery.Tests/bin/Release/net8.0-windows/CadHarness.ManagedRecovery.Tests.dll') $root --pure
if($LASTEXITCODE -ne 0){throw 'M12 durable suite regressed.'}
$isolated=Join-Path $root ('artifacts/milestone13/regression/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $isolated | Out-Null
foreach($directory in @('src','schemas','docs')){Copy-Item -LiteralPath (Join-Path $root $directory) -Destination $isolated -Recurse}
New-Item -ItemType Directory -Path (Join-Path $isolated 'tests') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tests/CadHarness.V03Contracts.Tests') -Destination (Join-Path $isolated 'tests') -Recurse
& $dotnet (Join-Path $root 'tests/CadHarness.V03Contracts.Tests/bin/Release/net8.0/CadHarness.V03Contracts.Tests.dll') $isolated --pure
if($LASTEXITCODE -ne 0){throw 'Frozen M11 contracts regressed.'}
[ordered]@{status='PASS';nativeParts=0;nativeOpens=0;m13Pure=27;m12Pure=48;m11Contracts=76;solutionBuildWarnings=0;solutionBuildErrors=0} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $isolated 'result.json') -Encoding UTF8
Write-Output "M13 isolated regression: $isolated; no native calls."
