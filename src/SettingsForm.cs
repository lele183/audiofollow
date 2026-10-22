using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BarracudaSwitch
{
    sealed class SettingsForm : Form
    {
        static readonly Color Ink = Color.FromArgb(28, 65, 86);
        static readonly Color Muted = Color.FromArgb(88, 108, 121);
        static readonly Color Paper = Color.FromArgb(243, 246, 248);
        static readonly Color Accent = Color.FromArgb(239, 180, 87);
        readonly Func<SwitchStatus> read;
        readonly Action<Settings> save;
        readonly Label output, connection, roles, hint, error, saved;
        readonly ComboBox headset, monitor, reference;
        readonly Button automatic, useHeadset, useMonitor, pause, devicesToggle;
        readonly TableLayoutPanel devices;
        readonly CheckBox startup;
        readonly bool preview;
        Settings initial;
        bool populated, changing;
        string endpointFingerprint;
        public bool AllowClose;

        public SettingsForm(SwitchEngine engine, Settings settings, HotkeyWindow hotkeys, Action<Settings> save)
            : this(delegate { return engine.Status; }, engine.SetMode, engine.Reconnect, settings, save, null) { }

        internal SettingsForm(Func<SwitchStatus> read, Action<SwitchMode> setMode, Action reconnect,
            Settings settings, Action<Settings> save, bool? previewStartup)
        {
            this.read = read; this.save = save; initial = settings; preview = previewStartup.HasValue;
            Text = "Barracuda Switch · 耳机自动切换";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(750, 590);
            MinimumSize = new Size(660, 540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Paper;
            Icon = SystemIcons.Application;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                Padding = new Padding(28, 22, 28, 18), AutoScroll = true };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            Label brand = Label("Barracuda Switch", 13, Ink);
            brand.Font = new Font("Bahnschrift", 15F);
            root.Controls.Add(brand);
            root.Controls.Add(Label("声音，随你切换。", 21, Ink, FontStyle.Bold));
            root.Controls.Add(Label("耳机连上就用耳机，断开就回到显示器。", 10, Muted));

            var hero = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true,
                BackColor = Ink, Padding = new Padding(22, 16, 22, 16), Margin = new Padding(0, 16, 0, 18) };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            connection = Label("正在确认耳机状态", 9, Accent);
            hero.Controls.Add(connection);
            output = Label("正在读取输出…", 20, Color.White, FontStyle.Bold);
            hero.Controls.Add(output);
            roles = Label("正在读取系统、媒体和通话输出", 9, Color.FromArgb(210, 224, 233));
            hero.Controls.Add(roles);
            root.Controls.Add(hero);

            var modes = new TableLayoutPanel { ColumnCount = 4, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            automatic = ModeButton(modes, "自动切换", delegate { setMode(SwitchMode.Automatic); });
            useHeadset = ModeButton(modes, "使用耳机", delegate { setMode(SwitchMode.Headset); });
            useMonitor = ModeButton(modes, "使用显示器", delegate { setMode(SwitchMode.Monitor); });
            pause = ModeButton(modes, "暂停切换", delegate { setMode(SwitchMode.Paused); });
            root.Controls.Add(modes);
            hint = Label("", 9, Muted); hint.Margin = new Padding(0, 8, 0, 8); root.Controls.Add(hint);
            error = Label("", 9, Color.FromArgb(166, 53, 37)); root.Controls.Add(error);

            devicesToggle = new Button { Text = "设备设置  +", FlatStyle = FlatStyle.Flat, ForeColor = Ink,
                BackColor = Paper, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 5, 8, 5),
                Margin = new Padding(0, 6, 0, 0), Cursor = Cursors.Hand };
            devicesToggle.FlatAppearance.BorderSize = 0;
            devicesToggle.Click += delegate { SetDevicesExpanded(!devices.Visible); };
            root.Controls.Add(devicesToggle);
            devices = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top,
                BackColor = Color.White, Padding = new Padding(16), Visible = false, Margin = new Padding(0, 4, 0, 12) };
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            devices.Controls.Add(Label("耳机连接时", 9, Muted)); headset = Combo(devices);
            devices.Controls.Add(Label("耳机断开时", 9, Muted)); monitor = Combo(devices);
            devices.Controls.Add(Label("关联的 2.4G 音频设备", 9, Muted)); reference = Combo(devices);
            headset.DropDownClosed += delegate { RefreshStatus(); };
            monitor.DropDownClosed += delegate { RefreshStatus(); };
            reference.DropDownClosed += delegate { RefreshStatus(); };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0) };
            ActionButton(actions, "保存设备", SaveSelection);
            ActionButton(actions, "重新检测", reconnect);
            saved = Label("", 9, Muted); actions.Controls.Add(saved);
            devices.Controls.Add(actions); root.Controls.Add(devices);

            startup = new CheckBox { Text = "登录 Windows 后自动在后台运行", AutoSize = true,
                ForeColor = Ink, Margin = new Padding(0, 14, 0, 10), Checked = previewStartup ?? Startup.Enabled };
            startup.CheckedChanged += delegate {
                if (changing || preview) return;
                try { Startup.Set(startup.Checked); }
                catch (Exception ex) {
                    changing = true; startup.Checked = !startup.Checked; changing = false;
                    MessageBox.Show(this, ex.Message, "无法修改开机启动");
                }
            };
            root.Controls.Add(startup);
            root.Controls.Add(Label("Ctrl + Alt + F10  手动切换     /     Ctrl + Alt + F11  恢复自动", 9, Muted));
            root.Controls.Add(Label("关闭窗口后继续在托盘运行。右键托盘图标可退出。", 9, Muted));
            VisibleChanged += delegate { if (Visible) RefreshStatus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!AllowClose && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            RefreshStatus();
        }

        internal void SetDevicesExpanded(bool expanded)
        {
            devices.Visible = expanded;
            devicesToggle.Text = expanded ? "设备设置  −" : "设备设置  +";
        }

        static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, style), Margin = new Padding(0, 3, 0, 4), UseMnemonic = false };
        }
        static ComboBox Combo(TableLayoutPanel grid)
        {
            var value = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 10), DropDownWidth = 600 };
            grid.Controls.Add(value); return value;
        }
        static Button ModeButton(TableLayoutPanel row, string text, Action click)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, Height = 44,
                FlatStyle = FlatStyle.Flat, ForeColor = Ink, BackColor = Color.White,
                Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand, AccessibleName = text };
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 220, 226);
            button.Click += delegate { click(); }; row.Controls.Add(button); return button;
        }
        static void ActionButton(FlowLayoutPanel row, string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
                ForeColor = Ink, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 10, 0) };
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 220, 226);
            button.Click += delegate { click(); }; row.Controls.Add(button);
        }
        static void SelectMode(Button button, bool selected)
        {
            Color back = selected ? Ink : Color.White;
            if (button.BackColor != back) button.BackColor = back;
            button.ForeColor = selected ? Color.White : Ink;
            button.AccessibleDescription = selected ? "当前模式" : "切换到此模式";
        }
        static void Fill(ComboBox combo, Endpoint[] values, string id, string defaultName)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                combo.Items.Add(new Endpoint { Id = "", Name = "自动匹配：" + defaultName, State = 1 });
                foreach (Endpoint value in values.OrderBy(e => !e.Active).ThenBy(e => e.Name)) combo.Items.Add(value);
                combo.SelectedIndex = 0;
                for (int i = 1; i < combo.Items.Count; i++) if (((Endpoint)combo.Items[i]).Id == id) combo.SelectedIndex = i;
                if (!string.IsNullOrEmpty(id) && combo.SelectedIndex == 0)
                {
                    combo.Items.Add(new Endpoint { Id = id, Name = "已保存的设备目前不存在：" + id, State = 4 });
                    combo.SelectedIndex = combo.Items.Count - 1;
                }
            }
            finally { combo.EndUpdate(); }
        }
        void SaveSelection()
        {
            if (!populated || headset.SelectedItem == null || monitor.SelectedItem == null || reference.SelectedItem == null) return;
            var value = new Settings {
                HeadsetEndpointId = ((Endpoint)headset.SelectedItem).Id,
                MonitorEndpointId = ((Endpoint)monitor.SelectedItem).Id,
                ReferenceEndpointId = ((Endpoint)reference.SelectedItem).Id
            };
            try { if (!preview) value.Save(); initial = value; save(value); saved.Text = "已保存"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存设置"); }
        }
        public void RefreshStatus()
        {
            SwitchStatus value = read();
            connection.Text = TrayApp.LinkText(value.Link);
            string[] names = value.DefaultNames;
            if (names != null && names.Length == 3 && names.Any(n => !string.IsNullOrEmpty(n) && n != "(无默认设备)"))
            {
                output.Text = names[1] == Settings.MonitorName ? "正在使用显示器" :
                    names[1] == Settings.HeadsetName ? "正在使用耳机" : "当前输出：" + (names[1] ?? "无");
                roles.Text = names.All(n => n == names[1])
                    ? names[1] + "\r\n系统、媒体、通话输出已统一"
                    : "系统：" + names[0] + "\r\n媒体：" + names[1] + "\r\n通话：" + names[2];
            }
            else { output.Text = "正在读取输出…"; roles.Text = "稍后将显示实际使用的播放设备"; }
            SelectMode(automatic, value.Mode == "Automatic"); SelectMode(useHeadset, value.Mode == "Headset");
            SelectMode(useMonitor, value.Mode == "Monitor"); SelectMode(pause, value.Mode == "Paused");
            hint.Text = value.Mode == "Automatic" ? (value.Link == "Unknown" ? "连接状态尚未确认，暂时保留当前输出。" : "自动跟随耳机连接状态，无需手动选择。") :
                value.Mode == "Paused" ? "已暂停。点击「自动切换」继续跟随耳机。" : "已固定输出。点击「自动切换」恢复跟随耳机。";
            error.Text = value.Error ?? (!value.HotkeyToggleRegistered || !value.HotkeyAutomaticRegistered
                ? "部分快捷键不可用，请使用窗口按钮或托盘菜单。" : "");
            error.Visible = error.Text.Length > 0;
            if (value.Endpoints != null)
            {
                string fingerprint = string.Join("|", value.Endpoints.Select(e => e.Id + ":" + e.State + ":" + e.Name));
                if (fingerprint != endpointFingerprint && !headset.DroppedDown && !monitor.DroppedDown && !reference.DroppedDown)
                {
                    string h = populated ? ((Endpoint)headset.SelectedItem).Id : initial.HeadsetEndpointId;
                    string m = populated ? ((Endpoint)monitor.SelectedItem).Id : initial.MonitorEndpointId;
                    string r = populated ? ((Endpoint)reference.SelectedItem).Id : initial.ReferenceEndpointId;
                    Fill(headset, value.Endpoints, h, Settings.HeadsetName); Fill(monitor, value.Endpoints, m, Settings.MonitorName);
                    Fill(reference, value.Endpoints, r, Settings.ReferenceName); populated = true; endpointFingerprint = fingerprint;
                }
            }
        }
    }
}
