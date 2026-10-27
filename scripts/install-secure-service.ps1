[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$log = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\install-secure.log"
try { Start-Transcript -Path $log -Append -Force | Out-Null } catch { }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PSCommandPath"
    exit
}

$serviceName = "PadKeySecure"
$exe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\dist\PadKeyServer.exe"))
Write-Host "exe=$exe"
if (-not (Test-Path $exe)) { Write-Host "missing exe" -ForegroundColor Red; Stop-Transcript; exit 1 }

$old = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($old) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    & sc.exe delete $serviceName | Out-Host
    Start-Sleep -Seconds 1
}
$bin = '"' + $exe + '" --secure-service'
Write-Host "bin=$bin"
& sc.exe create $serviceName binPath= $bin start= auto obj= LocalSystem DisplayName= "PadKey Secure Input"
Write-Host "create exit=$LASTEXITCODE"
& sc.exe description $serviceName "PadKey locked-screen keyboard input helper"
& sc.exe start $serviceName
Write-Host "start exit=$LASTEXITCODE"
Start-Sleep -Seconds 2
Get-Service -Name $serviceName -ErrorAction SilentlyContinue | Format-List Name,Status,StartType
if (Test-Path "C:\ProgramData\PadKey\secure.log") { Get-Content "C:\ProgramData\PadKey\secure.log" -Tail 20 }
Stop-Transcript
