[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Android = Join-Path $Root "android"
$Dist = Join-Path $Root "dist"

if (-not $env:JAVA_HOME) {
    $candidate = "C:\Program Files\Android\openjdk\jdk-21.0.8"
    if (Test-Path $candidate) { $env:JAVA_HOME = $candidate }
}
if (-not $env:ANDROID_HOME) {
    $env:ANDROID_HOME = Join-Path $Root "tools\android-sdk"
}
if (-not (Test-Path (Join-Path $env:ANDROID_HOME "platforms\android-34"))) {
    throw "Android SDK 34 未找到，请设置 ANDROID_HOME。"
}

$sdkForGradle = $env:ANDROID_HOME -replace "\\", "/"
"sdk.dir=$sdkForGradle" | Set-Content -Path (Join-Path $Android "local.properties") -Encoding ASCII

Write-Host "== 构建 Android 应用 ==" -ForegroundColor Cyan
& (Join-Path $Android "gradlew.bat") -p $Android :app:assembleRelease --no-daemon
if ($LASTEXITCODE -ne 0) { throw "Android 构建失败" }

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
Copy-Item (Join-Path $Android "app\build\outputs\apk\release\app-release.apk") (Join-Path $Dist "PadKey.apk") -Force
Write-Host "APK -> dist\PadKey.apk" -ForegroundColor Green

Write-Host "== 发布 Windows 服务端 ==" -ForegroundColor Cyan
Push-Location (Join-Path $Root "server")
try {
    & dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Dist
    if ($LASTEXITCODE -ne 0) { throw "服务端发布失败" }
} finally {
    Pop-Location
}
Write-Host "服务端 -> dist\PadKeyServer.exe" -ForegroundColor Green
