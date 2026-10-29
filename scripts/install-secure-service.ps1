[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principalCheck = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principalCheck.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}

$Root = Split-Path -Parent $PSScriptRoot
$SourceExe = Join-Path $Root "dist\PadKeyServer.exe"
if (-not (Test-Path $SourceExe)) { Write-Host "缺少 dist\PadKeyServer.exe" -ForegroundColor Red; exit 1 }
$InstallDir = Join-Path $env:ProgramFiles "PadKey"
$InstalledExe = Join-Path $InstallDir "PadKeyServer.exe"

Stop-ScheduledTask -TaskName PadKeyServerElevated -ErrorAction SilentlyContinue
Stop-Service PadKeySecure -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Get-Process PadKeyServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $SourceExe $InstalledExe -Force
& icacls $InstallDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null

$bin = '"' + $InstalledExe + '" --secure-service'
$svc = Get-Service PadKeySecure -ErrorAction SilentlyContinue
if ($svc) { & sc.exe config PadKeySecure binPath= $bin | Out-Null }
else { & sc.exe create PadKeySecure binPath= $bin start= auto obj= LocalSystem DisplayName= "PadKey Secure Input" | Out-Null }
Start-Service PadKeySecure
Start-Sleep -Seconds 1
Get-Service PadKeySecure | Select-Object Name,Status
Write-Host "LocalSystem 服务现在只运行受保护路径: $InstalledExe" -ForegroundColor Green