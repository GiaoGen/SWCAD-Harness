[CmdletBinding()]
param(
    [switch]$Live,
    [switch]$BuildOnly,
    [switch]$UseBundledCompiler,
    [string]$InteropDir = $env:SOLIDWORKS_INTEROP_DIR,
    [string]$PartTemplate = $env:CAD_HARNESS_PART_TEMPLATE
)
$ErrorActionPreference = 'Stop'
if ($Live -and $BuildOnly) { throw 'Live and BuildOnly are mutually exclusive.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot 'tests/CadHarness.SolidWorks.Tests/CadHarness.SolidWorks.Tests.csproj'
$taskOutputDir = Join-Path $taskRoot 'artifacts/milestone2'
if ([string]::IsNullOrWhiteSpace($InteropDir)) {
    $taskClsid = (Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
    $taskServer = (Get-Item -LiteralPath "Registry::HKEY_CLASSES_ROOT\CLSID\$taskClsid\LocalServer32").GetValue('').Trim('"')
    $InteropDir = Join-Path (Split-Path -Parent $taskServer) 'api/redist'
}
$taskInterop = (Resolve-Path -LiteralPath $InteropDir).Path
$taskInteropFiles = @(
    (Join-Path $taskInterop 'SolidWorks.Interop.sldworks.dll'),
    (Join-Path $taskInterop 'SolidWorks.Interop.swconst.dll')
)
foreach ($taskPath in $taskInteropFiles) {
    if (-not (Test-Path -LiteralPath $taskPath)) { throw "Required installed interop assembly is missing: $taskPath" }
}
$taskRunnerArgs = @($taskRoot, $(if ($Live) { '--live' } else { '--pure' }))
if ($Live -and -not [string]::IsNullOrWhiteSpace($PartTemplate)) { $taskRunnerArgs += $PartTemplate }
$taskDotnet = 'dotnet'
if (Test-Path -LiteralPath (Join-Path $taskRoot 'artifacts/toolchain/dotnet/dotnet.exe')) {
    . (Join-Path $PSScriptRoot 'sdk-environment.ps1')
    $taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
}
$taskSdks = @(& $taskDotnet --list-sdks)
if (-not $UseBundledCompiler -and $taskSdks.Count -gt 0) {
    if ($BuildOnly) {
        & $taskDotnet build $taskProject --configuration Release "-p:SolidWorksInteropDir=$taskInterop" --disable-build-servers
    } else {
        & $taskDotnet run --project $taskProject --configuration Release "-p:SolidWorksInteropDir=$taskInterop" -- @taskRunnerArgs
    }
    exit $LASTEXITCODE
}

# Compile the M1 library as a dependency, but do not run the M1 test suite.
# No SOLIDWORKS calls occur until the built runner is explicitly passed --live.
$taskCompilerPaths = @((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'), (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'))
foreach ($taskPath in $taskCompilerPaths) {
    if (-not (Test-Path -LiteralPath $taskPath)) { throw 'No SDK or PowerShell-bundled Roslyn compiler is available.' }
}
$taskRuntimeLine = @(& dotnet --list-runtimes | Where-Object { $_ -match '^Microsoft.NETCore.App 8\.0\.' } | Select-Object -Last 1)
if ($taskRuntimeLine.Count -eq 0 -or $taskRuntimeLine[0] -notmatch '^Microsoft.NETCore.App (\S+) \[(.+)\]$') { throw 'An installed .NET 8 runtime is required.' }
$taskRuntimeVersion = $Matches[1]
$taskRuntimeDir = Join-Path $Matches[2] $taskRuntimeVersion
New-Item -ItemType Directory -Path $taskOutputDir -Force | Out-Null
$taskReferences = @(Get-ChildItem -LiteralPath $taskRuntimeDir -Filter '*.dll' | Where-Object {
    try { [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true }
    catch [System.BadImageFormatException] { $false }
} | Select-Object -ExpandProperty FullName)
$taskHelperReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName) + $taskCompilerPaths
# Only the helper's PowerShell/Roslyn runtime identity warning is suppressed.
# All diagnostics for the project sources are treated as errors.
Add-Type -ReferencedAssemblies $taskHelperReferences -CompilerOptions '/nowarn:1701' -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class MilestoneTwoCompiler
{
    public static void Compile(string name, string[] sources, string[] references, string output, bool executable)
    {
        var trees = sources.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
            new CSharpParseOptions(LanguageVersion.CSharp12), path));
        var metadata = references.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name, trees, metadata,
            new CSharpCompilationOptions(executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                platform: Platform.X64, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        using var stream = File.Create(output);
        var result = compilation.Emit(stream);
        if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
    }
}
'@
$taskIrPath = Join-Path $taskOutputDir 'CadHarness.Ir.dll'
$taskBackendPath = Join-Path $taskOutputDir 'CadHarness.SolidWorks.dll'
$taskTestPath = Join-Path $taskOutputDir 'CadHarness.SolidWorks.Tests.dll'
$taskIrSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/CadHarness.Ir') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskBackendSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/CadHarness.SolidWorks') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskTestSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests/CadHarness.SolidWorks.Tests') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
[MilestoneTwoCompiler]::Compile('CadHarness.Ir', $taskIrSources, $taskReferences, $taskIrPath, $false)
$taskStatePath = Join-Path $taskOutputDir 'CadHarness.State.dll'
$taskStateSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/CadHarness.State') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
[MilestoneTwoCompiler]::Compile('CadHarness.State', $taskStateSources, ($taskReferences + $taskIrPath), $taskStatePath, $false)
[MilestoneTwoCompiler]::Compile('CadHarness.SolidWorks', $taskBackendSources, ($taskReferences + $taskInteropFiles + $taskIrPath + $taskStatePath), $taskBackendPath, $false)
[MilestoneTwoCompiler]::Compile('CadHarness.SolidWorks.Tests', $taskTestSources, ($taskReferences + $taskInteropFiles + $taskIrPath + $taskStatePath + $taskBackendPath), $taskTestPath, $true)
foreach ($taskPath in $taskInteropFiles) { Copy-Item -LiteralPath $taskPath -Destination $taskOutputDir -Force }
@{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $taskRuntimeVersion } } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutputDir 'CadHarness.SolidWorks.Tests.runtimeconfig.json') -Encoding utf8
if ($BuildOnly) { Write-Output 'Milestone 2 compile passed; no tests or native calls executed.'; exit 0 }
& dotnet $taskTestPath @taskRunnerArgs
exit $LASTEXITCODE
