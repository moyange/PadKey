# USB / ADB 停用记录

日期：2026-10-27 23:45（Asia/Hong_Kong）

## 当前状态

- 平板使用 **Wi-Fi（LAN）** 连接，App 显示“局域网已连接”
- USB 反向转发看护 `usb-reverse-watch.ps1` 已停止
- 开机启动项 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PadKeyUsbWatch` 已删除
- `adb server` 已执行 `adb kill-server`，TCP 5037 不再监听
- PadKey 安全输入服务 `PadKeySecure` 仍在运行（与 adb 无关）

## 为什么停

- 目前平板不需要 USB 调试/反向转发
- 避免后台每 5 秒调用 adb
- 避免登录时看到 adb daemon 启动信息

注意：停止 adb 后，`adb reverse tcp:8765` 会失效。如果 App 仍配置成
`127.0.0.1`，需要重新开启 USB 转发；走 Wi-Fi/LAN 则不受影响。

## 后期重新开启

最简单：

    powershell -ExecutionPolicy Bypass -File .\scripts\install-usb-watch.ps1

然后如果要让平板切回 USB 模式：

    powershell -ExecutionPolicy Bypass -File .\scripts\connect-usb.ps1

`connect-usb.ps1` 会：

1. 安装/确认 PadKey App
2. 执行 `adb reverse tcp:8765 tcp:8765`
3. 以 `127.0.0.1:8765` 启动 App

验证：

    tools\platform-tools\adb.exe devices
    tools\platform-tools\adb.exe reverse --list
    curl http://127.0.0.1:8765/health

`health` 中应看到：

    "connected": true,
    "link": "USB"

再次停止：

    powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-usb-watch.ps1

## 说明

adb 属于 Google Android SDK Platform-Tools，不是 PadKey 私有组件。
Android Studio、scrcpy、模拟器等也可能重新启动 adb server。