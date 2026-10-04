[CmdletBinding()]
param([switch]$Live, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone6 -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Transactions.Tests/bin/Release/net8.0-windows/CadHarness.Transactions.Tests.dll'
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } else { '--pure' }))
if ($Live -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
& $taskSdk $taskAssembly @taskRunnerArgs
if ($LASTEXITCODE -ne 0) { throw "Milestone 6 runner failed with exit code $LASTEXITCODE." }
