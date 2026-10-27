[CmdletBinding()]
param(
    [switch]$Admin,
    [int]$Port = 8765
)

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $PSScriptRoot
$ServerExe = Join-Path $Root "dist\PadKeyServer.exe"

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

if (-not (Test-LocalPort $Port)) {
    if (-not (Test-Path $ServerExe)) {
        Write-Host "缺少 dist\PadKeyServer.exe，请先构建。" -ForegroundColor Red
        exit 1
    }
    Write-Host "启动 PadKeyServer ..." -ForegroundColor Cyan
    if ($Admin) {
        Start-Process -FilePath $ServerExe -WorkingDirectory (Join-Path $Root "dist") -ArgumentList @("--port", $Port) -Verb RunAs
    } else {
        Start-Process -FilePath $ServerExe -WorkingDirectory (Join-Path $Root "dist") -ArgumentList @("--port", $Port)
    }
    Start-Sleep -Seconds 2
}

& (Join-Path $PSScriptRoot "connect-usb.ps1") -Port $Port -Install
