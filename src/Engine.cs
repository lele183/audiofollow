using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace AudioFollow
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
        public string Version = "1.3.0-preview.1";
        public string UpdatedUtc, Mode, Link, Evidence, RawFrame, Message, Error;
        public TriggerSettings Trigger;
        public string StableLink, ObservedUtc;
        public int DetectorGeneration;
        public string HeadsetEndpointId, MonitorEndpointId, ReferenceEndpointId;
        public string HeadsetMicrophoneId, FallbackMicrophoneId, MicrophoneError;
        public bool SwitchMicrophone;
        public bool OutputKeepCurrent, MicrophoneKeepCurrent;
        public string[] DefaultIds, DefaultNames;
        public string[] CaptureDefaultIds, CaptureDefaultNames;
        public Endpoint[] Endpoints;
        public Endpoint[] CaptureEndpoints;
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
        TriggerSession session;
        volatile bool stopping;
        volatile SwitchStatus status = new SwitchStatus { Message = "正在查询耳机连接状态…", Mode = "Automatic", Link = "Unknown" };
        bool reconnect;
        bool hotkeyToggle, hotkeyAutomatic;
        volatile bool firstStatusWritten;
        string lastLog;
        int audioDirty = 1, captureDirty = 1;
        readonly int processId = Process.GetCurrentProcess().Id;
        public event Action StatusChanged;

        public SwitchStatus Status { get { return status; } }

        public SwitchEngine(Settings settings)
        {
            this.settings = settings.Copy();
            worker = new Thread(Run) { IsBackground = true, Name = "AudioFollow audio monitor" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public void SetMode(SwitchMode value) { Enqueue(delegate { mode = value; }); }
        public void Configure(Settings value) { var copy = value.Copy(); Enqueue(delegate { settings = copy; reconnect = true; }); }
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
            long nextRead = 0, nextCaptureRead = 0, nextWrite = 0, nextSwitch = 0, nextMicrophoneSwitch = 0;
            long iterations = 0, audioReads = 0, statusWrites = 0;
            string lastUiKey = null;
            AudioSnapshot audio = null;
            AudioSnapshot capture = null;
            string error = null, message = null, outputSwitchError = null, inputSwitchError = null, inputReadError = null;
            using (session = new TriggerSession(delegate(TriggerSettings value) {
                return TriggerCatalog.Create(value, delegate { if (!stopping) wake.Set(); });
            }))
            {
                AudioCore core = null;
                AudioCore microphones = null;
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
                            if (Interlocked.Exchange(ref audioDirty, 0) != 0 || requested) nextRead = 0;
                            if (Interlocked.Exchange(ref captureDirty, 0) != 0 || requested) nextCaptureRead = 0;
                            if (requested) { outputSwitchError = inputSwitchError = null; nextSwitch = nextMicrophoneSwitch = 0; }
                            if (reconnect) nextRead = nextCaptureRead = 0;
                            Action devicesChanged = delegate { Interlocked.Exchange(ref audioDirty, 1); wake.Set(); };
                            if (core == null) core = new AudioCore(devicesChanged);
                            long now = clock.ElapsedMilliseconds;
                            if (audio == null || now >= nextRead)
                            {
                                audio = core.Read();
                                audioReads++;
                                nextRead = now + 30000;
                            }
                            if (capture == null || now >= nextCaptureRead)
                            {
                                try
                                {
                                    if (microphones == null) microphones = new AudioCore(delegate { Interlocked.Exchange(ref captureDirty, 1); wake.Set(); }, 1);
                                    capture = microphones.Read();
                                    inputReadError = null; nextCaptureRead = now + 30000;
                                }
                                catch (Exception ex)
                                {
                                    inputReadError = "无法读取麦克风设备：" + ex.Message;
                                    if (microphones != null) { microphones.Dispose(); microphones = null; }
                                    if (capture == null) capture = new AudioSnapshot();
                                    nextCaptureRead = now + 2000;
                                }
                            }
                            if (settings.Trigger == null || settings.Connected == null || settings.Disconnected == null)
                                settings = settings.Upgrade(audio, capture);
                            if (!session.HasDetector || reconnect)
                            {
                                reconnect = false;
                                settings.Validate();
                                session.Replace(settings.Trigger);
                            }
                            session.Poll(audio, inputReadError == null ? capture : null, now);
                            if (settings.Trigger.Kind == TriggerKind.Hid && string.IsNullOrEmpty(settings.Trigger.DeviceId) &&
                                !string.IsNullOrEmpty(session.Current.DeviceId))
                                settings.Trigger.DeviceId = session.Current.DeviceId;
                            Endpoint headset = settings.Resolve(audio, "headset");
                            Endpoint monitor = settings.Resolve(audio, "monitor");
                            Endpoint reference = settings.Resolve(audio, "reference");
                            Endpoint headsetMicrophone = settings.ResolveMicrophone(capture, true);
                            Endpoint fallbackMicrophone = settings.ResolveMicrophone(capture, false);
                            TriggerReading link = session.Current;
                            Route route = SwitchPlanner.Decide(mode, session.StableState,
                                settings.Trigger.Kind != TriggerKind.Hid || reference != null);
                            RouteTargets targets = route == Route.Headset ? settings.Connected : route == Route.Monitor ? settings.Disconnected : null;
                            Endpoint desired = route == Route.Headset ? headset : route == Route.Monitor ? monitor : null;
                            message = route == Route.None
                                ? (mode == SwitchMode.Paused ? "已暂停自动切换" : "正在确认触发状态，保留当前设备")
                                : route == Route.Headset ? "使用连接时设备组" : "使用断开时设备组";
                            error = null;
                            if (targets != null && !targets.Output.KeepCurrent && desired == null)
                                error = "指定的声音输出不可用，已保留当前输出。请检查或重新选择。";
                            if (desired == null || audio.Defaults.All(id => id == desired.Id)) outputSwitchError = null;
                            if (desired != null && audio.Defaults.Any(id => id != desired.Id))
                            {
                                if (now >= nextSwitch)
                                {
                                    nextSwitch = now + 2000;
                                    DirectionResult applied = RouteApplier.Apply(targets.Output, audio, 0, core.SetAllRoles, core.Read);
                                    audio = applied.Snapshot; if (applied.Changed) audioReads++;
                                    outputSwitchError = applied.Error;
                                }
                                error = outputSwitchError;
                            }
                            string microphoneError = inputReadError;
                            Endpoint desiredMicrophone = route == Route.Headset ? headsetMicrophone : route == Route.Monitor ? fallbackMicrophone : null;
                            if (!settings.SwitchMicrophone || route == Route.None || desiredMicrophone == null ||
                                capture.Defaults.All(id => id == desiredMicrophone.Id)) inputSwitchError = null;
                            if (settings.SwitchMicrophone && targets != null && !targets.Microphone.KeepCurrent && inputReadError == null)
                            {
                                if (desiredMicrophone == null) microphoneError = "目标麦克风不可用或名称重复，已保留当前输入。请在设备设置中选择。";
                                else if (capture.Defaults.Any(id => id != desiredMicrophone.Id))
                                {
                                    if (now >= nextMicrophoneSwitch)
                                    {
                                        nextMicrophoneSwitch = now + 2000;
                                        DirectionResult applied = RouteApplier.Apply(targets.Microphone, capture, 1, microphones.SetAllRoles, microphones.Read);
                                        capture = applied.Snapshot; inputSwitchError = applied.Error;
                                    }
                                    microphoneError = inputSwitchError;
                                }
                            }
                            status = new SwitchStatus {
                                ProcessId = processId, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                                Mode = mode.ToString(), Link = link.State.ToString(), Evidence = link.Reason,
                                Trigger = settings.Trigger, StableLink = session.StableState.ToString(),
                                ObservedUtc = link.ObservedUtc.ToString("o"), DetectorGeneration = session.Generation,
                                RawFrame = link.Raw, Message = message, Error = error,
                                HeadsetEndpointId = headset == null ? null : headset.Id,
                                MonitorEndpointId = monitor == null ? null : monitor.Id,
                                ReferenceEndpointId = reference == null ? null : reference.Id,
                                SwitchMicrophone = settings.SwitchMicrophone,
                                OutputKeepCurrent = targets != null && targets.Output.KeepCurrent,
                                MicrophoneKeepCurrent = targets != null && targets.Microphone.KeepCurrent,
                                HeadsetMicrophoneId = headsetMicrophone == null ? null : headsetMicrophone.Id,
                                FallbackMicrophoneId = fallbackMicrophone == null ? null : fallbackMicrophone.Id,
                                MicrophoneError = microphoneError,
                                DefaultIds = audio.Defaults, DefaultNames = audio.Defaults.Select(audio.NameOf).ToArray(),
                                CaptureDefaultIds = capture.Defaults, CaptureDefaultNames = capture.Defaults.Select(capture.NameOf).ToArray(),
                                Endpoints = audio.Endpoints.ToArray(),
                                CaptureEndpoints = capture.Endpoints.ToArray(),
                                HotkeyToggleRegistered = hotkeyToggle, HotkeyAutomaticRegistered = hotkeyAutomatic,
                                WorkerIterations = iterations, AudioReads = audioReads, StatusWrites = statusWrites
                            };
                            string entry = mode + " | " + link.State + " | " + message + " | " +
                                string.Join(", ", audio.Defaults) + " | input=" + string.Join(", ", capture.Defaults) +
                                " | micFollow=" + settings.SwitchMicrophone + " | " + error + " | " + microphoneError;
                            bool changed = entry != lastLog;
                            if (changed) { AppFiles.Log(entry + " | " + link.Reason); lastLog = entry; }
                            string uiKey = entry + "|" + link.Reason + "|" + session.StableState + "|" + settings.Trigger.DeviceId + "|" + hotkeyToggle + "|" + hotkeyAutomatic + "|" +
                                string.Join("|", audio.Endpoints.Concat(capture.Endpoints).Select(e => e.Id + ":" + e.State + ":" + e.Name));
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
                                Mode = mode.ToString(), Link = "Unknown", StableLink = "Unknown",
                                Trigger = settings.Trigger, Evidence = text, Message = "等待设备恢复", Error = text,
                                SwitchMicrophone = settings.SwitchMicrophone,
                                DefaultIds = audio == null ? null : audio.Defaults,
                                DefaultNames = audio == null ? null : audio.Defaults.Select(audio.NameOf).ToArray(),
                                CaptureDefaultIds = capture == null ? null : capture.Defaults,
                                CaptureDefaultNames = capture == null ? null : capture.Defaults.Select(capture.NameOf).ToArray()
                            };
                            NotifyStatus();
                            lastUiKey = null;
                            if (text != lastLog) { try { AppFiles.Log("ERROR " + text); } catch { } lastLog = text; }
                            try { AppFiles.WriteAtomic(AppFiles.StatusFile, Json.Encode(status)); } catch { }
                            nextRead = nextCaptureRead = 0;
                            if (core != null) { core.Dispose(); core = null; }
                            if (microphones != null) { microphones.Dispose(); microphones = null; }
                            wake.WaitOne(1500);
                        }
                        WaitHandle ready = session.Ready;
                        int wait = session.NextCheckMs(clock.ElapsedMilliseconds);
                        if (ready == null) wake.WaitOne(wait);
                        else WaitHandle.WaitAny(new WaitHandle[] { wake, ready }, wait);
                    }
                }
                finally { if (core != null) core.Dispose(); if (microphones != null) microphones.Dispose(); }
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
