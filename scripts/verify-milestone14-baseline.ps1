[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$common=Join-Path $root 'artifacts/milestone14'
$baseline=Get-Content -LiteralPath (Join-Path $common 'baseline.json') -Raw|ConvertFrom-Json
$expected=(Get-Content -LiteralPath (Join-Path $common 'baseline.json.sha256') -Raw).Trim()
if((Get-FileHash -LiteralPath (Join-Path $common 'baseline.json')).Hash.ToLowerInvariant() -ne $expected){throw 'Baseline itself changed.'}
$allowed=@('CadHarness.sln','src/CadHarness.State/ManagedRevisionStore.cs','src/CadHarness.SolidWorks/ExternalPartInspection.cs','src/CadHarness.SolidWorks/ParameterMutationHandlers.cs','src/CadHarness.SolidWorks/ParameterMutationRegistry.cs')
$changes=@()
foreach($entry in $baseline.entries){
    $path='\\?\'+[IO.Path]::GetFullPath((Join-Path $root $entry.path))
    $stream=[IO.File]::OpenRead($path)
    try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}finally{$stream.Dispose()}
    if($actual -ne $entry.sha256){
        if($entry.path -notin $allowed -or $entry.category -eq 'historical_evidence'){throw "Unexpected baseline change: $($entry.path)"}
        $changes+=$entry.path
    }
}
$ledger=Get-Content -LiteralPath (Join-Path $common 'native-budget.json') -Raw|ConvertFrom-Json
if($ledger.OpenAttempts -gt 12 -or $ledger.DocumentsClosed -ne $ledger.OpenAttempts -or $ledger.OwnedTitles.Count -ne 0){throw 'Native budget/cleanup mismatch.'}
$output=Join-Path $common ('audit/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
New-Item -ItemType Directory -Path $output|Out-Null
[ordered]@{status='PARTIAL_NATIVE_PAUSED';baselineEntries=$baseline.entries.Count;allowedChangedFiles=$changes;historicalEvidencePreserved=$true;opens=$ledger.OpenAttempts;closes=$ledger.DocumentsClosed;newParts=0;ownedRemaining=0}|
    ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
Write-Output "M14 baseline audited: $($baseline.entries.Count) entries; historical evidence unchanged; $($ledger.OpenAttempts)/12 opens, all closed. Status PARTIAL (native paused)."
