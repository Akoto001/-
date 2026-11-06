@echo off
rem Prints the public key that has to be added to GitHub (one time).
set "KEY=%~dp0..\.ssh-geo\id_ed25519.pub"
echo.
echo === public key (copy this whole line) ===
echo.
type "%KEY%"
echo.
echo === next steps ===
echo 1. open  https://github.com/settings/ssh/new
echo 2. Title: geoviewer laptop
echo 3. Key:   paste the line above
echo 4. press "Add SSH key"
echo 5. double click  push-ssh.cmd
echo.
start "" https://github.com/settings/ssh/new
pause
