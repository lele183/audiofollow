# 声随 · AudioFollow

让 Windows 11 的声音和麦克风一起跟随耳机：连上用耳机，断开回到桌面设备，接收器可以一直插着。

原生中文托盘应用，使用 Windows 自带的 .NET Framework 4.8，无联网服务、遥测或额外运行时。AudioFollow 是原 Barracuda Switch 的新名称。

![AudioFollow 主界面预览（模拟设备状态）](docs/ui.png)

## 下载与安装

从 [最新 Release](https://github.com/lele183/audiofollow/releases/latest) 下载 `AudioFollow-v1.2.0-win-x64.zip` 并解压。仓库为私有，需要登录获授权的 GitHub 账号。

在解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -StartNow
```

安装到 `%LOCALAPPDATA%\Programs\AudioFollow`，添加当前用户登录启动并开始后台运行。双击托盘图标打开设置；关闭窗口后继续运行，托盘菜单“退出”才停止。也可以直接双击 `AudioFollow.exe` 便携运行。

**从旧版升级：** 安装器正常退出旧实例，将旧设备设置复制到新数据目录，只替换指向已知旧安装位置的登录启动项。旧程序和原始设置保留，新目录已有设置时不会覆盖。升级后请使用 AudioFollow，避免同时启动两版。

## 自动跟随

| 耳机状态 | 默认声音输出 | 默认麦克风 |
| --- | --- | --- |
| 通过 2.4G 接收器连接 | 扬声器 (7.1 Surround Sound) | 麦克风 (Razer Barracuda X 2.4) |
| 耳机关闭、无线断开或接收器拔出 | XV271U M3 (NVIDIA High Definition Audio) | 麦克风 (Realtek(R) Audio) |
| 检测暂不可用 | 保留当前输出 | 保留当前输入 |

播放和录音各自的系统、媒体、通信三种默认角色会一起切换，并独立回读验证。麦克风目标缺失或同名设备有歧义时，保留当前输入并显示原因，声音输出仍能切换。程序只改变默认设备，不录音，也不改音量。

“配置设备”可分别选择耳机连接和断开时的输出与麦克风。取消“麦克风也自动跟随”可单独保留当前输入。模式按钮与快捷键同时控制这两组设备，手动模式持续到恢复自动或重启应用。

| 操作 | 快捷键 |
| --- | --- |
| 手动切换耳机 / 桌面设备 | Ctrl + Alt + F10 |
| 恢复自动跟随 | Ctrl + Alt + F11 |

**硬件范围：目前验证的是 Razer Barracuda X 2022，Macronix USB 2.4G 接收器 `1532:0552`。这不是通用蓝牙连接检测。** 耳机仅通过蓝牙连接时，不会被当作 2.4G 在线。设备名可配置，接收器检测协议仍限定上述型号。

通话和播放软件需要选择“默认 / 跟随系统”。绑定固定设备或已打开音频流的软件可能需要重新选择设备或重启通话。

## v1.2.0

- 增加独立麦克风目标、跟随开关和三角色切换验证；输入故障不阻塞输出切换。
- 更名为“声随 · AudioFollow”，保留旧设置迁移和旧版本历史。
- 并排展示实际输出与输入；清晰的模式选择、折叠设备设置、键盘焦点和可滚动布局。
- 参考 [Appica UI](https://appica.dev/ui) 的基础控件与可访问性交互，保持原生 WinForms，无前端框架依赖。设计来源见 [界面设计说明](docs/DESIGN.md)。
- 保留 v1.1 的持久 HID 读取、音频通知、30 秒兜底快照与状态心跳；隐藏窗口没有刷新定时器。

性能数据和实机验证边界见 [性能记录](docs/PERFORMANCE.md)。

## 开发与验证

Windows 11 x64 + .NET Framework 4.8。使用系统 C# 编译器，无需联网下载依赖。

```powershell
.\Build.ps1      # dist\AudioFollow 中生成程序与安装脚本
.\Test.ps1       # 39 项协议、路由、麦克风选择和旧设置兼容检查
.\Preview.ps1    # 9 个 UI 预览与控件交互，不访问硬件或改变默认设备
.\Package.ps1    # 构建、测试、ZIP 和 SHA-256
```

CI 在 Windows runner 上构建、测试并上传包。实机检查会短暂改变实际默认设备，结束时恢复自动模式：

```powershell
.\tools\Test-Installed.ps1
.\tools\Measure-Performance.ps1 -Seconds 30
```

`src/` 包含 CoreAudio、HID、托盘和界面；`tests/` 为无硬件测试；`installer/` 为安装与升级脚本。`build/`、`dist/`、`release/` 和本地设备设置均不提交到 Git。

## 诊断与卸载

在安装目录运行：

```powershell
.\AudioSwitchCtl.exe --status     # 活进程状态，不依赖状态文件
.\AudioSwitchCtl.exe --diagnose   # 独立回读 Windows 的输出和录音默认设备
.\AudioSwitchCtl.exe --auto
.\AudioSwitchCtl.exe --refresh

powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
# 加 -KeepSettings 可保留新版本设置与日志；旧版文件始终保留
```

运行数据在 `%LOCALAPPDATA%\AudioFollow`。状态未知超过 45 秒时保留当前设备；目标不可用或重名时不会随意换成其他设备。Windows 默认设备设置依赖未公开的 PolicyConfig COM 接口，系统大版本升级后可能需要兼容性更新。

详细操作、命令行与检测依据见 [中文使用说明](README.zh-CN.md)。历史见 [CHANGELOG](CHANGELOG.md)，协议与许可见 [第三方声明](THIRD-PARTY-NOTICES.txt)。本项目与 Razer Inc. 无关联，发行包未作代码签名。
