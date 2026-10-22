using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioFollow
{
    public static class DeviceIdentity
    {
        public static bool Same(string left, string right)
        { return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        public static bool ValidContainer(string value)
        { Guid id; return Guid.TryParse(value, out id) && id != Guid.Empty && id != new Guid("00000000-0000-0000-ffff-ffffffffffff"); }

        public static void Enrich(Endpoint endpoint)
        {
            uint node;
            string instance = endpoint.InstanceId ?? (@"SWD\MMDEVAPI\" + endpoint.Id);
            if (CM_Locate_DevNodeW(out node, instance, 0) != 0 && CM_Locate_DevNodeW(out node, instance, 1) != 0) return;
            // Walk the physical ancestry: MMDevice endpoints themselves are software nodes.
            for (int i = 0; i < 12; i++)
            {
                var text = new StringBuilder(512);
                if (CM_Get_Device_IDW(node, text, text.Capacity, 0) != 0) break;
                string id = text.ToString();
                string upper = id.ToUpperInvariant();
                if (upper.StartsWith("BTHENUM\\") || upper.StartsWith("BTHHFENUM\\"))
                { endpoint.Transport = "Bluetooth"; break; }
                if (upper.StartsWith("BTHLEDEVICE\\") || upper.StartsWith("BTHLE\\"))
                { endpoint.Transport = "BluetoothLE"; break; }
                if (upper.StartsWith("USB\\VID_"))
                {
                    endpoint.Transport = "USB";
                    endpoint.InstanceId = id;
                    // Use the audio function's container; never the host controller's container.
                    if (!ValidContainer(endpoint.ContainerId))
                    {
                        var key = new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
                        uint type, size = 16;
                        byte[] data = new byte[16];
                        if (CM_Get_DevNode_PropertyW(node, ref key, out type, data, ref size, 0) == 0 && type == 13)
                            endpoint.ContainerId = new Guid(data).ToString();
                    }
                    break;
                }
                uint parent;
                if (CM_Get_Parent(out parent, node, 0) != 0) break;
                node = parent;
            }
        }
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_IDW(uint node, StringBuilder buffer, int length, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, byte[] data, ref uint size, uint flags);
    }
}
