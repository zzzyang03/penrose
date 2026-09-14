@echo off
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
start "" "%~dp0MediaPlayer.App.WinUI.exe" %*
