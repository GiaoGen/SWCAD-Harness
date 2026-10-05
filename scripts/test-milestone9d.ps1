[CmdletBinding()]
param([ValidateSet('Prepare','Live','Matrix')][string]$Mode = 'Prepare', [string]$Case,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskProject = Join-Path $taskRoot 'tests/CadHarness.Generalization.Tests/CadHarness.Generalization.Tests.csproj'
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir is required for M9D evaluation.' }
& $taskSdk build $taskProject --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M9D build failed.' }
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Generalization.Tests/bin/Release/net8.0-windows/CadHarness.Generalization.Tests.dll'
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -eq 'Live') {
    if ([string]::IsNullOrWhiteSpace($Case)) { throw '-Case required; one independent Part per invocation, no automatic repeats.' }
    $taskArgs += $Case
    if (-not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
}
& $taskSdk $taskAssembly @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M9D evaluation failed with exit code $LASTEXITCODE." }
