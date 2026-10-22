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
            else { detail.ForeColor = UiTheme.Muted; detail.Text = !follow ? (microphone ? "保持当前输入，不自动切换" : "保持当前输出，不自动切换") : ids != null && ids.Length == 3 && !string.IsNullOrEmpty(ids[1]) && ids.All(id => id == ids[1]) ? "系统、媒体、通话已统一" : "默认设备角色不一致"; }
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
        readonly Label connection, hint, error, saved, inspection, methodHint;
        readonly ComboBox method, trigger, headset, monitor, reference, headsetMic, fallbackMic;
        readonly ModeButton automatic, useHeadset, useMonitor, pause;
        readonly Button devicesToggle, check;
        readonly TableLayoutPanel devices, referenceRow;
        readonly CheckBox startup, microphoneFollow;
        readonly bool preview;
        readonly Func<TriggerKind, TriggerCandidate[]> previewCatalog;
        Settings initial;
        bool populated, changing, expanded, listed;
        int catalogGeneration, checkGeneration;
        System.Threading.CancellationTokenSource catalogCancel, checkCancel;
        string endpointFingerprint;
        public bool AllowClose;

        public SettingsForm(SwitchEngine engine, Settings settings, HotkeyWindow hotkeys, Action<Settings> save)
            : this(delegate { return engine.Status; }, engine.SetMode, engine.Reconnect, settings, save, null, null) { }

        internal SettingsForm(Func<SwitchStatus> read, Action<SwitchMode> setMode, Action reconnect,
            Settings settings, Action<Settings> save, bool? previewStartup, Func<TriggerKind, TriggerCandidate[]> catalog = null)
        {
            this.read = read; this.save = save; initial = settings.Copy(); preview = previewStartup.HasValue; previewCatalog = catalog;
            Text = "声随 AudioFollow · v1.3 预览版"; Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 620); MinimumSize = new Size(750, 570);
            StartPosition = FormStartPosition.CenterScreen; BackColor = UiTheme.Paper;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
                Padding = new Padding(30, 24, 30, 22) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(root); Controls.Add(scroll);
            var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            var brand = Label("AudioFollow", 23, UiTheme.Ink, FontStyle.Bold); brand.Font = new Font("Bahnschrift", 25F);
            header.Controls.Add(brand);
            connection = Label("正在确认连接", 9, UiTheme.Green); connection.TextAlign = ContentAlignment.MiddleRight;
            connection.Anchor = AnchorStyles.Right; connection.Margin = new Padding(4, 16, 2, 0); header.Controls.Add(connection);
            root.Controls.Add(header);
            root.Controls.Add(Label("声随，让声音和麦克风跟随你选择的设备。", 10, UiTheme.Muted));
            var modes = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 16, 0, 5) };
            for (int i = 0; i < 4; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            automatic = AddMode(modes, "自动跟随", delegate { setMode(SwitchMode.Automatic); });
            useHeadset = AddMode(modes, "连接时设备", delegate { setMode(SwitchMode.Headset); });
            useMonitor = AddMode(modes, "断开时设备", delegate { setMode(SwitchMode.Monitor); });
            pause = AddMode(modes, "暂停切换", delegate { setMode(SwitchMode.Paused); }); root.Controls.Add(modes);
            hint = Label("", 9, UiTheme.Muted); root.Controls.Add(hint);
            var routes = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 174, Margin = new Padding(0, 14, 0, 12) };
            routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); routes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            output = new RouteCard(false) { Margin = new Padding(0, 0, 7, 0) };
            input = new RouteCard(true) { Margin = new Padding(7, 0, 0, 0) };
            routes.Controls.Add(output); routes.Controls.Add(input); root.Controls.Add(routes);
            error = Label("", 9, Color.FromArgb(166, 53, 37)); root.Controls.Add(error);
            devicesToggle = new Button { Text = "配置设备  +", AutoSize = true, Anchor = AnchorStyles.Left, FlatStyle = FlatStyle.Flat,
                ForeColor = UiTheme.Ink, Padding = new Padding(2, 5, 8, 5), Margin = new Padding(0, 2, 0, 0), Cursor = Cursors.Hand };
            devicesToggle.FlatAppearance.BorderSize = 0; devicesToggle.Click += delegate { SetDevicesExpanded(!expanded); }; root.Controls.Add(devicesToggle);
            devices = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, Visible = false,
                Padding = new Padding(18), BackColor = Color.White, Margin = new Padding(0, 4, 0, 8) };
            devices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            devices.Controls.Add(Label("跟随哪个设备", 11, UiTheme.Ink, FontStyle.Bold));
            devices.Controls.Add(Label("连接方式", 9, UiTheme.Muted)); method = Combo(devices, "连接方式");
            method.Items.AddRange(new object[] { "专用 2.4G 无线检测", "USB 音频设备可用性", "蓝牙实际连接" });
            devices.Controls.Add(Label("触发设备", 9, UiTheme.Muted)); trigger = Combo(devices, "触发设备");
            methodHint = Label("", 9, UiTheme.Muted); devices.Controls.Add(methodHint);
            inspection = Label("选择设备后可检查连接状态；保存前继续执行原配置。", 9, UiTheme.Muted); devices.Controls.Add(inspection);
            var groups = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
            groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var connected = Group(groups, "设备连接时", new Padding(0, 0, 12, 0));
            connected.Controls.Add(Label("声音输出", 9, UiTheme.Muted)); headset = Combo(connected, "连接时声音输出");
            connected.Controls.Add(Label("麦克风", 9, UiTheme.Muted)); headsetMic = Combo(connected, "连接时麦克风");
            var disconnected = Group(groups, "设备断开时", new Padding(12, 0, 0, 0));
            disconnected.Controls.Add(Label("声音输出", 9, UiTheme.Muted)); monitor = Combo(disconnected, "断开时声音输出");
            disconnected.Controls.Add(Label("麦克风", 9, UiTheme.Muted)); fallbackMic = Combo(disconnected, "断开时麦克风"); devices.Controls.Add(groups);
            referenceRow = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0) };
            referenceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            referenceRow.Controls.Add(Label("专用无线检测的关联播放设备", 9, UiTheme.Muted)); reference = Combo(referenceRow, "关联播放设备"); devices.Controls.Add(referenceRow);
            microphoneFollow = new CheckBox { Text = "麦克风也自动跟随", Checked = initial.SwitchMicrophone,
                AutoSize = true, ForeColor = UiTheme.Ink, Margin = new Padding(0, 6, 0, 7), AccessibleName = "麦克风也自动跟随" };
            devices.Controls.Add(microphoneFollow);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 0), WrapContents = true };
            check = ActionButton(actions, "检查选中设备", InspectSelection);
            ActionButton(actions, "刷新设备列表", LoadCandidates);
            ActionButton(actions, "保存并应用", SaveSelection);
            saved = Label("", 9, UiTheme.Green); actions.Controls.Add(saved); devices.Controls.Add(actions); root.Controls.Add(devices);
            method.SelectedIndexChanged += delegate { if (!changing) { CancelCheck(); UpdateMethodHint(); LoadCandidates(); Dirty(); } };
            trigger.SelectedIndexChanged += delegate { if (!changing) { CancelCheck(); Recommend(); Dirty(); } };
            foreach (var selector in new[] { headset, monitor, reference, headsetMic, fallbackMic })
                selector.SelectedIndexChanged += delegate { if (!changing) Dirty(); };
            microphoneFollow.CheckedChanged += delegate { if (!changing) Dirty(); };
            startup = new CheckBox { Text = "登录 Windows 后在后台启动", AutoSize = true, ForeColor = UiTheme.Ink,
                Margin = new Padding(0, 14, 0, 10), Checked = previewStartup ?? Startup.Enabled };
            startup.CheckedChanged += delegate {
                if (changing || preview) return;
                try { Startup.Set(startup.Checked); }
                catch (Exception ex) { changing = true; startup.Checked = !startup.Checked; changing = false; MessageBox.Show(this, ex.Message, "无法修改登录启动"); }
            };
            root.Controls.Add(startup);
            root.Controls.Add(Label("Ctrl + Alt + F10  切换设备    /    Ctrl + Alt + F11  恢复自动", 9, UiTheme.Muted));
            root.Controls.Add(Label("关闭窗口后留在托盘；只改变默认设备，不会录音。", 9, UiTheme.Muted));
            VisibleChanged += delegate { if (Visible) RefreshStatus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!AllowClose && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            RefreshStatus();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { catalogGeneration++; checkGeneration++; Cancel(ref catalogCancel); Cancel(ref checkCancel); }
            base.Dispose(disposing);
        }
        static void Cancel(ref System.Threading.CancellationTokenSource source)
        { if (source != null) { source.Cancel(); source.Dispose(); source = null; } }
        void CancelCheck() { checkGeneration++; Cancel(ref checkCancel); check.Enabled = true; }
        void Dirty() { saved.Text = "待保存"; saved.ForeColor = Color.FromArgb(165, 102, 32); }
        internal void SetDevicesExpanded(bool value)
        {
            expanded = value; devices.Visible = value; devicesToggle.Text = value ? "配置设备  −" : "配置设备  +";
            devicesToggle.AccessibleDescription = value ? "设备设置已展开" : "设备设置已收起";
            if (value && !listed) LoadCandidates();
        }
        static TableLayoutPanel Group(TableLayoutPanel parent, string title, Padding margin)
        {
            var group = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true, Margin = margin };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); group.Controls.Add(Label(title, 10, UiTheme.Ink, FontStyle.Bold)); parent.Controls.Add(group); return group;
        }
        static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        { return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = color, Font = new Font("Microsoft YaHei UI", size, style), Margin = new Padding(0, 4, 0, 5), UseMnemonic = false }; }
        static ComboBox Combo(TableLayoutPanel parent, string name)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 680, Margin = new Padding(0, 0, 0, 8), AccessibleName = name };
            parent.Controls.Add(combo); return combo;
        }
        static ModeButton AddMode(TableLayoutPanel parent, string text, Action action)
        {
            var button = new ModeButton { Text = text, AccessibleName = text, Dock = DockStyle.Fill, Height = 45, Margin = new Padding(0, 0, 6, 0) };
            button.Click += delegate { action(); }; parent.Controls.Add(button); return button;
        }
        static Button ActionButton(FlowLayoutPanel parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.Ink,
                Padding = new Padding(8, 5, 8, 5), Margin = new Padding(0, 0, 10, 0) };
            button.FlatAppearance.BorderColor = UiTheme.Line; button.Click += delegate { action(); }; parent.Controls.Add(button); return button;
        }
        sealed class Choice
        {
            public EndpointTarget Target;
            public string Text;
            public override string ToString() { return Text; }
        }
        static void Fill(ComboBox combo, Endpoint[] values, EndpointTarget selected, bool allowKeep = true)
        {
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                if (allowKeep) combo.Items.Add(new Choice { Target = EndpointTarget.Keep(), Text = "保持当前" });
                int ordinal = 0;
                foreach (Endpoint endpoint in values.OrderBy(e => !e.Active).ThenBy(e => e.Name).ThenBy(e => e.Id))
                    combo.Items.Add(new Choice { Target = EndpointTarget.For(endpoint), Text = endpoint.ToString() +
                        (values.Count(e => e.Name == endpoint.Name) > 1 ? " · 同名设备 " + (++ordinal) : "") });
                combo.SelectedIndex = allowKeep && selected.KeepCurrent ? 0 : -1;
                for (int i = allowKeep ? 1 : 0; i < combo.Items.Count; i++)
                    if (DeviceIdentity.Same(((Choice)combo.Items[i]).Target.EndpointId, selected.EndpointId)) combo.SelectedIndex = i;
                if (combo.SelectedIndex < 0)
                {
                    combo.Items.Add(new Choice { Target = selected, Text = string.IsNullOrEmpty(selected.EndpointId) ?
                        "请选择设备或保持当前" : "已保存设备不可用：" + (selected.DisplayName ?? "请重新选择") });
                    combo.SelectedIndex = combo.Items.Count - 1;
                }
            }
            finally { combo.EndUpdate(); }
        }
        static EndpointTarget Target(ComboBox combo)
        { return combo.SelectedItem == null ? new EndpointTarget() : ((Choice)combo.SelectedItem).Target; }
        TriggerSettings SelectedTrigger { get { var item = trigger.SelectedItem as TriggerCandidate; return item == null ? null : item.Settings; } }
        void UpdateMethodHint()
        {
            referenceRow.Visible = method.SelectedIndex == 0;
            methodHint.Text = method.SelectedIndex == 0 ? "读取已适配接收器的无线状态，可区分耳机关机和接收器在场。" : method.SelectedIndex == 1 ?
                "判断音频端点是否可用；不能判断常插接收器的无线耳机是否开机。" : "已配对仅用于列出设备；跟随所选蓝牙传输的实际连接。";
        }
        AudioSnapshot Snapshot(bool capture)
        {
            SwitchStatus state = read();
            return new AudioSnapshot { Endpoints = ((capture ? state.CaptureEndpoints : state.Endpoints) ?? new Endpoint[0]).ToList() };
        }
        async void LoadCandidates()
        {
            if (!populated || method.SelectedIndex < 0) return;
            listed = true; Cancel(ref catalogCancel); CancelCheck(); int request = ++catalogGeneration;
            catalogCancel = new System.Threading.CancellationTokenSource(); var token = catalogCancel.Token;
            TriggerKind kind = (TriggerKind)method.SelectedIndex;
            TriggerSettings selected = SelectedTrigger;
            if (selected == null || selected.Kind != kind) selected = initial.Trigger.Kind == kind ? initial.Trigger : null;
            changing = true; trigger.Items.Clear(); changing = false;
            inspection.Text = "正在查找设备… 原配置继续运行。";
            try
            {
                AudioSnapshot playback = Snapshot(false), capture = Snapshot(true);
                TriggerCandidate[] items = previewCatalog != null ? previewCatalog(kind) :
                    await System.Threading.Tasks.Task.Run(delegate { return TriggerInspection.List(kind, playback, capture, token); });
                if (IsDisposed || request != catalogGeneration || token.IsCancellationRequested) return;
                changing = true;
                trigger.BeginUpdate(); trigger.Items.Clear();
                foreach (var group in items.GroupBy(i => i.Settings.DisplayName).Where(g => g.Count() > 1))
                { int number = 0; foreach (var item in group.OrderBy(i => i.Settings.DeviceId)) item.Distinction = " · 同名设备 " + (++number); }
                foreach (TriggerCandidate item in items) trigger.Items.Add(item);
                trigger.SelectedIndex = -1;
                if (selected != null)
                {
                    for (int i = 0; i < trigger.Items.Count; i++)
                        if (DeviceIdentity.Same(((TriggerCandidate)trigger.Items[i]).Settings.DeviceId, selected.DeviceId)) trigger.SelectedIndex = i;
                    if (trigger.SelectedIndex < 0 && !string.IsNullOrEmpty(selected.DeviceId))
                    { trigger.Items.Add(new TriggerCandidate { Settings = selected, Status = "已保存，当前未枚举到" }); trigger.SelectedIndex = trigger.Items.Count - 1; }
                    if (trigger.SelectedIndex < 0 && kind == TriggerKind.Hid && trigger.Items.Count == 1) trigger.SelectedIndex = 0;
                }
                trigger.EndUpdate(); changing = false;
                inspection.Text = trigger.SelectedItem == null ? (items.Length == 0 ? "未找到设备；连接或配对后刷新列表。" : "请选择触发设备，然后检查连接状态。") : trigger.SelectedItem.ToString();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed && request == catalogGeneration) inspection.Text = "设备列表不可用：" + ex.Message; }
        }
        void Recommend()
        {
            TriggerSettings selected = SelectedTrigger;
            if (selected == null) return;
            SwitchStatus state = read(); changing = true;
            Endpoint recommendedOutput = TriggerCatalog.Recommend(selected, state.Endpoints ?? new Endpoint[0], 0);
            Endpoint recommendedInput = TriggerCatalog.Recommend(selected, state.CaptureEndpoints ?? new Endpoint[0], 1);
            Fill(headset, state.Endpoints ?? new Endpoint[0], recommendedOutput == null ? new EndpointTarget() : EndpointTarget.For(recommendedOutput));
            Fill(headsetMic, state.CaptureEndpoints ?? new Endpoint[0], recommendedInput == null ? new EndpointTarget() : EndpointTarget.For(recommendedInput));
            changing = false;
            inspection.Text = "已推荐唯一关联的设备；未能唯一确认的项目请手动选择。备用设备保持原选择。";
        }
        async void InspectSelection()
        {
            TriggerSettings selected = SelectedTrigger;
            if (selected == null) { inspection.Text = "请先选择触发设备。"; return; }
            CancelCheck(); int request = ++checkGeneration;
            checkCancel = new System.Threading.CancellationTokenSource(); var token = checkCancel.Token;
            check.Enabled = false;
            try
            {
                SwitchStatus state = read();
                if (state.Trigger != null && state.Trigger.Kind == selected.Kind &&
                    (DeviceIdentity.Same(state.Trigger.DeviceId, selected.DeviceId) ||
                    (selected.Kind == TriggerKind.Hid && string.IsNullOrEmpty(state.Trigger.DeviceId))))
                    inspection.Text = TrayApp.LinkText(state.Link) + "：" + state.Evidence;
                else if (preview) inspection.Text = "预览：" + trigger.SelectedItem;
                else
                {
                    inspection.Text = "正在只读检查所选设备…";
                    AudioSnapshot playback = Snapshot(false), capture = Snapshot(true);
                    TriggerReading result = await System.Threading.Tasks.Task.Run(delegate { return TriggerInspection.Check(selected, playback, capture, token); });
                    if (IsDisposed || request != checkGeneration || token.IsCancellationRequested) return;
                    inspection.Text = TrayApp.LinkText(result.State.ToString()) + "：" + result.Reason;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed && request == checkGeneration) inspection.Text = "检查失败：" + ex.Message; }
            finally { if (!IsDisposed && request == checkGeneration) check.Enabled = true; }
        }
        void SaveSelection()
        {
            if (!populated) return;
            var value = initial.Copy(); value.Trigger = SelectedTrigger;
            if (value.Trigger == null || (int)value.Trigger.Kind != method.SelectedIndex)
            { saved.Text = "未保存：请等待设备列表并选择触发设备。"; return; }
            value.Connected = new RouteTargets { Output = Target(headset), Microphone = Target(headsetMic) };
            value.Disconnected = new RouteTargets { Output = Target(monitor), Microphone = Target(fallbackMic) };
            value.ReferenceEndpointId = Target(reference).EndpointId;
            value.SwitchMicrophone = microphoneFollow.Checked;
            // Keep these fields for old control scripts and a documented downgrade path.
            value.HeadsetEndpointId = value.Connected.Output.EndpointId; value.MonitorEndpointId = value.Disconnected.Output.EndpointId;
            value.HeadsetMicrophoneId = value.Connected.Microphone.EndpointId; value.FallbackMicrophoneId = value.Disconnected.Microphone.EndpointId;
            try
            {
                value.Validate(true); if (!preview) value.Save(); initial = value.Copy(); save(value);
                saved.Text = "已保存并应用"; saved.ForeColor = UiTheme.Green;
            }
            catch (Exception ex) { saved.Text = "未保存：" + ex.Message; saved.ForeColor = Color.FromArgb(166, 53, 37); }
        }
        public void RefreshStatus()
        {
            SwitchStatus state = read(); connection.Text = state.Mode == "Automatic" && state.StableLink == "Unknown" ? "正在确认连接" : TrayApp.LinkText(state.Link);
            connection.ForeColor = state.Link == "Connected" ? UiTheme.Green : UiTheme.Muted;
            output.SetDevice(state.DefaultNames, state.DefaultIds, !state.OutputKeepCurrent, state.Error);
            input.SetDevice(state.CaptureDefaultNames, state.CaptureDefaultIds, state.SwitchMicrophone && !state.MicrophoneKeepCurrent, state.MicrophoneError);
            automatic.Selected = state.Mode == "Automatic"; useHeadset.Selected = state.Mode == "Headset";
            useMonitor.Selected = state.Mode == "Monitor"; pause.Selected = state.Mode == "Paused";
            hint.Text = state.Mode == "Automatic" ? (state.Trigger == null ? "正在确认触发设备…" : "正在跟随：" + state.Trigger.DisplayName +
                (state.StableLink == "Unknown" ? "；正在确认：" + (state.Evidence ?? "等待连接信息") : "")) : state.Mode == "Paused" ?
                "已暂停切换，连接检测仍在继续。" : "已固定设备组。选择「自动跟随」恢复连接规则。";
            error.Text = state.Error ?? state.MicrophoneError ?? (!state.HotkeyToggleRegistered || !state.HotkeyAutomaticRegistered ? "部分快捷键不可用，可使用窗口或托盘菜单。" : ""); error.Visible = error.Text.Length > 0;
            if (state.Endpoints == null || state.CaptureEndpoints == null) return;
            string fingerprint = string.Join("|", state.Endpoints.Concat(state.CaptureEndpoints).Select(e => e.Id + ":" + e.State + ":" + e.Name));
            if (fingerprint == endpointFingerprint || new[] { headset, monitor, reference, headsetMic, fallbackMic }.Any(c => c.DroppedDown)) return;
            changing = true;
            if (!populated)
            {
                initial = initial.Upgrade(Snapshot(false), Snapshot(true)); method.SelectedIndex = (int)initial.Trigger.Kind;
                if (initial.Trigger.Kind == TriggerKind.Hid && string.IsNullOrEmpty(initial.Trigger.DeviceId) &&
                    state.Trigger != null && state.Trigger.Kind == TriggerKind.Hid)
                    initial.Trigger = Json.Decode<TriggerSettings>(Json.Encode(state.Trigger));
                trigger.Items.Add(new TriggerCandidate { Settings = initial.Trigger, Status = "当前配置" }); trigger.SelectedIndex = 0;
                microphoneFollow.Checked = initial.SwitchMicrophone; UpdateMethodHint();
            }
            Fill(headset, state.Endpoints, populated ? Target(headset) : initial.Connected.Output);
            Fill(monitor, state.Endpoints, populated ? Target(monitor) : initial.Disconnected.Output);
            Fill(headsetMic, state.CaptureEndpoints, populated ? Target(headsetMic) : initial.Connected.Microphone);
            Fill(fallbackMic, state.CaptureEndpoints, populated ? Target(fallbackMic) : initial.Disconnected.Microphone);
            Endpoint physical = initial.Resolve(new AudioSnapshot { Endpoints = state.Endpoints.ToList() }, "reference");
            Fill(reference, state.Endpoints, populated ? Target(reference) : physical == null ? new EndpointTarget { EndpointId = initial.ReferenceEndpointId, DisplayName = Settings.ReferenceName } : EndpointTarget.For(physical), false);
            populated = true; endpointFingerprint = fingerprint; changing = false;
        }
    }
}
