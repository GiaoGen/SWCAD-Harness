[CmdletBinding()]
param(
    [ValidateSet('Pure','Native')][string]$Mode='Pure',
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z][a-zA-Z0-9_]*$')][string]$RunId,
    [ValidateSet('cross','full')][string]$Model='full',
    [ValidateSet('audit','candidate_bind_probe','candidate_fillet','candidate_create','candidate_edit','candidate_cold_edit','candidate_fault','create','edit','cold_edit','fault','recover','read')][string]$Phase='read',
    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9_]*$')][string]$PackageRun,
    [switch]$NoBuild,
    [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/m20-preparation/T3/bin'
if (-not $NoBuild) {
    & $sdk build (Join-Path $root 'tests/CadHarness.FeatureConstruction.Tests/CadHarness.FeatureConstruction.Tests.csproj') -c Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
    if ($LASTEXITCODE -ne 0) { throw 'T3 build failed.' }
}
$runner=Join-Path $bin 'CadHarness.FeatureConstruction.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --t3-pure $RunId }
elseif ($PackageRun) { & $sdk $runner $root --t3 $RunId $Model $Phase $PackageRun }
else { & $sdk $runner $root --t3 $RunId $Model $Phase }
if ($LASTEXITCODE -ne 0) { throw 'T3 failed. Preserve evidence; diagnose before a new slot.' }
