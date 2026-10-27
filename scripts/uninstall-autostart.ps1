[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root "dist\PadKeyServer.exe"
if (Test-Path $Exe) {
    & $Exe --remove-autostart
} else {
    Write-Host "找不到 dist\PadKeyServer.exe，尝试只清理启动快捷方式。" -ForegroundColor Yellow
    $Startup = [Environment]::GetFolderPath("Startup")
    $ShortcutPath = Join-Path $Startup "PadKey Server.lnk"
    if (Test-Path $ShortcutPath) { Remove-Item $ShortcutPath -Force }
}
Write-Host "已取消 PadKey 开机自启。" -ForegroundColor Green
