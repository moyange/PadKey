@echo off
setlocal
cd /d "%~dp0"
if exist "scripts\start-tray.vbs" (
    start "" wscript.exe "scripts\start-tray.vbs"
) else (
    start "" /min dist\PadKeyServer.exe --tray
)
