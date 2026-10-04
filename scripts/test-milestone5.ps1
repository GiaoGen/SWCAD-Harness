[CmdletBinding()]
param([switch]$Live, [ValidateSet('linear','rect2x2','rect2x3','combined')][string]$Case, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
if ($Live -and [string]::IsNullOrWhiteSpace($Case)) { throw 'Select one required native composition with -Case linear|rect2x2|rect2x3|combined.' }
if (-not $Live -and $Case) { throw '-Case requires -Live.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone5 -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Relations.Tests/bin/Release/net8.0-windows/CadHarness.Relations.Tests.dll'
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } else { '--pure' }))
if ($Live) { $taskRunnerArgs += $Case }
if ($Live -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
& $taskSdk $taskAssembly @taskRunnerArgs
if ($LASTEXITCODE -ne 0) { throw "Milestone 5 runner failed with exit code $LASTEXITCODE." }
