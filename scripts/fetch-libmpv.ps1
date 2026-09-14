# Download the locked shinchiro libmpv + player snapshot into third_party/libmpv/bin/x64.
# Requires: gh (authenticated), 7z/7zr.
# Every archive is verified against the SHA-256 recorded in third_party/libmpv/LOCK.md
# before anything is extracted; a mismatch aborts.
param(
    [string]$Release = "20260903",
    [string]$CommitShort = "69e63f425a",
    # SHA-256 from LOCK.md. Update both when moving the lock.
    [string]$DevSha256 = "FAC135C68A35B7639E39D72C0C365104EDBAEBDEA39A0DFDD8C36E8C8E80FAEF",
    [string]$PlayerSha256 = "418DBFB5FEB851CBED33D6C05D8481BA71802621BFD6EFE8974522B28D42AC97",
    [string]$DllSha256 = "673E6397920AB64A9C5B3A618F7F16D38854EFE72B58665F1F84E4E873B763A4",
    # Optional explicit 7-Zip executable; otherwise $env:SEVEN_ZIP, PATH, then Program Files.
    [string]$SevenZip = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Bin = Join-Path $RepoRoot "third_party\libmpv\bin\x64"
$Cache = Join-Path $env:TEMP "media-player-mpv"
New-Item -ItemType Directory -Force -Path $Bin, $Cache | Out-Null

$devName = "mpv-dev-x86_64-$Release-git-$CommitShort.7z"
$playerName = "mpv-x86_64-$Release-git-$CommitShort.7z"

function Assert-Sha256([string]$Path, [string]$Expected, [string]$What) {
    if (-not $Expected) { throw "No SHA-256 given for $What; refusing to trust an unverified download." }
    $actual = (Get-FileHash -Algorithm SHA256 -Path $Path).Hash
    if ($actual -ne $Expected.ToUpperInvariant()) {
        Remove-Item $Path -Force -ErrorAction SilentlyContinue
        throw "SHA-256 mismatch for $What`n  expected $Expected`n  actual   $actual`nThe file was deleted. Check LOCK.md or the release."
    }
    Write-Host "OK  $What  $actual"
}

gh release download $Release -R shinchiro/mpv-winbuild-cmake -p $devName -p $playerName -D $Cache --clobber
Assert-Sha256 (Join-Path $Cache $devName) $DevSha256 $devName
Assert-Sha256 (Join-Path $Cache $playerName) $PlayerSha256 $playerName

$candidates = @()
if ($SevenZip) { $candidates += $SevenZip }
if ($env:SEVEN_ZIP) { $candidates += $env:SEVEN_ZIP }
foreach ($name in @("7z", "7zr", "7za")) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
}
$candidates += "$env:ProgramFiles\7-Zip\7z.exe"
$candidates += "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
$seven = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $seven) {
    throw "7-Zip not found. Install 7-Zip, put 7z/7zr on PATH, or pass -SevenZip <path>."
}

$devDir = Join-Path $Cache "dev-$Release"
$playerDir = Join-Path $Cache "player-$Release"
New-Item -ItemType Directory -Force -Path $devDir, $playerDir | Out-Null
& $seven x (Join-Path $Cache $devName) "-o$devDir" -y | Out-Null
& $seven x (Join-Path $Cache $playerName) "-o$playerDir" -y | Out-Null

$dll = Get-ChildItem $devDir -Recurse -Filter "libmpv-2.dll" | Select-Object -First 1
if (-not $dll) { throw "libmpv-2.dll missing from $devName" }
Assert-Sha256 $dll.FullName $DllSha256 "libmpv-2.dll"
Copy-Item $dll.FullName (Join-Path $Bin "libmpv-2.dll") -Force

$mpv2 = Join-Path $Bin "mpv-2.dll"
if (Test-Path $mpv2) { Remove-Item $mpv2 -Force }
try {
    New-Item -ItemType HardLink -Path $mpv2 -Target (Join-Path $Bin "libmpv-2.dll") | Out-Null
}
catch {
    # Hard links need NTFS; a copy is equivalent for loading.
    Copy-Item (Join-Path $Bin "libmpv-2.dll") $mpv2 -Force
}

$compiler = Get-ChildItem $playerDir -Recurse -Filter "d3dcompiler_43.dll" | Select-Object -First 1
if ($compiler) { Copy-Item $compiler.FullName (Join-Path $Bin "d3dcompiler_43.dll") -Force }

$exe = Get-ChildItem $playerDir -Recurse -Filter "mpv.exe" | Select-Object -First 1
if ($exe) { Copy-Item $exe.FullName (Join-Path $Bin "mpv.exe") -Force }

Write-Host "Installed to $Bin"
Get-ChildItem $Bin | Format-Table Name, Length
& (Join-Path $Bin "mpv.exe") --version
