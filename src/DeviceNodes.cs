using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static class DeviceNodes
{
    private const uint Present = 0x100;
    private const uint ByEnumerator = 1;
    private const uint ByClass = 0x200;
    private const int BufferSmall = 0x1A;
    private const string MouseClass = "{4D36E96F-E325-11CE-BFC1-08002BE10318}";
    private const string MediaClass = "{4D36E96C-E325-11CE-BFC1-08002BE10318}";
    private const string AudioEndpointClass = "{C166523C-FE0C-4A94-A586-F1A80CFBBF3E}";
    private static readonly Guid HidClass = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly Guid SystemContainer = new Guid("00000000-0000-0000-ffff-ffffffffffff");
    private static readonly Guid DeviceProperties = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly PropertyKey Container = new PropertyKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
    private static readonly PropertyKey BluetoothBattery = new PropertyKey("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);
    private static readonly PropertyKey DeviceCategory = new PropertyKey("78c34fc8-104a-4aca-9ea4-524d52996e57", 90);

    internal static string[] HidPaths()
    {
        Guid guid = HidClass;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size;
            if (CM_Get_Device_Interface_List_SizeW(out size, ref guid, IntPtr.Zero, 0) != 0)
                break;
            var paths = new char[size];
            int code = CM_Get_Device_Interface_ListW(ref guid, IntPtr.Zero, paths, size, 0);
            if (code == 0)
                return Split(paths);
            if (code != BufferSmall)
                break;
        }
        return new string[0];
    }

    internal static IEnumerable<uint> Mice()
    {
        return Nodes(MouseClass, Present | ByClass);
    }

    internal static IEnumerable<uint> BluetoothNodes()
    {
        foreach (string name in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM" })
            foreach (uint node in Nodes(name, Present | ByEnumerator))
                yield return node;
    }

    internal static IEnumerable<uint> AudioEndpoints()
    {
        return Nodes(AudioEndpointClass, Present | ByClass);
    }

    internal static Dictionary<Guid, string> UsbAudioDevices()
    {
        var audio = new Dictionary<Guid, string>();
        foreach (uint node in Nodes(MediaClass, Present | ByClass))
        {
            if (!InstanceId(node).StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase))
                continue;
            Guid? container = ContainerFor(node);
            if (container.HasValue)
                audio[container.Value] = FriendlyName(node);
        }
        return audio;
    }

    private static IEnumerable<uint> Nodes(string filter, uint flags)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint size;
            if (CM_Get_Device_ID_List_SizeW(out size, filter, flags) != 0)
                yield break;
            var identifiers = new char[size];
            int code = CM_Get_Device_ID_ListW(filter, identifiers, size, flags);
            if (code == BufferSmall)
                continue;
            if (code != 0)
                yield break;

            foreach (string id in Split(identifiers))
            {
                uint node;
                if (CM_Locate_DevNodeW(out node, id, 0) == 0)
                    yield return node;
            }
            yield break;
        }
    }

    internal static IEnumerable<uint> Ancestors(uint node)
    {
        uint parent;
        while (CM_Get_Parent(out parent, node, 0) == 0)
        {
            yield return parent;
            node = parent;
        }
    }

    internal static Guid? ContainerFor(uint node)
    {
        byte[] value = Read(node, Container, 0x0D);
        if (value == null || value.Length != 16)
            return null;
        Guid id = new Guid(value);
        return id == Guid.Empty || id == SystemContainer ? (Guid?)null : id;
    }

    internal static Guid? ContainerForHidPath(string path)
    {
        string[] parts = path.Split('#');
        if (parts.Length < 4)
            return null;
        uint node;
        string id = "HID\\" + parts[1] + "\\" + parts[2];
        return CM_Locate_DevNodeW(out node, id, 0) == 0 ? ContainerFor(node) : null;
    }

    internal static bool IsBluetooth(uint node)
    {
        string id = InstanceId(node);
        return id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("BTHHFENUM\\", StringComparison.OrdinalIgnoreCase);
    }

    internal static string InstanceId(uint node)
    {
        var name = new StringBuilder(512);
        return CM_Get_Device_IDW(node, name, (uint)name.Capacity, 0) == 0 ? name.ToString() : "";
    }

    internal static int? BatteryFor(uint node)
    {
        byte[] value = Read(node, BluetoothBattery, 3);
        return value != null && value.Length == 1 && value[0] <= 100 ? (int?)value[0] : null;
    }

    internal static bool HasAudioCategory(uint node)
    {
        return HasCategory(node, "Audio.", true);
    }

    internal static bool HasCategory(uint node, string expected, bool prefix)
    {
        const uint StringList = 0x2012;
        byte[] value = Read(node, DeviceCategory, StringList);
        if (value == null)
            return false;
        foreach (string category in Encoding.Unicode.GetString(value).Split('\0'))
            if (prefix ? category.StartsWith(expected, StringComparison.OrdinalIgnoreCase) :
                category.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    internal static string FriendlyName(uint node)
    {
        string name = ReadString(node, new PropertyKey(DeviceProperties, 14));
        return name ?? ReadString(node, new PropertyKey(DeviceProperties, 2));
    }

    private static string ReadString(uint node, PropertyKey key)
    {
        byte[] bytes = Read(node, key, 0x12);
        return bytes == null ? null : Encoding.Unicode.GetString(bytes).TrimEnd('\0').Trim();
    }

    private static byte[] Read(uint node, PropertyKey key, uint type)
    {
        uint size = 0;
        uint actual;
        if (CM_Get_DevNode_PropertyW(node, ref key, out actual, null, ref size, 0) != BufferSmall || size > 2048)
            return null;
        var value = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out actual, value, ref size, 0) == 0 && actual == type
            ? value : null;
    }

    private static string[] Split(char[] values)
    {
        return new string(values).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
        public PropertyKey(string format, uint id) : this(new Guid(format), id) { }
        public PropertyKey(Guid format, uint id) { Format = format; Id = id; }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint size, ref Guid guid, IntPtr deviceId, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid guid, IntPtr deviceId, [Out] char[] buffer, uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint size, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(uint node, StringBuilder id, uint size, uint flags);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type,
        [Out] byte[] buffer, ref uint size, uint flags);
}
