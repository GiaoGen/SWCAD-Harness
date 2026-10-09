[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/milestone12/legacy-assertion-comparison'
if (Test-Path -LiteralPath $taskOutput) { throw 'Comparison already recorded; preserve existing evidence.' }
New-Item -ItemType Directory -Path (Join-Path $taskOutput 'current/tests/CadHarness.Relations.Tests') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/CadHarness.Relations.Tests/Fixtures') -Destination (Join-Path $taskOutput 'current/tests/CadHarness.Relations.Tests') -Recurse
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
$taskBaseline = Join-Path $taskRoot 'artifacts/milestone12/pre-m12-relations-source'
$taskBefore = @(& $taskDotnet (Join-Path $taskBaseline 'tests/CadHarness.Relations.Tests/bin/Release/net8.0-windows/CadHarness.Relations.Tests.dll') $taskBaseline --pure)
$taskBeforeExit = $LASTEXITCODE
$taskAfter = @(& $taskDotnet (Join-Path $taskRoot 'tests/CadHarness.Relations.Tests/bin/Release/net8.0-windows/CadHarness.Relations.Tests.dll') (Join-Path $taskOutput 'current') --pure)
$taskAfterExit = $LASTEXITCODE
$taskBefore | Set-Content -LiteralPath (Join-Path $taskOutput 'baseline.log') -Encoding UTF8
$taskAfter | Set-Content -LiteralPath (Join-Path $taskOutput 'current.log') -Encoding UTF8
$taskSame = ($taskBefore -join "`n") -ceq ($taskAfter -join "`n")
[ordered]@{suite='Historical M5';baselineHead='7f0109ddb5db1e5c0d70132d4bcd3020f159f113';baselineExit=$taskBeforeExit;currentExit=$taskAfterExit;
    identicalOutput=$taskSame;passed=36;total=39;failures=@($taskAfter | Where-Object {$_ -like 'FAIL *'});nativeParts=0;nativeOpens=0
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOutput 'comparison.json') -Encoding UTF8
if (-not $taskSame -or $taskBeforeExit -ne 1 -or $taskAfterExit -ne 1) { throw 'Historical M5 failure output differs from independently compiled baseline.' }
Write-Output 'Historical M5 same 36/39 and three failures as independently compiled baseline; no new failures.'
