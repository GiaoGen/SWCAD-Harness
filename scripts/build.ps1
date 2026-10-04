[CmdletBinding()]
param([ValidateSet('All','Bootstrap','Milestone3','Milestone4','Milestone5','Milestone6','Milestone7','Milestone8','Milestone9A')][string]$Scope = 'All', [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'sdk-environment.ps1')
$taskSdk = Initialize-CadHarnessSdk -Root $taskRoot
$taskInterop = ''
# M8 references only pure State/IR and needs no installed SOLIDWORKS/COM registry.
if ($Scope -ne 'Milestone8') {
    if ([string]::IsNullOrWhiteSpace($InteropDir)) {
        $taskClsid = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
        $taskServer = (Get-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\$taskClsid\LocalServer32").GetValue('').Trim('"')
        $InteropDir = Join-Path (Split-Path -Parent $taskServer) 'api/redist'
    }
    $taskInterop = (Resolve-Path -LiteralPath $InteropDir).Path
}
$taskTarget = switch ($Scope) {
    'Bootstrap' { Join-Path $taskRoot 'tests/CadHarness.Bootstrap.Tests/CadHarness.Bootstrap.Tests.csproj' }
    'Milestone3' { Join-Path $taskRoot 'tests/CadHarness.State.Tests/CadHarness.State.Tests.csproj' }
    'Milestone4' { Join-Path $taskRoot 'tests/CadHarness.Features.Tests/CadHarness.Features.Tests.csproj' }
    'Milestone5' { Join-Path $taskRoot 'tests/CadHarness.Relations.Tests/CadHarness.Relations.Tests.csproj' }
    'Milestone6' { Join-Path $taskRoot 'tests/CadHarness.Transactions.Tests/CadHarness.Transactions.Tests.csproj' }
    'Milestone7' { Join-Path $taskRoot 'tests/CadHarness.Planning.Tests/CadHarness.Planning.Tests.csproj' }
    'Milestone8' { Join-Path $taskRoot 'tests/CadHarness.Judging.Tests/CadHarness.Judging.Tests.csproj' }
    'Milestone9A' { Join-Path $taskRoot 'tests/CadHarness.ParameterMutations.Tests/CadHarness.ParameterMutations.Tests.csproj' }
    default { Join-Path $taskRoot 'CadHarness.sln' }
}
& $taskSdk build $taskTarget --configuration Release --nologo --disable-build-servers "-p:SolidWorksInteropDir=$taskInterop" '-p:NuGetAudit=false'
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
Write-Output "BUILD PASSED ($Scope); no tests or SOLIDWORKS calls executed."
