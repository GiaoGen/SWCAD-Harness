[CmdletBinding()]
param([ValidateSet('Pure','Prepare','Qualify','Smoke','Summary')][string]$Mode = 'Pure', [string]$Run,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir required.' }
& $taskSdk build (Join-Path $taskRoot 'tests/CadHarness.StepwiseStateContract.Tests/CadHarness.StepwiseStateContract.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M10C build failed.' }
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -eq 'Smoke') {
    if ([string]::IsNullOrWhiteSpace($Run)) { throw 'Smoke requires a frozen run ID.' }
    $taskArgs += $Run
    if (-not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
}
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.StepwiseStateContract.Tests/bin/Release/net8.0-windows/CadHarness.StepwiseStateContract.Tests.dll') @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M10C stopped with exit code $LASTEXITCODE. Do not retry consumed qualification/smoke evidence or run benchmark." }
