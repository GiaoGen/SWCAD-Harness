[CmdletBinding()]
param([ValidateSet('Pure','Prepare','Native','Cold','Read','Audit','Summarize')][string]$Mode='Pure',
 [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9_]*$')][string]$RunId,
 [string]$Slot,[string]$ContinuationPackage,[string]$ReuseRun,[string]$ExternalContinuation,[string]$BuildRunId,[switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
if(-not $BuildRunId){$BuildRunId=$RunId}
$bin=Join-Path $root "artifacts/m20-preparation/T5/frozen-bin-$BuildRunId"
if(-not $NoBuild){
 & $sdk build (Join-Path $root 'tests/CadHarness.CoreAcceptance.Tests/CadHarness.CoreAcceptance.Tests.csproj') -c Release --disable-build-servers '-p:NuGetAudit=false' '-p:SolidWorksInteropDir=D:\Solidworks Crops\SOLIDWORKS\api\redist' "-p:OutDir=$bin/"
 if($LASTEXITCODE -ne 0){throw 'T5 build failed'}
}
$runner=Join-Path $bin 'CadHarness.CoreAcceptance.Tests.dll'
$env:CAD_HARNESS_PART_TEMPLATE='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot'
$env:CAD_HARNESS_T5_CONTINUATION=$ContinuationPackage
$env:CAD_HARNESS_T5_REUSE_RUN=$ReuseRun
$env:CAD_HARNESS_T5_EXTERNAL_CONTINUATION=$ExternalContinuation
$phase=@{Pure='--pure';Prepare='--freeze';Native='--native';Cold='--cold';Read='--read';Audit='--audit';Summarize='--summarize'}[$Mode]
& $sdk $runner $root $phase $RunId $Slot --development
if($LASTEXITCODE -ne 0){throw 'T5 failed. Preserve evidence and diagnose; never retry the same frozen slot.'}
