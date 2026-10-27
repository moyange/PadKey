[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}
$serviceName = "PadKeySecure"
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    & sc.exe delete $serviceName | Out-Null
    Write-Host "已卸载 $serviceName。" -ForegroundColor Green
} else {
    Write-Host "$serviceName 未安装。"
}
