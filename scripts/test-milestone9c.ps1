[CmdletBinding()]
param([switch]$Live, [switch]$LiveTopology, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR, [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE)
$ErrorActionPreference = 'Stop'
if ($Live -and $LiveTopology) { throw '-Live and -LiveTopology are mutually exclusive.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone9C -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskAssembly = Join-Path $taskRoot 'tests/CadHarness.ConstructionTransactions.Tests/bin/Release/net8.0-windows/CadHarness.ConstructionTransactions.Tests.dll'
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } elseif ($LiveTopology) { '--live-topology' } else { '--pure' }))
if (($Live -or $LiveTopology) -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
& $taskSdk $taskAssembly @taskRunnerArgs
if ($LASTEXITCODE -ne 0) { throw "M9C runner failed with exit code $LASTEXITCODE." }
