@echo off
rem Push this repository to GitHub over SSH - no browser sign-in, no password.
rem
rem ONE TIME SETUP:
rem   1. open  https://github.com/settings/ssh/new
rem   2. Title: whatever (e.g. "geoviewer laptop")
rem   3. Key: paste the text printed by  show-key.cmd  (or open
rem      ..\.ssh-geo\id_ed25519.pub with Notepad)
rem   4. press "Add SSH key"
rem   5. run this file
setlocal
set "GITDIR=%~dp0..\.git-portable\cmd"
set "PATH=%GITDIR%;%PATH%"
set "HOME=%~dp0..\.ssh-geo\home"
set "GIT_TERMINAL_PROMPT=0"
if not exist "%HOME%" mkdir "%HOME%"
if not exist "%HOME%\.ssh" mkdir "%HOME%\.ssh"
if not exist "%HOME%\.ssh\id_ed25519" copy /y "%~dp0..\.ssh-geo\id_ed25519" "%HOME%\.ssh\id_ed25519" >nul
if not exist "%HOME%\.ssh\config" copy /y "%~dp0..\.ssh-geo\home\.ssh\config" "%HOME%\.ssh\config" >nul
cd /d "%~dp0"

echo === adding github.com to the known hosts ===
if not exist "%HOME%\.ssh" mkdir "%HOME%\.ssh"
findstr /C:"github.com" "%HOME%\.ssh\known_hosts" >nul 2>&1
if errorlevel 1 (
  echo github.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl>> "%HOME%\.ssh\known_hosts"
  echo github.com ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBEmKSENjQEezOmxkZMy7opKgwFB9nkt5YRrYMjNuG5N87uRgg6CLrbo5wAdT/y6v0mKV0U2w0WZ2YB/++Tpockg=>> "%HOME%\.ssh\known_hosts"
)

echo === checking the key ===
git ls-remote git@github.com:Akoto001/-.git HEAD
if errorlevel 1 goto :nokey

echo.
echo === switching remote to ssh ===
git remote set-url origin git@github.com:Akoto001/-.git

echo.
echo === pushing ===
git add -A
git diff --cached --quiet || git commit -m "update sources"
git push --force origin main
if errorlevel 1 goto :failed

echo.
echo Done. Open https://github.com/Akoto001/- to check.
pause
exit /b 0

:nokey
echo.
echo GitHub rejected the key. Add the public key first:
echo   type "%~dp0..\.ssh-geo\id_ed25519.pub"
echo and paste it at https://github.com/settings/ssh/new
pause
exit /b 1

:failed
echo.
echo Push failed - see the message above.
pause
exit /b 1
