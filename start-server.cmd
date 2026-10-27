@echo off
setlocal
cd /d "%~dp0"
dist\PadKeyServer.exe %*
if errorlevel 1 pause
