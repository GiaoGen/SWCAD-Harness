[CmdletBinding()]
param([ValidateSet('Prepare','Live','VerifyEvidence','Summary')][string]$Mode = 'Prepare', [ValidateSet('G1','G3','G5')][string]$Case,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir required.' }
if ($Mode -eq 'Live' -and [string]::IsNullOrWhiteSpace($Case)) { throw 'Live requires -Case G1/G3/G5.' }
& $taskSdk build (Join-Path $taskRoot 'tests/CadHarness.GeneralizationRevalidation.Tests/CadHarness.GeneralizationRevalidation.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M9F build failed.' }
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -eq 'Live') {
    $taskArgs += $Case
    if (-not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
}
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.GeneralizationRevalidation.Tests/bin/Release/net8.0-windows/CadHarness.GeneralizationRevalidation.Tests.dll') @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M9F runner failed with exit code $LASTEXITCODE." }
