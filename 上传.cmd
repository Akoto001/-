@echo off
rem Push this repository to GitHub using the portable Git that lives in the workspace.
rem Double click, or run from a terminal. The first time GitHub will ask you to sign in
rem (a browser window opens) - that is normal, it only happens once.
setlocal
set "GITDIR=%~dp0..\.git-portable\cmd"
set "PATH=%GITDIR%;%PATH%"
cd /d "%~dp0"

echo === repository ===
git remote -v
echo.
echo === committing any pending changes ===
git add -A
git diff --cached --quiet || git commit -m "更新源码"
echo.
echo === pushing to GitHub ===
git push --force origin main
if errorlevel 1 goto :failed

echo.
echo Done - open https://github.com/Akoto001/- to check.
pause
exit /b 0

:failed
echo.
echo Push failed. Most likely you are not signed in yet, or the network is blocked.
echo Try again; when a browser window asks you to authorise, sign in with your GitHub account.
pause
exit /b 1
