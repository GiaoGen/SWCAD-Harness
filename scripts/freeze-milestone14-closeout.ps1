[CmdletBinding()]
param([ValidatePattern('^acceptance-v[0-9]+$')][string]$Run='acceptance-v33')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=Join-Path $root "artifacts/milestone14/$Run"
if(Test-Path -LiteralPath $output){throw 'Never overwrite a frozen closeout run.'}
function Identity([string]$path){
    $full=[IO.Path]::GetFullPath($path)
    [ordered]@{path=$full;sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $full).Length}
}
function WriteNew([string]$path,$value){
    if(Test-Path -LiteralPath $path){throw "Evidence already exists: $path"}
    [IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
}
function Clone($value){$value|ConvertTo-Json -Depth 100|ConvertFrom-Json}
$budgetPath=Join-Path $root 'artifacts/milestone14/native-budget.json'
$budget=Get-Content -LiteralPath $budgetPath -Raw|ConvertFrom-Json
if($budget.OwnedTitles.Count -ne 0 -or $budget.OpenAttempts -ne $budget.DocumentsClosed -or $budget.MaximumNewParts -ne 0){throw 'Unresolved native lifecycle.'}
$old=Get-Content -LiteralPath (Join-Path $root 'artifacts/milestone14/acceptance-v32/schedule.json') -Raw|ConvertFrom-Json
$spec=Clone ($old.inputs|Where-Object id -eq 'manual-r2')
$package=$old.scenarios[0].package
$pointer=Get-Content -LiteralPath (Join-Path $package 'current.json') -Raw|ConvertFrom-Json
$manifest=Get-Content -LiteralPath $pointer.manifest.path -Raw|ConvertFrom-Json
if($manifest.revision -ne 2 -or (Identity $pointer.manifest.path).sha256 -ne $pointer.manifest.sha256 -or
    (Identity (Join-Path $package 'working/CADHarnessManagedPart.SLDPRT')).sha256 -ne $manifest.nativePart.sha256 -or
    (Test-Path -LiteralPath (Join-Path $package 'recovery.json'))){throw 'Manual authority is not the verified R2.'}
if((Identity $spec.source.path).sha256 -ne $spec.source.sha256){throw 'User source changed.'}
$final=Clone $spec
$final.id='manual-final-r5'
$final.depth=17
$final.pattern.count=3
$final.pattern.spacing=23
$final.provenance='Disclosed user manual model; NEW frozen task-level parameter combination, not a fresh-source Held-out; no production tuning permitted during run'
$factory=Get-Content -LiteralPath (Join-Path $root 'artifacts/milestone14/acceptance-v5/schedule.json') -Raw|ConvertFrom-Json
$core=Clone ($factory.inputs|Where-Object id -eq 'dev_core')
$equation=Clone ($factory.inputs|Where-Object id -eq 'dev_equation_driver')
$inputs=@($spec,$final,$core,$equation)
foreach($item in $inputs){if((Identity $item.source.path).sha256 -ne $item.source.sha256){throw 'Frozen native source changed.'}}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR='D:\Solidworks Crops\SOLIDWORKS\api\redist'
& $dotnet build (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/CadHarness.ExternalEditing.Tests.csproj') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Closeout build failed.'}
New-Item -ItemType Directory -Path "$output/source","$output/bin"|Out-Null
$paths=@(& git -c safe.directory=D:/CAD-Harness0.2 ls-files --cached --others --exclude-standard -- src tests/CadHarness.ExternalEditing.Tests scripts SWCAD_Harness_v0.3_PRD.md Directory.Build.props Directory.Build.targets)
if($LASTEXITCODE -ne 0){throw 'Source enumeration failed.'}
foreach($relative in $paths){
    $target=Join-Path "$output/source" $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Copy-Item -Path (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/*') -Destination "$output/bin/"
Copy-Item -LiteralPath $budgetPath -Destination "$output/initial-budget.json"
Copy-Item -LiteralPath (Join-Path $package 'current.json') -Destination "$output/initial-manual-pointer.json"
$grant=Join-Path $output 'authorization.json'
WriteNew $grant ([ordered]@{schemaVersion='0.3';userAuthorized=$true;milestone=14;previousMaximumOpens=27;additionalOpenCycles=2147483620;maximumCumulativeOpens=2147483647;maximumNewParts=0;humanAuthorization='No user limit. Explicit current user instruction: M14 closeout using user model and existing fixtures; stop third-party searching; preserve cumulative accounting and failures, originals untouched, no new Parts.'})
function Slot($id,$kind,$inputId,$ownedPackage,$previous,$opens){[ordered]@{id=$id;kind=$kind;input=$inputId;package=$ownedPackage;previousPackage=$previous;maximumOpens=$opens;publicEntry=$true}}
$slots=@(
    (Slot 'manual-remaining-scalars' 'scalar-manual-closeout' $spec.id $package $null 4),
    (Slot 'manual-final-cold' 'scalar-manual-closeout-cold' $final.id $package 'manual-remaining-scalars' 1),
    (Slot 'equivalent-direct' 'read-only-oracle' $core.id (Join-Path $output 'packages/direct') $null 1),
    (Slot 'equivalent-equation' 'read-only-oracle' $equation.id (Join-Path $output 'packages/equation') $null 1))
$schedule=Join-Path $output 'schedule.json'
WriteNew $schedule ([ordered]@{schemaVersion='0.3';run=$Run;authorization=$grant;inputs=$inputs;scenarios=$slots})
$head=(& git -c safe.directory=D:/CAD-Harness0.2 rev-parse HEAD).Trim()
WriteNew "$output/baseline.json" ([ordered]@{head=$head;opens=$budget.OpenAttempts;closes=$budget.DocumentsClosed;newParts=0;owned=$budget.OwnedTitles;manualRevision=2;manifest=(Identity $pointer.manifest.path);native=(Identity $manifest.nativePart.path);source=$spec.source;sourceAmendment='M14 source provenance only; no technical acceptance requirements removed';heldOut='New task combination 17mm depth/3 instances/23mm spacing on disclosed existing manual geometry. No production tuning. Prior source diagnostic use retained.';remainingGap='Actual P0 ambiguous binding evidence is not created by this schedule.'})
$files=@(Get-ChildItem -LiteralPath "$output/source","$output/bin" -File -Recurse|ForEach-Object {Identity $_.FullName})
$files+=@(Identity $grant;Identity "$output/initial-budget.json";Identity "$output/initial-manual-pointer.json";Identity "$output/baseline.json";Identity $pointer.manifest.path;Identity $manifest.nativePart.path)
$files+=@($inputs|ForEach-Object {$_.source})
WriteNew "$output/freeze.json" ([ordered]@{schemaVersion='0.3';run=$Run;utc=[DateTime]::UtcNow;schedule=(Identity $schedule);files=$files;initialBudget=(Identity $budgetPath)})
Write-Output "M14 closeout frozen: four slots, at most seven opens; production unchanged, no native calls during preparation. $output"
