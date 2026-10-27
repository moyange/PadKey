[CmdletBinding()]
param([int]$Port = 8765)

$ErrorActionPreference = "SilentlyContinue"
$Root = Split-Path -Parent $PSScriptRoot
$Adb = Join-Path $Root "tools\platform-tools\adb.exe"
if (-not (Test-Path $Adb)) { $Adb = "adb" }
$log = Join-Path $Root "tools\usb-watch.log"
while ($true) {
    try {
        $devices = @()
        foreach ($line in (& $Adb devices)) {
            if ($line -match '^(\S+)\s+device\b') { $devices += $Matches[1] }
        }
        foreach ($serial in $devices) {
            $list = (& $Adb -s $serial reverse --list | Out-String)
            if ($list -notmatch [regex]::Escape("tcp:$Port")) {
                & $Adb -s $serial reverse "tcp:$Port" "tcp:$Port" | Out-Null
                "$(Get-Date -Format 'HH:mm:ss') restored $serial tcp:$Port" | Out-File $log -Append -Encoding utf8
            }
        }
    } catch { }
    Start-Sleep -Seconds 5
}