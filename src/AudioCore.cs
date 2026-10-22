using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace AudioFollow
{
    public sealed class Endpoint
    {
        public string Id, Name, Diagnostic, ContainerId, InstanceId, Transport;
        public int Flow;
        public uint State;
        public bool Active { get { return State == 1; } }
        public override string ToString() { return Name + (Active ? "" : " [不可用]"); }
    }

    public sealed class AudioSnapshot
    {
        public List<Endpoint> Endpoints = new List<Endpoint>();
        public string[] Defaults = new string[3];
        public string NameOf(string id)
        {
            Endpoint endpoint = Endpoints.FirstOrDefault(e => e.Id == id);
            return endpoint == null ? (id ?? "(无默认设备)") : endpoint.Name;
        }
    }

    public sealed class AudioCore : IDisposable
    {
        IMMDeviceEnumerator enumerator;
        Notification notification;
        readonly int flow;

        public AudioCore(Action changed, int flow = 0)
        {
            if (flow != 0 && flow != 1) throw new ArgumentOutOfRangeException("flow");
            this.flow = flow;
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (changed != null)
            {
                notification = new Notification(changed, flow);
                Check(enumerator.RegisterEndpointNotificationCallback(notification));
            }
        }

        public AudioSnapshot Read()
        {
            var result = new AudioSnapshot();
            IMMDeviceCollection devices = null;
            try
            {
                // Exclude phantom devices left by removed drivers; retain disabled/unplugged devices.
                Check(enumerator.EnumAudioEndpoints(flow, 1 | 2 | 8, out devices));
                uint count;
                Check(devices.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IPropertyStore properties = null;
                    try
                    {
                        Check(devices.Item(i, out device));
                        string id;
                        uint state;
                        Check(device.GetId(out id));
                        Check(device.GetState(out state));
                        string name = id, diagnostic = null;
                        try
                        {
                            Check(device.OpenPropertyStore(0, out properties));
                            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                            PropVariant value;
                            Check(properties.GetValue(ref key, out value));
                            try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : id; }
                            finally { PropVariantClear(ref value); }
                        }
                        catch (COMException ex)
                        {
                            // Removed virtual devices can remain in MMDevice but reject property access.
                            diagnostic = "Friendly name unavailable: " + ex.ErrorCode.ToString("X8");
                        }
                        var endpoint = new Endpoint { Id = id, Name = name, State = state, Diagnostic = diagnostic, Flow = flow };
                        if (properties != null)
                        {
                            endpoint.ContainerId = ReadProperty(properties, new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2));
                            endpoint.InstanceId = ReadProperty(properties, new PropertyKey(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256));
                        }
                        DeviceIdentity.Enrich(endpoint);
                        result.Endpoints.Add(endpoint);
                    }
                    finally { Release(properties); Release(device); }
                }
                for (int role = 0; role < 3; role++) result.Defaults[role] = DefaultId(role);
                return result;
            }
            finally { Release(devices); }
        }

        static string ReadProperty(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            if (store.GetValue(ref key, out value) < 0) return null;
            try
            {
                if (value.Type == 31) return Marshal.PtrToStringUni(value.Pointer);
                if (value.Type == 72 && value.Pointer != IntPtr.Zero)
                    return ((Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid))).ToString();
                return null;
            }
            finally { PropVariantClear(ref value); }
        }

        string DefaultId(int role)
        {
            IMMDevice device = null;
            try
            {
                int hr = enumerator.GetDefaultAudioEndpoint(flow, role, out device);
                if (hr == unchecked((int)0x80070490)) return null;
                Check(hr);
                string id;
                Check(device.GetId(out id));
                return id;
            }
            finally { Release(device); }
        }

        public void SetAllRoles(string id)
        {
            AudioSnapshot before = Read();
            if (!before.Endpoints.Any(e => e.Id == id && e.Active))
                throw new InvalidOperationException("Selected audio device is not active.");
            IPolicyConfig policy = null;
            var changed = new List<int>();
            try
            {
                policy = (IPolicyConfig)new PolicyConfigClient();
                for (int role = 0; role < 3; role++)
                {
                    if (before.Defaults[role] == id) continue;
                    Check(policy.SetDefaultEndpoint(id, role));
                    changed.Add(role);
                }
                for (int role = 0; role < 3; role++)
                    if (DefaultId(role) != id) throw new InvalidOperationException("Default audio device verification failed for role " + role);
            }
            catch (Exception original)
            {
                var rollbackErrors = new List<string>();
                foreach (int role in changed)
                {
                    if (before.Defaults[role] == null) continue;
                    try { Check(policy.SetDefaultEndpoint(before.Defaults[role], role)); }
                    catch (Exception ex) { rollbackErrors.Add("role " + role + ": " + ex.Message); }
                }
                throw new InvalidOperationException(original.Message +
                    (rollbackErrors.Count == 0 ? "" : "; rollback: " + string.Join("; ", rollbackErrors)), original);
            }
            finally { Release(policy); }
        }

        public void RestoreDefaults(string[] ids)
        {
            IPolicyConfig policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                for (int role = 0; role < 3; role++)
                    if (!string.IsNullOrEmpty(ids[role])) Check(policy.SetDefaultEndpoint(ids[role], role));
            }
            finally { Release(policy); }
        }

        public void Dispose()
        {
            if (enumerator != null && notification != null)
                enumerator.UnregisterEndpointNotificationCallback(notification);
            notification = null;
            Release(enumerator);
            enumerator = null;
        }

        static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        sealed class Notification : IMMNotificationClient
        {
            readonly Action changed;
            readonly int flow;
            public Notification(Action changed, int flow) { this.changed = changed; this.flow = flow; }
            void Signal() { try { changed(); } catch (ObjectDisposedException) { } }
            public int OnDeviceStateChanged(string id, uint state) { Signal(); return 0; }
            public int OnDeviceAdded(string id) { Signal(); return 0; }
            public int OnDeviceRemoved(string id) { Signal(); return 0; }
            public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == this.flow) Signal(); return 0; }
            public int OnPropertyValueChanged(string id, PropertyKey key) { Signal(); return 0; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid guid, uint id) { FormatId = guid; PropertyId = id; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
