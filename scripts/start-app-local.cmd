@echo off
rem Copied next to framework-dependent builds and used by the "open with" verb.
rem Points DOTNET_ROOT at a user-local .NET when there is one.
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
start "" "%~dp0Penrose.exe" %*
