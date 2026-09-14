# Prints "build" when the host exe is missing or older than any source input,
# otherwise "skip". Used by run-app.cmd to avoid a 10-15 s no-op WinUI build.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$RepoRoot
)

$ErrorActionPreference = "SilentlyContinue"
$built = Get-Item -LiteralPath $Exe
if (-not $built) {
    "build"
    exit 0
}

$inputs = @(
    (Join-Path $RepoRoot "src"),
    (Join-Path $RepoRoot "Directory.Build.props"),
    (Join-Path $RepoRoot "Directory.Packages.props")
)
$newer = Get-ChildItem -Recurse -File -Path $inputs -Include *.cs,*.xaml,*.csproj,*.props,*.manifest,*.png,*.ico,*.svg |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $built.LastWriteTime } |
    Select-Object -First 1
if ($newer) { "build" } else { "skip" }
