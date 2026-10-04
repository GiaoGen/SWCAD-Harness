[CmdletBinding()]
param([Parameter(Mandatory)][string]$Intent, [string]$Fixtures, [switch]$UseLlm, [string]$ModelProgram,
      [string]$State, [string]$Output, [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ($UseLlm.IsPresent -eq (-not [string]::IsNullOrWhiteSpace($Fixtures))) { throw 'Choose exactly one of -Fixtures or -UseLlm.' }
if ([string]::IsNullOrWhiteSpace($ModelProgram) -ne [string]::IsNullOrWhiteSpace($State)) { throw 'Edit planning requires both -ModelProgram and -State.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Scope Milestone7 -InteropDir $InteropDir
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskArguments = @('--intent', $Intent)
if ($UseLlm) { $taskArguments += '--llm' } else { $taskArguments += @('--fixtures', $Fixtures) }
if ($ModelProgram) { $taskArguments += @('--model-program', $ModelProgram, '--state', $State) }
if ($Output) { $taskArguments += @('--output', $Output) }
& $taskSdk (Join-Path $taskRoot 'src/CadHarness.Planner.Cli/bin/Release/net8.0-windows/CadHarness.Planner.Cli.dll') @taskArguments
if ($LASTEXITCODE -eq 2) { Write-Output 'INTENT UNSUPPORTED; no native mutation or Part creation.' }
elseif ($LASTEXITCODE -ne 0) { throw "Planner failed with exit code $LASTEXITCODE." }
