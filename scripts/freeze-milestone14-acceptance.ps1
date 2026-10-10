[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^acceptance-v[0-9]+$')][string]$Run,[string]$Schedule,[switch]$Replan,[switch]$ResumeCore,[switch]$ScalarA,[switch]$ScalarPublic,[switch]$BatchB,[switch]$BatchPublic,[ValidateSet('candidate','remaining','probe','boundaries')][string]$ScalarPhase='candidate')
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
$tracked=@(& git -c safe.directory=D:/CAD-Harness0.2 ls-files --cached --others --exclude-standard -- src tests/CadHarness.ExternalEditing.Tests scripts SWCAD_Harness_v0.3_PRD.md Directory.Build.props Directory.Build.targets CadHarness.sln)
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate source-only freeze.'}
foreach($relative in $tracked){
    $target=Join-Path "$output/source" $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
Copy-Item -Path (Join-Path $root 'tests/CadHarness.ExternalEditing.Tests/bin/Release/net8.0-windows/*') -Destination "$output/bin/"
if($BatchB){$phase=if($BatchPublic){'public'}else{'candidate'};& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --prepare-batch-b $Run $phase}
elseif($ScalarA){$phase=if($ScalarPublic){'public'}else{$ScalarPhase};& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --prepare-scalar-a $Run $phase}
elseif($ResumeCore){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --resume-acceptance $Run $Schedule}
elseif($Replan){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --replan-acceptance $Run $Schedule}
elseif($Schedule){& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --freeze-stage $Run $Schedule}
else{& $dotnet "$output/bin/CadHarness.ExternalEditing.Tests.dll" $root --prepare-acceptance $Run unused}
if($LASTEXITCODE -ne 0){throw 'M14 schedule preparation failed; retain namespace.'}
