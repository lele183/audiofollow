using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BarracudaSwitch
{
    public enum SwitchMode { Automatic, Headset, Monitor, Paused }
    public enum Route { None, Headset, Monitor }

    public static class SwitchPlanner
    {
        public static Route Decide(SwitchMode mode, LinkState link, bool referenceAvailable)
        {
            if (mode == SwitchMode.Paused) return Route.None;
            if (mode == SwitchMode.Headset) return Route.Headset;
            if (mode == SwitchMode.Monitor) return Route.Monitor;
            if (link == LinkState.Connected) return referenceAvailable ? Route.Headset : Route.Monitor;
            if (link == LinkState.Disconnected || link == LinkState.ReceiverMissing) return Route.Monitor;
            return Route.None;
        }
    }

    public sealed class SwitchStatus
    {
        public int ProcessId;
        public string Version = "1.1.0";
        public string UpdatedUtc, Mode, Link, Evidence, RawFrame, Message, Error;
        public string HeadsetEndpointId, MonitorEndpointId, ReferenceEndpointId;
        public string[] DefaultIds, DefaultNames;
        public Endpoint[] Endpoints;
        public bool HotkeyToggleRegistered, HotkeyAutomaticRegistered;
        public long WorkerIterations, AudioReads, StatusWrites;
    }

    public sealed class SwitchEngine : IDisposable
    {
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        readonly Thread worker;
        Settings settings;
        SwitchMode mode = SwitchMode.Automatic;
        HeadsetSensor sensor;
        volatile bool stopping;
        volatile SwitchStatus status = new SwitchStatus { Message = "正在查询耳机连接状态…", Mode = "Automatic", Link = "Unknown" };
        bool reconnect;
        bool hotkeyToggle, hotkeyAutomatic;
        volatile bool firstStatusWritten;
        string lastLog;
        int audioDirty = 1;
        readonly int processId = Process.GetCurrentProcess().Id;
        public event Action StatusChanged;

        public SwitchStatus Status { get { return status; } }

        public SwitchEngine(Settings settings)
        {
            this.settings = settings;
            worker = new Thread(Run) { IsBackground = true, Name = "Barracuda audio monitor" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public void SetMode(SwitchMode value) { Enqueue(delegate { mode = value; }); }
        public void Configure(Settings value) { Enqueue(delegate { settings = value; }); }
        public void Reconnect() { Enqueue(delegate { reconnect = true; }); }
        public void SetHotkeys(bool toggle, bool automatic)
        {
            Enqueue(delegate { hotkeyToggle = toggle; hotkeyAutomatic = automatic; });
        }
        public void Toggle()
        {
            Enqueue(delegate {
                bool headset = status.DefaultIds != null && status.DefaultIds[1] == status.HeadsetEndpointId;
                mode = headset ? SwitchMode.Monitor : SwitchMode.Headset;
            });
        }
        void Enqueue(Action action) { commands.Enqueue(action); wake.Set(); }

        void Run()
        {
            var clock = Stopwatch.StartNew();
            long nextRead = 0, nextWrite = 0, nextSwitch = 0;
            long iterations = 0, audioReads = 0, statusWrites = 0;
            string lastUiKey = null;
            AudioSnapshot audio = null;
            string error = null, message = null;
            using (sensor = new HeadsetSensor())
            {
                AudioCore core = null;
                try
                {
                    while (!stopping)
                    {
                        try
                        {
                            Action command;
                            bool requested = false;
                            while (commands.TryDequeue(out command)) { command(); requested = true; }
                            iterations++;
                            if (requested || Interlocked.Exchange(ref audioDirty, 0) != 0) nextRead = 0;
                            if (reconnect) { reconnect = false; sensor.Reconnect(); nextRead = nextSwitch = 0; }
                            if (core == null) core = new AudioCore(delegate { Interlocked.Exchange(ref audioDirty, 1); wake.Set(); });
                            long now = clock.ElapsedMilliseconds;
                            sensor.Poll(0);
                            if (audio == null || now >= nextRead)
                            {
                                audio = core.Read();
                                audioReads++;
                                nextRead = now + 30000;
                            }
                            Endpoint headset = settings.Resolve(audio, "headset");
                            Endpoint monitor = settings.Resolve(audio, "monitor");
                            Endpoint reference = settings.Resolve(audio, "reference");
                            LinkReading link = sensor.Current;
                            Route route = SwitchPlanner.Decide(mode, link.State, reference != null);
                            Endpoint desired = route == Route.Headset ? headset : route == Route.Monitor ? monitor : null;
                            message = route == Route.None
                                ? (mode == SwitchMode.Paused ? "已暂停自动切换" : "耳机状态未知，保留当前输出；可用快捷键切换")
                                : route == Route.Headset ? "耳机输出：7.1 Surround Sound" : "显示器输出：XV271U M3";
                            error = null;
                            if (route != Route.None && desired == null)
                                error = "目标输出不可用或同名设备有歧义，请在设置中选择设备。";
                            if (desired != null && audio.Defaults.Any(id => id != desired.Id) && now >= nextSwitch)
                            {
                                nextSwitch = now + 2000;
                                core.SetAllRoles(desired.Id);
                                audio = core.Read();
                                audioReads++;
                            }
                            status = new SwitchStatus {
                                ProcessId = processId, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                                Mode = mode.ToString(), Link = link.State.ToString(), Evidence = link.Evidence,
                                RawFrame = link.Raw, Message = message, Error = error,
                                HeadsetEndpointId = headset == null ? null : headset.Id,
                                MonitorEndpointId = monitor == null ? null : monitor.Id,
                                ReferenceEndpointId = reference == null ? null : reference.Id,
                                DefaultIds = audio.Defaults, DefaultNames = audio.Defaults.Select(audio.NameOf).ToArray(),
                                Endpoints = audio.Endpoints.ToArray(),
                                HotkeyToggleRegistered = hotkeyToggle, HotkeyAutomaticRegistered = hotkeyAutomatic,
                                WorkerIterations = iterations, AudioReads = audioReads, StatusWrites = statusWrites
                            };
                            string entry = mode + " | " + link.State + " | " + message + " | " +
                                string.Join(", ", audio.Defaults) + " | " + error;
                            bool changed = entry != lastLog;
                            if (changed) { AppFiles.Log(entry + " | " + link.Evidence); lastLog = entry; }
                            string uiKey = entry + "|" + hotkeyToggle + "|" + hotkeyAutomatic + "|" +
                                string.Join("|", audio.Endpoints.Select(e => e.Id + ":" + e.State + ":" + e.Name));
                            if (uiKey != lastUiKey) { lastUiKey = uiKey; NotifyStatus(); }
                            if (now >= nextWrite || changed || requested)
                            {
                                status.StatusWrites = ++statusWrites;
                                AppFiles.WriteAtomic(AppFiles.StatusFile, Json.Encode(status));
                                firstStatusWritten = true;
                                nextWrite = now + 30000;
                            }
                        }
                        catch (Exception ex)
                        {
                            string text = ex.Message;
                            status = new SwitchStatus {
                                ProcessId = processId, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                                Mode = mode.ToString(), Link = sensor.Current.State.ToString(),
                                Evidence = sensor.Current.Evidence, Message = "等待音频设备恢复", Error = text,
                                DefaultIds = audio == null ? null : audio.Defaults,
                                DefaultNames = audio == null ? null : audio.Defaults.Select(audio.NameOf).ToArray()
                            };
                            NotifyStatus();
                            lastUiKey = null;
                            if (text != lastLog) { try { AppFiles.Log("ERROR " + text); } catch { } lastLog = text; }
                            try { AppFiles.WriteAtomic(AppFiles.StatusFile, Json.Encode(status)); } catch { }
                            nextRead = 0;
                            if (core != null) { core.Dispose(); core = null; }
                            wake.WaitOne(1500);
                        }
                        WaitHandle ready = sensor.ReadReady;
                        if (ready == null) wake.WaitOne(1000);
                        else WaitHandle.WaitAny(new WaitHandle[] { wake, ready }, 1000);
                    }
                }
                finally { if (core != null) core.Dispose(); }
            }
        }

        void NotifyStatus()
        {
            Action changed = StatusChanged;
            if (changed != null) changed();
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
            bool joined = worker.Join(5000);
            if (joined) wake.Dispose();
            if (joined && firstStatusWritten)
            {
                try
                {
                    if (System.IO.File.Exists(AppFiles.StatusFile)) System.IO.File.Delete(AppFiles.StatusFile);
                }
                catch { }
            }
        }
    }
}
