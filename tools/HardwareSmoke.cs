using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AudioFollow;

class HardwareSmoke
{
    static int checks;
    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    static SwitchStatus Await(SwitchEngine engine, Func<SwitchStatus, bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            SwitchStatus state = engine.Status;
            if (predicate(state)) return state;
            Thread.Sleep(100);
        } while (clock.ElapsedMilliseconds < 9000);
        throw new Exception("Engine did not reach expected state: " + engine.Status.Link + " / " + engine.Status.Error + " / " + engine.Status.Evidence);
    }
    static bool Defaults(AudioCore core, EndpointTarget target, int flow)
    {
        AudioSnapshot snapshot = core.Read();
        Endpoint endpoint = target.Resolve(snapshot, flow);
        return endpoint != null && snapshot.Defaults.All(id => id == endpoint.Id);
    }
    static Settings Keeping(Settings baseline, TriggerSettings trigger)
    {
        Settings result = baseline.Copy(); result.Trigger = trigger;
        result.Connected = new RouteTargets(); result.Disconnected = new RouteTargets();
        return result;
    }
    [MTAThread]
    static int Main(string[] args)
    {
        string running;
        if (Ipc.Send("status", out running)) throw new Exception("Exit the tray app before this opt-in real routing test.");
        using (var output = new AudioCore(null))
        using (var input = new AudioCore(null, 1))
        {
            AudioSnapshot before = output.Read(), captureBefore = input.Read();
            Settings baseline = Settings.Load().Upgrade(before, captureBefore);
            TriggerCandidate[] usb = TriggerInspection.List(TriggerKind.UsbAudio, before, captureBefore, CancellationToken.None);
            TriggerCandidate[] bt = TriggerInspection.List(TriggerKind.Bluetooth, before, captureBefore, CancellationToken.None);
            TriggerCandidate chosenUsb = usb.FirstOrDefault(c => c.Status == "音频可用");
            TriggerCandidate chosenBt = bt.FirstOrDefault(c => c.Settings.DisplayName == "M17") ?? bt.FirstOrDefault();
            Check(chosenUsb != null, "actual USB audio container enumerated");
            Check(chosenBt != null, "actual paired Bluetooth identity enumerated");
            var samples = new System.Collections.Generic.List<object>();
            try
            {
                using (var engine = new SwitchEngine(baseline))
                {
                    SwitchStatus state = Await(engine, s => s.StableLink == "Connected" || s.StableLink == "Disconnected" || s.StableLink == "ReceiverMissing");
                    Check(state.Trigger.Kind == TriggerKind.Hid && state.Evidence.Contains("matched ACK"), "original selected HID reports actual matched E3 status through interface");
                    Check(state.Link == "Disconnected" && state.ReferenceEndpointId != null, "actual headset off with receiver playback endpoint still active");
                    Await(engine, s => s.Error == null && s.MicrophoneError == null && s.Message == "使用断开时设备组");
                    Check(Defaults(output, baseline.Disconnected.Output, 0) && Defaults(input, baseline.Disconnected.Microphone, 1), "automatic HID-off route read back for all playback and input roles");
                    engine.SetMode(SwitchMode.Headset);
                    Await(engine, s => s.Mode == "Headset" && s.DefaultIds.All(id => id == s.HeadsetEndpointId) && s.CaptureDefaultIds.All(id => id == s.HeadsetMicrophoneId));
                    Check(Defaults(output, baseline.Connected.Output, 0) && Defaults(input, baseline.Connected.Microphone, 1), "compatible manual connected-group command sets all six roles");
                    engine.SetMode(SwitchMode.Monitor);
                    Await(engine, s => s.Mode == "Monitor" && s.DefaultIds.All(id => id == s.MonitorEndpointId) && s.CaptureDefaultIds.All(id => id == s.FallbackMicrophoneId));
                    Check(Defaults(output, baseline.Disconnected.Output, 0) && Defaults(input, baseline.Disconnected.Microphone, 1), "manual disconnected group read back");
                    Settings keepingUsb = Keeping(baseline, chosenUsb.Settings);
                    string[] keepOutput = output.Read().Defaults, keepInput = input.Read().Defaults;
                    engine.Configure(keepingUsb); engine.SetMode(SwitchMode.Automatic);
                    Await(engine, s => s.Trigger.Kind == TriggerKind.UsbAudio && s.StableLink == "Connected");
                    Check(output.Read().Defaults.SequenceEqual(keepOutput) && input.Read().Defaults.SequenceEqual(keepInput), "real USB availability detector plus explicit Keep preserves all roles");
                    engine.Configure(Keeping(baseline, chosenBt.Settings));
                    state = Await(engine, s => s.Trigger.Kind == TriggerKind.Bluetooth && (s.StableLink == "Connected" || s.StableLink == "Disconnected"));
                    Check(state.Link == "Disconnected" && state.Error == null, "actual selected paired Bluetooth transport reports disconnected");
                    Settings failure = baseline.Copy(); failure.Trigger = chosenUsb.Settings;
                    failure.Connected.Output = new EndpointTarget { EndpointId = "AudioFollow-nonexistent-output" };
                    engine.Configure(failure); engine.SetMode(SwitchMode.Headset);
                    Await(engine, s => s.Error != null && s.CaptureDefaultIds.All(id => id == s.HeadsetMicrophoneId));
                    Check(output.Read().Defaults.SequenceEqual(keepOutput) && Defaults(input, baseline.Connected.Microphone, 1), "real missing output preserves playback while microphone switches independently");
                    failure = baseline.Copy(); failure.Trigger = chosenUsb.Settings;
                    failure.Connected.Microphone = new EndpointTarget { EndpointId = "AudioFollow-nonexistent-input" };
                    string[] preservedInput = input.Read().Defaults;
                    engine.Configure(failure);
                    Await(engine, s => s.MicrophoneError != null && s.DefaultIds.All(id => id == s.HeadsetEndpointId));
                    Check(Defaults(output, baseline.Connected.Output, 0) && input.Read().Defaults.SequenceEqual(preservedInput), "real missing microphone preserves input while playback switches independently");
                    engine.SetMode(SwitchMode.Paused);
                    engine.Configure(Keeping(baseline, chosenUsb.Settings));
                    state = Await(engine, s => s.Mode == "Paused" && s.Trigger.Kind == TriggerKind.UsbAudio && s.StableLink == "Connected");
                    Check(state.Message == "已暂停自动切换", "paused engine continues actual detection");
                    var process = Process.GetCurrentProcess();
                    for (int cycle = 0; cycle < 12; cycle++)
                    {
                        int generation = engine.Status.DetectorGeneration;
                        TriggerSettings selected = cycle % 3 == 0 ? baseline.Trigger : cycle % 3 == 1 ? chosenUsb.Settings : chosenBt.Settings;
                        engine.Configure(Keeping(baseline, selected));
                        Await(engine, s => s.DetectorGeneration > generation && s.Trigger.Kind == selected.Kind &&
                            (s.StableLink == "Connected" || s.StableLink == "Disconnected"));
                        if (cycle % 3 == 2)
                        {
                            GC.Collect(); GC.WaitForPendingFinalizers(); process.Refresh();
                            samples.Add(new { Cycle = cycle + 1, Handles = process.HandleCount, WorkingSetBytes = process.WorkingSet64 });
                        }
                    }
                    Check(samples.Count == 4, "12 real HID/USB/Bluetooth detector replacements complete and dispose prior instances");
                    engine.Configure(baseline); engine.SetMode(SwitchMode.Automatic);
                    Await(engine, s => s.Trigger.Kind == TriggerKind.Hid && s.StableLink == "Disconnected" && s.Error == null && s.MicrophoneError == null);
                    process.Dispose();
                }
            }
            finally
            {
                try { output.RestoreDefaults(before.Defaults); }
                finally { input.RestoreDefaults(captureBefore.Defaults); }
                Check(before.Defaults.SequenceEqual(output.Read().Defaults) && captureBefore.Defaults.SequenceEqual(input.Read().Defaults),
                    "original Windows defaults restored after real routing tests");
            }
            string report = Json.Encode(new { Checks = checks, HeadsetPowerCycleTested = false, UsbPhysicalUnplugTested = false,
                BluetoothPhysicalConnectionCycleTested = false, ResourceSamples = samples });
            if (args.Length > 0) File.WriteAllText(args[0], report);
            Console.WriteLine(report);
        }
        return 0;
    }
}
