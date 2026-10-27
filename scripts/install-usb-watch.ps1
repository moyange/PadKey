[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $PSScriptRoot
$Adb = Join-Path $Root "tools\platform-tools\adb.exe"
$Watch = Join-Path $Root "scripts\usb-reverse-watch.ps1"
if (-not (Test-Path $Adb)) { Write-Host "找不到 adb: $Adb" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $Watch)) { Write-Host "找不到看护脚本: $Watch" -ForegroundColor Red; exit 1 }

& $Adb start-server | Out-Null
$running = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*usb-reverse-watch.ps1*' }
if (-not $running) {
    Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList '-NoProfile','-WindowStyle','Hidden','-ExecutionPolicy','Bypass','-File',$Watch -WindowStyle Hidden
}
$cmd = '"' + "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" + '" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $Watch + '"'
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PadKeyUsbWatch -Value $cmd
Write-Host "USB/ADB 看护已开启。" -ForegroundColor Green
Write-Host "如要把平板切回 USB 模式，再运行 scripts\connect-usb.ps1。" -ForegroundColor Gray