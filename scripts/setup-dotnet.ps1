[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'config/toolchain.json') -Raw | ConvertFrom-Json
$taskDirectory = Join-Path $taskRoot 'artifacts/toolchain/dotnet'
$taskExe = Join-Path $taskDirectory 'dotnet.exe'
if (Test-Path -LiteralPath $taskExe) {
    $taskInstalled = & $taskExe --list-sdks
    if ($LASTEXITCODE -eq 0 -and ($taskInstalled -match "^$([regex]::Escape($taskManifest.sdkVersion)) ")) {
        Write-Output "Workspace SDK $($taskManifest.sdkVersion) already installed."
        exit 0
    }
    throw 'Existing workspace toolchain differs from the pinned SDK; it was not overwritten.'
}
$taskZip = Join-Path $taskRoot 'artifacts/toolchain/sdk.zip'
New-Item -ItemType Directory -Path (Split-Path -Parent $taskZip) -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskZip)) {
    Invoke-WebRequest -Uri $taskManifest.url -OutFile $taskZip
}
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash
if ($taskHash -ne $taskManifest.sha512) { throw 'SDK archive SHA-512 differs from the pinned official release hash; archive was not extracted.' }
Write-Output 'Official SDK archive hash verified; extracting in the workspace.'
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskDirectory
& $taskExe --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Workspace SDK did not start.' }
