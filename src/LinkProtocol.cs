using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BarracudaSwitch
{
    public enum LinkState { Unknown, Connected, Disconnected, ReceiverMissing }

    public sealed class LinkReading
    {
        public LinkState State;
        public string Evidence;
        public DateTime ObservedUtc;
        public string Raw;
    }

    // Macronix 1532:0552 E3 query and link notifications; protocol reference:
    // leag/razer-barracuda, docs/PROTOCOL.md. No route, pairing or firmware commands.
    public sealed class LinkProtocol
    {
        readonly List<byte> pending = new List<byte>();
        byte sequence;
        bool waiting, acknowledged;
        long queryAt, fragmentAt;
        public Action<LinkReading> OnReading;
        public void Reset() { pending.Clear(); waiting = acknowledged = false; }

        public byte[] NewQuery()
        {
            sequence = (byte)(sequence % 127 + 1);
            waiting = true;
            acknowledged = false;
            queryAt = Stopwatch.GetTimestamp();
            return new byte[] { 1, 0x80, 6, 0x50, 0x41, 0x0e, sequence, 1, 0xe3 };
        }

        public void Feed(byte[] report)
        {
            if (report == null || report.Length < 3 || report[0] != 1 || report[1] != 0x80 ||
                report[2] == 0 || report[2] > report.Length - 3) return;
            long now = Stopwatch.GetTimestamp();
            if (pending.Count > 0 && Seconds(now - fragmentAt) > 2) pending.Clear();
            // A fresh header cannot continue a prior incomplete response.
            if (pending.Count > 0 && report[2] >= 2 && report[3] == 0x50 &&
                (report[2] < 3 || report[4] == 0x49))
                pending.Clear();
            fragmentAt = now;
            for (int i = 3; i < 3 + report[2]; i++) pending.Add(report[i]);
            while (pending.Count >= 10)
            {
                if (pending[0] != 0x50 || pending[1] != 0x49) { pending.Clear(); return; }
                int length = 10 + pending[8] + (pending[9] << 8);
                if (length > 2048) { pending.Clear(); return; }
                if (pending.Count < length) return;
                byte[] frame = pending.GetRange(0, length).ToArray();
                pending.RemoveRange(0, length);
                ProcessFrame(frame, now);
            }
        }

        void ProcessFrame(byte[] p, long now)
        {
            if (waiting && Seconds(now - queryAt) > 2) { waiting = false; acknowledged = false; }
            if (waiting && p.Length >= 13 && p[2] == 1 && p[10] == 0x0e &&
                p[11] == (byte)(sequence | 0x80))
            {
                acknowledged = p[12] == 0;
                return;
            }
            if (waiting && acknowledged && p.Length == 12 && p[2] == 0x0e &&
                p[8] == 2 && p[9] == 0 && p[10] == 0xe3 && p[11] <= 1)
            {
                waiting = false;
                Emit(p[11], "HID E3 query (matched ACK)", p);
            }
            else if (p.Length == 14 && p[2] == 8 && p[8] == 4 && p[9] == 0 &&
                p[10] == 0x20 && p[11] == 2 && p[12] == 1 && p[13] <= 1)
            {
                Emit(p[13], "HID wireless link event", p);
            }
        }

        void Emit(byte value, string evidence, byte[] frame)
        {
            if (OnReading != null) OnReading(new LinkReading {
                State = value == 1 ? LinkState.Connected : LinkState.Disconnected,
                Evidence = evidence, ObservedUtc = DateTime.UtcNow, Raw = BitConverter.ToString(frame)
            });
        }

        static double Seconds(long ticks) { return (double)ticks / Stopwatch.Frequency; }
    }

    public sealed class HeadsetSensor : IDisposable
    {
        HidDevice device;
        readonly LinkProtocol protocol = new LinkProtocol();
        long lastQuery, lastVerified, lastOpen;
        LinkReading current = new LinkReading { State = LinkState.Unknown, Evidence = "Not queried yet" };
        public Action<string> Trace;
        public LinkReading Current { get { return current; } }

        public HeadsetSensor()
        {
            protocol.OnReading = delegate(LinkReading reading) {
                current = reading;
                lastVerified = Stopwatch.GetTimestamp();
            };
        }

        public void Poll(int timeoutMs, bool forceRead = true)
        {
            long now = Stopwatch.GetTimestamp();
            if (device == null)
            {
                if (lastOpen != 0 && Elapsed(now, lastOpen) < 2) return;
                lastOpen = now;
                try
                {
                    List<HidInfo> found = Hid.Enumerate();
                    if (found.Count == 0) { Set(LinkState.ReceiverMissing, "HID enumeration: receiver absent"); return; }
                    if (found.Count != 1) { Set(LinkState.Unknown, "More than one matching Razer receiver"); return; }
                    device = new HidDevice(found[0]);
                    protocol.Reset();
                    lastQuery = 0;
                    Set(LinkState.Unknown, "Receiver opened; waiting for verified link status");
                }
                catch (Exception ex) { Set(LinkState.Unknown, "HID open error: " + ex.Message); return; }
            }
            try
            {
                if (lastQuery == 0 || Elapsed(now, lastQuery) >= (current.State == LinkState.Unknown ? 3 : 15))
                {
                    byte[] query = protocol.NewQuery();
                    if (Trace != null) Trace("TX " + BitConverter.ToString(query));
                    if (!device.Write(query, 500)) throw new TimeoutException("HID status query write timed out.");
                    lastQuery = now;
                }
                // Passive transition reports normally arrive on their own. Reads are interleaved
                // with idle iterations so command/UI work and audio polling remain responsive.
                if (!forceRead) return;
                byte[] data = device.Read(timeoutMs);
                if (data != null)
                {
                    if (Trace != null) Trace("RX " + BitConverter.ToString(data));
                    protocol.Feed(data);
                }
                if (lastVerified != 0 && Elapsed(Stopwatch.GetTimestamp(), lastVerified) > 45)
                    Set(LinkState.Unknown, "No verified link response for 45 seconds; preserving current output");
            }
            catch (Exception ex)
            {
                device.Dispose();
                device = null;
                Set(LinkState.Unknown, "HID read error: " + ex.Message);
                lastOpen = 0;
            }
        }

        public void Reconnect()
        {
            if (device != null) device.Dispose();
            device = null;
            protocol.Reset();
            lastOpen = lastQuery = lastVerified = 0;
            Set(LinkState.Unknown, "Reconnecting after device/power change");
        }

        void Set(LinkState state, string evidence)
        {
            current = new LinkReading { State = state, Evidence = evidence, ObservedUtc = DateTime.UtcNow };
        }

        static double Elapsed(long now, long then) { return (double)(now - then) / Stopwatch.Frequency; }
        public void Dispose() { if (device != null) device.Dispose(); device = null; }
    }
}
