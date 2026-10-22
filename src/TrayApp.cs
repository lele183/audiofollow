using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioFollow
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
            CreateHandle(new CreateParams { Caption = "AudioFollow events" });
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
        int updatePending;
        bool exiting;
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
            auto = new ToolStripMenuItem("自动跟随", null, delegate { engine.SetMode(SwitchMode.Automatic); });
            headset = new ToolStripMenuItem("连接时设备", null, delegate { engine.SetMode(SwitchMode.Headset); });
            monitor = new ToolStripMenuItem("断开时设备", null, delegate { engine.SetMode(SwitchMode.Monitor); });
            pause = new ToolStripMenuItem("暂停切换", null, delegate { engine.SetMode(SwitchMode.Paused); });
            menu.Items.AddRange(new ToolStripItem[] { auto, headset, monitor, pause, new ToolStripSeparator() });
            menu.Items.Add("重新检测设备", null, delegate { engine.Reconnect(); });
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = iconGood, Text = "声随 AudioFollow", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            hotkeys = new HotkeyWindow(engine, ShowSettings);
            ipc = new Ipc(HandleCommand);
            engine.StatusChanged += ScheduleUpdate;
            UpdateStatus();
            AppFiles.Log("START version=1.3.0-preview.1; toggleHotkey=" + hotkeys.ToggleRegistered + "; autoHotkey=" + hotkeys.AutoRegistered);
            if (!background) ShowSettings();
        }

        void ScheduleUpdate()
        {
            if (System.Threading.Interlocked.Exchange(ref updatePending, 1) != 0) return;
            try { dispatcher.BeginInvoke((Action)UpdateStatus); }
            catch (InvalidOperationException) { System.Threading.Interlocked.Exchange(ref updatePending, 0); }
        }

        void UpdateStatus()
        {
                System.Threading.Interlocked.Exchange(ref updatePending, 0);
                if (exiting) return;
                SwitchStatus status = engine.Status;
                string text = "声随 · " + LinkText(status.Link);
                text = text.Length > 63 ? text.Substring(0, 63) : text;
                if (tray.Text != text) tray.Text = text;
                Icon icon = status.Error == null && status.MicrophoneError == null && (status.Link == "Connected" || status.Link == "Disconnected") ? iconGood : iconUnknown;
                if (tray.Icon != icon) tray.Icon = icon;
                auto.Checked = status.Mode == "Automatic"; headset.Checked = status.Mode == "Headset";
                monitor.Checked = status.Mode == "Monitor"; pause.Checked = status.Mode == "Paused";
                if (form != null && !form.IsDisposed && form.Visible) form.RefreshStatus();
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
            exiting = true;
            engine.StatusChanged -= ScheduleUpdate;
            if (form != null) { form.AllowClose = true; form.Close(); form.Dispose(); }
            ipc.Dispose(); hotkeys.Dispose(); engine.Dispose();
            tray.Visible = false; tray.Dispose(); dispatcher.Dispose();
            iconGood.Dispose(); iconUnknown.Dispose();
            try { AppFiles.Log("STOP"); } catch { }
            base.ExitThreadCore();
        }

        public static string LinkText(string state)
        {
            switch (state)
            {
                case "Connected": return "设备已连接 / 可用";
                case "Disconnected": return "设备未连接 / 不可用";
                case "ReceiverMissing": return "接收器未插入";
                default: return "正在确认设备状态";
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

}
