# 声随 AudioFollow 使用说明

AudioFollow 是 Windows 11 原生托盘应用。耳机连接时切换到耳机输出和耳机麦克风，断开时回到桌面输出和备用麦克风。新名称不再使用耳机品牌；当前连接检测仍专门适配 `1532:0552` 接收器。

## 设备与规则

| 条件 | 播放设备默认值 | 录音设备默认值 |
| --- | --- | --- |
| 耳机已与 2.4G 接收器连接 | `扬声器 (7.1 Surround Sound)` | `麦克风 (Razer Barracuda X 2.4)` |
| 关闭、无线断开、切到蓝牙或接收器拔出 | `XV271U M3 (NVIDIA High Definition Audio)` | `麦克风 (Realtek(R) Audio)` |
| HID 状态未知 | 保留当前输出 | 保留当前输入 |

关联播放端点是 `扬声器 (Razer Barracuda X 2.4)`。接收器保持插入时，这个端点通常仍处于 Active，程序通过 HID 无线状态确认耳机连接，不用端点 Active 推断耳机电源。

播放与录音分别设置系统、媒体、通信三种角色，然后回读 Windows 验证。麦克风不可用、名称重复或切换失败时，保留原输入并显示输入错误，输出切换独立处理。失败时会尝试还原已改变的默认角色。程序不录音、不改音量或驱动设置。

有多个同名活动设备时，请明确选择。保存了端点 ID 后，设备暂时不可用不会被同名其他设备替代；驱动重装后可重新选择，或切回“自动匹配”。

## 安装与升级

在下载包解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -StartNow
```

默认安装位置 `%LOCALAPPDATA%\Programs\AudioFollow`，数据目录 `%LOCALAPPDATA%\AudioFollow`。不需要管理员权限。`-NoStartup` 不启用登录启动；不传 `-StartNow` 时只安装。

从旧版 Barracuda Switch 升级时：

1. 正常退出正在运行的旧版和新版实例。
2. 新版没有设置时，复制 `%LOCALAPPDATA%\BarracudaSwitch\settings.json` 到新目录，保留原文件。
3. 保留原输出选择，新增麦克风按上述完整名称匹配，“麦克风也自动跟随”默认启用。
4. 移除指向已知旧安装目录的 `BarracudaSwitch` 登录项，注册新版本；若旧项指向其他位置，提示从旧位置关闭。
5. 旧版程序、设置、日志均保留，不再同时运行。

便携模式也能读取旧设置，但不会自动退出旧程序或处理旧登录项，升级推荐使用安装器。新数据目录已有设置时优先使用，不覆盖它。

## 窗口与托盘

双击 `AudioFollow.exe` 或托盘图标打开设置，点击模式按钮选择自动、耳机、桌面或暂停。

- “自动跟随”：根据真实连接状态选择两组设备。
- “使用耳机 / 使用桌面”：立即固定对应设备组，直到恢复自动或重启程序。
- “暂停切换”：保留当前输出和输入。
- “麦克风也自动跟随”：取消后只切换输出，保留输入。
- “配置设备”：分别设置连接、断开时的输出和麦克风，再“保存设备”。

两个设备卡片展示实际 Windows 默认设备及三角色是否一致。连接未知或设备错误时会提示下一步。窄窗口和展开设置可滚动；所有控件可用 Tab、方向键及空格操作。

关闭窗口会隐藏到托盘，Windows 11 可能把图标放在任务栏 `^` 内。托盘右键“退出”或 `Stop.cmd` 停止程序。退出时保留当前默认设备，下次启动回到自动模式。

`Ctrl + Alt + F10` 在耳机和桌面设备间手动切换，`Ctrl + Alt + F11` 恢复自动。快捷键被占用时窗口显示提示，仍可使用模式按钮或托盘菜单。

## 登录启动

勾选“登录 Windows 后在后台启动”，或执行 `Enable-Startup.cmd`。它添加当前用户：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AudioFollow
"<当前程序目录>\AudioFollow.exe" --background
```

取消勾选或 `Disable-Startup.cmd` 撤销。`Stop.cmd` 只停止进程，不改变登录项。移动便携目录后，须在新位置重新启用登录启动；实际注销再登录后的启动应单独验证。

## 实机检查

1. 保持接收器插入，关闭耳机，检查输出回到显示器，输入回到 Realtek 或你选择的备用麦克风。
2. 打开耳机并等待无线连接，检查输出为 7.1，输入为 Razer 或配置的耳机麦克风。
3. 再关闭耳机，两组默认设备应回到桌面配置，无需拔接收器。
4. 用快捷键测试手动切换，恢复自动后关闭设置窗口，确认托盘继续运行。
5. 在使用系统默认设备的软件中试听和测试麦克风。端点回读只能证明 Windows 设置，不能证明实际出声或录音质量。

程序优先处理无线事件，每 15 秒发送一次只读状态查询。音频变化由 Windows 通知触发，另有 30 秒快照兜底。设备和 HID 工作在后台线程。

## 命令行

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

控制命令需要当前托盘进程。`--status` 通过当前用户命名管道读取活状态；`--diagnose` 独立回读播放和录音默认设备，并附进程状态。未运行时它独立查询 HID。

停止托盘应用后可执行：

```powershell
.\AudioSwitchCtl.exe --probe 30
.\AudioSwitchCtl.exe --verify-routing
```

`--probe` 输出协议查询和无线事件，不切换设备。`--verify-routing` 会实际测试两个输出及两个麦克风（跟随启用时）的全部默认角色，并在 finally 中恢复原输出和输入，再次回读验证。

## 设置、日志与卸载

`%LOCALAPPDATA%\AudioFollow\` 包含：

- `settings.json`：五个设备选择和麦克风跟随选项。新版缺少设置时也会尝试读取旧目录设置。
- `status.json`：状态变化立即更新，空闲每 30 秒心跳；正常退出删除。崩溃后可能残留，实时状态以 `--status` 为准。
- `switcher.log`：模式、连接和设备切换结果；512 KiB 轮转，最多一份备份。

日志和诊断含本机设备 ID，分享前请自行检查。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\AudioFollow\uninstall.ps1"
```

默认删除新版登录启动、停止新版并移除默认安装目录和新数据。加 `-KeepSettings` 保留设置和日志；自定义或便携安装目录不会递归删除。旧版安装和数据始终保留。

## 检测协议与构建

硬件范围为 Razer Barracuda X 2022 / Macronix USB 接收器 `1532:0552`，厂商 HID 集合 `FF00`。仅发出填充到 64 字节的只读命令：

```text
01 80 06 50 41 0E SEQ 01 E3
```

校验对应 SEQ 的成功 ACK 后，接收 E3 `00` / `01`。被动无线事件必须包含完整 `20 02 01` 标记和合法连接值。电量、充电、媒体按键、USB 存在、超时都不是开关机证据。超过 45 秒没有经过校验的状态时保留当前设备。不发送配对、扫描、固件或耳机控制命令。

源码目录运行 `Build.ps1`，生成 `dist\AudioFollow\AudioFollow.exe` 与控制程序。`Test.ps1` 覆盖 39 项检查，`Preview.ps1` 验证九个布局和控件操作，`Package.ps1` 输出发行 ZIP 与 SHA-256。使用 Windows 自带 .NET Framework 编译器，无 NuGet。模拟协议和 UI 检查不能代替物理连接、实际听音或录音验证。

协议来源及第三方许可见 `THIRD-PARTY-NOTICES.txt`。本项目与 Razer Inc. 无关联，默认设备切换使用未公开的 Windows PolicyConfig COM 接口。
