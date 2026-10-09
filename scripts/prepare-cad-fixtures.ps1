[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,48}$')][string]$RunId,
    [ValidateSet('dev_core','dev_origin','dev_unknown_descendant','dev_equation_driver')][string]$FixtureId,
    [switch]$FreezeOnly,
    [string]$PartTemplate='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot',
    [string]$RecheckPackage
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$budget=Join-Path $root 'artifacts/fixture-factory'
$run=Join-Path $budget "runs/$RunId"
function Identity([string]$Path){
    $full=[IO.Path]::GetFullPath($Path)
    $s=[IO.File]::Open('\\?\'+$full,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try{[ordered]@{path=$full;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant();bytes=$s.Length}}finally{$s.Dispose()}
}
function NewJson([string]$Path,$Value){
    $data=[Text.Encoding]::UTF8.GetBytes(($Value|ConvertTo-Json -Depth 20))
    $f=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$f.Write($data);$f.Flush($true)}finally{$f.Dispose()}
}
if($FreezeOnly){
    if(Test-Path -LiteralPath $run){throw 'Run namespace already exists. Never overwrite a failed/frozen run.'}
    New-Item -ItemType Directory -Path $run|Out-Null
    $files=@()
    if([IO.Path]::GetExtension($PartTemplate) -ine '.prtdot'){throw 'An explicit existing .prtdot template is required.'}
    $files+=Identity $PartTemplate
    $additionalGrant=Join-Path $budget "authorizations/$RunId.json"
    if(Test-Path -LiteralPath $additionalGrant){$files+=Identity $additionalGrant}
    if($RecheckPackage){
        $original=Get-Content -LiteralPath (Join-Path $RecheckPackage 'manifest.json') -Raw|ConvertFrom-Json
        if($original.fixtureId -ne $FixtureId){throw 'Recheck fixture identity mismatch.'}
        $files+=Identity (Join-Path $RecheckPackage 'manifest.json')
        $files+=Identity (Join-Path $RecheckPackage 'reader-proof.json')
        $files+=Identity $original.specification.path
        $files+=Identity $original.nativePart.path
    }
    foreach($name in 'Builder','Reader'){
        $source=Join-Path $root "tools/CadFixtureFactory.$name/bin/Release/net8.0-windows"
        $dest=Join-Path $run "bin/$($name.ToLowerInvariant())"
        New-Item -ItemType Directory -Path $dest -Force|Out-Null
        foreach($f in Get-ChildItem -LiteralPath $source -File){
            Copy-Item -LiteralPath $f.FullName -Destination $dest
            $files+=Identity (Join-Path $dest $f.Name)
        }
    }
    $sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'tools') -Recurse -File|Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'})
    $sources+=Get-Item -LiteralPath $PSCommandPath,(Join-Path $root 'scripts/test-fixture-factory.ps1'),(Join-Path $root 'docs/prd-v0.3-fixture-factory-addendum.md'),(Join-Path $root 'artifacts/fixture-factory/authorization.json')
    foreach($f in $sources){
        $relative=[IO.Path]::GetRelativePath($root,$f.FullName)
        $snapshot=Join-Path (Join-Path $run 'source') $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $snapshot) -Force|Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $snapshot
        $files+=Identity $snapshot
    }
    $schedule=Join-Path $run 'preparation-schedule.json'
    NewJson $schedule ([ordered]@{scope='development-preparation-only';m14Acceptance=$false;heldOut=$false;recheckPackage=$RecheckPackage;recheckFixtureId=$FixtureId;slots=@('dev_core','dev_origin','dev_unknown_descendant','dev_equation_driver')|ForEach-Object{[ordered]@{fixtureId=$_;builder='one creation attempt unless explicitly frozen as original-source recheck';reader='one independent read-only cold reopen';package=(Join-Path $run "packages/$_")}}})
    $files+=Identity $schedule
    NewJson (Join-Path $run 'source-freeze.json') ([ordered]@{schemaVersion='1.0';purpose='development-fixture-preparation-only';utc=[DateTime]::UtcNow.ToString('o');baseline=(Identity (Join-Path $budget 'baseline.json'));files=$files})
    Write-Output "Frozen development preparation only: $run"
    return
}
if(!$FixtureId){throw 'FixtureId required; no implicit full native suite.'}
$freezePath=Join-Path $run 'source-freeze.json'
$freeze=Get-Content -LiteralPath $freezePath -Raw|ConvertFrom-Json
foreach($entry in @($freeze.baseline)+@($freeze.files)){
    $actual=Identity $entry.path
    if($actual.sha256 -ne $entry.sha256 -or $actual.bytes -ne $entry.bytes){throw "Frozen preparation artifact changed: $($entry.path)"}
}
if(!(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue)){throw 'Existing running SOLIDWORKS required. Factory never starts the application.'}
$package=Join-Path $run "packages/$FixtureId"
$spec=Join-Path $run "source/tools/fixture-specs/$FixtureId.json"
$logs=Join-Path $run 'logs'
New-Item -ItemType Directory -Path $logs -Force|Out-Null
function Controller([string]$Role,[string[]]$Arguments){
    $stdout=Join-Path $logs "$FixtureId-$Role.stdout.txt"
    $stderr=Join-Path $logs "$FixtureId-$Role.stderr.txt"
    if((Test-Path -LiteralPath $stdout) -or (Test-Path -LiteralPath $stderr)){throw 'Controller slot already used. Failed evidence is immutable.'}
    $quoted=@($Arguments|ForEach-Object{'"'+$_+'"'})
    $p=Start-Process -FilePath $dotnet -ArgumentList $quoted -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $finished=$p.WaitForExit(180000)
    if(!$finished){
        Stop-Process -Id $p.Id
        NewJson (Join-Path $logs "$FixtureId-$Role.timeout.json") ([ordered]@{controllerPid=$p.Id;nativeProcessKilled=$false;utc=[DateTime]::UtcNow.ToString('o');status='STOPPED_UNRESOLVED_OWNERSHIP_REQUIRES_INSPECTION'})
        throw 'Controller timed out. Do not continue or close engineer documents. Inspect ownership ledger.'
    }
    $p.WaitForExit()
    Get-Content -LiteralPath $stdout
    if($p.ExitCode -ne 0){Get-Content -LiteralPath $stderr;throw "$Role failed (exit $($p.ExitCode)); evidence retained. Do not rerun this slot."}
}
$schedule=Get-Content -LiteralPath (Join-Path $run 'preparation-schedule.json') -Raw|ConvertFrom-Json
if($schedule.recheckPackage -and $schedule.recheckFixtureId -eq $FixtureId){
    if(Test-Path -LiteralPath $package){throw 'Fresh recheck proof namespace required.'}
    Controller 'read' @((Join-Path $run 'bin/reader/CadFixtureFactory.Reader.dll'),$budget,$schedule.recheckPackage,$RunId,$package)
}else{
    Controller 'build' @((Join-Path $run 'bin/builder/CadFixtureFactory.Builder.dll'),$budget,$spec,$package,$RunId)
    Controller 'read' @((Join-Path $run 'bin/reader/CadFixtureFactory.Reader.dll'),$budget,$package,$RunId)
}
Write-Output "Prepared development fixture; M14 NOT RUN: $package"
