[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=Join-Path $root 'artifacts/fixture-factory'
if(Test-Path -LiteralPath (Join-Path $out 'baseline.json')){throw 'Factory baseline exists; never overwrite.'}
$paths=@(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root ls-files)
if($LASTEXITCODE -ne 0){throw 'Cannot read baseline.'}
$entries=@($paths|ForEach-Object {
    $s=[IO.File]::OpenRead('\\?\'+[IO.Path]::GetFullPath((Join-Path $root $_)))
    try{$h=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}
    [ordered]@{path=$_;sha256=$h;kind='source'}
})
foreach($f in Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/milestone14') -Recurse -File){
    $s=[IO.File]::OpenRead('\\?\'+$f.FullName)
    try{$h=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}
    $entries+=[ordered]@{path=$f.FullName.Substring($root.Length+1).Replace('\','/');sha256=$h;kind='historical_m14'}
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
[ordered]@{utc=[DateTime]::UtcNow.ToString('o');head=(& git -c "safe.directory=$($root.Replace('\','/'))" -C $root rev-parse HEAD);entries=$entries}|
    ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'baseline.json') -Encoding UTF8
(Get-FileHash -LiteralPath (Join-Path $out 'baseline.json')).Hash.ToLowerInvariant()|Set-Content -LiteralPath (Join-Path $out 'baseline.json.sha256') -Encoding ASCII
Write-Output "Factory baseline: $($entries.Count) entries; no native calls."
