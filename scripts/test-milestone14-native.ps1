[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$EvidenceNamespace,[ValidateSet('--intake')][string]$Mode='--intake')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if($EvidenceNamespace -notmatch '^source-v[1-9][0-9]*$'){throw 'Use a fresh numbered evidence namespace.'}
$output=Join-Path $root "artifacts/milestone14/$EvidenceNamespace"
if(Test-Path -LiteralPath $output){throw 'Native attempt namespace exists; preserve every attempt.'}
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$dll=Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/CadHarness.ExternalEditing.Tests.dll'
$Source=[IO.Path]::GetFullPath($Source)
$paths=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate native source.'}
$paths+=@(Get-ChildItem -LiteralPath (Split-Path -Parent $dll) -File | ForEach-Object {$_.FullName.Substring($root.Length+1).Replace('\','/')})
New-Item -ItemType Directory -Path $output | Out-Null
$files=@($paths|Sort-Object -Unique|ForEach-Object {
    $relative=$_.Replace('\','/'); $from=Join-Path $root $_; $to=Join-Path $output "snapshot/$relative"
    New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
    [IO.File]::Copy('\\?\'+[IO.Path]::GetFullPath($from),'\\?\'+[IO.Path]::GetFullPath($to),$false)
    [ordered]@{path=$relative;sha256=(Get-FileHash -LiteralPath $from).Hash.ToLowerInvariant()}
})
[ordered]@{milestone=14;utc=[DateTime]::UtcNow.ToString('o');files=$files;source=[ordered]@{path=$Source;sha256=(Get-FileHash -LiteralPath $Source).Hash.ToLowerInvariant();sizeBytes=(Get-Item -LiteralPath $Source).Length};
    mode=$Mode;newParts=0;maximumCumulativeOpens=12;
    schedule='intake; four scalar edits with independent saved reopen; one independent two-target batch; first-edit/file/state-publication rollback; fresh-controller cold read';
    oracle='Complete native inventory/refs/dependencies; all native scalars; independent solid count, analytic volume/envelope, every cylinder center/radius/axis and through-boundaries';
    missingFixturePolicy='No new Parts, no source mutation, no manufactured external history; unmet matrix row means PARTIAL'
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'native-freeze.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $output 'native-freeze.json')).Hash.ToLowerInvariant()|Set-Content -LiteralPath (Join-Path $output 'native-freeze.json.sha256') -Encoding ASCII
# Run the retained binaries, not a subsequent build.
$frozen=Join-Path $output 'snapshot/tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/CadHarness.ExternalEditing.Tests.dll'
& $dotnet $frozen $root $Mode $EvidenceNamespace $Source 2>&1|Tee-Object -FilePath (Join-Path $output 'controller.log')
$code=$LASTEXITCODE
[ordered]@{exitCode=$code;utc=[DateTime]::UtcNow.ToString('o');originalPreserved=((Get-FileHash -LiteralPath $Source).Hash.ToLowerInvariant() -eq (Get-Content -LiteralPath (Join-Path $output 'native-freeze.json') -Raw|ConvertFrom-Json).source.sha256)}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'attempt-result.json') -Encoding UTF8
if($code -ne 0){throw "M14 native attempt returned $code; evidence retained; no implicit retry."}
