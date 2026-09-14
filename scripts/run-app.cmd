@echo off
setlocal
rem Double-click this, or run it from any directory. Optional first argument
rem "--build" forces a rebuild; otherwise the host is rebuilt only when a source
rem file is newer than the built exe (a no-op WinUI build still costs 10-15 s).
rem Do not double-click MediaPlayer.App.WinUI.exe: .NET 10 is in
rem %USERPROFILE%\.dotnet, while C:\Program Files\dotnet only has net6/net8.
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"

cd /d "%~dp0\.."
set "REPO=%CD%"
set "PROJ=%REPO%\src\MediaPlayer.App.WinUI\MediaPlayer.App.WinUI.csproj"
set "OUTDIR=%REPO%\src\MediaPlayer.App.WinUI\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64"
set "EXE=%OUTDIR%\MediaPlayer.App.WinUI.exe"

if not exist "%PROJ%" (
  echo Project not found:
  echo   %PROJ%
  pause
  exit /b 1
)

set "FORCE="
if /I "%~1"=="--build" (
  set "FORCE=1"
  shift
)

set "NEEDBUILD=build"
if defined FORCE goto :build
if not exist "%EXE%" goto :build
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%REPO%\scripts\needs-build.ps1" -Exe "%EXE%" -RepoRoot "%REPO%"`) do set "NEEDBUILD=%%i"
if /I "%NEEDBUILD%"=="skip" (
  echo Sources unchanged; skipping build ^(pass --build to force^).
  goto :start
)

:build
echo Building host...
"%DOTNET_ROOT%\dotnet.exe" build "%PROJ%" -p:Platform=x64 -v q
if errorlevel 1 (
  pause
  exit /b 1
)

:start
if not exist "%EXE%" (
  echo Missing "%EXE%"
  pause
  exit /b 1
)

echo Starting "%EXE%"
for %%T in ("%EXE%") do echo Built %%~tT
start "Penrose" /D "%OUTDIR%" "%EXE%" %*
endlocal
