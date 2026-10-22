using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AudioFollow;

class UiPreview
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string folder = args.Length == 0 ? "build" : args[0]; Directory.CreateDirectory(folder);
        string usbContainer = "11111111-2222-3333-4444-555555555555", btContainer = "22222222-2222-3333-4444-555555555555";
        var state = new SwitchStatus { Mode = "Automatic", Link = "Disconnected", StableLink = "Disconnected", HotkeyToggleRegistered = true,
            HotkeyAutomaticRegistered = true, SwitchMicrophone = true,
            Trigger = new TriggerSettings { Kind = TriggerKind.Hid, DeviceId = "hid-a", DisplayName = "Barracuda X 2022" },
            DefaultNames = Enumerable.Repeat(Settings.MonitorName, 3).ToArray(), DefaultIds = Enumerable.Repeat("monitor", 3).ToArray(),
            CaptureDefaultNames = Enumerable.Repeat(Settings.FallbackMicrophoneName, 3).ToArray(), CaptureDefaultIds = Enumerable.Repeat("desktop-mic", 3).ToArray(),
            CaptureEndpoints = new[] { new Endpoint { Id = "headset-mic", Name = Settings.HeadsetMicrophoneName, Flow = 1, State = 1 },
                new Endpoint { Id = "desktop-mic", Name = Settings.FallbackMicrophoneName, Flow = 1, State = 1 },
                new Endpoint { Id = "usb-mic", Name = "USB 桌面声卡 · 麦克风", ContainerId = usbContainer, Transport = "USB", Flow = 1, State = 1 },
                new Endpoint { Id = "bt-mic", Name = "蓝牙耳机 · 通话麦克风", ContainerId = btContainer, Transport = "Bluetooth", Flow = 1, State = 8 } },
            Endpoints = new[] { new Endpoint { Id = "headset", Name = Settings.HeadsetName, State = 1 }, new Endpoint { Id = "monitor", Name = Settings.MonitorName, State = 1 },
                new Endpoint { Id = "reference", Name = Settings.ReferenceName, State = 1 },
                new Endpoint { Id = "usb-output", Name = "USB 桌面声卡 · 声音输出", ContainerId = usbContainer, Transport = "USB", State = 1 },
                new Endpoint { Id = "bt-output", Name = "蓝牙耳机 · 高质量声音输出", ContainerId = btContainer, Transport = "Bluetooth", State = 8 } } };
        var hid = new TriggerCandidate { Settings = state.Trigger, Status = "接收器在场" };
        var usb = new TriggerCandidate { Settings = new TriggerSettings { Kind = TriggerKind.UsbAudio, DeviceId = "usb:" + usbContainer, ContainerId = usbContainer, DisplayName = "USB 桌面声卡" }, Status = "音频可用" };
        var bt = new TriggerCandidate { Settings = new TriggerSettings { Kind = TriggerKind.Bluetooth, DeviceId = "bt-a", ContainerId = btContainer, ProtocolId = BluetoothIdentity.Classic,
            DisplayName = "我的蓝牙耳机 · 很长的设备名称用于检查窗口缩放与列表展示" }, Status = "已配对，未连接" };
        Settings saved = null; string chosen = null; int writes = 0;
        using (var form = new SettingsForm(delegate { return state; }, delegate(SwitchMode mode) { chosen = mode.ToString(); }, delegate { }, new Settings(),
            delegate(Settings value) { saved = value; writes++; }, true, delegate(TriggerKind kind) { return new[] { kind == TriggerKind.Hid ? hid : kind == TriggerKind.UsbAudio ? usb : bt }; }))
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-3000, -3000); form.Show(); Application.DoEvents();
            Capture(form, folder, "ui-monitor");
            var buttons = Descendants(form).OfType<Button>().Where(b => new[] { "自动跟随", "连接时设备", "断开时设备", "暂停切换" }.Contains(b.Text)).ToArray();
            Assert(buttons.Length == 4, "four mode controls");
            foreach (var button in buttons) { chosen = null; button.PerformClick(); Assert(chosen != null, "mode invokes action"); }
            state.Link = "Connected"; state.StableLink = "Connected"; state.DefaultNames = Enumerable.Repeat(Settings.HeadsetName, 3).ToArray();
            state.DefaultIds = Enumerable.Repeat("headset", 3).ToArray(); state.CaptureDefaultNames = Enumerable.Repeat(Settings.HeadsetMicrophoneName, 3).ToArray();
            state.CaptureDefaultIds = Enumerable.Repeat("headset-mic", 3).ToArray(); Capture(form, folder, "ui-headset");
            state.MicrophoneError = "目标麦克风暂不可用，已保留当前输入。"; Capture(form, folder, "ui-microphone-error"); state.MicrophoneError = null;
            form.ClientSize = new Size(860, 1100); form.SetDevicesExpanded(true); Application.DoEvents();
            Assert(Descendants(form).OfType<ComboBox>().Count() == 7, "seven selectors including trigger type and identity");
            Capture(form, folder, "ui-settings-hid");
            CheckBox follow = Descendants(form).OfType<CheckBox>().Single(c => c.Text == "麦克风也自动跟随");
            follow.Checked = false; Assert(writes == 0, "microphone checkbox edits are drafts"); follow.Checked = true;
            ComboBox method = Selector(form, "连接方式"), trigger = Selector(form, "触发设备");
            method.SelectedIndex = 1; trigger.SelectedIndex = 0;
            Assert(writes == 0, "switching trigger only changes draft");
            Capture(form, folder, "ui-settings-usb-draft");
            Button(form, "检查选中设备").PerformClick(); Assert(writes == 0, "inspection never saves");
            Button(form, "保存并应用").PerformClick();
            Assert(saved != null && saved.Trigger.Kind == TriggerKind.UsbAudio && saved.Connected.Output.EndpointId == "usb-output" &&
                saved.Connected.Microphone.EndpointId == "usb-mic", "unique output and microphone recommendations saved");
            Assert(saved.Disconnected.Output.EndpointId == "monitor" && saved.Disconnected.Microphone.EndpointId == "desktop-mic", "fallback group preserved on trigger change");
            method.SelectedIndex = 2; trigger.SelectedIndex = 0; Capture(form, folder, "ui-settings-bluetooth");
            Selector(form, "连接时麦克风").SelectedIndex = 0; Button(form, "保存并应用").PerformClick();
            Assert(saved.Trigger.Kind == TriggerKind.Bluetooth && saved.Connected.Microphone.KeepCurrent, "Bluetooth and Keep save explicitly");
            Assert(saved.Connected.Output.EndpointId == "bt-output", "unavailable selected output remains pinned");
            state.CaptureDefaultIds = new[] { "headset-mic", "headset-mic", "other-mic" }; Capture(form, folder, "ui-mixed-roles");
            Assert(Descendants(form).OfType<Label>().Any(l => l.Text == "默认设备角色不一致"), "role mismatch is based on identity");
            state.Link = "Unknown"; state.StableLink = "Unknown"; state.Evidence = "蓝牙连接属性暂不可用"; state.Error = "指定输出不可用，保留当前输出。";
            Capture(form, folder, "ui-unknown");
            state.Mode = "Paused"; state.Error = null; form.SetDevicesExpanded(false); form.ClientSize = new Size(750, 570); Capture(form, folder, "ui-small");
            form.ClientSize = new Size(860, 1100); form.SetDevicesExpanded(true); form.Scale(new SizeF(1.5F, 1.5F)); Capture(form, folder, "ui-scaled");
            form.Close(); Assert(!form.IsDisposed && !form.Visible, "close hides to tray"); form.AllowClose = true;
        }
        Console.WriteLine("PASS UI: 10 layouts; all modes; draft isolation; read-only inspection; USB/Bluetooth recommendations; fallback preservation; Keep; long names; missing devices; 150% scaling; close-to-tray.");
        return 0;
    }
    static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL " + name); }
    static ComboBox Selector(Control root, string name) { return Descendants(root).OfType<ComboBox>().Single(c => c.AccessibleName == name); }
    static Button Button(Control root, string text) { return Descendants(root).OfType<Button>().Single(b => b.Text == text); }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; }
    }
    static void Capture(SettingsForm form, string folder, string name)
    {
        form.RefreshStatus(); form.PerformLayout(); Application.DoEvents(); Control content = form.Controls[0];
        using (var image = new Bitmap(content.Width, content.Height)) { content.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png); }
    }
}