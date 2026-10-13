[CmdletBinding()]
param([ValidateSet('Prepare','Execute','Summarize')][string]$Mode='Prepare',
      [string]$RunId='acceptance_v1',
      [string]$Manifest,
      [string]$Authorization,
      [string]$CandidateBin,
      [string]$InteropDir='D:\Solidworks Crops\SOLIDWORKS\api\redist',
      [string]$PartTemplate='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$sdk=Initialize-CadHarnessSdk -Root $root
$output=Join-Path $root "artifacts/milestone20/$RunId"
$bin=if($CandidateBin){[IO.Path]::GetFullPath($CandidateBin)}else{Join-Path $root "artifacts/milestone20/frozen-bin-$RunId"}
$runner=Join-Path $bin 'CadHarness.CoreAcceptance.Tests.dll'
$env:CAD_HARNESS_PART_TEMPLATE=$PartTemplate
function Worker([string]$Phase,[string]$Slot) {
    $log=[IO.File]::Open((Join-Path $output "slots/$Slot/$($Phase.TrimStart('-')).log"),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $writer=[IO.StreamWriter]::new($log,[Text.UTF8Encoding]::new($false))
    try{& $sdk $runner $root $Phase $RunId $Slot 2>&1 | ForEach-Object {$writer.WriteLine([string]$_);$writer.Flush();Write-Output $_}}
    finally{$writer.Dispose()}
    if($LASTEXITCODE -ne 0){throw "Controller/resource failure in $Slot; stop and preserve scene. No automatic retry."}
}
if($Mode -eq 'Prepare') {
    if(-not $Manifest){throw 'Formal Prepare requires an independently sealed manifest; no provider/native execution is authorized'}
    if(-not $CandidateBin){throw 'Use the candidate binaries locked before maintainer sealing; do not rebuild after reveal'}
    if(Test-Path -LiteralPath $output){throw 'Frozen namespace already exists'}
    if(-not (Test-Path -LiteralPath $runner)){throw 'Locked candidate runner is missing'}
    & $sdk $runner $root --pure
    if($LASTEXITCODE -ne 0){throw 'M20 frozen-task pure checks failed'}
    & $sdk $runner $root --freeze $RunId ([IO.Path]::GetFullPath($Manifest))
    if($LASTEXITCODE -ne 0){throw 'M20 freeze failed'}
    return
}
$freeze=Get-Content -LiteralPath (Join-Path $output 'freeze.json') -Raw | ConvertFrom-Json
if($Mode -eq 'Execute') {
    if(-not $Authorization){throw 'Separate explicit human execution and finite budget approval file required'}
    if(Test-Path -LiteralPath (Join-Path $output 'execution-authorized.json')){throw 'Formal run cannot be repeated automatically'}
    & $sdk $runner $root --begin-execution $RunId ([IO.Path]::GetFullPath($Authorization))
    if($LASTEXITCODE -ne 0){throw 'Execution authorization or frozen integrity failed; no native/provider calls'}
    $stream=[IO.File]::Open((Join-Path $output 'execution-started.json'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$bytes=[Text.Encoding]::UTF8.GetBytes(([ordered]@{utc=[DateTime]::UtcNow.ToString('o');noRepair=$true;noRetry=$true}|ConvertTo-Json));$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    foreach($slot in $freeze.slots) {
        New-Item -ItemType Directory -Path (Join-Path $output "slots/$($slot.id)") -Force | Out-Null
        if($slot.mode -in @('real','negative-planning')){Worker '--plan' $slot.id}
        if($slot.mode -ne 'negative-planning') {
            if($slot.mode -in @('external-cold','g2-cold')){
                $prior=Get-Content -LiteralPath (Join-Path $output "slots/$($slot.pair)/native-result.json") -Raw | ConvertFrom-Json
                if($prior.status -ne 'PASS'){continue}
            }
            Worker '--native' $slot.id
            $native=Get-Content -LiteralPath (Join-Path $output "slots/$($slot.id)/native-result.json") -Raw | ConvertFrom-Json
            if($slot.mode -in @('real','deterministic') -and $native.status -eq 'PASS'){
                Worker '--cold' $slot.id
                $cold=Get-Content -LiteralPath (Join-Path $output "slots/$($slot.id)/cold-result.json") -Raw | ConvertFrom-Json
                if($cold.status -eq 'PASS'){Worker '--read' $slot.id}
            }
        }
    }
}
& $sdk $runner $root --summarize $RunId
if($LASTEXITCODE -ne 0){throw 'M20 summary failed; previous summaries remain immutable'}
