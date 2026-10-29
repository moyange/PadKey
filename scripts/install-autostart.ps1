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
$TaskName = "PadKeyServerElevated"

Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Stop-Service PadKeySecure -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Get-Process PadKeyServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item $SourceExe $InstalledExe -Force
& icacls $InstallDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null

$vbs = Join-Path $InstallDir "start-tray.vbs"
$vbsText = 'Set sh = CreateObject("WScript.Shell")' + [Environment]::NewLine + 'sh.Run Chr(34) & "' + $InstalledExe + '" & Chr(34) & " --tray", 7, False' + [Environment]::NewLine
Set-Content -Path $vbs -Value $vbsText -Encoding ASCII
& icacls $vbs /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' '*S-1-5-32-545:RX' | Out-Null

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PadKeyServer -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA "PadKey\start-tray.vbs") -Force -ErrorAction SilentlyContinue

$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\wscript.exe" -Argument ('"' + $vbs + '"')
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

# Remote UAC/lock input is a security boundary. Remove the privileged helper
# service so the server stays at ordinary-user integrity.
$svc = Get-Service PadKeySecure -ErrorAction SilentlyContinue
if ($svc) {
    Stop-Service PadKeySecure -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    & sc.exe delete PadKeySecure | Out-Null
}

Start-ScheduledTask -TaskName $TaskName
Write-Host "已安装到受保护目录: $InstallDir" -ForegroundColor Green
Write-Host "计划任务只运行该目录中的管理员专属文件。" -ForegroundColor Gray
Write-Host "当前用户可执行但不能修改该目录。"