[CmdletBinding()]
param([ValidateSet('Pure','Native')][string]$Mode='Pure', [string]$RunId, [string]$Scenario, [string]$OwnedBlankPart,
      [ValidateRange(5,2147483647)][int]$MaximumOpenCycles=5,
      [string]$PartTemplate='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot',
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/milestone16/bin'
& $sdk build (Join-Path $root 'tests/CadHarness.FeatureConstruction.Tests/CadHarness.FeatureConstruction.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M16 build failed.' }
$runner=Join-Path $bin 'CadHarness.FeatureConstruction.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --pure }
else {
    if (!$RunId -or !$Scenario) { throw 'Native requires a unique RunId and frozen Scenario.' }
    $env:CAD_HARNESS_PART_TEMPLATE=$PartTemplate
    $env:CAD_HARNESS_M16_OPEN_LIMIT=$MaximumOpenCycles.ToString()
    if ($OwnedBlankPart) { & $sdk $runner $root --native $RunId $Scenario $OwnedBlankPart }
    else { & $sdk $runner $root --native $RunId $Scenario }
}
if ($LASTEXITCODE -ne 0) { throw 'M16 failed; preserve evidence and diagnose before retry.' }
