# 耳机自动切换 · Barracuda X

适用于这台 Windows 11 电脑的轻量托盘应用。无需安装 Python、音频插件或雷蛇 SDK，使用 Windows 自带的 .NET Framework 4.8。

## 自动切换规则

| 耳机状态 | 默认播放输出 |
| --- | --- |
| Barracuda X 已通过 USB 2.4G 接收器建立无线连接 | `扬声器 (7.1 Surround Sound)` |
| 耳机关闭、无线断开、切到蓝牙模式，或 USB 接收器被拔出 | `XV271U M3 (NVIDIA High Definition Audio)` |
| HID 查询失败、接收器被其他程序独占、状态未知 | 保留当前输出，继续重试；可手动切换 |

同时设置并回读验证 Windows 的系统、媒体、通信三种默认播放角色。默认麦克风和音量不变。播放软件需要选择“默认 / 跟随系统”；固定了具体输出设备的软件可能需要自行改回默认，部分软件需要重开音频流。

关联端点是 `扬声器 (Razer Barracuda X 2.4)`。接收器一直插着时，这个端点仍可能显示可用，因此程序不会用它的 Active 状态判断耳机电源。

硬件检测限定为本机的 `1532:0552` / Macronix / Barracuda X 2022。耳机只连电脑蓝牙、未连 2.4G 接收器时不算 2.4G 在线。

## 运行

在 `dist` 文件夹中：

1. 双击 `BarracudaSwitch.exe` 打开中文设置窗口。默认已匹配上述三个设备。
2. 关闭窗口后，程序继续在托盘运行；Windows 11 可能将图标放在任务栏的 `^` 隐藏区域。
3. 双击托盘图标可重新打开设置。右键可切换模式、重新检测或退出。
4. 只想静默启动时双击 `Start.cmd`。

初次运行默认为自动模式。手动模式或“暂停”会持续到恢复自动状态或退出程序；下次启动仍为自动模式。

程序退出时不会还原音频输出，以免下次开机自动选择已不存在的设备；下一次自动启动会立即按当时耳机状态纠正三个默认输出。

### 安装到固定位置

推荐把程序放到固定目录并启用登录启动，避免移动或清理下载文件夹后启动项失效。在 `dist` 文件夹打开 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -StartNow
```

安装默认位置是 `%LOCALAPPDATA%\Programs\BarracudaSwitch`。脚本会先正常退出旧实例，再复制程序、添加当前用户开机启动，并可选立即后台运行。不需要管理员权限。

卸载：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\BarracudaSwitch\uninstall.ps1"
```

这样会撤销开机启动、退出程序，并删除固定目录和 `%LOCALAPPDATA%\BarracudaSwitch` 里的设置/日志。只想撤销启动而保留设置时使用 `-KeepSettings`；从源码 `dist` 目录直接运行卸载脚本时，脚本会撤销启动和进程，但不会删除源码目录。

## 快捷键

- `Ctrl + Alt + F10`：在 7.1 耳机输出和 XV271U M3 显示器输出之间手动切换。
- `Ctrl + Alt + F11`：恢复自动切换。

如果快捷键已被其他程序占用，设置窗口会提示，仍可使用托盘菜单。

## 开机启动

在窗口中勾选“登录 Windows 后在后台启动”，或运行 `Enable-Startup.cmd`。

它只添加当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\BarracudaSwitch`，指向这份 `BarracudaSwitch.exe --background`，登录后无控制台窗口运行。不需要管理员权限或系统服务。

取消勾选或运行 `Disable-Startup.cmd` 可撤销；`Stop.cmd` 只停止当前进程，开机启动设置不变。移动程序文件夹后，需要从新位置重新启用开机启动。

## 实机验收

1. 保持 USB 2.4G 接收器插入，关闭耳机。默认音频和默认通信输出应为 XV271U M3。
2. 打开耳机，等耳机与接收器建立无线连接。默认输出应变为 7.1 Surround Sound。
3. 再关闭耳机，应回到 XV271U M3。USB 接收器不需要拔出。
4. 按 F10 快捷键测试手动切换，再按 F11 恢复自动。
5. 在默认设备中播放一段已知音频，分别检查显示器和耳机实际出声；应用只能验证系统端点，不能替代听音确认。

被动无线事件通常触发后立即处理；程序还每 15 秒发送一次只读 E3 查询以补齐启动状态、遗漏事件。检测和音频设置均在后台线程，不阻塞界面。

## 命令行

使用 `AudioSwitchCtl.exe`，避免托盘程序无控制台导致看不到诊断输出：

```powershell
.\AudioSwitchCtl.exe --status
.\AudioSwitchCtl.exe --diagnose
.\AudioSwitchCtl.exe --headset
.\AudioSwitchCtl.exe --monitor
.\AudioSwitchCtl.exe --toggle
.\AudioSwitchCtl.exe --auto
.\AudioSwitchCtl.exe --pause
.\AudioSwitchCtl.exe --refresh
.\AudioSwitchCtl.exe --show
.\AudioSwitchCtl.exe --exit
```

控制命令需要托盘程序正在运行。`--status` 从当前进程获取实时状态，不依赖旧状态文件。`--diagnose` 只读：运行中读取进程状态；未运行时独立查询硬件和音频端点。

停止托盘程序后，可以执行：

```powershell
# 输出原始 ACK、E3 和被动事件；持续 30 秒，适合手动开关耳机。
.\AudioSwitchCtl.exe --probe 30

# 短暂将三种播放角色切到耳机、显示器，各自回读校验，然后恢复原来的三个默认输出。
.\AudioSwitchCtl.exe --verify-routing
```

## 设置和日志

`%LOCALAPPDATA%\BarracudaSwitch\`

- `settings.json`：用户保存的端点 ID。不保存音量、麦克风或耳机设置。
- `status.json`：状态变化时更新，空闲时每 30 秒保存心跳；正常退出时移除。进程崩溃后可能遗留，应以 `--status` 的在线响应为准。
- `switcher.log`：启动、模式、真实连接状态和音频切换结果。达到 512 KiB 后保留一份轮转。

默认按完整端点名称匹配，优先要求唯一的活动端点。同名活动设备有多个时，请在设置里明确选择。手动保存的端点 ID 不会悄悄替换为同名其他设备；设备驱动重装后可能需要重新选择或切回“自动匹配”。

## 检测依据

程序通过 Windows SetupAPI 和 HID 查找 `VID_1532&PID_0552` 的 `FF00` 厂商集合。

唯一发送的状态命令是填充到 64 字节的：

```text
01 80 06 50 41 0E SEQ 01 E3
```

先校验对应 SEQ 的成功 ACK，再接收 E3 的 `00` / `01` 连接值。被动事件必须包含完整的 `20 02 01` 标记及合法连接值。电量、充电、媒体按键、USB 存在、查询超时都不会被当作耳机开关机信号。超过 45 秒没有任何经过校验的连接状态时，改为未知并保留当前输出。

不发送配对、扫描、设置路由、固件升级或耳机关机命令。协议来源是 `leag/razer-barracuda` 的 `docs/PROTOCOL.md`；HID 传输部分按 MIT 许可改编，见 `THIRD-PARTY-NOTICES.txt`。

## 构建与测试

源码目录运行：

```powershell
.\Build.ps1
.\Test.ps1
```

`Build.ps1` 使用 Windows 自带 C# 编译器，生成 `dist\BarracudaSwitch.exe` 和 `dist\AudioSwitchCtl.exe`。无需联网、NuGet 或额外 SDK。

自动检查覆盖 ACK 和响应匹配、错误帧、分片、多帧、被动事件、电量误判防护、自动和手动规则、重复设备名称。`--verify-routing` 验证本机 Windows 输出切换。物理开关机和实际听音应按上面的实机步骤验收；模拟帧测试不能证明物理音频已经出声。
