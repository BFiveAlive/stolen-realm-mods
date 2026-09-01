<#
.SYNOPSIS
    Builds every mod, packages release zips, and regenerates mods.json.

.DESCRIPTION
    One command produces everything a release needs, so the published mods.json can never
    disagree with the zips beside it: versions come from each csproj, checksums are computed
    from the bytes that are actually uploaded, and the download URLs are derived from the tag.

    Zips are laid out relative to the GAME FOLDER, so every path inside starts with BepInEx/.
    That single convention lets the installer and the in-game updater share one install step:
    extract over the game root.

    CI cannot do this. The mods compile against Assembly-CSharp.dll and the Sirenix assemblies
    from the game install, which cannot be redistributed - so releases are cut from a machine
    that owns the game.

.PARAMETER Tag
    Release tag to build URLs for. Defaults to v<ModManager version>.

.PARAMETER Publish
    Also create the GitHub release and upload the zips. Without this, everything is written to
    dist/ and nothing leaves the machine.
#>
[CmdletBinding()]
param(
    [string] $Tag,
    [string] $Owner = 'BFiveAlive',
    [string] $Repo = 'stolen-realm-mods',
    [string] $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Stolen Realm',
    [switch] $Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'

# Nothing here lists the mods. A mod declares its own description and data files in its csproj
# and is found by looking for one, so adding a mod is adding a folder - this script was
# previously a second place you had to remember to edit, and forgetting meant a mod that built
# fine and silently never reached anyone.

function Get-CsprojProperty([string] $csproj, [string] $name) {
    $xml = [xml](Get-Content -LiteralPath $csproj)
    $value = $xml.Project.PropertyGroup.$name | Where-Object { $_ } | Select-Object -First 1
    if ($value) { return "$value".Trim() }
    return $null
}

function Get-ModProjects([string] $root) {
    # A mod is a folder with a matching csproj whose plugin declares [BepInPlugin]. That rules
    # out ModUpdatePatcher, which is a preloader patcher rather than a plugin, and the installer,
    # which is a console app - without either of them needing to be named here.
    Get-ChildItem -LiteralPath $root -Directory |
        Where-Object {
            $csproj = Join-Path $_.FullName ($_.Name + '.csproj')
            $plugin = Join-Path $_.FullName 'src\Plugin.cs'

            (Test-Path $csproj) -and (Test-Path $plugin) -and
                (Select-String -LiteralPath $plugin -Pattern '\[BepInPlugin' -Quiet)
        } |
        Sort-Object Name |
        ForEach-Object { $_.Name }
}

function Get-CsprojVersion([string] $csproj) {
    $xml = [xml](Get-Content -LiteralPath $csproj)
    $version = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw "No <Version> in $csproj" }
    return "$version".Trim()
}

function Get-PluginGuid([string] $modDir) {
    # The GUID is the identity the updater matches on, and it lives in the plugin source rather
    # than in any build output. Reading it here keeps mods.json honest automatically.
    $plugin = Join-Path $modDir 'src\Plugin.cs'
    if (-not (Test-Path $plugin)) { return $null }

    $match = Select-String -LiteralPath $plugin -Pattern 'Guid\s*=\s*"([^"]+)"' | Select-Object -First 1
    if (-not $match) { return $null }
    return $match.Matches[0].Groups[1].Value
}

if (-not (Test-Path $GameDir)) {
    throw "Game folder not found: $GameDir. Pass -GameDir."
}

# --- Build -------------------------------------------------------------------------------

$modProjects = @(Get-ModProjects $root)
if ($modProjects.Count -eq 0) { throw "No mod projects found under $root" }

Write-Host ("Found " + $modProjects.Count + " mod(s): " + ($modProjects -join ', ')) -ForegroundColor Cyan

$allProjects = $modProjects + @('ModUpdatePatcher')

foreach ($name in $allProjects) {
    $csproj = Join-Path $root "$name\$name.csproj"
    if (-not (Test-Path $csproj)) {
        Write-Warning "Skipping $name - no csproj"
        continue
    }

    Write-Host "Building $name..." -ForegroundColor Cyan
    dotnet build $csproj -c Release -v quiet --nologo -p:GameDir="$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
}

# --- Package -----------------------------------------------------------------------------

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

if (-not $Tag) {
    $Tag = 'v' + (Get-CsprojVersion (Join-Path $root 'ModManager\ModManager.csproj'))
}

Write-Host "Packaging for tag $Tag" -ForegroundColor Cyan

$entries = @()

foreach ($name in $modProjects) {
    $modDir = Join-Path $root $name
    $csproj = Join-Path $modDir "$name.csproj"
    if (-not (Test-Path $csproj)) { continue }

    $version = Get-CsprojVersion $csproj

    $description = Get-CsprojProperty $csproj 'Description'
    if (-not $description) {
        Write-Warning "$name has no <Description> in its csproj; it will show up unexplained."
        $description = ''
    }

    $dll = Join-Path $modDir "bin\Release\netstandard2.1\$name.dll"
    if (-not (Test-Path $dll)) { throw "Missing build output: $dll" }

    $stage = Join-Path $dist "staging\$name"
    $pluginDir = Join-Path $stage "BepInEx\plugins\$name"
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item $dll $pluginDir

    $declared = Get-CsprojProperty $csproj 'ModDataFiles'
    $dataFiles = @()
    if ($declared) { $dataFiles = $declared -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }

    foreach ($extra in $dataFiles) {
        $source = Join-Path $modDir $extra
        if (Test-Path $source) { Copy-Item $source $pluginDir }
        else { Write-Warning "$name declares data file '$extra' but it is missing" }
    }

    # The manager is the one mod that also needs the preloader patcher, which is what actually
    # applies a staged update on the next launch. Shipping them together means installing the
    # manager installs a working updater rather than half of one.
    if ($name -eq 'ModManager') {
        $patcher = Join-Path $root 'ModUpdatePatcher\bin\Release\netstandard2.1\ModUpdatePatcher.dll'
        if (-not (Test-Path $patcher)) { throw "Missing build output: $patcher" }

        $patcherDir = Join-Path $stage 'BepInEx\patchers'
        New-Item -ItemType Directory -Path $patcherDir -Force | Out-Null
        Copy-Item $patcher $patcherDir
    }

    $zip = Join-Path $dist "$name-$version.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower()

    $entries += [ordered]@{
        guid        = (Get-PluginGuid $modDir)
        name        = (Get-Content -LiteralPath (Join-Path $modDir 'src\Plugin.cs') |
                        Select-String -Pattern 'Name\s*=\s*"([^"]+)"' |
                        Select-Object -First 1 |
                        ForEach-Object { $_.Matches[0].Groups[1].Value })
        folder      = $name
        version     = $version
        description = $description
        url         = "https://github.com/$Owner/$Repo/releases/download/$Tag/$name-$version.zip"
        sha256      = $hash
        recommended = $true
    }

    Write-Host ("  {0,-20} v{1,-8} {2,8:N0} KB" -f $name, $version, ((Get-Item $zip).Length / 1KB))
}

Remove-Item (Join-Path $dist 'staging') -Recurse -Force

# --- Installer ---------------------------------------------------------------------------

Write-Host "Publishing installer..." -ForegroundColor Cyan
$installerOut = Join-Path $dist 'installer'
dotnet publish (Join-Path $root 'Installer\Installer.csproj') -c Release -o $installerOut --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }

$installerExe = Join-Path $dist 'StolenRealmModInstaller.exe'
Copy-Item (Join-Path $installerOut 'StolenRealmModInstaller.exe') $installerExe -Force
Remove-Item $installerOut -Recurse -Force

# --- Manifest ----------------------------------------------------------------------------

# Pinned rather than resolved at install time: a BepInEx release that changes under us is a
# silent way for every future install to start behaving differently from this one.
$bepinexVersion = '5.4.23.5'

# SHA-256 of BepInEx_win_x64_5.4.23.5.zip, recorded once and checked by the installer before it
# unpacks anything over the game folder. Two independent sources agreed on it: hashing the
# downloaded file, and the digest GitHub reports for that asset in its releases API.
#
# This must be updated deliberately whenever $bepinexVersion changes - a stale hash fails the
# install loudly, which is the intended failure. Never "fix" a mismatch by regenerating this from
# whatever the download happened to produce; that is the check answering its own question.
$bepinexSha256 = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'

$manifest = [ordered]@{
    schemaVersion = 1
    bepinex       = [ordered]@{
        version = $bepinexVersion
        url     = "https://github.com/BepInEx/BepInEx/releases/download/v$bepinexVersion/BepInEx_win_x64_$bepinexVersion.zip"
        sha256  = $bepinexSha256
    }
    mods          = $entries
}

$manifestPath = Join-Path $root 'mods.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Wrote $manifestPath" -ForegroundColor Green

# --- Publish -----------------------------------------------------------------------------

if (-not $Publish) {
    Write-Host ""
    Write-Host "Built to $dist. Re-run with -Publish to create the GitHub release." -ForegroundColor Yellow
    Write-Host "Commit mods.json before publishing - the updater reads it from the repo, not the release." -ForegroundColor Yellow
    return
}

$assets = @(Get-ChildItem $dist -Filter '*.zip') + @(Get-Item $installerExe)

Write-Host "Creating release $Tag..." -ForegroundColor Cyan
gh release create $Tag @($assets.FullName) --repo "$Owner/$Repo" --title $Tag --generate-notes
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }

Write-Host "Released $Tag" -ForegroundColor Green
