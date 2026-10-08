[CmdletBinding()]
param([string]$PartTemplate = 'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskEvidence = Join-Path $taskRoot 'artifacts/milestone10-formal-a2edad2'
$taskDll = Join-Path $taskRoot 'tests/CadHarness.FormalBenchmark.Tests/bin/Release/net8.0-windows/CadHarness.FormalBenchmark.Tests.dll'
$taskManifest = Get-Content -LiteralPath (Join-Path $taskEvidence 'manifest.json') -Raw | ConvertFrom-Json
if ($taskManifest.MaximumParts -ne 24 -or $taskManifest.Schedule.Count -ne 24) { throw 'Frozen schedule is not the authorized 24-Part schedule.' }
$taskController = Join-Path $taskEvidence 'controller-attempt.json'
if (Test-Path -LiteralPath $taskController) { throw 'Formal controller already attempted. Do not retry consumed slots.' }
@{StartedUtc=[DateTimeOffset]::UtcNow;MaximumParts=24;Schedule='manifest.json';FrozenDll=$taskDll} | ConvertTo-Json | Set-Content -LiteralPath $taskController -Encoding utf8
foreach ($taskSlot in $taskManifest.Schedule) {
    Write-Output "SLOT_START $($taskSlot.Order)/24 $($taskSlot.Id)"
    & $taskSdk $taskDll $taskRoot --live $taskSlot.Id $PartTemplate
    if ($LASTEXITCODE -ne 0) { throw "M10 BLOCKED at $($taskSlot.Id): experiment safety/integrity/infrastructure stop. Do not consume another Part." }
    $taskResult = Get-Content -LiteralPath (Join-Path $taskEvidence $taskSlot.Id 'result.json') -Raw | ConvertFrom-Json
    if (-not $taskResult.IntegrityValid -or -not $taskResult.Continuable) { throw "M10 BLOCKED at $($taskSlot.Id): continuation forbidden." }
    Write-Output "SLOT_END $($taskSlot.Order)/24 creation=$($taskResult.Success) editable=$($taskResult.EditableModelSuccess) parts=$($taskResult.PartsCreated)/$($taskResult.PartsClosed)"
}
& $taskSdk $taskDll $taskRoot --summary
if ($LASTEXITCODE -ne 0) { throw 'Formal schedule finished; successful comparison remains BLOCKED. Summary preserves all attempts.' }
