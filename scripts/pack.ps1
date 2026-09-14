# Publish a self-contained portable zip, and a Velopack installer when vpk is available.
# Usage (repo root):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pack.ps1
param(
    [string]$Version = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$DotnetRoot = Join-Path $env:USERPROFILE ".dotnet"
$Dotnet = Join-Path $DotnetRoot "dotnet.exe"
if (-not (Test-Path $Dotnet)) {
    throw "dotnet not found at $Dotnet"
}

$env:DOTNET_ROOT = $DotnetRoot
$env:PATH = "$DotnetRoot;$env:PATH"

if (-not $Version) {
    $props = Get-Content (Join-Path $Root "Directory.Build.props") -Raw
    if ($props -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
    else {
        $Version = "0.1.0"
    }
}

$Proj = Join-Path $Root "src\MediaPlayer.App.WinUI\MediaPlayer.App.WinUI.csproj"
$Publish = Join-Path $Root "artifacts\publish\win-x64"
$Zip = Join-Path $Root "artifacts\MediaPlayer-$Version-win-x64.zip"
$InstallerOut = Join-Path $Root "artifacts\installer"

if (Test-Path $Publish) {
    Remove-Item $Publish -Recurse -Force
}

Write-Host "Publishing $Version (self-contained, Windows App SDK bundled)..."
& $Dotnet publish $Proj -c Release -r win-x64 -p:Platform=x64 --self-contained true `
    -p:PublishReadyToRun=false -o $Publish
if ($LASTEXITCODE -ne 0) {
    throw "publish failed"
}

$exe = Join-Path $Publish "MediaPlayer.App.WinUI.exe"
if (-not (Test-Path $exe)) {
    throw "missing $exe"
}
if (-not (Test-Path (Join-Path $Publish "mpv-2.dll")) -and -not (Test-Path (Join-Path $Publish "libmpv-2.dll"))) {
    throw "libmpv was not copied into the publish folder. Run scripts\fetch-libmpv.ps1 first."
}

Copy-Item (Join-Path $Root "LICENSE") (Join-Path $Publish "LICENSE") -Force
Copy-Item (Join-Path $Root "third_party\libmpv\LICENSE.notes.md") (Join-Path $Publish "LICENSE.libmpv.md") -Force
$readme = @"
Media Player $Version portable

Run MediaPlayer.App.WinUI.exe in this folder. Keep the DLLs (including mpv-2.dll).
Settings and resume data: %LOCALAPPDATA%\media-player\
License: GPL-3.0-or-later (LICENSE). libmpv is also GPL.
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

Write-Host "Velopack pack..."
& $Dotnet tool run vpk -- pack -u MediaPlayer -v $Version -p $Publish -e MediaPlayer.App.WinUI.exe -o $InstallerOut --packTitle "Media Player"
if ($LASTEXITCODE -ne 0) {
    Write-Warning "vpk pack failed; portable zip is still valid."
    return
}

Write-Host "Installer: $InstallerOut"
