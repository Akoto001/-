@echo off
rem ---------------------------------------------------------------
rem  Push this repository to GitHub with the portable Git that
rem  lives one level above this folder (.git-portable).
rem  Keep this file ASCII-only: cmd.exe reads it with the OEM code page.
rem ---------------------------------------------------------------
setlocal
set "GITROOT=%~dp0..\.git-portable"
set "PATH=%GITROOT%\cmd;%GITROOT%\ucrt64\bin;%PATH%"
cd /d "%~dp0"

echo === git version ===
git --version
if errorlevel 1 goto :nogit

echo.
echo === signing-in helper ===
rem Git Credential Manager ships inside the portable package; use it so the
rem first push can open a browser sign-in window instead of asking for a password.
git config credential.helper manager
where git-credential-manager >nul 2>&1
if errorlevel 1 echo (helper not found on PATH - if the push asks for a password, use a token)

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
echo If a window asks you to sign in, choose "Sign in with your browser".
git push --force origin main
if errorlevel 1 goto :failed

echo.
echo Done. Open https://github.com/Akoto001/- to check.
pause
exit /b 0

:nogit
echo.
echo ERROR: git not found under "%GITROOT%".
echo The portable Git folder is missing - ask the assistant to re-create it.
pause
exit /b 1

:failed
echo.
echo Push failed.
echo  - If it asked for a username/password: GitHub needs a Personal Access Token
echo    (github.com - Settings - Developer settings - Tokens) used as the password.
echo  - Otherwise re-run this file once the sign-in window is finished.
pause
exit /b 1
