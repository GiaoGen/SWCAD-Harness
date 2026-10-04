[CmdletBinding()]
param([switch]$Live, [ValidateSet('g1','g2','aux')][string]$Case, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
if ($Live -and [string]::IsNullOrWhiteSpace($Case)) { throw 'Select one minimal native composition with -Case g1|g2|aux.' }
if (-not $Live -and $Case) { throw '-Case requires -Live.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone4 -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.Features.Tests/bin/Release/net8.0-windows/CadHarness.Features.Tests.dll'
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } else { '--pure' }))
if ($Live) { $taskRunnerArgs += $Case }
if ($Live -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
& $taskSdk $taskAssembly @taskRunnerArgs
if ($LASTEXITCODE -ne 0) { throw "Milestone 4 runner failed with exit code $LASTEXITCODE." }
