// HID transport adapted from Barracuda Battery, Copyright (c) 2026 Mehdi Bouchami.
// MIT license; see THIRD-PARTY-NOTICES.txt.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BarracudaSwitch
{
    public sealed class HidInfo
    {
        public string Path;
        public ushort VendorId, ProductId, UsagePage, Usage;
        public int InputLength, OutputLength, FeatureLength;

        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} in={4} out={5} feature={6}",
                VendorId, ProductId, UsagePage, Usage, InputLength, OutputLength, FeatureLength);
        }
    }

    public static class Hid
    {
        public static List<HidInfo> Enumerate()
        {
            var result = new List<HidInfo>();
            Guid guid;
            HidNative.HidD_GetHidGuid(out guid);
            IntPtr set = HidNative.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var data = new HidNative.InterfaceData();
                data.Size = Marshal.SizeOf(data);
                for (uint index = 0; ; index++)
                {
                    if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 259) throw new Win32Exception(error);
                        break;
                    }
                    int size;
                    HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, IntPtr.Zero))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (path.IndexOf("vid_1532&pid_0552", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        using (SafeFileHandle h = HidNative.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                            var attributes = new HidNative.Attributes { Size = Marshal.SizeOf(typeof(HidNative.Attributes)) };
                            if (!HidNative.HidD_GetAttributes(h, ref attributes))
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            IntPtr pre;
                            if (!HidNative.HidD_GetPreparsedData(h, out pre))
                                throw new Win32Exception(Marshal.GetLastWin32Error());
                            try
                            {
                                var caps = new HidNative.Caps();
                                if (HidNative.HidP_GetCaps(pre, ref caps) != 0x00110000)
                                    throw new InvalidOperationException("HID capabilities could not be read.");
                                if (attributes.VendorId == 0x1532 && attributes.ProductId == 0x0552 &&
                                    caps.UsagePage == 0xff00 && caps.InputLength == 64 && caps.OutputLength == 64)
                                    result.Add(new HidInfo {
                                        Path = path, VendorId = attributes.VendorId, ProductId = attributes.ProductId,
                                        UsagePage = caps.UsagePage, Usage = caps.Usage, InputLength = caps.InputLength,
                                        OutputLength = caps.OutputLength, FeatureLength = caps.FeatureLength
                                    });
                            }
                            finally { HidNative.HidD_FreePreparsedData(pre); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { HidNative.SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
    }

    public sealed class HidDevice : IDisposable
    {
        readonly SafeFileHandle handle;
        public readonly HidInfo Info;

        public HidDevice(HidInfo info)
        {
            Info = info;
            handle = HidNative.CreateFile(info.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open Razer HID.");
        }

        public bool Write(byte[] report, int timeoutMs)
        {
            if (report.Length > Info.OutputLength) throw new ArgumentException("HID report too long.");
            var buffer = new byte[Info.OutputLength];
            Array.Copy(report, buffer, report.Length);
            return Io(buffer, true, timeoutMs) == buffer.Length;
        }

        public byte[] Read(int timeoutMs)
        {
            var buffer = new byte[Info.InputLength];
            int count = Io(buffer, false, timeoutMs);
            if (count == 0) return null;
            if (count < buffer.Length) Array.Resize(ref buffer, count);
            return buffer;
        }

        int Io(byte[] data, bool write, int timeoutMs)
        {
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            using (var signal = new ManualResetEvent(false))
            {
                try
                {
                    var state = new NativeOverlapped();
                    state.EventHandle = signal.SafeWaitHandle.DangerousGetHandle();
                    Marshal.StructureToPtr(state, overlapped, false);
                    if (write) Marshal.Copy(data, 0, buffer, data.Length);
                    int transferred;
                    bool complete = write
                        ? HidNative.WriteFile(handle, buffer, data.Length, out transferred, overlapped)
                        : HidNative.ReadFile(handle, buffer, data.Length, out transferred, overlapped);
                    if (!complete)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 997) throw new Win32Exception(error);
                        if (!signal.WaitOne(timeoutMs)) HidNative.CancelIoEx(handle, overlapped);
                    }
                    // A canceled operation must finish before its unmanaged buffers can be freed.
                    if (!HidNative.GetOverlappedResult(handle, overlapped, out transferred, true))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 995) return 0;
                        throw new Win32Exception(error);
                    }
                    if (transferred > data.Length) throw new InvalidOperationException("Invalid HID transfer length.");
                    if (!write) Marshal.Copy(buffer, data, 0, transferred);
                    return transferred;
                }
                finally
                {
                    Marshal.FreeHGlobal(overlapped);
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public void Dispose() { handle.Dispose(); }
    }

    static class HidNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct InterfaceData { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Attributes { public int Size; public ushort VendorId, ProductId, Version; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Caps
        {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues,
                OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
        }
        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes data);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, ref Caps caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data,
            IntPtr detail, int size, out int requiredSize, IntPtr device);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int length, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int length, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int count, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    }
}
