@echo off
rem ---------------------------------------------------------------
rem  Push this repository to GitHub with the portable Git that
rem  lives one level above this folder (.git-portable\cmd).
rem  Keep this file ASCII-only: cmd.exe reads it with the OEM code page.
rem ---------------------------------------------------------------
setlocal
set "GITDIR=%~dp0..\.git-portable\cmd"
set "PATH=%GITDIR%;%PATH%"
cd /d "%~dp0"

echo === git version ===
git --version
if errorlevel 1 goto :nogit

echo.
echo === repository ===
git remote -v

echo.
echo === committing pending changes ===
git add -A
git diff --cached --quiet
if errorlevel 1 git commit -m "update sources"

echo.
echo === pushing to GitHub ===
echo (a browser window may open now - sign in with your GitHub account)
git push --force origin main
if errorlevel 1 goto :failed

echo.
echo Done. Open https://github.com/Akoto001/- to check.
pause
exit /b 0

:nogit
echo.
echo ERROR: git not found at "%GITDIR%".
echo The portable Git folder is missing. Ask the assistant to re-create it.
pause
exit /b 1

:failed
echo.
echo Push failed. Sign in when the browser window opens, then run this file again.
pause
exit /b 1
