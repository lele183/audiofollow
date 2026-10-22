using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AudioFollow
{
    static class UiTheme
    {
        public static readonly Color Ink = Color.FromArgb(26, 52, 69);
        public static readonly Color Muted = Color.FromArgb(88, 108, 121);
        public static readonly Color Paper = Color.FromArgb(242, 246, 248);
        public static readonly Color Line = Color.FromArgb(217, 227, 232);
        public static readonly Color Green = Color.FromArgb(30, 112, 81);
        public static GraphicsPath Round(Rectangle bounds, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    sealed class ModeButton : Button
    {
        bool selected;
        public bool Selected { get { return selected; } set { if (selected != value) { selected = value; AccessibleDescription = value ? "当前模式" : "选择此模式"; Invalidate(); } } }
        public ModeButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = UiTheme.Round(box, 9))
            using (var brush = new SolidBrush(selected ? UiTheme.Ink : Color.White))
            using (var pen = new Pen(selected ? UiTheme.Ink : UiTheme.Line))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, selected ? Color.White : UiTheme.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(box, -5, -5), selected ? Color.White : UiTheme.Ink, selected ? UiTheme.Ink : Color.White);
        }
    }

    sealed class RouteCard : Panel
    {
        readonly Label title, device, detail;
        readonly bool microphone;
        float glyphScale = 1;
        public RouteCard(bool microphone)
        {
            this.microphone = microphone; DoubleBuffered = true; BackColor = UiTheme.Paper;
            Height = 168; Dock = DockStyle.Fill; Padding = new Padding(20);
            title = new Label { Text = microphone ? "当前麦克风" : "当前声音输出", AutoSize = true, BackColor = Color.Transparent,
                ForeColor = UiTheme.Muted, Location = new Point(52, 22) };
            device = new Label { Text = "正在读取…", AutoSize = true, ForeColor = UiTheme.Ink, BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), Location = new Point(20, 60) };
            detail = new Label { AutoSize = true, ForeColor = UiTheme.Muted, BackColor = Color.Transparent, Location = new Point(20, 126) };
            Controls.AddRange(new Control[] { title, device, detail });
            Resize += delegate { device.MaximumSize = new Size(Math.Max(100, Width - device.Left * 2), detail.Top - device.Top - 6); detail.MaximumSize = new Size(Math.Max(100, Width - detail.Left * 2), Math.Max(20, Height - detail.Top - 12)); };
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        { glyphScale *= factor.Width; base.ScaleControl(factor, specified); }
        public void SetDevice(string[] names, string[] ids, bool follow, string problem)
        {
            device.Text = names != null && names.Length == 3 ? (names[1] ?? "未设置默认设备") : "正在读取…";
            if (problem != null) { detail.Text = "保留当前设备，请检查设置"; detail.ForeColor = Color.FromArgb(165, 72, 32); }
            else { detail.ForeColor = UiTheme.Muted; detail.Text = !follow ? "保持当前输入，不自动切换" : ids != null && ids.Length == 3 && !string.IsNullOrEmpty(ids[1]) && ids.All(id => id == ids[1]) ? "系统、媒体、通话已统一" : "默认设备角色不一致"; }
            AccessibleName = (microphone ? "当前麦克风：" : "当前声音输出：") + device.Text;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Paper); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UiTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 14))
            using (var brush = new SolidBrush(Color.White))
            using (var pen = new Pen(UiTheme.Line))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            GraphicsState state = e.Graphics.Save();
            e.Graphics.ScaleTransform(glyphScale, glyphScale);
            using (var pen = new Pen(UiTheme.Ink, 1.8F))
            {
                if (microphone)
                {
                    e.Graphics.DrawArc(pen, 22, 19, 10, 18, 180, 180);
                    e.Graphics.DrawArc(pen, 22, 19, 10, 18, 0, 180);
                    e.Graphics.DrawArc(pen, 18, 26, 18, 15, 0, 180);
                    e.Graphics.DrawLine(pen, 27, 41, 27, 46); e.Graphics.DrawLine(pen, 22, 46, 32, 46);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, 19, 26, 6, 11); e.Graphics.DrawLine(pen, 25, 26, 32, 20);
                    e.Graphics.DrawLine(pen, 32, 20, 32, 43); e.Graphics.DrawLine(pen, 32, 43, 25, 37);
                    e.Graphics.DrawArc(pen, 32, 23, 10, 17, -65, 130);
                }
            }
            e.Graphics.Restore(state);
        }
    }

    sealed class SettingsForm : Form
    {
        readonly Func<SwitchStatus> read;
        readonly Action<Settings> save;
        readonly RouteCard output, input;
        readonly Label connection, hint, error, saved;
        readonly ComboBox headset, monitor, reference, headsetMic, fallbackMic;
        readonly ModeButton automatic, useHeadset, useMonitor, pause;
        readonly Button devicesToggle;
        readonly TableLayoutPanel devices;
        readonly CheckBox startup, microphoneFollow;
        readonly bool preview;
        Settings initial;
        bool populated, changing, expanded;
        string endpointFingerprint;
        public bool AllowClose;

        public SettingsForm(SwitchEngine engine, Settings settings, HotkeyWindow hotkeys, Action<Settings> save)
            : this(delegate { return engine.Status; }, engine.SetMode, engine.Reconnect, settings, save, null) { }

        internal SettingsForm(Func<SwitchStatus> read, Action<SwitchMode> setMode, Action reconnect,
            Settings settings, Action<Settings> save, bool? previewStartup)
        {
            this.read = read; this.save = save; initial = settings; preview = previewStartup.HasValue;
            Text = "声随 AudioFollow"; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(840, 620); MinimumSize = new Size(720, 570);
            StartPosition = FormStartPosition.CenterScreen; BackColor = UiTheme.Paper;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
                Padding = new Padding(30, 26, 30, 22) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(root); Controls.Add(scroll);
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            var brand = Label("AudioFollow", 23, UiTheme.Ink, FontStyle.Bold); brand.Font = new Font("Bahnschrift", 25F);
            header.Controls.Add(brand);
            connection = Label("正在确认连接", 9, UiTheme.Green); connection.TextAlign = ContentAlignment.MiddleRight;
            connection.Anchor = AnchorStyles.Right; connection.Margin = new Padding(4, 16, 2, 0); header.Controls.Add(connection);
            root.Controls.Add(header);
            root.Controls.Add(Label("声随，让声音和麦克风一起跟随耳机。", 10, UiTheme.Muted));
            root.Controls.Add(Label("切换方式", 10, UiTheme.Ink, FontStyle.Bold));
            var modes = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 6, 0, 5) };
            for (int i = 0; i < 4; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            automatic = AddMode(modes, "自动跟随", delegate { setMode(SwitchMode.Automatic); });
            useHeadset = AddMode(modes, "使用耳机", delegate { setMode(SwitchMode.Headset); });
            useMonitor = AddMode(modes, "使用桌面", delegate { setMode(SwitchMode.Monitor); });
            pause = AddMode(modes, "暂停切换", delegate { setMode(SwitchMode.Paused); }); root.Controls.Add(modes);
            hint = Label("", 9, UiTheme.Muted); root.Controls.Add(hint);
            var routes = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 174, Margin = new Padding(0, 14, 0, 12) };
            routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            output = new RouteCard(false) { Margin = new Padding(0, 0, 7, 0) };
            input = new RouteCard(true) { Margin = new Padding(7, 0, 0, 0) };
            routes.Controls.Add(output); routes.Controls.Add(input); root.Controls.Add(routes);
            microphoneFollow = new CheckBox { Text = "麦克风也自动跟随", Checked = initial.SwitchMicrophone,
                AutoSize = true, ForeColor = UiTheme.Ink, Margin = new Padding(0, 6, 0, 7), AccessibleName = "麦克风也自动跟随" };
            microphoneFollow.CheckedChanged += delegate { if (!changing) SaveSelection(); }; root.Controls.Add(microphoneFollow);
            error = Label("", 9, Color.FromArgb(166, 53, 37)); root.Controls.Add(error);
            devicesToggle = new Button { Text = "配置设备  +", AutoSize = true, Anchor = AnchorStyles.Left, FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.Ink, Padding = new Padding(2, 5, 8, 5), Margin = new Padding(0, 2, 0, 0), Cursor = Cursors.Hand };
            devicesToggle.FlatAppearance.BorderSize = 0; devicesToggle.Click += delegate { SetDevicesExpanded(!expanded); }; root.Controls.Add(devicesToggle);
            devices = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, Visible = false,
                Padding = new Padding(18), BackColor = Color.White, Margin = new Padding(0, 4, 0, 8) };
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            devices.Controls.Add(Label("连接检测：当前适配 Barracuda X 2022 的 2.4G 接收器。", 9, UiTheme.Muted));
            devices.Controls.Add(Label("耳机连接时", 10, UiTheme.Ink, FontStyle.Bold));
            devices.Controls.Add(Label("声音输出", 9, UiTheme.Muted)); headset = Combo(devices, "耳机连接时的声音输出");
            devices.Controls.Add(Label("麦克风", 9, UiTheme.Muted)); headsetMic = Combo(devices, "耳机连接时的麦克风");
            devices.Controls.Add(Label("耳机断开时", 10, UiTheme.Ink, FontStyle.Bold));
            devices.Controls.Add(Label("声音输出", 9, UiTheme.Muted)); monitor = Combo(devices, "耳机断开时的声音输出");
            devices.Controls.Add(Label("麦克风", 9, UiTheme.Muted)); fallbackMic = Combo(devices, "耳机断开时的麦克风");
            devices.Controls.Add(Label("耳机的关联播放设备（用于检查输出可用性）", 9, UiTheme.Muted)); reference = Combo(devices, "关联的耳机音频设备");
            foreach (ComboBox combo in new[] { headset, monitor, reference, headsetMic, fallbackMic }) combo.DropDownClosed += delegate { RefreshStatus(); };
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0) };
            ActionButton(actions, "保存设备", SaveSelection); ActionButton(actions, "重新检测", reconnect);
            saved = Label("", 9, UiTheme.Green); actions.Controls.Add(saved); devices.Controls.Add(actions); root.Controls.Add(devices);
            startup = new CheckBox { Text = "登录 Windows 后在后台启动", AutoSize = true, ForeColor = UiTheme.Ink,
                Margin = new Padding(0, 14, 0, 10), Checked = previewStartup ?? Startup.Enabled };
            startup.CheckedChanged += delegate {
                if (changing || preview) return;
                try { Startup.Set(startup.Checked); }
                catch (Exception ex) { changing = true; startup.Checked = !startup.Checked; changing = false; MessageBox.Show(this, ex.Message, "无法修改登录启动"); }
            };
            root.Controls.Add(startup);
            root.Controls.Add(Label("Ctrl + Alt + F10  切换设备    /    Ctrl + Alt + F11  恢复自动", 9, UiTheme.Muted));
            root.Controls.Add(Label("关闭窗口后留在托盘；麦克风设置只改变默认输入，不会录音。", 9, UiTheme.Muted));
            VisibleChanged += delegate { if (Visible) RefreshStatus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!AllowClose && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            RefreshStatus();
        }
        internal void SetDevicesExpanded(bool value) { expanded = value; devices.Visible = value; devicesToggle.Text = value ? "配置设备  −" : "配置设备  +"; devicesToggle.AccessibleDescription = value ? "设备设置已展开" : "设备设置已收起"; }
        static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        { return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = color, Font = new Font("Microsoft YaHei UI", size, style), Margin = new Padding(0, 4, 0, 5), UseMnemonic = false }; }
        static ComboBox Combo(TableLayoutPanel parent, string name)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 670, Margin = new Padding(0, 0, 0, 10), AccessibleName = name };
            parent.Controls.Add(combo); return combo;
        }
        static ModeButton AddMode(TableLayoutPanel parent, string text, Action action)
        {
            var button = new ModeButton { Text = text, AccessibleName = text, Dock = DockStyle.Fill, Height = 45, Margin = new Padding(0, 0, 6, 0) };
            button.Click += delegate { action(); }; parent.Controls.Add(button); return button;
        }
        static void ActionButton(FlowLayoutPanel parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.Ink,
                Padding = new Padding(8, 5, 8, 5), Margin = new Padding(0, 0, 10, 0) };
            button.FlatAppearance.BorderColor = UiTheme.Line; button.Click += delegate { action(); }; parent.Controls.Add(button);
        }
        static void Fill(ComboBox combo, Endpoint[] values, string id, string defaultName)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear(); combo.Items.Add(new Endpoint { Id = "", Name = "自动匹配：" + defaultName, State = 1 });
                foreach (Endpoint endpoint in values.OrderBy(e => !e.Active).ThenBy(e => e.Name)) combo.Items.Add(endpoint);
                combo.SelectedIndex = 0;
                for (int i = 1; i < combo.Items.Count; i++) if (((Endpoint)combo.Items[i]).Id == id) combo.SelectedIndex = i;
                if (!string.IsNullOrEmpty(id) && combo.SelectedIndex == 0) { combo.Items.Add(new Endpoint { Id = id, Name = "已保存设备暂不可用：" + id, State = 4 }); combo.SelectedIndex = combo.Items.Count - 1; }
            }
            finally { combo.EndUpdate(); }
        }
        string Id(ComboBox combo) { return combo.SelectedItem == null ? "" : ((Endpoint)combo.SelectedItem).Id; }
        void SaveSelection()
        {
            if (!populated) return;
            var value = new Settings { HeadsetEndpointId = Id(headset), MonitorEndpointId = Id(monitor), ReferenceEndpointId = Id(reference),
                HeadsetMicrophoneId = Id(headsetMic), FallbackMicrophoneId = Id(fallbackMic), SwitchMicrophone = microphoneFollow.Checked };
            try { if (!preview) value.Save(); initial = value; save(value); saved.Text = "已保存"; }
            catch (Exception ex) { changing = true; microphoneFollow.Checked = initial.SwitchMicrophone; changing = false; MessageBox.Show(this, ex.Message, "无法保存设置"); }
        }
        public void RefreshStatus()
        {
            SwitchStatus state = read(); connection.Text = TrayApp.LinkText(state.Link);
            connection.ForeColor = state.Link == "Connected" ? UiTheme.Green : UiTheme.Muted;
            output.SetDevice(state.DefaultNames, state.DefaultIds, true, state.Error);
            input.SetDevice(state.CaptureDefaultNames, state.CaptureDefaultIds, state.SwitchMicrophone, state.MicrophoneError);
            automatic.Selected = state.Mode == "Automatic"; useHeadset.Selected = state.Mode == "Headset";
            useMonitor.Selected = state.Mode == "Monitor"; pause.Selected = state.Mode == "Paused";
            hint.Text = state.Mode == "Automatic" ? (state.Link == "Unknown" ? "连接状态待确认，暂时保留当前设备。" : "耳机连上用耳机，断开后回到桌面设备。") : state.Mode == "Paused" ? "已暂停。选择「自动跟随」继续。" : "已固定设备。选择「自动跟随」恢复跟随连接。";
            error.Text = state.Error ?? state.MicrophoneError ?? (!state.HotkeyToggleRegistered || !state.HotkeyAutomaticRegistered ? "部分快捷键不可用，可使用窗口或托盘菜单。" : ""); error.Visible = error.Text.Length > 0;
            if (state.Endpoints == null || state.CaptureEndpoints == null) return;
            string fingerprint = string.Join("|", state.Endpoints.Concat(state.CaptureEndpoints).Select(e => e.Id + ":" + e.State + ":" + e.Name));
            if (fingerprint == endpointFingerprint || new[] { headset, monitor, reference, headsetMic, fallbackMic }.Any(c => c.DroppedDown)) return;
            string h = populated ? Id(headset) : initial.HeadsetEndpointId, m = populated ? Id(monitor) : initial.MonitorEndpointId;
            string r = populated ? Id(reference) : initial.ReferenceEndpointId, hm = populated ? Id(headsetMic) : initial.HeadsetMicrophoneId;
            string fm = populated ? Id(fallbackMic) : initial.FallbackMicrophoneId;
            Fill(headset, state.Endpoints, h, Settings.HeadsetName); Fill(monitor, state.Endpoints, m, Settings.MonitorName); Fill(reference, state.Endpoints, r, Settings.ReferenceName);
            Fill(headsetMic, state.CaptureEndpoints, hm, Settings.HeadsetMicrophoneName); Fill(fallbackMic, state.CaptureEndpoints, fm, Settings.FallbackMicrophoneName);
            populated = true; endpointFingerprint = fingerprint;
        }
    }
}
