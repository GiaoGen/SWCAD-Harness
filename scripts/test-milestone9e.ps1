[CmdletBinding()]
param([ValidateSet('Pure','Diagnose','Linear','Rectangular')][string]$Mode = 'Pure', [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir required.' }
& $taskSdk build (Join-Path $taskRoot 'tests/CadHarness.PatternDirections.Tests/CadHarness.PatternDirections.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M9E build failed.' }
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -ne 'Pure' -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.PatternDirections.Tests/bin/Release/net8.0-windows/CadHarness.PatternDirections.Tests.dll') @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M9E runner failed with exit code $LASTEXITCODE." }
