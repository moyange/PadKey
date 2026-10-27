[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PadKeyUsbWatch -ErrorAction SilentlyContinue
$me = $PID
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.ProcessId -ne $me -and $_.CommandLine -like '*usb-reverse-watch.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
$Adb = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\platform-tools\adb.exe"
if (Test-Path $Adb) { & $Adb kill-server 2>$null }
Write-Host "USB/ADB 看护和 adb server 已停止。" -ForegroundColor Green