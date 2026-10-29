[CmdletBinding()]
param(
    [int]$Port = 8765,
    [string]$Serial = "",
    [switch]$Install,
    [switch]$StartServer
)

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $PSScriptRoot
$AdbCandidates = @(
    (Join-Path $Root "tools\platform-tools\adb.exe"),
    (Join-Path $Root "tools\android-sdk\platform-tools\adb.exe")
)
$Adb = $AdbCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Adb) {
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) { $Adb = $cmd.Source }
}
if (-not $Adb) {
    Write-Host "找不到 adb。" -ForegroundColor Red
    Write-Host "请安装 Android platform-tools，或把 adb.exe 放到 tools\platform-tools\。" -ForegroundColor Yellow
    exit 1
}

$Apk = Join-Path $Root "dist\PadKey.apk"
$ServerExe = Join-Path $Root "dist\PadKeyServer.exe"
$Token = ""
$Cfg = Join-Path $env:APPDATA "PadKey\server.json"
if (Test-Path $Cfg) { try { $Token = (Get-Content -Raw $Cfg | ConvertFrom-Json).Token } catch { } }

function Test-LocalPort([int]$P) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $iar = $client.BeginConnect("127.0.0.1", $P, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne(350)) { return $false }
        $client.EndConnect($iar)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

if ($StartServer -and -not (Test-LocalPort $Port)) {
    if (-not (Test-Path $ServerExe)) {
        Write-Host "缺少 dist\PadKeyServer.exe，请先运行 scripts\build.ps1 或使用已发布的 dist。" -ForegroundColor Red
        exit 1
    }
    Write-Host "启动服务端 (端口 $Port)..." -ForegroundColor Cyan
    Start-Process -FilePath $ServerExe -WorkingDirectory (Join-Path $Root "dist") -ArgumentList @("--port", $Port)
    Start-Sleep -Seconds 1
}

& $Adb start-server | Out-Null
$devices = @()
foreach ($line in (& $Adb devices -l)) {
    if ($line -match '^(\S+)\s+(device|unauthorized|offline)\b') {
        $devices += [pscustomobject]@{ Serial = $Matches[1]; State = $Matches[2] }
    }
}

if ($Serial) {
    $target = $devices | Where-Object { $_.Serial -eq $Serial } | Select-Object -First 1
    if (-not $target) {
        Write-Host "找不到设备 $Serial。当前设备：" -ForegroundColor Red
        $devices | Format-Table -AutoSize
        exit 1
    }
} else {
    $target = $devices | Where-Object { $_.State -eq "device" } | Select-Object -First 1
    if (-not $target) {
        $unauthorized = $devices | Where-Object { $_.State -eq "unauthorized" } | Select-Object -First 1
        if ($unauthorized) {
            Write-Host "平板已连接，但 USB 调试尚未授权。" -ForegroundColor Yellow
            Write-Host "请解锁平板，在“允许 USB 调试吗？”弹窗中勾选“始终允许”并点“允许”。" -ForegroundColor Yellow
            Write-Host "如果没有弹窗：重新插拔数据线，或把 USB 模式改为“传输文件/MTP”。" -ForegroundColor Yellow
        } else {
            Write-Host "没有找到已授权的 Android 设备。" -ForegroundColor Red
            Write-Host "请用数据线连接平板，打开开发者选项 → USB 调试。" -ForegroundColor Yellow
        }
        exit 1
    }
}

$Serial = $target.Serial
Write-Host "设备: $Serial" -ForegroundColor Green

if ($Install -or -not (Test-Path $Apk)) {
    if (-not (Test-Path $Apk)) {
        Write-Host "缺少 dist\PadKey.apk，请先运行 scripts\build.ps1。" -ForegroundColor Red
        exit 1
    }
}

$installed = & $Adb -s $Serial shell pm path com.padkey.app 2>$null
if ($Install -or -not ($installed -match "package:")) {
    Write-Host "安装 PadKey.apk ..." -ForegroundColor Cyan
    & $Adb -s $Serial install -r $Apk
    if ($LASTEXITCODE -ne 0) {
        Write-Host "安装失败。请确认平板允许安装未知来源应用，并重新授权 USB 调试。" -ForegroundColor Red
        exit 1
    }
}

Write-Host "建立 USB 端口转发 127.0.0.1:$Port ..." -ForegroundColor Cyan
& $Adb -s $Serial reverse "tcp:$Port" "tcp:$Port" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "adb reverse 失败。" -ForegroundColor Red
    exit 1
}

if (-not (Test-LocalPort $Port)) {
    Write-Host "提示：电脑端 $Port 端口还没有监听。请先运行 dist\PadKeyServer.exe。" -ForegroundColor Yellow
}

Write-Host "启动 PadKey ..." -ForegroundColor Cyan
& $Adb -s $Serial shell am force-stop com.padkey.app | Out-Null
$amArgs = @("-s", $Serial, "shell", "am", "start", "-W", "-n", "com.padkey.app/.MainActivity", "--es", "host", "127.0.0.1", "--ei", "port", "$Port")
if ($Token) { $amArgs += @("--es", "token", $Token) }
& $Adb @amArgs | Out-Null

Write-Host ""
Write-Host "完成。平板应显示绿色“USB 已连接”。" -ForegroundColor Green
Write-Host "若显示未连接：先确认 PadKeyServer.exe 正在运行，再重新执行本脚本。" -ForegroundColor Gray
