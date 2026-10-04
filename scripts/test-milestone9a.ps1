[CmdletBinding()]
param([switch]$Live, [switch]$VerifyEvidence, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
if ($Live -and $VerifyEvidence) { throw '-Live and -VerifyEvidence are mutually exclusive.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone9A -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.ParameterMutations.Tests/bin/Release/net8.0-windows/CadHarness.ParameterMutations.Tests.dll'
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } elseif ($VerifyEvidence) { '--verify-evidence' } else { '--pure' }))
if ($Live -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
& $taskSdk $taskAssembly @taskRunnerArgs
if ($LASTEXITCODE -ne 0) { throw "M9A runner failed with exit code $LASTEXITCODE." }
