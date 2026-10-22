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
        try { ProtocolTests(); RoutingTests(); MicrophoneTests(); Console.WriteLine("All " + passed + " checks passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
