[CmdletBinding()]
param([ValidateSet('Pure','Live','Native','Compat')][string]$Mode='Pure',
 [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9_]*$')][string]$RunId,
 [ValidateSet('shaft','bracket')][string]$Case,
 [ValidateSet('audit','create','read','edit')][string]$Phase='read',
 [string]$PackageRun,[string]$LiveRun,[switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$bin=Join-Path $root 'artifacts/m20-preparation/T4C/bin'
if(-not $NoBuild){
 & $sdk build (Join-Path $root 'tests/CadHarness.ConstructionPlanning.Tests/CadHarness.ConstructionPlanning.Tests.csproj') -c Release --disable-build-servers '-p:NuGetAudit=false' '-p:SolidWorksInteropDir=D:\Solidworks Crops\SOLIDWORKS\api\redist' "-p:OutDir=$bin/"
 if($LASTEXITCODE -ne 0){throw 'T4C build failed'}
}
$runner=Join-Path $bin 'CadHarness.ConstructionPlanning.Tests.dll'
if($Mode -eq 'Pure'){ & $sdk $runner $root --t4c-pure $RunId }
elseif($Mode -eq 'Live'){ & $sdk $runner $root --t4c-live $RunId }
elseif($Mode -eq 'Compat'){
 if(-not $Case -or -not $PackageRun){throw 'Compatibility read requires case and existing T4B package run'}
 & $sdk $runner $root --t4c-compat $RunId $Case $PackageRun
}
else{
 if(-not $Case){throw 'Native case required'}
 if(-not $PackageRun){$PackageRun=$RunId}
 if(-not $LiveRun){$LiveRun='live_v1'}
 & $sdk $runner $root --t4c-native $RunId $Case $Phase $PackageRun $LiveRun
}
if($LASTEXITCODE -ne 0){throw 'T4C failed; preserve evidence and diagnose before a new frozen schedule. No automatic retries.'}
