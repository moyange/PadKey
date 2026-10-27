[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}
Write-Host "恢复“唤醒后需要登录”..." -ForegroundColor Cyan
powercfg /SETACVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 1
powercfg /SETDCVALUEINDEX SCHEME_CURRENT SUB_NONE CONSOLELOCK 1
powercfg /SETACTIVE SCHEME_CURRENT
Write-Host "完成：唤醒后需要登录。" -ForegroundColor Green
