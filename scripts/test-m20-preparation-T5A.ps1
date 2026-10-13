[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9_]*$')][string]$RunId,
 [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$output=Join-Path $root "artifacts/m20-preparation/T5A/$RunId"
$bin=Join-Path $output 'bin'
if(Test-Path -LiteralPath $output){throw 'Do not overwrite an existing T5A namespace'}
New-Item -ItemType Directory -Path $output | Out-Null
function OfflineStage([string]$Name,[string[]]$Arguments){
 $stream=[IO.File]::Open((Join-Path $output "$Name.log"),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
 $writer=[IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($false))
 try{& $sdk @Arguments 2>&1 | ForEach-Object {$writer.WriteLine([string]$_);$writer.Flush();Write-Output $_}}
 finally{$writer.Dispose()}
 if($LASTEXITCODE -ne 0){throw "T5A $Name failed; preserve namespace. No provider/native execution authorized."}
}
OfflineStage 'build' @('build',(Join-Path $root 'tests/CadHarness.CoreAcceptance.Tests/CadHarness.CoreAcceptance.Tests.csproj'),'-c','Release','--disable-build-servers','-p:NuGetAudit=false',"-p:SolidWorksInteropDir=$InteropDir","-p:OutDir=$bin/")
OfflineStage 'regression' @((Join-Path $bin 'CadHarness.CoreAcceptance.Tests.dll'),$root,'--pure')
OfflineStage 'verification' @((Join-Path $bin 'CadHarness.CoreAcceptance.Tests.dll'),$root,'--t5a-pure',$RunId)
