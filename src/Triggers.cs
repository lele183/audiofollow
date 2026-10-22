using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace AudioFollow
{
    public enum TriggerKind { Hid, UsbAudio, Bluetooth }

    public sealed class TriggerSettings
    {
        public TriggerKind Kind;
        public string DeviceId = "", ContainerId = "", ProtocolId = "", DisplayName = "";
    }

    public sealed class TriggerReading
    {
        public LinkState State;
        public string Reason, DeviceId, Raw;
        public DateTime ObservedUtc;
        public static TriggerReading Make(LinkState state, string reason, string id)
        { return new TriggerReading { State = state, Reason = reason, DeviceId = id, ObservedUtc = DateTime.UtcNow }; }
    }

    public sealed class TriggerCandidate
    {
        public TriggerSettings Settings;
        public string Status;
        public string Distinction = "";
        public override string ToString() { return Settings.DisplayName + Distinction + "  [" + Status + "]"; }
    }

    public interface ITriggerDetector : IDisposable
    {
        TriggerReading Current { get; }
        WaitHandle Ready { get; }
        void Poll(AudioSnapshot output, AudioSnapshot input);
    }

    public sealed class HidTriggerDetector : ITriggerDetector
    {
        readonly HeadsetSensor sensor;
        public HidTriggerDetector(TriggerSettings settings) { sensor = new HeadsetSensor(settings.DeviceId); }
        public WaitHandle Ready { get { return sensor.ReadReady; } }
        public TriggerReading Current
        {
            get
            {
                LinkReading value = sensor.Current;
                return new TriggerReading { State = value.State, Reason = value.Evidence,
                    ObservedUtc = value.ObservedUtc, Raw = value.Raw, DeviceId = sensor.DeviceId };
            }
        }
        public void Poll(AudioSnapshot output, AudioSnapshot input) { sensor.Poll(0); }
        public void Dispose() { sensor.Dispose(); }
    }

    public sealed class UsbAudioDetector : ITriggerDetector
    {
        readonly TriggerSettings settings;
        public UsbAudioDetector(TriggerSettings settings) { this.settings = settings; }
        public WaitHandle Ready { get { return null; } }
        public TriggerReading Current { get; private set; }
        public void Poll(AudioSnapshot output, AudioSnapshot input) { Current = Observe(settings, output, input); }
        public static TriggerReading Observe(TriggerSettings settings, AudioSnapshot output, AudioSnapshot input)
        {
            if (output == null || input == null)
                return TriggerReading.Make(LinkState.Unknown, "正在枚举播放和录音设备", settings.DeviceId);
            if (!DeviceIdentity.ValidContainer(settings.ContainerId) || settings.DeviceId != "usb:" + settings.ContainerId)
                return TriggerReading.Make(LinkState.Unknown, "USB 设备身份无效，请重新选择", settings.DeviceId);
            Endpoint[] related = output.Endpoints.Concat(input.Endpoints).Where(e =>
                e.Transport == "USB" && DeviceIdentity.Same(e.ContainerId, settings.ContainerId)).ToArray();
            bool active = related.Any(e => e.Active);
            return TriggerReading.Make(active ? LinkState.Connected : LinkState.Disconnected,
                active ? "所选 USB 设备至少一个音频端点可用" : "所选 USB 音频端点全部不可用或已移除", settings.DeviceId);
        }
        public void Dispose() { }
    }

    // All state used for routing is pulled from the current instance. Old async callbacks
    // can only wake the loop; they cannot inject a reading into a replacement detector.
    public sealed class TriggerSession : IDisposable
    {
        readonly Func<TriggerSettings, ITriggerDetector> create;
        readonly TriggerDebouncer debounce = new TriggerDebouncer();
        ITriggerDetector detector;
        public int Generation { get; private set; }
        public bool HasDetector { get { return detector != null; } }
        public TriggerReading Current { get { return detector == null || detector.Current == null ?
            TriggerReading.Make(LinkState.Unknown, "正在确认所选设备", null) : detector.Current; } }
        public LinkState StableState { get { return debounce.Stable; } }
        public int NextCheckMs(long now) { return debounce.NextCheckMs(now); }
        public WaitHandle Ready { get { return detector == null ? null : detector.Ready; } }
        public TriggerSession(Func<TriggerSettings, ITriggerDetector> create) { this.create = create; }
        public void Replace(TriggerSettings settings)
        {
            Generation++;
            if (detector != null) { detector.Dispose(); detector = null; }
            debounce.Reset();
            detector = create(settings);
        }
        public void Poll(AudioSnapshot output, AudioSnapshot input, long now)
        {
            if (detector == null) return;
            try { detector.Poll(output, input); }
            catch { debounce.Reset(); throw; }
            debounce.Update(Current, now);
        }
        public void Dispose() { if (detector != null) detector.Dispose(); detector = null; debounce.Reset(); }
    }

    public sealed class TriggerDebouncer
    {
        LinkState candidate = LinkState.Unknown;
        string identity;
        long since;
        public LinkState Stable { get; private set; }
        public void Reset() { candidate = Stable = LinkState.Unknown; identity = null; }
        public void Update(TriggerReading reading, long now)
        {
            if (reading.State == LinkState.Unknown) { Reset(); return; }
            if (reading.State != candidate || !string.Equals(identity, reading.DeviceId, StringComparison.OrdinalIgnoreCase))
            { candidate = reading.State; identity = reading.DeviceId; since = now; Stable = LinkState.Unknown; }
            int delay = candidate == LinkState.Connected ? 500 : 1500;
            if (now - since >= delay) Stable = candidate;
        }
        public int NextCheckMs(long now)
        {
            if (candidate == LinkState.Unknown || Stable == candidate) return 1000;
            return (int)Math.Max(1, Math.Min(1000, since + (candidate == LinkState.Connected ? 500 : 1500) - now));
        }
    }

    public static class TriggerCatalog
    {
        public static ITriggerDetector Create(TriggerSettings settings, Action changed)
        {
            if (settings.Kind == TriggerKind.Hid) return new HidTriggerDetector(settings);
            if (settings.Kind == TriggerKind.UsbAudio) return new UsbAudioDetector(settings);
            if (settings.Kind == TriggerKind.Bluetooth) return new BluetoothTriggerDetector(settings, changed);
            throw new InvalidOperationException("不支持的检测方式");
        }
        public static TriggerCandidate[] Usb(AudioSnapshot output, AudioSnapshot input)
        {
            return output.Endpoints.Concat(input.Endpoints).Where(e => e.Transport == "USB" && DeviceIdentity.ValidContainer(e.ContainerId))
                .GroupBy(e => e.ContainerId, StringComparer.OrdinalIgnoreCase).Select(g => new TriggerCandidate {
                    Settings = new TriggerSettings { Kind = TriggerKind.UsbAudio, DeviceId = "usb:" + g.Key,
                        ContainerId = g.Key, DisplayName = g.OrderBy(e => e.Flow).First().Name },
                    Status = g.Any(e => e.Active) ? "音频可用" : "音频不可用"
                }).ToArray();
        }
        public static TriggerCandidate[] HidDevices(AudioSnapshot output)
        {
            return Hid.Enumerate().Select((d, i) => new TriggerCandidate {
                Settings = new TriggerSettings { Kind = TriggerKind.Hid, DeviceId = d.Path,
                    ContainerId = ContainerForHid(d.Path, output), DisplayName = "Barracuda X 2022 · 接收器 " + (i + 1) },
                Status = "接收器在场，需无线查询"
            }).ToArray();
        }
        static string ContainerForHid(string path, AudioSnapshot output)
        {
            // Resolve HID ancestry through the same PnP container, never a fuzzy device name.
            int end = path.LastIndexOf("#{", StringComparison.Ordinal);
            if (!path.StartsWith(@"\\?\") || end < 4) return "";
            var endpoint = new Endpoint { InstanceId = path.Substring(4, end - 4).Replace('#', '\\') };
            DeviceIdentity.Enrich(endpoint);
            return endpoint.ContainerId ?? "";
        }
        public static Endpoint Recommend(TriggerSettings settings, IEnumerable<Endpoint> endpoints, int flow)
        {
            if (!DeviceIdentity.ValidContainer(settings.ContainerId)) return null;
            Endpoint[] matches = endpoints.Where(e => e.Flow == flow && DeviceIdentity.Same(e.ContainerId, settings.ContainerId)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }
}
