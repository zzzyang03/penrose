# Publish a self-contained portable zip, and a Velopack installer when vpk is available.
# Usage (repo root):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack.ps1
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# A user-local SDK (%USERPROFILE%\.dotnet) wins over dotnet on PATH.
$UserDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path $UserDotnet) {
    $Dotnet = $UserDotnet
    $env:DOTNET_ROOT = Split-Path $UserDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}
else {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "dotnet not found. Install the .NET SDK pinned in global.json."
    }
    $Dotnet = $command.Source
}

if (-not $Version) {
    $props = Get-Content (Join-Path $Root "Directory.Build.props") -Raw
    if ($props -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
    else {
        $Version = "0.1.0"
    }
}

$Proj = Join-Path $Root "src\Penrose.App.WinUI\Penrose.App.WinUI.csproj"
$Publish = Join-Path $Root "artifacts\publish\win-x64"
$Zip = Join-Path $Root "artifacts\Penrose-$Version-win-x64-portable.zip"
$InstallerOut = Join-Path $Root "artifacts\installer"
$Icon = Join-Path $Root "src\Penrose.App.WinUI\Assets\AppIcon.ico"

if (Test-Path $Publish) {
    Remove-Item $Publish -Recurse -Force
}

# Start the app's Release output from scratch. The PRI generation step decides from file
# timestamps whether to rebuild its list of input PRIs, so after a framework-dependent
# Release build it keeps a list without the Windows App SDK PRIs and the published app
# fails at launch ("Cannot locate resource ... themeresources.xaml").
$AppDir = Split-Path $Proj
foreach ($stale in (Join-Path $AppDir "obj\x64\Release"), (Join-Path $AppDir "bin\x64\Release")) {
    if (Test-Path $stale) {
        Remove-Item $stale -Recurse -Force
    }
}

Write-Host "Publishing Penrose $Version (self-contained, Windows App SDK bundled)..."
# No -r here: the csproj already sets RuntimeIdentifier=win-x64. Passing it on the command
# line makes it global, so every referenced library is restored for win-x64 too, which
# rewrites their packages.lock.json and breaks the RID-less locked restore in CI.
& $Dotnet publish $Proj -c Release -p:Platform=x64 --self-contained true `
    -p:PublishReadyToRun=false -o $Publish
if ($LASTEXITCODE -ne 0) {
    throw "publish failed"
}

$exe = Join-Path $Publish "Penrose.exe"
if (-not (Test-Path $exe)) {
    throw "missing $exe"
}
if (-not (Test-Path (Join-Path $Publish "mpv-2.dll"))) {
    throw "libmpv was not copied into the publish folder. Run scripts\fetch-libmpv.ps1 first."
}
# The merged PRI carries the WinUI theme resources (about 2 MB); the app's own part is tiny.
$Pri = Join-Path $Publish "Penrose.pri"
if (-not (Test-Path $Pri) -or (Get-Item $Pri).Length -lt 1MB) {
    throw "Penrose.pri is missing or lacks the Windows App SDK resources; the app would fail at launch."
}

Copy-Item (Join-Path $Root "LICENSE") (Join-Path $Publish "LICENSE.txt") -Force
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.md") (Join-Path $Publish "THIRD-PARTY-NOTICES.md") -Force
$readme = @"
Penrose $Version (win-x64)

Run Penrose.exe in this folder and keep every file next to it
(mpv-2.dll is the playback engine).

Settings, resume data and logs: %LOCALAPPDATA%\Penrose\
Source code: https://github.com/zzzyang03/penrose
License: GPL-3.0-or-later (LICENSE.txt). Bundled components: THIRD-PARTY-NOTICES.md
"@
Set-Content -Path (Join-Path $Publish "README.txt") -Value $readme -Encoding UTF8

New-Item -ItemType Directory -Force -Path (Split-Path $Zip) | Out-Null
if (Test-Path $Zip) {
    Remove-Item $Zip -Force
}
Compress-Archive -Path (Join-Path $Publish "*") -DestinationPath $Zip -Force
Write-Host "Portable: $Zip"

if ($SkipInstaller) {
    return
}

Write-Host "Restoring vpk..."
& $Dotnet tool restore --tool-manifest (Join-Path $Root ".config\dotnet-tools.json") -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Warning "vpk restore failed; portable zip is still valid."
    return
}

if (Test-Path $InstallerOut) {
    Remove-Item $InstallerOut -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $InstallerOut | Out-Null

# The pack id is also the install folder (%LOCALAPPDATA%\PenrosePlayer). It must differ
# from the data folder (%LOCALAPPDATA%\Penrose), which an uninstall must not delete.
Write-Host "Velopack pack..."
& $Dotnet tool run vpk -- pack -u PenrosePlayer -v $Version -p $Publish -e Penrose.exe -o $InstallerOut `
    --packTitle "Penrose" --packAuthors "Zhehao Yang" --icon $Icon
if ($LASTEXITCODE -ne 0) {
    Write-Warning "vpk pack failed; portable zip is still valid."
    return
}

Write-Host "Installer: $InstallerOut"
