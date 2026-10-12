[CmdletBinding()]
param([ValidateSet('Pure','Live','Native')][string]$Mode='Pure',
 [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9_]*$')][string]$RunId,
 [ValidateSet('shaft','sleeve','bracket','slots')][string]$Case,
 [ValidateSet('audit','create','read','edit')][string]$Phase='read',
 [string]$PackageRun,[string]$LiveRun,[switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/m20-preparation/T4/bin'
if(-not $NoBuild){
 & $sdk build (Join-Path $root 'tests/CadHarness.ConstructionPlanning.Tests/CadHarness.ConstructionPlanning.Tests.csproj') -c Release --disable-build-servers '-p:NuGetAudit=false' '-p:SolidWorksInteropDir=D:\Solidworks Crops\SOLIDWORKS\api\redist' "-p:OutDir=$bin/"
 if($LASTEXITCODE -ne 0){throw 'T4 build failed'}
}
$runner=Join-Path $bin 'CadHarness.ConstructionPlanning.Tests.dll'
if($Mode -eq 'Pure'){ & $sdk $runner $root --t4-pure $RunId }
elseif($Mode -eq 'Live'){
 if($Case){ & $sdk $runner $root --t4-live $RunId $Case }else{ & $sdk $runner $root --t4-live $RunId }
}else{
 if(-not $Case){throw 'Native case required'}
 if(-not $PackageRun){$PackageRun=$RunId}
 if(-not $LiveRun){$LiveRun='fixture'}
 & $sdk $runner $root --t4-native $RunId $Case $Phase $PackageRun $LiveRun
}
if($LASTEXITCODE -ne 0){throw 'T4 failed; preserve evidence, diagnose, then use a new targeted schedule. No automatic retry.'}
