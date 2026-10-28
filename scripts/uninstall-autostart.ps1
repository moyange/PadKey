[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}

Unregister-ScheduledTask -TaskName "PadKeyServerElevated" -Confirm:$false -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PadKeyServer -ErrorAction SilentlyContinue
$startup = [Environment]::GetFolderPath("Startup")
$shortcut = Join-Path $startup "PadKey Server.lnk"
if (Test-Path $shortcut) { Remove-Item $shortcut -Force }
Write-Host "已取消 PadKey 高权限开机自启。" -ForegroundColor Green