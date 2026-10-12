[CmdletBinding()]
param(
    [ValidateSet('Pure','Native')][string]$Mode='Pure',
    [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z][a-zA-Z0-9_]*$')][string]$RunId,
    [ValidateSet('shaft','sleeve')][string]$Model='shaft',
    [ValidateSet('candidate_create','candidate_edit','candidate_cold_edit','candidate_reverse','candidate_fault','create','edit','cold_edit','fault','recover','read','legacy_read','audit')][string]$Phase='read',
    [ValidatePattern('^[a-zA-Z][a-zA-Z0-9_]*$')][string]$PackageRun,
    [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/m20-preparation/T2/bin'
& $sdk build (Join-Path $root 'tests/CadHarness.FeatureConstruction.Tests/CadHarness.FeatureConstruction.Tests.csproj') -c Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'T2 build failed.' }
$runner=Join-Path $bin 'CadHarness.FeatureConstruction.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --t2-pure $RunId }
elseif ($PackageRun) { & $sdk $runner $root --t2 $RunId $Model $Phase $PackageRun }
else { & $sdk $runner $root --t2 $RunId $Model $Phase }
if ($LASTEXITCODE -ne 0) { throw 'T2 failed. Preserve evidence; diagnose before any new slot.' }
