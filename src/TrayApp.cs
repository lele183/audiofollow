using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BarracudaSwitch
{
    public sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly SwitchEngine engine;
        readonly Action show;
        public bool ToggleRegistered, AutoRegistered;
        public HotkeyWindow(SwitchEngine engine, Action show)
        {
            this.engine = engine;
            this.show = show;
            CreateHandle(new CreateParams { Caption = "BarracudaSwitch events" });
            ToggleRegistered = RegisterHotKey(Handle, 1, 0x4003, 0x79);
            AutoRegistered = RegisterHotKey(Handle, 2, 0x4003, 0x7a);
            engine.SetHotkeys(ToggleRegistered, AutoRegistered);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1) engine.Toggle();
                if (m.WParam.ToInt32() == 2) engine.SetMode(SwitchMode.Automatic);
            }
            else if (m.Msg == 0x0218 && (m.WParam.ToInt32() == 7 || m.WParam.ToInt32() == 18))
                engine.Reconnect();
            else if (m.Msg == 0x0219 && (m.WParam.ToInt32() == 0x8000 || m.WParam.ToInt32() == 0x8004))
                engine.Reconnect();
            else if (m.Msg == 0x8001) show();
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); DestroyHandle();
        }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    }

    public sealed class TrayApp : ApplicationContext
    {
        readonly SwitchEngine engine;
        readonly NotifyIcon tray;
        readonly Control dispatcher = new Control();
        readonly Timer timer = new Timer { Interval = 750 };
        readonly HotkeyWindow hotkeys;
        readonly Ipc ipc;
        SettingsForm form;
        Settings settings;
        readonly ToolStripMenuItem auto, headset, monitor, pause;

        public TrayApp(bool background)
        {
            settings = Settings.Load();
            engine = new SwitchEngine(settings);
            IntPtr dispatcherHandle = dispatcher.Handle;
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            auto = new ToolStripMenuItem("自动：耳机开机 → 7.1；关机 → 显示器", null, delegate { engine.SetMode(SwitchMode.Automatic); });
            headset = new ToolStripMenuItem("手动使用耳机", null, delegate { engine.SetMode(SwitchMode.Headset); });
            monitor = new ToolStripMenuItem("手动使用显示器", null, delegate { engine.SetMode(SwitchMode.Monitor); });
            pause = new ToolStripMenuItem("暂停切换", null, delegate { engine.SetMode(SwitchMode.Paused); });
            menu.Items.AddRange(new ToolStripItem[] { auto, headset, monitor, pause, new ToolStripSeparator() });
            menu.Items.Add("重新检测设备", null, delegate { engine.Reconnect(); });
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = iconGood, Text = "耳机自动切换", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            hotkeys = new HotkeyWindow(engine, ShowSettings);
            ipc = new Ipc(HandleCommand);
            timer.Tick += delegate {
                SwitchStatus status = engine.Status;
                string text = "耳机自动切换 · " + LinkText(status.Link);
                tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                tray.Icon = status.Link == "Connected" || status.Link == "Disconnected" ? iconGood : iconUnknown;
                auto.Checked = status.Mode == "Automatic"; headset.Checked = status.Mode == "Headset";
                monitor.Checked = status.Mode == "Monitor"; pause.Checked = status.Mode == "Paused";
            };
            timer.Start();
            AppFiles.Log("START version=1.0.0; toggleHotkey=" + hotkeys.ToggleRegistered + "; autoHotkey=" + hotkeys.AutoRegistered);
            if (!background) ShowSettings();
        }

        string HandleCommand(string command)
        {
            switch (command)
            {
                case "status": return Json.Encode(engine.Status);
                case "headset": engine.SetMode(SwitchMode.Headset); break;
                case "monitor": engine.SetMode(SwitchMode.Monitor); break;
                case "auto": engine.SetMode(SwitchMode.Automatic); break;
                case "pause": engine.SetMode(SwitchMode.Paused); break;
                case "toggle": engine.Toggle(); break;
                case "refresh": engine.Reconnect(); break;
                case "show": dispatcher.BeginInvoke((Action)ShowSettings); break;
                case "exit": dispatcher.BeginInvoke((Action)ExitThread); break;
                default: return "ERROR unknown command";
            }
            return "OK";
        }

        public void ShowSettings()
        {
            if (form == null || form.IsDisposed)
                form = new SettingsForm(engine, settings, hotkeys, delegate(Settings value) { settings = value; engine.Configure(value); });
            form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop(); timer.Dispose();
            if (form != null) { form.AllowClose = true; form.Close(); form.Dispose(); }
            ipc.Dispose(); hotkeys.Dispose(); engine.Dispose();
            tray.Visible = false; tray.Dispose(); dispatcher.Dispose();
            try { AppFiles.Log("STOP"); } catch { }
            base.ExitThreadCore();
        }

        public static string LinkText(string state)
        {
            switch (state)
            {
                case "Connected": return "耳机已连接";
                case "Disconnected": return "耳机已关闭 / 无线未连接";
                case "ReceiverMissing": return "接收器未插入";
                default: return "正在确认耳机状态";
            }
        }
        readonly Icon iconGood = AppIcon(false), iconUnknown = AppIcon(true);
        static Icon AppIcon(bool unknown)
        {
            using (var image = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    graphics.Clear(Color.FromArgb(28, 65, 86));
                    using (var brush = new SolidBrush(Color.FromArgb(239, 180, 87)))
                    {
                        graphics.FillEllipse(brush, 5, 8, 22, 7);
                        graphics.FillEllipse(brush, 5, 16, 22, 7);
                        graphics.FillRectangle(brush, 4, 12, 5, 8);
                        graphics.FillRectangle(brush, 23, 12, 5, 8);
                    }
                    if (unknown) using (var shade = new SolidBrush(Color.FromArgb(155, 85, 85, 85))) graphics.FillRectangle(shade, 0, 0, image.Width, image.Height);
                }
                IntPtr handle = image.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    }

    sealed class SettingsForm : Form
    {
        readonly SwitchEngine engine;
        readonly Action<Settings> save;
        readonly Label headline, detail, defaults, error, evidence;
        readonly ComboBox headset, monitor, reference;
        readonly CheckBox startup;
        readonly Timer timer = new Timer { Interval = 750 };
        Settings initial;
        bool populated, changing;
        string endpointFingerprint;
        public bool AllowClose;

        public SettingsForm(SwitchEngine engine, Settings settings, HotkeyWindow hotkeys, Action<Settings> save)
        {
            this.engine = engine; this.save = save; initial = settings;
            Text = "耳机自动切换 · Barracuda X";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(690, 640);
            MinimumSize = new Size(650, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 248, 250);
            Icon = System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.ico"))
                ? new Icon(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.ico")) : SystemIcons.Application;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, AutoScroll = true, AutoSize = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(grid);
            headline = AddLabel(grid, "正在确认耳机状态", 18, Color.FromArgb(28, 65, 86));
            detail = AddLabel(grid, "自动模式", 10, Color.DimGray);
            defaults = AddLabel(grid, "正在读取默认输出…", 9, Color.FromArgb(50, 56, 64));
            AddLabel(grid, "耳机连接时", 9, Color.DimGray);
            headset = AddCombo(grid);
            AddLabel(grid, "耳机断开时", 9, Color.DimGray);
            monitor = AddCombo(grid);
            AddLabel(grid, "关联的 2.4G 音频端点", 9, Color.DimGray);
            reference = AddCombo(grid);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 2) };
            grid.Controls.Add(actions);
            AddButton(actions, "自动切换", delegate { engine.SetMode(SwitchMode.Automatic); });
            AddButton(actions, "使用耳机", delegate { engine.SetMode(SwitchMode.Headset); });
            AddButton(actions, "使用显示器", delegate { engine.SetMode(SwitchMode.Monitor); });
            AddButton(actions, "暂停", delegate { engine.SetMode(SwitchMode.Paused); });
            var settingsRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            grid.Controls.Add(settingsRow);
            AddButton(settingsRow, "保存设备选择", SaveSelection);
            AddButton(settingsRow, "重新检测", delegate { engine.Reconnect(); });
            startup = new CheckBox { AutoSize = true, Text = "登录 Windows 后在后台启动", Checked = Startup.Enabled, Margin = new Padding(8, 8, 0, 0) };
            startup.CheckedChanged += delegate {
                if (changing) return;
                try { Startup.Set(startup.Checked); }
                catch (Exception ex) { changing = true; startup.Checked = Startup.Enabled; changing = false; MessageBox.Show(this, ex.Message, "无法修改开机启动"); }
            };
            settingsRow.Controls.Add(startup);
            string hotkeyText = "Ctrl + Alt + F10：手动切换    Ctrl + Alt + F11：恢复自动";
            if (!hotkeys.ToggleRegistered || !hotkeys.AutoRegistered) hotkeyText += "\r\n快捷键被其他程序占用，请使用托盘菜单。";
            AddLabel(grid, hotkeyText + "\r\n关闭窗口后继续在系统托盘运行；退出请右键托盘图标。", 9, Color.DimGray);
            error = AddLabel(grid, "", 9, Color.FromArgb(170, 58, 38));
            evidence = AddLabel(grid, "", 8, Color.DimGray);
            timer.Tick += delegate { RefreshStatus(); };
            timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!AllowClose && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
            RefreshStatus();
        }

        static Label AddLabel(TableLayoutPanel grid, string text, float size, Color color)
        {
            var label = new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), Margin = new Padding(0, 4, 0, 5), MaximumSize = new Size(1200, 0) };
            grid.Controls.Add(label);
            return label;
        }
        static ComboBox AddCombo(TableLayoutPanel grid)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5), DropDownWidth = 620 };
            grid.Controls.Add(combo); return combo;
        }
        static void AddButton(FlowLayoutPanel row, string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 8, 5) };
            button.Click += delegate { click(); };
            row.Controls.Add(button);
        }
        static void Fill(ComboBox combo, Endpoint[] values, string id, string defaultName)
        {
            combo.Items.Clear();
            combo.Items.Add(new Endpoint { Id = "", Name = "自动匹配：" + defaultName, State = 1,
                Diagnostic = "auto:" + (defaultName == Settings.HeadsetName ? "headset" : defaultName == Settings.MonitorName ? "monitor" : "reference") });
            foreach (Endpoint value in values.OrderBy(e => !e.Active).ThenBy(e => e.Name)) combo.Items.Add(value);
            combo.SelectedIndex = 0;
            for (int i = 1; i < combo.Items.Count; i++)
                if (((Endpoint)combo.Items[i]).Id == id) combo.SelectedIndex = i;
            if (!string.IsNullOrEmpty(id) && combo.SelectedIndex == 0)
            {
                combo.Items.Add(new Endpoint { Id = id, Name = "已保存的设备目前不存在：" + id, State = 4 });
                combo.SelectedIndex = combo.Items.Count - 1;
            }
        }
        void SaveSelection()
        {
            if (!populated) return;
            if (headset.SelectedItem == null || monitor.SelectedItem == null || reference.SelectedItem == null)
            {
                MessageBox.Show(this, "设备列表尚未载入完成，请稍后再保存。", "无法保存设置");
                return;
            }
            var value = new Settings {
                HeadsetEndpointId = ((Endpoint)headset.SelectedItem).Id,
                MonitorEndpointId = ((Endpoint)monitor.SelectedItem).Id,
                ReferenceEndpointId = ((Endpoint)reference.SelectedItem).Id
            };
            try { value.Save(); initial = value; save(value); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
        }
        void RefreshStatus()
        {
            SwitchStatus value = engine.Status;
            headline.Text = TrayApp.LinkText(value.Link);
            detail.Text = (value.Mode == "Automatic" ? "自动模式" : value.Mode == "Paused" ? "已暂停" : "手动模式 · 按 Ctrl + Alt + F11 恢复自动") + "   ·   " + value.Message;
            if (value.DefaultNames != null)
                defaults.Text = "系统输出：" + value.DefaultNames[0] + "\r\n媒体输出：" + value.DefaultNames[1] + "\r\n通信输出：" + value.DefaultNames[2];
            error.Text = value.Error ?? "";
            evidence.Text = "检测依据：" + value.Evidence;
            if (value.Endpoints != null)
            {
                string fingerprint = string.Join("|", value.Endpoints.Select(e => e.Id + ":" + e.State));
                if (fingerprint != endpointFingerprint && !headset.DroppedDown && !monitor.DroppedDown && !reference.DroppedDown)
                {
                    string h = populated ? ((Endpoint)headset.SelectedItem).Id : initial.HeadsetEndpointId;
                    string m = populated ? ((Endpoint)monitor.SelectedItem).Id : initial.MonitorEndpointId;
                    string r = populated ? ((Endpoint)reference.SelectedItem).Id : initial.ReferenceEndpointId;
                    Fill(headset, value.Endpoints, h, Settings.HeadsetName);
                    Fill(monitor, value.Endpoints, m, Settings.MonitorName);
                    Fill(reference, value.Endpoints, r, Settings.ReferenceName);
                    populated = true; endpointFingerprint = fingerprint;
                }
            }
        }
    }
}
