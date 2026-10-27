[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root "dist\PadKeyServer.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "缺少 dist\PadKeyServer.exe，请先运行 scripts\build.ps1。" -ForegroundColor Red
    exit 1
}
& $Exe --install-autostart
Write-Host "开机自启已设置：登录后服务器在系统托盘运行，不弹出控制台。" -ForegroundColor Green
Write-Host "以后可用托盘菜单或 scripts\uninstall-autostart.ps1 取消。"
