using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BarracudaSwitch
{
    public sealed class Settings
    {
        public const string HeadsetName = "扬声器 (7.1 Surround Sound)";
        public const string MonitorName = "XV271U M3 (NVIDIA High Definition Audio)";
        public const string ReferenceName = "扬声器 (Razer Barracuda X 2.4)";
        public string HeadsetEndpointId = "";
        public string MonitorEndpointId = "";
        public string ReferenceEndpointId = "";

        public static Settings Load()
        {
            if (!File.Exists(AppFiles.SettingsFile)) return new Settings();
            try
            {
                Settings value = Json.Decode<Settings>(File.ReadAllText(AppFiles.SettingsFile, Encoding.UTF8));
                if (value == null) throw new InvalidDataException("Settings must be a JSON object.");
                return value;
            }
            catch (Exception ex)
            {
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

        public void Save() { AppFiles.WriteAtomic(AppFiles.SettingsFile, Json.Encode(this)); }

        public Endpoint Resolve(AudioSnapshot snapshot, string kind)
        {
            string id = kind == "headset" ? HeadsetEndpointId : kind == "monitor" ? MonitorEndpointId : ReferenceEndpointId;
            string name = kind == "headset" ? HeadsetName : kind == "monitor" ? MonitorName : ReferenceName;
            if (!string.IsNullOrEmpty(id)) return snapshot.Endpoints.FirstOrDefault(e => e.Id == id && e.Active);
            Endpoint[] matches = snapshot.Endpoints.Where(e => e.Active && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }

    public static class Json
    {
        public static string Encode(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static T Decode<T>(string value) { return new JavaScriptSerializer().Deserialize<T>(value); }
    }

    public static class AppFiles
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarracudaSwitch");
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
        const string Value = "BarracudaSwitch";
        public static string Command { get { return "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BarracudaSwitch.exe") + "\" --background"; } }
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
