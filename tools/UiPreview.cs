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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string folder = args.Length == 0 ? "build" : args[0];
        Directory.CreateDirectory(folder);
        var state = new SwitchStatus { Mode = "Automatic", Link = "Disconnected", HotkeyToggleRegistered = true,
            HotkeyAutomaticRegistered = true, SwitchMicrophone = true,
            DefaultNames = Enumerable.Repeat(Settings.MonitorName, 3).ToArray(),
            DefaultIds = Enumerable.Repeat("monitor", 3).ToArray(),
            CaptureDefaultNames = Enumerable.Repeat(Settings.FallbackMicrophoneName, 3).ToArray(),
            CaptureDefaultIds = Enumerable.Repeat("desktop-mic", 3).ToArray(),
            CaptureEndpoints = new[] { new Endpoint { Id = "headset-mic", Name = Settings.HeadsetMicrophoneName, Flow = 1, State = 1 },
                new Endpoint { Id = "desktop-mic", Name = Settings.FallbackMicrophoneName, Flow = 1, State = 1 } },
            Endpoints = new[] { new Endpoint { Id = "headset", Name = Settings.HeadsetName, State = 1 },
                new Endpoint { Id = "monitor", Name = Settings.MonitorName, State = 1 },
                new Endpoint { Id = "reference", Name = Settings.ReferenceName, State = 1 } } };
        string chosen = null;
        Settings saved = null;
        using (var form = new SettingsForm(delegate { return state; }, delegate(SwitchMode mode) { chosen = mode.ToString(); },
            delegate { }, new Settings(), delegate(Settings value) { saved = value; }, true))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, -3000);
            form.Show(); Application.DoEvents();
            Capture(form, folder, "ui-monitor");
            var buttons = Descendants(form).OfType<Button>().Where(b => new[] { "自动跟随", "使用耳机", "使用桌面", "暂停切换" }.Contains(b.Text)).ToArray();
            if (buttons.Length != 4) throw new Exception("Expected all four mode controls.");
            foreach (var button in buttons)
            {
                chosen = null; button.PerformClick();
                if (chosen == null) throw new Exception("Mode button did not invoke its action: " + button.Text);
            }
            state.Link = "Connected"; state.DefaultNames = Enumerable.Repeat(Settings.HeadsetName, 3).ToArray();
            state.DefaultIds = Enumerable.Repeat("headset", 3).ToArray();
            state.CaptureDefaultNames = Enumerable.Repeat(Settings.HeadsetMicrophoneName, 3).ToArray();
            state.CaptureDefaultIds = Enumerable.Repeat("headset-mic", 3).ToArray();
            Capture(form, folder, "ui-headset");
            state.MicrophoneError = "目标麦克风不可用或名称重复，已保留当前输入。请在设备设置中选择。";
            Capture(form, folder, "ui-microphone-error");
            state.MicrophoneError = null;
            var follow = Descendants(form).OfType<CheckBox>().Single(c => c.Text == "麦克风也自动跟随");
            follow.Checked = false;
            if (saved == null || saved.SwitchMicrophone) throw new Exception("Microphone-follow toggle must save its state.");
            state.SwitchMicrophone = false;
            Capture(form, folder, "ui-microphone-disabled");
            follow.Checked = true; state.SwitchMicrophone = true;
            state.CaptureDefaultIds = new[] { "headset-mic", "headset-mic", "other-mic-with-same-name" };
            Capture(form, folder, "ui-mixed-roles");
            if (!Descendants(form).OfType<Label>().Any(l => l.Text == "默认设备角色不一致")) throw new Exception("Identically named inputs with different IDs must show a role mismatch.");
            state.CaptureDefaultIds = Enumerable.Repeat("headset-mic", 3).ToArray();
            state.Link = "Unknown"; state.Error = "目标输出不可用或同名设备有歧义，请在设置中选择设备。";
            Capture(form, folder, "ui-error");
            state.Mode = "Paused"; state.Error = null; form.ClientSize = new Size(704, 570);
            Capture(form, folder, "ui-small");
            form.ClientSize = new Size(840, 1120); form.SetDevicesExpanded(true);
            Capture(form, folder, "ui-settings");
            var selectors = Descendants(form).OfType<ComboBox>().ToArray();
            if (selectors.Length != 5 || selectors.Any(c => c.Items.Count < 2)) throw new Exception("Five device selectors must be populated.");
            if (selectors[1].Items.Cast<Endpoint>().Skip(1).Any(e => e.Flow != 1)) throw new Exception("Microphone selector contains a playback device.");
            selectors[1].SelectedIndex = 1; selectors[3].SelectedIndex = 2;
            Descendants(form).OfType<Button>().Single(b => b.Text == "保存设备").PerformClick();
            if (saved.HeadsetMicrophoneId == "" || saved.FallbackMicrophoneId == "") throw new Exception("Microphone choices were not saved.");
            form.ClientSize = new Size(840, 620); form.SetDevicesExpanded(false);
            form.Scale(new SizeF(1.5F, 1.5F));
            Capture(form, folder, "ui-scaled");
            state.Mode = "Automatic"; form.SetDevicesExpanded(false);
            form.Close();
            if (form.IsDisposed || form.Visible) throw new Exception("Close must hide the window.");
            form.AllowClose = true;
        }
        Console.WriteLine("PASS UI: nine layouts, four mode actions, microphone toggle/selection saves, 150% scaling, and close-to-tray.");
        return 0;
    }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }
    static void Capture(SettingsForm form, string folder, string name)
    {
        form.RefreshStatus(); form.PerformLayout(); Application.DoEvents();
        Control content = form.Controls[0];
        using (var image = new Bitmap(content.Width, content.Height))
        {
            content.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
        }
    }
}
