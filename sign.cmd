@echo off
rem ============================================================
rem  Code signing helper for GeometryViewer.exe  (Windows)
rem  Fill in the two paths below, then run:   sign.cmd dist\GeometryViewer.exe
rem  Requires signtool.exe (Windows SDK / Visual Studio Build Tools).
rem ============================================================
setlocal

set "EXE=%~1"
if "%EXE%"=="" (
  echo usage: sign.cmd ^<path-to-exe^>
  exit /b 1
)

rem ---- 1. your certificate (either a .pfx or a store thumbprint) ----
set "PFX=%~dp0secrets\codesign.pfx"
set "PFXPASS=CHANGE_ME"

rem ---- 2. timestamp server ----
set "TIMESTAMP=http://timestamp.digicert.com"

rem ---- 3. description shown in the UAC / properties dialog ----
set "DESC=GeometryViewer - geometry reference viewer"

where signtool >nul 2>&1
if errorlevel 1 (
  echo [ERROR] signtool.exe not found. Install the Windows SDK, or run this
  echo         from a "Developer Command Prompt for VS".
  exit /b 1
)

if not exist "%PFX%" (
  echo [ERROR] certificate not found: %PFX%
  echo         Put your .pfx there, or change PFX above.
  exit /b 1
)

echo Signing %EXE% ...
signtool sign /fd SHA256 /td SHA256 /tr "%TIMESTAMP%" /f "%PFX%" /p "%PFXPASS%" /d "%DESC%" "%EXE%"
if errorlevel 1 (
  echo [ERROR] signing failed.
  exit /b 1
)

echo Verifying ...
signtool verify /pa /v "%EXE%"
if errorlevel 1 (
  echo [ERROR] verification failed.
  exit /b 1
)

echo.
echo OK - signed and verified.
echo Remember: users still need download reputation, unless you use an EV certificate
echo or Azure Trusted Signing. See SIGNING.md.
exit /b 0
