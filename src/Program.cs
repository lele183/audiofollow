using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AudioFollow - Voice and playback routing")]
[assembly: System.Reflection.AssemblyDescription("Automatic playback and microphone routing for Windows 11")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.3.0-preview.1")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

namespace AudioFollow
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string command = args.Length == 0 ? "" : args[0].ToLowerInvariant();
            try
            {
                switch (command)
                {
                    case "--help":
                        Console.WriteLine("AudioFollow.exe [--background]\r\nAudioSwitchCtl.exe --status|--headset|--monitor|--toggle|--auto|--pause|--refresh|--show|--exit\r\nAudioSwitchCtl.exe --diagnose|--probe [seconds]|--verify-routing|--install-startup|--uninstall-startup\r\nAudioSwitchCtl.exe --list-triggers Hid|UsbAudio|Bluetooth\r\nAudioSwitchCtl.exe --check-trigger <settings.json> (read-only)");
                        return 0;
                    case "--install-startup": Startup.Set(true); Console.WriteLine(Startup.Command); return 0;
                    case "--uninstall-startup": Startup.Set(false); Console.WriteLine("Startup disabled."); return 0;
                    case "--diagnose": return Diagnose();
                    case "--probe": return Probe(args);
                    case "--verify-routing": return VerifyRouting();
                    case "--list-triggers": return ListTriggers(args);
                    case "--check-trigger": return CheckTrigger(args);
                    case "--status": case "--headset": case "--monitor": case "--toggle":
                    case "--auto": case "--pause": case "--refresh": case "--show": case "--exit":
                        string reply;
                        if (!Ipc.Send(command.Substring(2), out reply)) { Console.Error.WriteLine("Switcher is not running or did not respond. Run AudioFollow.exe first."); return 2; }
                        Console.WriteLine(reply);
                        return reply.StartsWith("ERROR") ? 1 : 0;
                    case "": case "--background": break;
                    default: Console.Error.WriteLine("Unknown argument. Use --help."); return 1;
                }
                bool created;
                using (var mutex = new Mutex(true, Ipc.MutexName, out created))
                {
                    if (!created)
                    {
                        string reply;
                        if (!Ipc.Send("show", out reply)) return 2;
                        return 0;
                    }
                    try
                    {
                        AppFiles.Log("START version=1.3.0-preview.1; process=" + Process.GetCurrentProcess().Id);
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                            try { AppFiles.Log("UI ERROR " + e.Exception); } catch { }
                            MessageBox.Show(e.Exception.Message, "耳机自动切换");
                        };
                        Application.Run(new TrayApp(command == "--background"));
                    }
                    catch (Exception ex)
                    {
                        try { AppFiles.Log("FATAL " + ex); } catch { }
                        throw;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                if (command == "" || command == "--background") MessageBox.Show(ex.Message, "耳机自动切换启动失败");
                return 1;
            }
        }

        static int Diagnose()
        {
            using (var audio = new AudioCore(null))
            using (var microphones = new AudioCore(null, 1))
            {
                AudioSnapshot snapshot = audio.Read();
                AudioSnapshot capture = microphones.Read();
                string running;
                if (Ipc.Send("status", out running))
                {
                    Console.WriteLine(Json.Encode(new {
                        Defaults = snapshot.Defaults, DefaultNames = snapshot.Defaults.Select(snapshot.NameOf).ToArray(),
                        CaptureDefaults = capture.Defaults, CaptureDefaultNames = capture.Defaults.Select(capture.NameOf).ToArray(),
                        CaptureEndpoints = capture.Endpoints,
                        Endpoints = snapshot.Endpoints, Running = Json.Decode<object>(running), Startup = Startup.Enabled,
                        DataFolder = AppFiles.Folder
                    }));
                    return 0;
                }
                Settings selected = Settings.Load().Upgrade(snapshot, capture);
                TriggerReading reading = System.Threading.Tasks.Task.Run(delegate {
                    return TriggerInspection.Check(selected.Trigger, snapshot, capture, CancellationToken.None);
                }).GetAwaiter().GetResult();
                Console.WriteLine(Json.Encode(new {
                    Defaults = snapshot.Defaults, DefaultNames = snapshot.Defaults.Select(snapshot.NameOf).ToArray(),
                    CaptureDefaults = capture.Defaults, CaptureDefaultNames = capture.Defaults.Select(capture.NameOf).ToArray(),
                    CaptureEndpoints = capture.Endpoints,
                    Endpoints = snapshot.Endpoints, Trigger = selected.Trigger, Link = reading, Startup = Startup.Enabled,
                    DataFolder = AppFiles.Folder
                }));
                return reading.State == LinkState.Unknown ? 2 : 0;
            }
        }

        static int ListTriggers(string[] args)
        {
            TriggerKind kind;
            if (args.Length < 2 || !Enum.TryParse<TriggerKind>(args[1], true, out kind))
                throw new ArgumentException("Use --list-triggers Hid|UsbAudio|Bluetooth.");
            using (var audio = new AudioCore(null))
            using (var capture = new AudioCore(null, 1))
            {
                AudioSnapshot output = audio.Read(), input = capture.Read();
                Console.WriteLine(Json.Encode(System.Threading.Tasks.Task.Run(delegate {
                    return TriggerInspection.List(kind, output, input, CancellationToken.None);
                }).GetAwaiter().GetResult()));
            }
            return 0;
        }

        static int CheckTrigger(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Use --check-trigger <settings.json>. This only reads; it never applies settings or routes.");
            Settings selected = Json.Decode<Settings>(File.ReadAllText(args[1]));
            selected.Validate();
            using (var audio = new AudioCore(null))
            using (var capture = new AudioCore(null, 1))
            {
                AudioSnapshot output = audio.Read(), input = capture.Read();
                TriggerReading reading = System.Threading.Tasks.Task.Run(delegate {
                    return TriggerInspection.Check(selected.Trigger, output, input, CancellationToken.None);
                }).GetAwaiter().GetResult();
                Console.WriteLine(Json.Encode(reading));
                return reading.State == LinkState.Unknown ? 2 : 0;
            }
        }

        static int Probe(string[] args)
        {
            string running;
            if (Ipc.Send("status", out running)) { Console.Error.WriteLine("Exit the tray app before using --probe; use --status while it is running."); return 2; }
            double seconds = 15;
            if (args.Length > 1 && (!double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 1 || seconds > 600))
                throw new ArgumentOutOfRangeException("seconds", "Use 1..600 seconds.");
            foreach (HidInfo item in Hid.Enumerate()) Console.WriteLine("INTERFACE " + item);
            using (var sensor = new HeadsetSensor())
            {
                sensor.Trace = Console.WriteLine;
                var clock = Stopwatch.StartNew();
                DateTime previous = DateTime.MinValue;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    sensor.Poll(250);
                    if (sensor.Current.ObservedUtc != previous)
                    {
                        Console.WriteLine(Json.Encode(sensor.Current));
                        previous = sensor.Current.ObservedUtc;
                    }
                    Thread.Sleep(25);
                }
                return sensor.Current.State == LinkState.Unknown ? 2 : 0;
            }
        }

        static int VerifyRouting()
        {
            string running;
            if (Ipc.Send("status", out running)) throw new InvalidOperationException("Exit the tray app before --verify-routing.");
            var report = new List<object>();
            using (var audio = new AudioCore(null))
            using (var microphones = new AudioCore(null, 1))
            {
                AudioSnapshot before = audio.Read();
                AudioSnapshot captureBefore = microphones.Read();
                Settings settings = Settings.Load();
                Endpoint headset = settings.Resolve(before, "headset"), monitor = settings.Resolve(before, "monitor");
                Endpoint headsetMicrophone = settings.ResolveMicrophone(captureBefore, true);
                Endpoint fallbackMicrophone = settings.ResolveMicrophone(captureBefore, false);
                settings = settings.Upgrade(before, captureBefore);
                if ((headset == null && !settings.Connected.Output.KeepCurrent) || (monitor == null && !settings.Disconnected.Output.KeepCurrent))
                    throw new InvalidOperationException("Selected target outputs must be active for the routing test.");
                if (settings.SwitchMicrophone && ((headsetMicrophone == null && !settings.Connected.Microphone.KeepCurrent) ||
                    (fallbackMicrophone == null && !settings.Disconnected.Microphone.KeepCurrent)))
                    throw new InvalidOperationException("Both selected microphones must be active for the routing test.");
                try
                {
                    foreach (Endpoint target in new[] { headset, monitor }.Where(e => e != null))
                    {
                        audio.SetAllRoles(target.Id);
                        AudioSnapshot after = audio.Read();
                        report.Add(new { Target = target.Name, VerifiedAllThreeRoles = after.Defaults.All(id => id == target.Id), Defaults = after.Defaults });
                    }
                    if (settings.SwitchMicrophone)
                        foreach (Endpoint target in new[] { headsetMicrophone, fallbackMicrophone }.Where(e => e != null))
                        {
                            microphones.SetAllRoles(target.Id);
                            AudioSnapshot after = microphones.Read();
                            report.Add(new { Microphone = target.Name, VerifiedAllThreeRoles = after.Defaults.All(id => id == target.Id), Defaults = after.Defaults });
                        }
                }
                finally
                {
                    try { audio.RestoreDefaults(before.Defaults); }
                    finally { microphones.RestoreDefaults(captureBefore.Defaults); }
                    AudioSnapshot restored = audio.Read();
                    if (!before.Defaults.SequenceEqual(restored.Defaults)) throw new InvalidOperationException("Original output restoration did not verify.");
                    if (!captureBefore.Defaults.SequenceEqual(microphones.Read().Defaults)) throw new InvalidOperationException("Original microphone restoration did not verify.");
                }
                Console.WriteLine(Json.Encode(new { Tests = report, OriginalDefaultsRestored = true, OriginalDefaults = before.Defaults }));
                return 0;
            }
        }
    }
}
