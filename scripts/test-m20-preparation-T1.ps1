[CmdletBinding()]
param(
    [ValidateSet('Pure','Native')][string]$Mode='Pure',
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z][a-zA-Z0-9_]*$')][string]$RunId,
    [ValidateSet('create','edit','read','create_torus','edit_torus','read_torus','fault_save','recover_save','fault_before','recover_before','fault_after','recover_after','fault_initial','audit')][string]$Phase='read',
    [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/m20-preparation/T1/bin'
& $sdk build (Join-Path $root 'tests/CadHarness.FeatureConstruction.Tests/CadHarness.FeatureConstruction.Tests.csproj') -c Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'T1 build failed.' }
$runner=Join-Path $bin 'CadHarness.FeatureConstruction.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --t1-pure $RunId }
else { & $sdk $runner $root --t1 $RunId $Phase }
if ($LASTEXITCODE -ne 0) { throw 'T1 failed. Preserve the namespace; diagnose before a targeted new attempt.' }
