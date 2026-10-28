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
$Exe = Join-Path $Root "dist\PadKeyServer.exe"
if (-not (Test-Path $Exe)) { Write-Host "缺少 dist\PadKeyServer.exe" -ForegroundColor Red; exit 1 }

$dataDir = Join-Path $env:APPDATA "PadKey"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$vbs = Join-Path $dataDir "start-tray.vbs"
$vbsText = 'Set sh = CreateObject("WScript.Shell")' + [Environment]::NewLine + 'sh.Run Chr(34) & "' + $Exe + '" & Chr(34) & " --tray", 7, False' + [Environment]::NewLine
Set-Content -Path $vbs -Value $vbsText -Encoding ASCII

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PadKeyServer -ErrorAction SilentlyContinue

$taskName = "PadKeyServerElevated"
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\wscript.exe" -Argument ('"' + $vbs + '"')
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "已设置高权限开机自启任务: $taskName" -ForegroundColor Green
Write-Host "登录后服务器以管理员令牌在托盘运行，可控制 UAC/杀毒软件等高权限窗口。" -ForegroundColor Gray
Write-Host "取消: scripts\uninstall-autostart.ps1"