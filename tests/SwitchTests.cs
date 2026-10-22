using System;
using System.Collections.Generic;
using System.Linq;
using AudioFollow;

class SwitchTests
{
    static int passed;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        passed++; Console.WriteLine("PASS " + name);
    }
    static byte[] Frame(byte family, params byte[] data)
    {
        var result = new byte[10 + data.Length];
        result[0] = 0x50; result[1] = 0x49; result[2] = family;
        result[8] = (byte)data.Length;
        Array.Copy(data, 0, result, 10, data.Length);
        return result;
    }
    static byte[] Report(params byte[][] frames)
    {
        int length = frames.Sum(f => f.Length);
        var result = new byte[64]; result[0] = 1; result[1] = 0x80; result[2] = (byte)length;
        int offset = 3;
        foreach (byte[] frame in frames) { Array.Copy(frame, 0, result, offset, frame.Length); offset += frame.Length; }
        return result;
    }
    static void ProtocolTests()
    {
        var observations = new List<LinkReading>();
        var protocol = new LinkProtocol { OnReading = observations.Add };
        byte[] query = protocol.NewQuery();
        Check(BitConverter.ToString(query) == "01-80-06-50-41-0E-01-01-E3", "only validated read-only E3 command is emitted");
        protocol.Feed(Report(Frame(0x0e, 0xe3, 1)));
        Check(observations.Count == 0, "E3 without a matched ACK cannot invent a startup state");
        protocol.Feed(Report(Frame(1, 0x0e, 0x82, 0), Frame(0x0e, 0xe3, 0)));
        Check(observations.Count == 0, "ACK from another sequence is ignored");
        protocol.Feed(Report(Frame(1, 0x0e, 0x81, 1), Frame(0x0e, 0xe3, 0)));
        Check(observations.Count == 0, "failed status query is not treated as disconnected");
        protocol.Feed(Report(Frame(1, 0x0e, 0x81, 0), Frame(0x0e, 0xe3, 1)));
        Check(observations.Count == 1 && observations[0].State == LinkState.Connected, "coalesced ACK and E3 response establish an active link");
        protocol.Feed(Report(Frame(8, 0x21, 2, 1, 0), Frame(8, 0x2a, 2, 1, 0)));
        Check(observations.Count == 1, "zero battery and removed charging cable do not mean headset off");
        protocol.Feed(Report(Frame(8, 0x20, 2, 1, 0)));
        Check(observations.Count == 2 && observations[1].State == LinkState.Disconnected, "passive power-off event establishes disconnection");
        protocol.Feed(Report(Frame(8, 0x20, 2, 1, 1)));
        Check(observations.Count == 3 && observations[2].State == LinkState.Connected, "passive power-on event establishes connection");
        byte[] shortPrefix = new byte[5]; shortPrefix[0] = 1; shortPrefix[1] = 0x80; shortPrefix[2] = 2; shortPrefix[3] = 0x50; shortPrefix[4] = 0;
        protocol.Feed(shortPrefix);
        Check(observations.Count == 3, "short non-E3 report cannot crash or create a state");
        byte[] transmission = Frame(8, 0x20, 2, 1, 0);
        protocol.Feed(Report(transmission));
        Check(observations.Count == 4 && observations[3].State == LinkState.Disconnected, "fresh header replaces stale prefix and emits a complete event");
        protocol.Feed(Report(Frame(8, 0x20, 2, 1, 2)));
        Check(observations.Count == 4, "unknown link values are rejected");
        byte[] malformed = Report(Frame(8, 0x20, 2, 1, 0)); malformed[2] = 63;
        protocol.Feed(malformed);
        Check(observations.Count == 4, "truncated HID framing is rejected");
        byte[] media = new byte[64]; media[0] = 2; media[12] = 0x20; media[13] = 2; media[14] = 1; media[15] = 0;
        protocol.Feed(media);
        Check(observations.Count == 4, "consumer/media-key collection is not connection evidence");
        byte[] next = protocol.NewQuery();
        protocol.Feed(Report(Frame(1, 0x0e, (byte)(next[6] | 0x80), 0)));
        byte[] response = Frame(0x0e, 0xe3, 0);
        protocol.Feed(Report(response.Take(7).ToArray()));
        Check(observations.Count == 4, "partial response is buffered without changing state");
        protocol.Feed(Report(response.Skip(7).ToArray()));
        Check(observations.Count == 5 && observations[4].State == LinkState.Disconnected, "fragmented E3 response is reassembled");
        next = protocol.NewQuery();
        protocol.Feed(Report(Frame(1, 0x0e, (byte)(next[6] | 0x80), 0)));
        protocol.Reset();
        protocol.Feed(Report(Frame(0x0e, 0xe3, 1)));
        Check(observations.Count == 5, "reconnect discards a previous session ACK");
        next = protocol.NewQuery();
        protocol.Feed(Report(Frame(1, 0x0e, (byte)(next[6] | 0x80), 0), Frame(0x0e, 0xe3, 0)));
        Check(observations.Count == 6 && observations[5].State == LinkState.Disconnected, "headset can be verified off while USB receiver remains present");
    }
    static void RoutingTests()
    {
        Check(SwitchPlanner.Decide(SwitchMode.Automatic, LinkState.Connected, true) == Route.Headset, "automatic connected headset uses surround");
        Check(SwitchPlanner.Decide(SwitchMode.Automatic, LinkState.Disconnected, true) == Route.Monitor, "automatic disconnected headset uses monitor even while dongle endpoint is active");
        Check(SwitchPlanner.Decide(SwitchMode.Automatic, LinkState.ReceiverMissing, false) == Route.Monitor, "receiver unplug uses monitor");
        Check(SwitchPlanner.Decide(SwitchMode.Automatic, LinkState.Unknown, true) == Route.None, "unknown hardware state preserves current output");
        Check(SwitchPlanner.Decide(SwitchMode.Automatic, LinkState.Connected, false) == Route.Monitor, "missing physical playback endpoint does not route into surround");
        Check(SwitchPlanner.Decide(SwitchMode.Headset, LinkState.Disconnected, true) == Route.Headset, "manual fallback can select headset independently of detection");
        Check(SwitchPlanner.Decide(SwitchMode.Monitor, LinkState.Connected, true) == Route.Monitor, "manual monitor override persists until auto is resumed");
        Check(SwitchPlanner.Decide(SwitchMode.Paused, LinkState.Connected, true) == Route.None, "pause never switches outputs");
        var settings = new Settings();
        var snapshot = new AudioSnapshot();
        snapshot.Endpoints.Add(new Endpoint { Id = "old", Name = Settings.MonitorName, State = 8 });
        snapshot.Endpoints.Add(new Endpoint { Id = "active", Name = Settings.MonitorName, State = 1 });
        Check(settings.Resolve(snapshot, "monitor").Id == "active", "inactive duplicate display endpoint is not selected");
        snapshot.Endpoints.Add(new Endpoint { Id = "second", Name = Settings.MonitorName, State = 1 });
        Check(settings.Resolve(snapshot, "monitor") == null, "two active identical names require explicit selection");
        settings.MonitorEndpointId = "active";
        Check(settings.Resolve(snapshot, "monitor").Id == "active", "saved endpoint ID resolves duplicate names");
        settings.MonitorEndpointId = "old";
        Check(settings.Resolve(snapshot, "monitor") == null, "unavailable pinned endpoint does not silently select another device");
    }
    static void MicrophoneTests()
    {
        var settings = new Settings();
        var snapshot = new AudioSnapshot();
        snapshot.Endpoints.Add(new Endpoint { Id = "output-with-same-name", Name = Settings.HeadsetMicrophoneName, Flow = 0, State = 1 });
        snapshot.Endpoints.Add(new Endpoint { Id = "headset-mic", Name = Settings.HeadsetMicrophoneName, Flow = 1, State = 1 });
        snapshot.Endpoints.Add(new Endpoint { Id = "unplugged", Name = Settings.FallbackMicrophoneName, Flow = 1, State = 8 });
        Check(settings.ResolveMicrophone(snapshot, true).Id == "headset-mic", "microphone matching cannot select a playback endpoint with the same name");
        Check(settings.ResolveMicrophone(snapshot, false) == null, "unavailable fallback microphone is never selected");
        snapshot.Endpoints.Add(new Endpoint { Id = "desktop-mic", Name = Settings.FallbackMicrophoneName, Flow = 1, State = 1 });
        Check(settings.ResolveMicrophone(snapshot, false).Id == "desktop-mic", "unique active desktop microphone resolves despite an unplugged duplicate");
        snapshot.Endpoints.Add(new Endpoint { Id = "second-mic", Name = Settings.HeadsetMicrophoneName, Flow = 1, State = 1 });
        Check(settings.ResolveMicrophone(snapshot, true) == null, "duplicate active microphones require an explicit choice");
        settings.HeadsetMicrophoneId = "headset-mic";
        Check(settings.ResolveMicrophone(snapshot, true).Id == "headset-mic", "pinned microphone resolves duplicate names");
        settings.HeadsetMicrophoneId = "missing";
        Check(settings.ResolveMicrophone(snapshot, true) == null, "missing pinned microphone never falls back to another microphone");
        settings.HeadsetMicrophoneId = "output-with-same-name";
        Check(settings.ResolveMicrophone(snapshot, true) == null, "pinned playback ID cannot be selected as a microphone");
        settings.HeadsetEndpointId = "headset-mic";
        Check(settings.Resolve(snapshot, "headset") == null, "pinned microphone ID cannot be selected for playback");
        Settings legacy = Json.Decode<Settings>("{\"HeadsetEndpointId\":\"saved-output\",\"MonitorEndpointId\":\"saved-monitor\",\"ReferenceEndpointId\":\"saved-reference\"}");
        Check(legacy.SwitchMicrophone && legacy.HeadsetEndpointId == "saved-output" &&
            legacy.MonitorEndpointId == "saved-monitor" && legacy.ReferenceEndpointId == "saved-reference" &&
            legacy.HeadsetMicrophoneId == "" && legacy.FallbackMicrophoneId == "", "legacy settings preserve outputs and enable default microphone following");
        settings.SwitchMicrophone = false; settings.HeadsetMicrophoneId = "headset-mic"; settings.FallbackMicrophoneId = "desktop-mic";
        Settings restored = Json.Decode<Settings>(Json.Encode(settings));
        Check(!restored.SwitchMicrophone && restored.HeadsetMicrophoneId == "headset-mic" &&
            restored.FallbackMicrophoneId == "desktop-mic", "microphone preferences and disabled-follow setting survive serialization");
    }
    static int Main()
    {
        try { ProtocolTests(); RoutingTests(); MicrophoneTests(); TriggerTests(); MigrationTests(); IndependentRoutingTests(); Console.WriteLine("All " + passed + " checks passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    sealed class FakeDetector : ITriggerDetector
    {
        public TriggerReading Reading = TriggerReading.Make(LinkState.Unknown, "enumerating", "a");
        public bool Disposed;
        public bool FailPoll;
        public TriggerReading Current { get { return Reading; } }
        public System.Threading.WaitHandle Ready { get { return null; } }
        public void Poll(AudioSnapshot output, AudioSnapshot input) { if (FailPoll) throw new Exception("read failed"); }
        public void Dispose() { Disposed = true; }
    }
    static void TriggerTests()
    {
        var first = new FakeDetector();
        var second = new FakeDetector();
        int created = 0;
        using (var session = new TriggerSession(delegate { return created++ == 0 ? first : second; }))
        {
            var selected = new TriggerSettings();
            session.Replace(selected); session.Poll(null, null, 0);
            Check(session.StableState == LinkState.Unknown, "initial enumeration preserves devices");
            first.Reading = TriggerReading.Make(LinkState.Connected, "connected", "a");
            session.Poll(null, null, 10); session.Poll(null, null, 509);
            Check(session.StableState == LinkState.Unknown && session.NextCheckMs(509) == 1, "connection waits for full 500ms");
            session.Poll(null, null, 510);
            Check(session.StableState == LinkState.Connected, "connection confirms after 500ms");
            first.Reading.State = LinkState.Disconnected; session.Poll(null, null, 600); session.Poll(null, null, 2099);
            Check(session.StableState == LinkState.Unknown, "disconnection waits for full 1500ms");
            first.Reading.State = LinkState.Connected; session.Poll(null, null, 2100); session.Poll(null, null, 2600);
            Check(session.StableState == LinkState.Connected, "short disconnect bounce cannot trigger fallback");
            first.Reading.State = LinkState.ReceiverMissing; session.Poll(null, null, 3000); session.Poll(null, null, 4500);
            Check(session.StableState == LinkState.ReceiverMissing, "receiver missing uses disconnect debounce");
            first.Reading.State = LinkState.Unknown; session.Poll(null, null, 4600);
            Check(session.StableState == LinkState.Unknown, "unknown immediately cancels confirmed state");
            first.Reading.State = LinkState.Connected; session.Poll(null, null, 4700);
            first.Reading.DeviceId = "different"; session.Poll(null, null, 5100); session.Poll(null, null, 5200);
            Check(session.StableState == LinkState.Unknown, "device identity change resets the debounce");
            session.Replace(selected);
            Check(first.Disposed && session.Generation == 2, "replacement disposes previous detector and advances generation");
            first.Reading = TriggerReading.Make(LinkState.Disconnected, "late callback", "a");
            session.Poll(null, null, 6000);
            Check(session.StableState == LinkState.Unknown && session.Current.Reason == "enumerating", "late old detector reading cannot enter new session");
            second.Reading.State = LinkState.Connected; session.Poll(null, null, 7000); session.Poll(null, null, 7500);
            Check(session.StableState == LinkState.Connected, "replacement must establish its own state");
            Check(SwitchPlanner.Decide(SwitchMode.Paused, session.StableState, true) == Route.None &&
                SwitchPlanner.Decide(SwitchMode.Monitor, session.StableState, true) == Route.Monitor, "manual and paused modes keep live detection without auto override");
        }
        Check(second.Disposed, "session exit disposes active detector");
        var retry = new FakeDetector { Reading = TriggerReading.Make(LinkState.Connected, "connected", "retry") };
        int attempts = 0;
        using (var session = new TriggerSession(delegate { if (attempts++ == 0) throw new Exception("open failed"); return retry; }))
        {
            try { session.Replace(new TriggerSettings()); } catch { }
            Check(!session.HasDetector && session.StableState == LinkState.Unknown, "factory failure leaves no active detector or stale confirmed state");
            session.Replace(new TriggerSettings()); session.Poll(null, null, 0); session.Poll(null, null, 500);
            Check(session.HasDetector && session.StableState == LinkState.Connected, "failed detector creation can be retried");
            retry.FailPoll = true;
            try { session.Poll(null, null, 600); } catch { }
            Check(session.StableState == LinkState.Unknown, "detector read exception clears pending and confirmed state");
        }
        string container = Guid.NewGuid().ToString(), other = Guid.NewGuid().ToString();
        var usb = new TriggerSettings { Kind = TriggerKind.UsbAudio, DeviceId = "usb:" + container, ContainerId = container };
        var playback = new AudioSnapshot(); var capture = new AudioSnapshot();
        var input = new Endpoint { Id = "input", Name = "same name", ContainerId = container, Transport = "USB", Flow = 1, State = 1 };
        capture.Endpoints.Add(input);
        Check(UsbAudioDetector.Observe(usb, playback, null).State == LinkState.Unknown, "partial USB enumeration cannot invent a disconnect");
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Connected, "input-only USB device can trigger connection");
        input.State = 2;
        playback.Endpoints.Add(new Endpoint { Id = "other", Name = "same name", ContainerId = other, Transport = "USB", State = 1 });
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Disconnected, "disabled selected endpoint cannot use a same-name device");
        playback.Endpoints.Add(new Endpoint { Id = "selected", Name = "same name", ContainerId = container, Transport = "USB", State = 1 });
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Connected, "one available USB direction is sufficient");
        playback.Endpoints.Last().State = 8;
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Disconnected, "all unavailable USB endpoints disconnect");
        playback.Endpoints.Clear(); capture.Endpoints.Clear();
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Disconnected, "removed USB container disconnects");
        usb.ContainerId = "";
        Check(UsbAudioDetector.Observe(usb, playback, capture).State == LinkState.Unknown, "missing USB identity preserves devices");
        usb.ContainerId = container;
        input.State = 1; capture.Endpoints.Add(input);
        Check(TriggerCatalog.Recommend(usb, capture.Endpoints, 1) == input, "unique container association recommends microphone");
        capture.Endpoints.Add(new Endpoint { Id = "ambiguous", ContainerId = container, Flow = 1, State = 8 });
        Check(TriggerCatalog.Recommend(usb, capture.Endpoints, 1) == null, "multiple associated endpoints require explicit choice even if one is inactive");
        Check(TriggerCatalog.Recommend(new TriggerSettings(), capture.Endpoints, 1) == null, "names never establish an association");
        Check(TriggerCatalog.Usb(playback, capture).Length == 1, "USB candidates group playback and capture by container");

        var bt = new TriggerSettings { Kind = TriggerKind.Bluetooth, DeviceId = "bt-a", ProtocolId = BluetoothIdentity.Classic };
        Check(BluetoothIdentity.Observe(bt, "bt-a", bt.ProtocolId, false, true, true, null).State == LinkState.Disconnected, "paired Bluetooth device without connection is disconnected");
        Check(BluetoothIdentity.Observe(bt, "bt-a", bt.ProtocolId, true, true, true, null).State == LinkState.Connected, "Bluetooth actual connection property confirms selected identity");
        Check(BluetoothIdentity.Observe(bt, "bt-b", bt.ProtocolId, true, true, true, null).State == LinkState.Unknown, "another Bluetooth device cannot trigger selected device");
        Check(BluetoothIdentity.Observe(bt, "bt-a", BluetoothIdentity.LowEnergy, true, true, true, null).State == LinkState.Unknown, "companion LE connection cannot substitute for classic audio transport");
        Check(BluetoothIdentity.Observe(bt, "bt-a", bt.ProtocolId, null, false, false, null).State == LinkState.Unknown, "Bluetooth startup enumeration preserves current route");
        Check(BluetoothIdentity.Observe(bt, "bt-a", bt.ProtocolId, null, true, false, null).State == LinkState.Disconnected, "completed Bluetooth enumeration can confirm device absent");
        Check(BluetoothIdentity.Observe(bt, "bt-a", bt.ProtocolId, true, true, true, "watcher failed").State == LinkState.Unknown, "Bluetooth watcher failure never reuses cached connected state");
        Check(!BluetoothIdentity.CanList(BluetoothIdentity.LowEnergy, container, capture.Endpoints), "auxiliary BLE services are excluded from audio candidates");
        input.Transport = "BluetoothLE";
        Check(BluetoothIdentity.CanList(BluetoothIdentity.LowEnergy, container, capture.Endpoints), "LE candidate requires actual LE audio endpoint association");
    }
    static void MigrationTests()
    {
        var old = new Settings { HeadsetEndpointId = "pinned", SwitchMicrophone = false, FallbackMicrophoneId = "old-mic" };
        var playback = new AudioSnapshot(); var capture = new AudioSnapshot();
        Settings upgraded = old.Upgrade(playback, capture);
        Check(old.Trigger == null && upgraded.Trigger.Kind == TriggerKind.Hid && upgraded.Connected.Output.EndpointId == "pinned" &&
            upgraded.Disconnected.Microphone.EndpointId == "old-mic" && !upgraded.SwitchMicrophone, "legacy upgrade preserves pinned routes and disabled microphone flag without mutating source");
        Check(!upgraded.Connected.Output.KeepCurrent && upgraded.Connected.Output.Resolve(playback, 0) == null, "unavailable selected route is distinct from Keep");
        upgraded.Connected.Output = EndpointTarget.Keep();
        Settings restored = Json.Decode<Settings>(Json.Encode(upgraded));
        Check(restored.Connected.Output.KeepCurrent && restored.Trigger.Kind == TriggerKind.Hid, "new route actions and trigger serialize");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AudioFollow-test-" + Guid.NewGuid().ToString("N") + ".json");
        string text = "{\"HeadsetEndpointId\":\"pinned\",\"SwitchMicrophone\":false}";
        try
        {
            System.IO.File.WriteAllText(path, text);
            Settings.PreserveLegacy(path);
            Check(System.IO.File.ReadAllText(path + ".pre-v1.3.bak") == text && System.IO.File.ReadAllText(path) == text, "first upgraded save preserves exact original bytes without changing current file");
            System.IO.File.WriteAllText(path, "{}"); Settings.PreserveLegacy(path);
            Check(System.IO.File.ReadAllText(path + ".pre-v1.3.bak") == text, "later saves never overwrite original migration backup");
        }
        finally { System.IO.File.Delete(path); System.IO.File.Delete(path + ".pre-v1.3.bak"); }
        capture.Endpoints.Add(new Endpoint { Id = "legacy-mic", Name = Settings.HeadsetMicrophoneName, State = 1, Flow = 1 });
        upgraded = new Settings().Upgrade(playback, capture);
        Check(upgraded.Connected.Microphone.EndpointId == "legacy-mic", "legacy unique name resolves once into fixed identity");
        capture.Endpoints.Add(new Endpoint { Id = "new-same-name", Name = Settings.HeadsetMicrophoneName, State = 1, Flow = 1 });
        capture.Endpoints.RemoveAt(0);
        Check(upgraded.ResolveMicrophone(capture, true) == null, "identity change cannot silently substitute same-name device");
    }
    static void IndependentRoutingTests()
    {
        var output = new AudioSnapshot(); var input = new AudioSnapshot();
        output.Defaults = new[] { "old", "old", "old" }; input.Defaults = new[] { "old-input", "old-input", "old-input" };
        output.Endpoints.Add(new Endpoint { Id = "speaker", Flow = 0, State = 1 });
        input.Endpoints.Add(new Endpoint { Id = "mic", Flow = 1, State = 1 });
        var outputTarget = new EndpointTarget { EndpointId = "speaker" };
        var inputTarget = new EndpointTarget { EndpointId = "mic" };
        DirectionResult result = RouteApplier.Apply(outputTarget, output, 0, delegate { throw new Exception("output failed"); }, delegate { return output; });
        DirectionResult microphone = RouteApplier.Apply(inputTarget, input, 1, delegate(string id) { input.Defaults = Enumerable.Repeat(id, 3).ToArray(); }, delegate { return input; });
        Check(result.Error != null && microphone.Changed && microphone.Error == null, "output switch failure does not block microphone");
        input.Defaults = new[] { "old-input", "old-input", "old-input" };
        microphone = RouteApplier.Apply(inputTarget, input, 1, delegate { throw new Exception("input failed"); }, delegate { return input; });
        result = RouteApplier.Apply(outputTarget, output, 0, delegate(string id) { output.Defaults = Enumerable.Repeat(id, 3).ToArray(); }, delegate { return output; });
        Check(result.Changed && microphone.Error != null, "microphone failure does not block output");
        bool called = false;
        result = RouteApplier.Apply(EndpointTarget.Keep(), output, 0, delegate { called = true; }, delegate { return output; });
        Check(!called && result.Error == null && !result.Changed, "Keep explicitly skips setters and has no unavailable error");
        output.Endpoints.Clear();
        result = RouteApplier.Apply(outputTarget, output, 0, delegate { called = true; }, delegate { return output; });
        Check(!called && result.Error != null, "missing target preserves actual defaults and reports error");
        output.Endpoints.Add(new Endpoint { Id = "speaker", Flow = 0, State = 1 });
        output.Defaults = new[] { "old", "old", "old" };
        result = RouteApplier.Apply(outputTarget, output, 0, delegate(string id) { output.Defaults = Enumerable.Repeat(id, 3).ToArray(); }, delegate { return output; });
        Check(result.Changed && result.Error == null, "late arriving target becomes routable without trigger transition");
        output.Defaults = new[] { "speaker", "speaker", "other" };
        result = RouteApplier.Apply(outputTarget, output, 0, delegate { }, delegate { return output; });
        Check(result.Error != null, "readback disagreement is reported per direction");
    }
}
