[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot "manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.version_number
$stagingDirectory = Join-Path $PSScriptRoot "staging"
$archivePath = Join-Path $PSScriptRoot "$($manifest.name)-$version.zip"
$releaseDirectory = Join-Path $projectRoot "bin\Release\net472"
$pluginPath = Join-Path $releaseDirectory "CharactersVault.dll"

if (-not $SkipBuild)
{
    $dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (-not (Test-Path $dotnetPath))
    {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -eq $dotnetCommand)
        {
            throw "The .NET SDK was not found. Install a current .NET SDK before packaging."
        }

        $dotnetPath = $dotnetCommand.Source
    }

    & $dotnetPath build (Join-Path $projectRoot "CharacterVault.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Release build failed; package creation stopped."
    }
}

$pluginVersion = Select-String -Path (Join-Path $projectRoot "Plugin.cs") -Pattern 'ModVersion\s*=\s*"([^"]+)"' |
    Select-Object -First 1
if ($null -eq $pluginVersion -or $pluginVersion.Matches[0].Groups[1].Value -ne $version)
{
    throw "Plugin.cs and manifest.json must use the same version number."
}

$requiredFiles = @(
    (Join-Path $projectRoot "README.md"),
    (Join-Path $projectRoot "CHANGELOG.md"),
    $manifestPath,
    (Join-Path $PSScriptRoot "icon.png"),
    $pluginPath
)

foreach ($file in $requiredFiles)
{
    if (-not (Test-Path $file))
    {
        throw "Required package file is missing: $file"
    }
}

Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
New-Item $stagingDirectory -ItemType Directory | Out-Null

foreach ($file in $requiredFiles)
{
    Copy-Item $file -Destination $stagingDirectory
}

Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try
{
    $expectedEntries = @("README.md", "CHANGELOG.md", "manifest.json", "icon.png", "CharactersVault.dll")
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName })
    $missingEntries = @($expectedEntries | Where-Object { $_ -notin $actualEntries })
    $unexpectedEntries = @($actualEntries | Where-Object { $_ -notin $expectedEntries })

    if ($missingEntries.Count -gt 0 -or $unexpectedEntries.Count -gt 0)
    {
        throw "Invalid archive contents. Missing: $($missingEntries -join ', '); unexpected: $($unexpectedEntries -join ', ')."
    }
}
finally
{
    $archive.Dispose()
}

Write-Host "Created Thunderstore package: $archivePath"