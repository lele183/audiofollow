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
        public string Version = "1.0.0";
        public string UpdatedUtc, Mode, Link, Evidence, RawFrame, Message, Error;
        public string HeadsetEndpointId, MonitorEndpointId, ReferenceEndpointId;
        public string[] DefaultIds, DefaultNames;
        public Endpoint[] Endpoints;
        public bool HotkeyToggleRegistered, HotkeyAutomaticRegistered;
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
            long nextRead = 0, nextWrite = 0, nextSwitch = 0, nextHidRead = 0;
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
                            if (requested || wake.WaitOne(0)) nextRead = 0;
                            if (reconnect) { reconnect = false; sensor.Reconnect(); nextRead = nextSwitch = nextHidRead = 0; }
                            if (core == null) core = new AudioCore(delegate { wake.Set(); });
                            long now = clock.ElapsedMilliseconds;
                            bool readHid = now >= nextHidRead;
                            sensor.Poll(readHid ? 100 : 5, readHid);
                            if (readHid) nextHidRead = now + 200;
                            if (audio == null || now >= nextRead)
                            {
                                audio = core.Read();
                                nextRead = now + 3000;
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
                            }
                            status = new SwitchStatus {
                                ProcessId = Process.GetCurrentProcess().Id, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                                Mode = mode.ToString(), Link = link.State.ToString(), Evidence = link.Evidence,
                                RawFrame = link.Raw, Message = message, Error = error,
                                HeadsetEndpointId = headset == null ? null : headset.Id,
                                MonitorEndpointId = monitor == null ? null : monitor.Id,
                                ReferenceEndpointId = reference == null ? null : reference.Id,
                                DefaultIds = audio.Defaults, DefaultNames = audio.Defaults.Select(audio.NameOf).ToArray(),
                                Endpoints = audio.Endpoints.ToArray(),
                                HotkeyToggleRegistered = hotkeyToggle, HotkeyAutomaticRegistered = hotkeyAutomatic
                            };
                            string entry = mode + " | " + link.State + " | " + message + " | " +
                                string.Join(", ", audio.Defaults) + " | " + error;
                            if (entry != lastLog) { AppFiles.Log(entry + " | " + link.Evidence); lastLog = entry; }
                            if (now >= nextWrite || requested)
                            {
                                AppFiles.WriteAtomic(AppFiles.StatusFile, Json.Encode(status));
                                firstStatusWritten = true;
                                nextWrite = now + 2000;
                            }
                        }
                        catch (Exception ex)
                        {
                            string text = ex.Message;
                            status = new SwitchStatus {
                                ProcessId = Process.GetCurrentProcess().Id, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                                Mode = mode.ToString(), Link = sensor.Current.State.ToString(),
                                Evidence = sensor.Current.Evidence, Message = "等待音频设备恢复", Error = text,
                                DefaultIds = audio == null ? null : audio.Defaults,
                                DefaultNames = audio == null ? null : audio.Defaults.Select(audio.NameOf).ToArray()
                            };
                            if (text != lastLog) { try { AppFiles.Log("ERROR " + text); } catch { } lastLog = text; }
                            try { AppFiles.WriteAtomic(AppFiles.StatusFile, Json.Encode(status)); } catch { }
                            nextRead = 0;
                            if (core != null) { core.Dispose(); core = null; }
                            wake.WaitOne(1500);
                        }
                        wake.WaitOne(sensor.Current.State == LinkState.ReceiverMissing || sensor.Current.State == LinkState.Unknown ? 150 : 20);
                    }
                }
                finally { if (core != null) core.Dispose(); }
            }
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
