[CmdletBinding()]
param([ValidateSet('Pure','Live','Live-Targeted','Native','Native-Replay')][string]$Mode='Pure', [string]$RunId, [string]$OwnedBlankPart,
      [string]$PartTemplate='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot',
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/milestone17/bin'
& $sdk build (Join-Path $root 'tests/CadHarness.ConstructionPlanning.Tests/CadHarness.ConstructionPlanning.Tests.csproj') --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$InteropDir" "-p:OutDir=$bin/" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw 'M17 build failed.' }
$runner=Join-Path $bin 'CadHarness.ConstructionPlanning.Tests.dll'
if ($Mode -eq 'Pure') { & $sdk $runner $root --pure }
else {
    if (!$RunId) { throw 'Live/native requires a unique evidence namespace.' }
    $env:CAD_HARNESS_PART_TEMPLATE=$PartTemplate
    if ($Mode -eq 'Native-Replay' -and $OwnedBlankPart) { & $sdk $runner $root --native-replay $RunId $OwnedBlankPart }
    elseif ($Mode -eq 'Native' -and $OwnedBlankPart) { & $sdk $runner $root --native-copy $RunId $OwnedBlankPart }
    else { & $sdk $runner $root "--$($Mode.ToLowerInvariant())" $RunId }
}
if ($LASTEXITCODE -ne 0) { throw 'M17 failed; preserve evidence and diagnose before retry.' }
