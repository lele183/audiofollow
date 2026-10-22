using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BarracudaSwitch;

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
            HotkeyAutomaticRegistered = true, DefaultNames = Enumerable.Repeat(Settings.MonitorName, 3).ToArray(),
            Endpoints = new[] { new Endpoint { Id = "headset", Name = Settings.HeadsetName, State = 1 },
                new Endpoint { Id = "monitor", Name = Settings.MonitorName, State = 1 },
                new Endpoint { Id = "reference", Name = Settings.ReferenceName, State = 1 } } };
        string chosen = null;
        using (var form = new SettingsForm(delegate { return state; }, delegate(SwitchMode mode) { chosen = mode.ToString(); },
            delegate { }, new Settings(), delegate { }, true))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, -3000);
            form.Show(); Application.DoEvents();
            Capture(form, folder, "ui-monitor");
            foreach (var button in Descendants(form).OfType<Button>().Where(b => new[] { "自动切换", "使用耳机", "使用显示器", "暂停切换" }.Contains(b.Text)))
            {
                chosen = null; button.PerformClick();
                if (chosen == null) throw new Exception("Mode button did not invoke its action: " + button.Text);
            }
            state.Link = "Connected"; state.DefaultNames = Enumerable.Repeat(Settings.HeadsetName, 3).ToArray();
            Capture(form, folder, "ui-headset");
            state.Link = "Unknown"; state.Error = "目标输出不可用或同名设备有歧义，请在设置中选择设备。";
            Capture(form, folder, "ui-error");
            state.Mode = "Paused"; state.Error = null; form.ClientSize = new Size(650, 560);
            Capture(form, folder, "ui-small");
            form.ClientSize = new Size(750, 920); form.SetDevicesExpanded(true);
            Capture(form, folder, "ui-settings");
            form.ClientSize = new Size(750, 590); form.SetDevicesExpanded(false);
            form.Scale(new SizeF(1.5F, 1.5F));
            Capture(form, folder, "ui-scaled");
            state.Mode = "Automatic"; form.SetDevicesExpanded(false);
            form.Close();
            if (form.IsDisposed || form.Visible) throw new Exception("Close must hide the window.");
            form.AllowClose = true;
        }
        Console.WriteLine("PASS UI renders six layouts, four mode actions, 150% scaling, and close-to-tray.");
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
