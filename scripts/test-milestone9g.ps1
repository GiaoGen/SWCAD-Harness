[CmdletBinding()]
param([ValidateSet('Prepare','Live','Summary')][string]$Mode = 'Prepare', [ValidateSet('G2','HeldOut')][string]$Case,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir required.' }
if ($Mode -eq 'Live' -and [string]::IsNullOrWhiteSpace($Case)) { throw 'Live requires -Case G2/HeldOut.' }
& $taskSdk build (Join-Path $taskRoot 'tests/CadHarness.CurrentRuntimeCompatibility.Tests/CadHarness.CurrentRuntimeCompatibility.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M9G build failed.' }
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -eq 'Live') {
    $taskArgs += $Case
    if (-not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
}
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.CurrentRuntimeCompatibility.Tests/bin/Release/net8.0-windows/CadHarness.CurrentRuntimeCompatibility.Tests.dll') @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M9G runner failed with exit code $LASTEXITCODE." }
