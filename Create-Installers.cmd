@echo off
setlocal
rem Build the current OS installer through the canonical .NET packaging tool.
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 10 SDK is required. Install it and reopen this launcher.
    set "result=1"
    goto finish
)

echo Creating the native Release installer for this computer...
call dotnet run --project "%~dp0tools\InstallerBuilder\InstallerBuilder.csproj" --configuration Release -- %*
set "result=%errorlevel%"

:finish
if "%result%"=="0" (
    echo Packaging command completed. Installer output: "%~dp0artifacts\packages"
) else (
    echo Installer creation failed with exit code %result%. See the error above.
)
popd
rem Double-click launches stay visible; command-line arguments make runs noninteractive.
if "%~1"=="" pause
exit /b %result%
