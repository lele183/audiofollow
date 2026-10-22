using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace AudioFollow
{
    public sealed class Settings
    {
        public const string HeadsetName = "扬声器 (7.1 Surround Sound)";
        public const string MonitorName = "XV271U M3 (NVIDIA High Definition Audio)";
        public const string ReferenceName = "扬声器 (Razer Barracuda X 2.4)";
        public const string HeadsetMicrophoneName = "麦克风 (Razer Barracuda X 2.4)";
        public const string FallbackMicrophoneName = "麦克风 (Realtek(R) Audio)";
        public string HeadsetEndpointId = "";
        public string MonitorEndpointId = "";
        public string ReferenceEndpointId = "";
        public bool SwitchMicrophone = true;
        public string HeadsetMicrophoneId = "";
        public string FallbackMicrophoneId = "";
        public int SchemaVersion;
        public TriggerSettings Trigger;
        public RouteTargets Connected;
        public RouteTargets Disconnected;

        public Settings Copy() { return Json.Decode<Settings>(Json.Encode(this)); }

        public Settings Upgrade(AudioSnapshot output, AudioSnapshot input)
        {
            Settings value = Copy();
            if (value.Trigger == null) value.Trigger = new TriggerSettings { Kind = TriggerKind.Hid, DisplayName = "Barracuda X 2022 · 专用无线检测" };
            if (value.Connected == null) value.Connected = new RouteTargets {
                Output = LegacyTarget(output, HeadsetEndpointId, HeadsetName, 0),
                Microphone = LegacyTarget(input, HeadsetMicrophoneId, HeadsetMicrophoneName, 1)
            };
            if (value.Disconnected == null) value.Disconnected = new RouteTargets {
                Output = LegacyTarget(output, MonitorEndpointId, MonitorName, 0),
                Microphone = LegacyTarget(input, FallbackMicrophoneId, FallbackMicrophoneName, 1)
            };
            if (string.IsNullOrEmpty(value.ReferenceEndpointId) && output != null)
            {
                Endpoint reference = ResolveDevice(output, "", ReferenceName, 0);
                if (reference != null) value.ReferenceEndpointId = reference.Id;
            }
            value.SchemaVersion = 3;
            return value;
        }

        static EndpointTarget LegacyTarget(AudioSnapshot snapshot, string id, string name, int flow)
        {
            Endpoint found = snapshot == null ? null : ResolveDevice(snapshot, id, name, flow);
            return new EndpointTarget { EndpointId = found == null ? id : found.Id, DisplayName = found == null ? name : found.Name };
        }

        public static Settings Load()
        {
            string source = File.Exists(AppFiles.SettingsFile) ? AppFiles.SettingsFile : AppFiles.LegacySettingsFile;
            if (!File.Exists(source)) return new Settings();
            try
            {
                Settings value = Json.Decode<Settings>(File.ReadAllText(source, Encoding.UTF8));
                if (value == null) throw new InvalidDataException("Settings must be a JSON object.");
                return value;
            }
            catch (Exception ex)
            {
                if (source == AppFiles.LegacySettingsFile) return new Settings();
                string backup = AppFiles.SettingsFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try
                {
                    File.Move(AppFiles.SettingsFile, backup);
                    AppFiles.Log("SETTINGS invalid; moved to " + backup + ": " + ex.Message);
                }
                catch (Exception back) { AppFiles.Log("SETTINGS invalid and could not be preserved: " + back.Message); }
                return new Settings();
            }
        }

        public void Save()
        {
            Validate(true);
            PreserveLegacy(AppFiles.SettingsFile);
            AppFiles.WriteAtomic(AppFiles.SettingsFile, Json.Encode(this));
        }

        public static void PreserveLegacy(string path)
        {
            if (!File.Exists(path) || File.Exists(path + ".pre-v1.3.bak")) return;
            Settings old = Json.Decode<Settings>(File.ReadAllText(path, Encoding.UTF8));
            if (old != null && old.Trigger == null) File.Copy(path, path + ".pre-v1.3.bak");
        }

        public void Validate(bool requireTargets = false)
        {
            if (Trigger == null || !Enum.IsDefined(typeof(TriggerKind), Trigger.Kind))
                throw new InvalidDataException("请选择连接方式。");
            if (Trigger.Kind != TriggerKind.Hid && string.IsNullOrEmpty(Trigger.DeviceId))
                throw new InvalidDataException("请选择触发设备。");
            if (Trigger.Kind == TriggerKind.UsbAudio && !DeviceIdentity.ValidContainer(Trigger.ContainerId))
                throw new InvalidDataException("设备没有可用的固定容器标识，请重新选择。");
            if (Trigger.Kind == TriggerKind.Bluetooth && !BluetoothIdentity.AudioProtocol(Trigger.ProtocolId))
                throw new InvalidDataException("蓝牙传输标识无效，请重新选择设备。");
            if (Connected == null || Disconnected == null || Connected.Output == null || Connected.Microphone == null ||
                Disconnected.Output == null || Disconnected.Microphone == null)
                throw new InvalidDataException("请为两组声音输出和麦克风选择设备或「保持当前」。");
            if (requireTargets && new[] { Connected.Output, Connected.Microphone, Disconnected.Output, Disconnected.Microphone }
                .Any(t => !t.KeepCurrent && string.IsNullOrEmpty(t.EndpointId)))
                throw new InvalidDataException("有设备尚未选择，请指定设备或明确选择「保持当前」。");
            SchemaVersion = 3;
        }

        public Endpoint Resolve(AudioSnapshot snapshot, string kind)
        {
            if (kind != "reference" && Connected != null && Disconnected != null)
                return (kind == "headset" ? Connected : Disconnected).Output.Resolve(snapshot, 0);
            string id = kind == "headset" ? HeadsetEndpointId : kind == "monitor" ? MonitorEndpointId : ReferenceEndpointId;
            string name = kind == "headset" ? HeadsetName : kind == "monitor" ? MonitorName : ReferenceName;
            return ResolveDevice(snapshot, id, name, 0);
        }

        public Endpoint ResolveMicrophone(AudioSnapshot snapshot, bool headset)
        {
            if (Connected != null && Disconnected != null) return (headset ? Connected : Disconnected).Microphone.Resolve(snapshot, 1);
            return ResolveDevice(snapshot, headset ? HeadsetMicrophoneId : FallbackMicrophoneId,
                headset ? HeadsetMicrophoneName : FallbackMicrophoneName, 1);
        }

        static Endpoint ResolveDevice(AudioSnapshot snapshot, string id, string name, int flow)
        {
            if (!string.IsNullOrEmpty(id)) return snapshot.Endpoints.FirstOrDefault(e => e.Id == id && e.Active && e.Flow == flow);
            Endpoint[] matches = snapshot.Endpoints.Where(e => e.Active && e.Flow == flow && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }

    public sealed class EndpointTarget
    {
        public bool KeepCurrent;
        public string EndpointId = "", DisplayName = "";
        public Endpoint Resolve(AudioSnapshot snapshot, int flow)
        {
            return KeepCurrent || snapshot == null ? null :
                snapshot.Endpoints.FirstOrDefault(e => DeviceIdentity.Same(e.Id, EndpointId) && e.Active && e.Flow == flow);
        }
        public static EndpointTarget Keep() { return new EndpointTarget { KeepCurrent = true, DisplayName = "保持当前" }; }
        public static EndpointTarget For(Endpoint endpoint) { return new EndpointTarget { EndpointId = endpoint.Id, DisplayName = endpoint.Name }; }
    }

    public sealed class RouteTargets
    {
        public EndpointTarget Output = EndpointTarget.Keep();
        public EndpointTarget Microphone = EndpointTarget.Keep();
    }

    public static class Json
    {
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static T Decode<T>(string value) { return new JavaScriptSerializer().Deserialize<T>(value); }
    }

    public static class AppFiles
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFollow");
        public static readonly string LegacySettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarracudaSwitch", "settings.json");
        public static readonly string SettingsFile = Path.Combine(Folder, "settings.json");
        public static readonly string StatusFile = Path.Combine(Folder, "status.json");
        public static readonly string LogFile = Path.Combine(Folder, "switcher.log");
        static readonly object logLock = new object();

        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Folder);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static void Log(string message)
        {
            lock (logLock)
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 512 * 1024)
                {
                    if (File.Exists(LogFile + ".1")) File.Delete(LogFile + ".1");
                    File.Move(LogFile, LogFile + ".1");
                }
                File.AppendAllText(LogFile, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
        }
    }

    public static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Value = "AudioFollow";
        public static string Command { get { return "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioFollow.exe") + "\" --background"; } }
        public static bool Enabled
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                    return key != null && string.Equals(key.GetValue(Value) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue(Value, Command, RegistryValueKind.String);
                else
                {
                    string existing = key.GetValue(Value) as string;
                    if (existing != null && !string.Equals(existing, Command, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("开机启动项指向另一份程序；请从那份程序中关闭。");
                    key.DeleteValue(Value, false);
                }
            }
        }
    }
}
