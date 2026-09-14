# Download the locked shinchiro libmpv + player snapshot into third_party/libmpv/bin/x64.
# Requires: 7-Zip (7z/7zr/7za on PATH, $env:SEVEN_ZIP, -SevenZip, or Program Files),
# or the tar.exe that ships with current Windows 11.
# Every archive is verified against the SHA-256 recorded in third_party/libmpv/LOCK.md
# before anything is extracted; a mismatch aborts. Verified archives are reused from
# the cache directory on later runs.
param(
    [string]$Release = "20260903",
    [string]$CommitShort = "69e63f425a",
    # SHA-256 from LOCK.md. Update both when moving the lock.
    [string]$DevSha256 = "FAC135C68A35B7639E39D72C0C365104EDBAEBDEA39A0DFDD8C36E8C8E80FAEF",
    [string]$PlayerSha256 = "418DBFB5FEB851CBED33D6C05D8481BA71802621BFD6EFE8974522B28D42AC97",
    [string]$DllSha256 = "673E6397920AB64A9C5B3A618F7F16D38854EFE72B58665F1F84E4E873B763A4",
    # Optional explicit 7-Zip executable; otherwise $env:SEVEN_ZIP, PATH, then Program Files.
    [string]$SevenZip = "",
    # Where downloaded archives are kept (CI points this at a cached directory).
    [string]$CacheDir = (Join-Path $env:TEMP "penrose-mpv")
)

$ErrorActionPreference = "Stop"
# Invoke-WebRequest is many times slower with the progress bar on Windows PowerShell 5.1.
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Bin = Join-Path $RepoRoot "third_party\libmpv\bin\x64"
$Cache = $CacheDir
New-Item -ItemType Directory -Force -Path $Bin, $Cache | Out-Null

$BaseUrl = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Release"
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

function Get-LockedArchive([string]$Name, [string]$Expected) {
    $path = Join-Path $Cache $Name
    if ((Test-Path $path) -and (Get-FileHash -Algorithm SHA256 -Path $path).Hash -eq $Expected.ToUpperInvariant()) {
        Write-Host "Cached $Name"
    }
    else {
        Write-Host "Downloading $BaseUrl/$Name"
        Invoke-WebRequest -Uri "$BaseUrl/$Name" -OutFile $path -UseBasicParsing
    }
    Assert-Sha256 $path $Expected $Name
    return $path
}

$devArchive = Get-LockedArchive $devName $DevSha256
$playerArchive = Get-LockedArchive $playerName $PlayerSha256

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
# Without 7-Zip, fall back to the bsdtar that ships with Windows 11 (libarchive reads 7z).
# A tar too old to decode the archive fails here or at the libmpv-2.dll hash check below.
$tar = Join-Path $env:SystemRoot "System32\tar.exe"
if (-not $seven -and -not (Test-Path $tar)) {
    throw "7-Zip not found. Install 7-Zip, put 7z/7zr on PATH, or pass -SevenZip <path>."
}

function Expand-LockedArchive([string]$Archive, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    if ($seven) {
        & $seven x $Archive "-o$Destination" -y | Out-Null
    }
    else {
        & $tar -xf $Archive -C $Destination
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract $Archive. Install 7-Zip and try again."
    }
}

$devDir = Join-Path $Cache "dev-$Release"
$playerDir = Join-Path $Cache "player-$Release"
Expand-LockedArchive $devArchive $devDir
Expand-LockedArchive $playerArchive $playerDir

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

# The bare player from the same commit, for side-by-side comparisons. Never shipped.
$exe = Get-ChildItem $playerDir -Recurse -Filter "mpv.exe" | Select-Object -First 1
if ($exe) { Copy-Item $exe.FullName (Join-Path $Bin "mpv.exe") -Force }

Write-Host "Installed to $Bin"
Get-ChildItem $Bin | Format-Table Name, Length
& (Join-Path $Bin "mpv.exe") --version
