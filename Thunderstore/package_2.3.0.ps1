[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot "CharactersVault_v2.3.0"
if (-not (Test-Path $sourceRoot))
{
    # Check if sibling directory exists
    $siblingRoot = Join-Path (Split-Path -Parent $projectRoot) "CharactersVault_v2.3.0"
    if (Test-Path $siblingRoot)
    {
        $sourceRoot = $siblingRoot
    }
    else
    {
        throw "Could not find CharactersVault_v2.3.0 source directory."
    }
}

# Ensure reference DLLs exist in source libs directory
$sourceLibsDir = Join-Path $sourceRoot "libs"
$rootLibsDir = Join-Path $projectRoot "libs"
if (Test-Path $rootLibsDir)
{
    if (-not (Test-Path $sourceLibsDir))
    {
        New-Item -ItemType Directory -Path $sourceLibsDir -Force | Out-Null
    }
    Get-ChildItem -Path (Join-Path $rootLibsDir "*.dll") | ForEach-Object {
        $dest = Join-Path $sourceLibsDir $_.Name
        if (-not (Test-Path $dest))
        {
            Copy-Item $_.FullName -Destination $dest
        }
    }
}

$version = "2.3.0"
$stagingDirectory = Join-Path $PSScriptRoot "staging_2.3.0"
$archivePath = Join-Path $PSScriptRoot "CharactersVault-$version.zip"
$releaseDirectory = Join-Path $sourceRoot "bin\Release\net472"
$pluginPath = Join-Path $releaseDirectory "CharactersVault.dll"
$pluginDirectory = Join-Path $stagingDirectory "BepInEx\plugins\CharactersVault"

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

    $csprojPath = Join-Path $sourceRoot "CharacterVault.csproj"
    Write-Host "Building $csprojPath (Release)..."
    & $dotnetPath build $csprojPath -c Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Release build failed; package creation stopped."
    }
}

$pluginVersion = Select-String -Path (Join-Path $sourceRoot "Plugin.cs") -Pattern 'ModVersion\s*=\s*"([^"]+)"' |
    Select-Object -First 1
if ($null -eq $pluginVersion -or $pluginVersion.Matches[0].Groups[1].Value -ne $version)
{
    throw "Plugin.cs in 2.3.0 source and target version ($version) must match."
}

# Prepare staging
Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
New-Item $stagingDirectory -ItemType Directory | Out-Null
New-Item $pluginDirectory -ItemType Directory -Force | Out-Null

# 1. Copy CharactersVault.dll to staging BepInEx/plugins/CharactersVault/
if (-not (Test-Path $pluginPath))
{
    throw "Plugin DLL not found at $pluginPath"
}
Copy-Item $pluginPath -Destination $pluginDirectory

# 2. Manifest
$manifestData = [ordered]@{
    name           = "CharactersVault"
    version_number = $version
    website_url    = "https://github.com/BiigAI/CharactersVault"
    description    = "Server-authoritative Valheim character storage with one-character-per-player bindings."
    dependencies   = @(
        "denikson-BepInExPack_Valheim-5.4.2333"
    )
}
$stagingManifestPath = Join-Path $stagingDirectory "manifest.json"
$manifestData | ConvertTo-Json -Depth 4 | Set-Content -Path $stagingManifestPath -Encoding UTF8

# 3. Icon
$iconSource = Join-Path $PSScriptRoot "icon.png"
if (-not (Test-Path $iconSource))
{
    throw "Icon not found at $iconSource"
}
Copy-Item $iconSource -Destination $stagingDirectory

# 4. README.md
$readmeSource = Join-Path $sourceRoot "README.md"
if (-not (Test-Path $readmeSource))
{
    $readmeSource = Join-Path $projectRoot "README.md"
}
Copy-Item $readmeSource -Destination (Join-Path $stagingDirectory "README.md")

# 5. CHANGELOG.md
$changelogSource = Join-Path $sourceRoot "CHANGELOG.md"
$stagingChangelogPath = Join-Path $stagingDirectory "CHANGELOG.md"
if (Test-Path $changelogSource)
{
    Copy-Item $changelogSource -Destination $stagingChangelogPath
}
else
{
    $changelogContent = @"
## v2.3.0

- Initial release of CharactersVault (renamed from ServerCharacters).
- Server-authoritative character storage and syncing with one-character-per-player bindings.
- Automatic profile synchronization and admin management commands.
"@
    Set-Content -Path $stagingChangelogPath -Value $changelogContent -Encoding UTF8
}

# Create Zip Archive
Compress-Archive -Path (Join-Path $stagingDirectory "*") -DestinationPath $archivePath -CompressionLevel Optimal

# Verify Zip Archive
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try
{
    $expectedEntries = @(
        "README.md",
        "CHANGELOG.md",
        "manifest.json",
        "icon.png",
        "BepInEx/plugins/CharactersVault/CharactersVault.dll"
    )
    $actualEntries = @(
        $archive.Entries |
            Where-Object { -not $_.FullName.EndsWith("\") -and -not $_.FullName.EndsWith("/") } |
            ForEach-Object { $_.FullName.Replace("\", "/") }
    )
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

# Clean up staging directory
Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Successfully created 2.3.0 Thunderstore package: $archivePath"
