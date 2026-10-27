@echo off
setlocal
cd /d "%~dp0"
tools\platform-tools\adb.exe reverse --remove-all
echo USB port forwarding removed.
pause
