[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}
Write-Host "关闭“唤醒后需要登录”..." -ForegroundColor Cyan
powercfg /SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
if ($LASTEXITCODE -ne 0) { Write-Host "设置失败，可能需要管理员权限或组策略限制。" -ForegroundColor Red; exit 1 }
powercfg /SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
powercfg /SETACTIVE SCHEME_CURRENT
Write-Host "完成：唤醒电脑后不再要求输入 PIN，会直接回到桌面。" -ForegroundColor Green
Write-Host "注意：这会降低电脑在无人看管时的安全性。恢复请运行 restore-wake-lock.ps1。"
