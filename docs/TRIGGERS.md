# 触发设备与连接检测

v1.3 将触发条件与切换目标分开：`ITriggerDetector` 只报告状态、身份、原因和观测时间；`TriggerSession` 负责检测器生命周期与防抖；`SwitchEngine` 根据稳定状态选择两个 `RouteTargets`，分别处理输出和麦克风。

## 三种检测器

- `HidTriggerDetector` 包装原 `HeadsetSensor`。固定 VID 1532 / PID 0552、UsagePage FF00、64 字节报告，保留 E3 匹配 ACK、15 秒查询、45 秒超时保护和被动无线事件。选中接收器按完整 HID 路径绑定；旧配置首次发现唯一接收器后也固定该路径。多个未绑定的匹配接收器保持未知。只发送原有只读状态命令。
- `UsbAudioDetector` 将 CoreAudio 端点经 PnP 容器和 USB 祖先关联。容器至少一个播放/录音端点 Active 时可用；全部禁用、拔出或移除时断开。不能据此推断常插接收器对应无线耳机的电源。音频通知触发快照刷新，另有 30 秒兜底。
- `BluetoothTriggerDetector` 使用 WinRT `DeviceWatcher`，类型 AssociationEndpoint，订阅 Added、Updated、Removed、EnumerationCompleted、Stopped。固定 AEP ID 和 ProtocolId，读取 `System.Devices.Aep.IsConnected`。IsPaired 只过滤候选，不作为在线证据。当前选中 ID 每 30 秒用 `CreateFromIdAsync` 复查，15 秒超时变未知并重试。初始化及枚举未完成时不把缺席猜成断开。

蓝牙经典与 LE AEP 的协议标识分开。经典连接不能被同容器的 BLE 电量服务替代；LE 只有同时存在 PnP 证明的 LE 音频端点时才列为候选和接收连接证据。列表可先显示已收到的候选；完整蓝牙枚举可能持续较久。暂时没有可确认身份的设备会显示原因并允许刷新，用户不必填写硬件编号。

## 生命周期与防抖

每次保存或恢复后替换当前检测器，先释放旧实例并清空防抖。连接 500ms、断开/接收器缺失 1500ms，Unknown 立即撤销待确认状态。稳定计时使用单调时钟，离线测试可直接控制毫秒值。

旧回调只持有旧源的对象，不能向新检测器注入读数。蓝牙回调校验观察器实例并在锁内检查 disposed；异步复查同时校验生命周期代数与事件修订号，防止过期快照覆盖新事件。退出时取消异步操作、注销所有 WinRT 事件并停止观察器。持续运行的只有当前触发器；界面临时列表和只读检查有超时、取消与独立释放。

## 身份、建议与目标

`TriggerSettings` 保存检测类型、固定 DeviceId、ContainerId、ProtocolId、展示名称。USB ID 为容器 ID；蓝牙为 AEP ID + 协议身份；HID 为完整接口路径。驱动重装后身份改变需要重选，名称不能代替身份。

`EndpointTarget` 明确区分 KeepCurrent 与固定 EndpointId。固定目标缺失会报错并保留该方向当前设备，绝不自动选同名端点。只有同容器、同流向且唯一的候选才自动推荐；多个候选（包括不可用候选）不猜测。蓝牙候选的实际连接验证和 LE 音频关联仍需更多实机覆盖。

旧设置在内存中 Upgrade，首次保存前原样备份为 `settings.json.pre-v1.3.bak`。界面的编辑、列表刷新与检查不会替换运行配置。手动/暂停不停止检测，也不会被自动规则覆盖。

## 验证边界

88 项离线检查和实机当前状态检查已执行。模拟的 USB 禁用/移除、蓝牙连接变化、睡眠后事件和防抖不能代替物理验收；正式 v1.3.0 仍以三类设备的实机开关/插拔/连接循环通过为条件。当前发行是预览版，见 [验收记录](ACCEPTANCE-v1.3.md)。