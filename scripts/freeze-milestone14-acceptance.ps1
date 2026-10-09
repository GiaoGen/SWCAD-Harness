[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^acceptance-v[0-9]+$')][string]$Run,[string]$Schedule,[switch]$Replan,[switch]$ResumeCore)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$dotnet=Initialize-CadHarnessSdk -Root $root
$env:SOLIDWORKS_INTEROP_DIR='D:\Solidworks Crops\SOLIDWORKS\api\redist'
& $dotnet build (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/CadHarness.ExternalEditing.Tests.csproj') -c Release -m:1 --disable-build-servers -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'M14 acceptance build failed.'}
$output=Join-Path $root "artifacts/milestone14/$Run"
if(Test-Path -LiteralPath $output){throw 'Never overwrite a frozen namespace.'}
New-Item -ItemType Directory -Path "$output/source","$output/bin" | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src') -Destination "$output/source/src" -Recurse
Copy-Item -LiteralPath (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests') -Destination "$output/source/tests" -Recurse
Copy-Item -LiteralPath (Join-Path $root 'SWCAD_Harness_v0.3_PRD.md') -Destination "$output/source/"
Copy-Item -Path (Join-Path $PSScriptRoot '*milestone14*.ps1') -Destination "$output/source/"
Copy-Item -Path (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/*') -Destination "$output/bin/"
if($ResumeCore){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --resume-acceptance $Run $Schedule}
elseif($Replan){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --replan-acceptance $Run $Schedule}
elseif($Schedule){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --freeze-stage $Run $Schedule}
else{& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --prepare-acceptance $Run unused}
if($LASTEXITCODE -ne 0){throw 'M14 schedule preparation failed; retain namespace.'}
