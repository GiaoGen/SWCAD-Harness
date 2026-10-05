[CmdletBinding()]
param([ValidateSet('Pure','Prepare','Live','Summary')][string]$Mode = 'Pure', [string]$Run,
    [ValidatePattern('^milestone10(?:-[a-z0-9]+)*$')][string]$EvidenceName = 'milestone10',
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
if ([string]::IsNullOrWhiteSpace($InteropDir)) { throw 'Explicit -InteropDir required.' }
if ($Mode -eq 'Live' -and [string]::IsNullOrWhiteSpace($Run)) { throw 'Live requires a run ID from the frozen manifest schedule.' }
& $taskSdk build (Join-Path $taskRoot 'tests/CadHarness.Benchmark.Tests/CadHarness.Benchmark.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M10 build failed.' }
$taskArgs = @($taskRoot, "--$($Mode.ToLowerInvariant())")
if ($Mode -eq 'Live') {
    $taskArgs += $Run
    if (-not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskArgs += $PartTemplate }
}
$taskArgs += "--evidence-name=$EvidenceName"
& $taskSdk (Join-Path $taskRoot 'tests/CadHarness.Benchmark.Tests/bin/Release/net8.0-windows/CadHarness.Benchmark.Tests.dll') @taskArgs
if ($LASTEXITCODE -ne 0) { throw "M10 runner failed with exit code $LASTEXITCODE. Do not retry consumed native runs." }
