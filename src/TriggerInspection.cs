using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace AudioFollow
{
    public static class TriggerInspection
    {
        public static TriggerCandidate[] List(TriggerKind kind, AudioSnapshot output, AudioSnapshot input, CancellationToken cancel)
        {
            if (kind == TriggerKind.Hid) return TriggerCatalog.HidDevices(output);
            if (kind == TriggerKind.UsbAudio) return TriggerCatalog.Usb(output, input);
            using (var signal = new AutoResetEvent(false))
            using (var source = new BluetoothSource(delegate { signal.Set(); }))
            {
                var clock = Stopwatch.StartNew();
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    bool complete; string error;
                    BluetoothRecord[] items = source.Snapshot(out complete, out error);
                    if (error != null) throw new InvalidOperationException(error);
                    if (complete || (clock.ElapsedMilliseconds >= 2000 && items.Length > 0))
                        return items.Where(r => BluetoothIdentity.CanList(r.ProtocolId, r.ContainerId, output.Endpoints.Concat(input.Endpoints)))
                            .Select(r => new TriggerCandidate {
                                Settings = new TriggerSettings { Kind = kind, DeviceId = r.Id, DisplayName = r.Name,
                                    ContainerId = r.ContainerId ?? "", ProtocolId = r.ProtocolId ?? "" },
                                Status = r.Connected == true ? "已连接" : r.Connected == false ? "已配对，未连接" : "连接状态待确认"
                            }).ToArray();
                    if (clock.ElapsedMilliseconds >= 7000) throw new TimeoutException("蓝牙设备枚举尚未完成，请稍后检查。");
                    signal.WaitOne(100);
                }
            }
        }

        public static TriggerReading Check(TriggerSettings settings, AudioSnapshot output, AudioSnapshot input, CancellationToken cancel)
        {
            using (var signal = new AutoResetEvent(false))
            using (var detector = TriggerCatalog.Create(settings, delegate { signal.Set(); }))
            {
                var clock = Stopwatch.StartNew();
                do
                {
                    cancel.ThrowIfCancellationRequested();
                    detector.Poll(output, input);
                    TriggerReading reading = detector.Current;
                    if (reading != null && reading.State != LinkState.Unknown) return reading;
                    WaitHandle ready = detector.Ready;
                    if (ready == null) signal.WaitOne(100);
                    else WaitHandle.WaitAny(new[] { signal, ready }, 100);
                } while (clock.ElapsedMilliseconds < 3500);
                return detector.Current ?? TriggerReading.Make(LinkState.Unknown, "正在确认设备", settings.DeviceId);
            }
        }
    }
}
