@echo off
rem ============================================================
rem  Geometry Viewer - one click build (single file exe in dist\)
rem  Requires: .NET 8 SDK  https://dotnet.microsoft.com/download
rem  Notes are ASCII only on purpose: cmd.exe mis-parses UTF-8.
rem ============================================================
setlocal

set "HERE=%~dp0"
set "DOTNET_CLI_HOME=%HERE%.build\clihome"
set "NUGET_PACKAGES=%HERE%.build\nuget"
set "NUGET_HTTP_CACHE_PATH=%HERE%.build\nuget-http"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Install the .NET 8 SDK first.
    echo         https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

echo.
echo Building... first run downloads the win-x64 runtime pack.
echo.

dotnet publish "%HERE%src\GeoViewer.csproj" -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none ^
    -o "%HERE%dist"

if errorlevel 1 (
    echo.
    echo [FAILED] build error, see the messages above.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  OK -^> %HERE%dist\GeometryViewer.exe
echo ============================================================
pause
