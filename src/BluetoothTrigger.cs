using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Windows.Devices.Enumeration;

namespace AudioFollow
{
    public static class BluetoothIdentity
    {
        public const string Classic = "e0cbf06c-cd8b-4647-bb8a-263b43f0f974";
        public const string LowEnergy = "bb7bb05e-5972-42b5-94fc-76eaa7084d49";
        public static bool AudioProtocol(string protocol)
        { return DeviceIdentity.Same(protocol, Classic) || DeviceIdentity.Same(protocol, LowEnergy); }
        public static bool CanList(string protocol, string container, IEnumerable<Endpoint> endpoints)
        {
            if (DeviceIdentity.Same(protocol, Classic)) return true;
            // A battery/companion BLE association is never evidence for the audio transport.
            return DeviceIdentity.Same(protocol, LowEnergy) && DeviceIdentity.ValidContainer(container) &&
                endpoints.Any(e => e.Transport == "BluetoothLE" && DeviceIdentity.Same(e.ContainerId, container));
        }
        public static TriggerReading Observe(TriggerSettings settings, string id, string protocol, bool? connected, bool complete, bool present, string failure)
        {
            if (!string.IsNullOrEmpty(failure)) return TriggerReading.Make(LinkState.Unknown, failure, settings.DeviceId);
            if (!DeviceIdentity.Same(settings.DeviceId, id) || !DeviceIdentity.Same(settings.ProtocolId, protocol))
                return TriggerReading.Make(LinkState.Unknown, "所选蓝牙设备或传输身份不匹配", settings.DeviceId);
            if (!present && complete) return TriggerReading.Make(LinkState.Disconnected, "蓝牙枚举完成，所选设备不在场", id);
            if (!present || !connected.HasValue) return TriggerReading.Make(LinkState.Unknown, "等待所选蓝牙设备的实际连接属性", id);
            return TriggerReading.Make(connected.Value ? LinkState.Connected : LinkState.Disconnected,
                connected.Value ? "Windows AEP：所选蓝牙传输已连接" : "Windows AEP：已配对，当前未连接", id);
        }
    }

    public sealed class BluetoothRecord
    {
        public string Id, Name, ContainerId, ProtocolId;
        public bool? Connected;
        public DateTime ObservedUtc;
    }

    // No callbacks escape this object's lock after disposal. Reconciliation is read-only
    // and revision-checked so a delayed snapshot cannot overwrite a newer watcher event.
    public sealed class BluetoothSource : IDisposable
    {
        const string Query = "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True AND " +
            "(System.Devices.Aep.ProtocolId:=\"{" + BluetoothIdentity.Classic + "}\" OR System.Devices.Aep.ProtocolId:=\"{" + BluetoothIdentity.LowEnergy + "}\")";
        static readonly string[] Properties = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.ProtocolId", "System.Devices.Aep.ContainerId" };
        readonly object sync = new object();
        readonly Dictionary<string, BluetoothRecord> records = new Dictionary<string, BluetoothRecord>(StringComparer.OrdinalIgnoreCase);
        readonly Action changed;
        readonly string selectedId;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        DeviceWatcher watcher;
        bool disposed, complete, reconciling;
        int generation, revision;
        long nextReconcile;
        string failure;
        public BluetoothSource(Action changed, string selectedId = null) { this.changed = changed; this.selectedId = selectedId; Start(); }

        void Start()
        {
            lock (sync)
            {
                if (disposed) return;
                try
                {
                    complete = false; failure = null;
                    watcher = DeviceInformation.CreateWatcher(Query, Properties, DeviceInformationKind.AssociationEndpoint);
                    watcher.Added += Added; watcher.Updated += Updated; watcher.Removed += Removed;
                    watcher.EnumerationCompleted += Completed; watcher.Stopped += Stopped;
                    watcher.Start();
                    nextReconcile = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
                    if (!string.IsNullOrEmpty(selectedId)) { reconciling = true; Reconcile(generation, revision); }
                }
                catch (Exception ex) { failure = "无法启动蓝牙观察器：" + ex.Message; nextReconcile = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency; }
            }
        }
        bool Active(DeviceWatcher sender) { return !disposed && object.ReferenceEquals(sender, watcher); }
        void Signal() { if (changed != null) changed(); }
        void Added(DeviceWatcher sender, DeviceInformation info)
        {
            lock (sync)
            {
                if (!Active(sender) || (!string.IsNullOrEmpty(selectedId) && !DeviceIdentity.Same(selectedId, info.Id))) return;
                records[info.Id] = From(info); revision++; Signal();
            }
        }
        void Updated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (sync)
            {
                if (!Active(sender) || (!string.IsNullOrEmpty(selectedId) && !DeviceIdentity.Same(selectedId, update.Id))) return;
                BluetoothRecord item;
                if (!records.TryGetValue(update.Id, out item)) return;
                Apply(item, update.Properties); revision++; Signal();
            }
        }
        void Removed(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            lock (sync)
            {
                if (!Active(sender) || (!string.IsNullOrEmpty(selectedId) && !DeviceIdentity.Same(selectedId, update.Id))) return;
                records.Remove(update.Id);
                if (DeviceIdentity.Same(selectedId, update.Id)) complete = true;
                revision++; Signal();
            }
        }
        void Completed(DeviceWatcher sender, object args)
        { lock (sync) { if (!Active(sender)) return; complete = true; revision++; Signal(); } }
        void Stopped(DeviceWatcher sender, object args)
        { lock (sync) { if (!Active(sender)) return; failure = "蓝牙观察器已停止，正在重试"; nextReconcile = 0; Signal(); } }
        static BluetoothRecord From(DeviceInformation info)
        {
            var item = new BluetoothRecord { Id = info.Id, Name = info.Name };
            Apply(item, info.Properties); return item;
        }
        static void Apply(BluetoothRecord item, IReadOnlyDictionary<string, object> properties)
        {
            object value;
            if (properties.TryGetValue("System.Devices.Aep.IsConnected", out value))
                item.Connected = value is bool ? (bool?)value : null;
            if (properties.TryGetValue("System.Devices.Aep.ProtocolId", out value))
                item.ProtocolId = value == null ? null : value.ToString().Trim('{', '}');
            if (properties.TryGetValue("System.Devices.Aep.ContainerId", out value))
                item.ContainerId = value == null ? null : value.ToString().Trim('{', '}');
            item.ObservedUtc = DateTime.UtcNow;
        }
        public BluetoothRecord[] Snapshot(out bool enumerated, out string error)
        {
            lock (sync)
            {
                enumerated = complete; error = failure;
                return records.Values.Select(r => new BluetoothRecord { Id = r.Id, Name = r.Name, ContainerId = r.ContainerId,
                    ProtocolId = r.ProtocolId, Connected = r.Connected, ObservedUtc = r.ObservedUtc }).ToArray();
            }
        }
        public void Poll()
        {
            lock (sync)
            {
                if (disposed || reconciling || Stopwatch.GetTimestamp() < nextReconcile) return;
                nextReconcile = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
                if (failure != null)
                {
                    StopWatcher(); records.Clear(); generation++; Start(); return;
                }
                reconciling = true;
                Reconcile(generation, revision);
            }
        }
        async void Reconcile(int expectedGeneration, int expectedRevision)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                timeout.CancelAfter(15000);
                try
                {
                    DeviceInformation[] items;
                    if (!string.IsNullOrEmpty(selectedId))
                    {
                        DeviceInformation item = await DeviceInformation.CreateFromIdAsync(selectedId, Properties, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false);
                        items = item == null ? new DeviceInformation[0] : new[] { item };
                    }
                    else items = (await DeviceInformation.FindAllAsync(Query, Properties, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token).ConfigureAwait(false)).ToArray();
                    lock (sync)
                    {
                        if (disposed || generation != expectedGeneration) return;
                        if (revision == expectedRevision)
                        {
                            records.Clear();
                            foreach (DeviceInformation info in items) records[info.Id] = From(info);
                            complete = true; failure = null; revision++; Signal();
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        if (!disposed && generation == expectedGeneration)
                        { failure = ex is OperationCanceledException ? "蓝牙连接复查超时，保留当前设备" : "蓝牙连接复查失败：" + ex.Message; nextReconcile = 0; Signal(); }
                    }
                }
                finally { lock (sync) { if (generation == expectedGeneration) reconciling = false; } }
            }
        }
        void StopWatcher()
        {
            DeviceWatcher old = watcher; watcher = null;
            if (old == null) return;
            old.Added -= Added; old.Updated -= Updated; old.Removed -= Removed;
            old.EnumerationCompleted -= Completed; old.Stopped -= Stopped;
            if (old.Status == DeviceWatcherStatus.Started || old.Status == DeviceWatcherStatus.EnumerationCompleted) old.Stop();
        }
        public void Dispose()
        {
            lock (sync) { if (disposed) return; disposed = true; generation++; StopWatcher(); records.Clear(); }
            lifetime.Cancel(); lifetime.Dispose();
        }
    }

    public sealed class BluetoothTriggerDetector : ITriggerDetector
    {
        readonly TriggerSettings settings;
        readonly BluetoothSource source;
        public BluetoothTriggerDetector(TriggerSettings settings, Action changed)
        { this.settings = settings; source = new BluetoothSource(changed, settings.DeviceId); }
        public WaitHandle Ready { get { return null; } }
        public TriggerReading Current { get; private set; }
        public void Poll(AudioSnapshot output, AudioSnapshot input)
        {
            source.Poll();
            if (DeviceIdentity.Same(settings.ProtocolId, BluetoothIdentity.LowEnergy) &&
                (output == null || input == null || !BluetoothIdentity.CanList(settings.ProtocolId, settings.ContainerId, output.Endpoints.Concat(input.Endpoints))))
            {
                Current = TriggerReading.Make(LinkState.Unknown, "无法确认所选 LE 设备属于音频传输，保留当前设备", settings.DeviceId);
                return;
            }
            bool complete; string error;
            BluetoothRecord[] items = source.Snapshot(out complete, out error);
            BluetoothRecord item = items.FirstOrDefault(i => DeviceIdentity.Same(i.Id, settings.DeviceId));
            Current = BluetoothIdentity.Observe(settings, item == null ? settings.DeviceId : item.Id,
                item == null ? settings.ProtocolId : item.ProtocolId, item == null ? null : item.Connected, complete, item != null, error);
            if (item != null) Current.ObservedUtc = item.ObservedUtc;
        }
        public void Dispose() { source.Dispose(); }
    }
}
