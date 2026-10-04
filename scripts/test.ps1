[CmdletBinding()]
param([switch]$UseBundledCompiler, [switch]$WriteSchema)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskTestProject = Join-Path $taskRoot 'tests/CadHarness.Ir.Tests/CadHarness.Ir.Tests.csproj'
$taskRunnerArgs = @($taskRoot)
if ($WriteSchema) { $taskRunnerArgs += '--write-schema' }
$taskDotnet = 'dotnet'
if (Test-Path -LiteralPath (Join-Path $taskRoot 'artifacts/toolchain/dotnet/dotnet.exe')) {
    . (Join-Path $PSScriptRoot 'sdk-environment.ps1')
    $taskDotnet = Initialize-CadHarnessSdk -Root $taskRoot
}
$taskSdks = @(& $taskDotnet --list-sdks)
if (-not $UseBundledCompiler -and $taskSdks.Count -gt 0) {
    & $taskDotnet run --project $taskTestProject --configuration Release -- @taskRunnerArgs
    exit $LASTEXITCODE
}

# Local, offline fallback for hosts with a runtime but no SDK. This compiles the
# same library and tests as the projects, using PowerShell's bundled Roslyn.
$taskCompilerPaths = @(
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'),
    (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
)
foreach ($taskPath in $taskCompilerPaths) {
    if (-not (Test-Path -LiteralPath $taskPath)) { throw 'No SDK or bundled Roslyn compiler is available.' }
}
$taskRuntimeLine = @(& dotnet --list-runtimes | Where-Object { $_ -match '^Microsoft.NETCore.App 8\.0\.' } | Select-Object -Last 1)
if ($taskRuntimeLine.Count -eq 0 -or $taskRuntimeLine[0] -notmatch '^Microsoft.NETCore.App (\S+) \[(.+)\]$') {
    throw 'The offline compiler requires an installed .NET 8 runtime.'
}
$taskRuntimeVersion = $Matches[1]
$taskRuntimeDir = Join-Path $Matches[2] $taskRuntimeVersion
$taskOutputDir = Join-Path $taskRoot 'artifacts/milestone1'
New-Item -ItemType Directory -Path $taskOutputDir -Force | Out-Null
$taskReferences = @(Get-ChildItem -LiteralPath $taskRuntimeDir -Filter '*.dll' | Where-Object {
    try { [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; $true }
    catch [System.BadImageFormatException] { $false }
} | Select-Object -ExpandProperty FullName)
$taskHelperReferences = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | Select-Object -ExpandProperty FullName) + $taskCompilerPaths
# Roslyn shipped with PowerShell may reference the previous runtime major;
# PowerShell already loads it under its current runtime. Suppress that helper
# reference-identity warning only, never project source diagnostics.
Add-Type -ReferencedAssemblies $taskHelperReferences -CompilerOptions '/nowarn:1701' -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class MilestoneOneCompiler
{
    public static void Compile(string name, string[] sources, string[] references, string output, bool executable)
    {
        var trees = sources.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path),
            new CSharpParseOptions(LanguageVersion.CSharp12), path));
        var metadata = references.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name, trees, metadata,
            new CSharpCompilationOptions(executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
        using var stream = File.Create(output);
        var result = compilation.Emit(stream);
        if (!result.Success) throw new Exception(string.Join(Environment.NewLine, result.Diagnostics));
    }
}
'@
$taskLibraryPath = Join-Path $taskOutputDir 'CadHarness.Ir.dll'
$taskTestPath = Join-Path $taskOutputDir 'CadHarness.Ir.Tests.dll'
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src/CadHarness.Ir') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$taskTests = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tests/CadHarness.Ir.Tests') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
[MilestoneOneCompiler]::Compile('CadHarness.Ir', $taskSources, $taskReferences, $taskLibraryPath, $false)
[MilestoneOneCompiler]::Compile('CadHarness.Ir.Tests', $taskTests, ($taskReferences + $taskLibraryPath), $taskTestPath, $true)
@{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $taskRuntimeVersion } } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskOutputDir 'CadHarness.Ir.Tests.runtimeconfig.json') -Encoding utf8
& dotnet $taskTestPath @taskRunnerArgs
exit $LASTEXITCODE
