using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class HidBattery
{
    internal const int Success = 0x00110000;
    private const int Feature = 2;
    private const ushort DeviceControlsPage = 0x06;
    private const ushort BatteryStrength = 0x20;

    internal static IEnumerable<MouseReading> Read(string[] paths)
    {
        foreach (string path in paths)
        {
            using (SafeFileHandle handle = Open(path))
            {
                IntPtr descriptor;
                if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out descriptor))
                    continue;

                int? percent;
                try { percent = ReadBattery(handle, descriptor); }
                finally { HidD_FreePreparsedData(descriptor); }
                if (!percent.HasValue)
                    continue;

                var product = new StringBuilder(128);
                string name = HidD_GetProductString(handle, product, product.Capacity * 2)
                    ? product.ToString().Trim() : "";
                Guid? container = DeviceNodes.ContainerForHidPath(path);
                string id = container.HasValue ? container.Value.ToString() : path;
                yield return new MouseReading(id, name.Length == 0 ? "Mouse HID" : name, percent.Value, "HID estándar");
            }
        }
    }

    private static int? ReadBattery(SafeFileHandle handle, IntPtr descriptor)
    {
        Caps caps;
        if (HidP_GetCaps(descriptor, out caps) != Success || caps.UsagePage != 1 || caps.Usage != 2 ||
            caps.FeatureLength == 0 || caps.FeatureValues == 0)
            return null;

        ushort count = caps.FeatureValues;
        var values = new ValueCaps[count];
        if (HidP_GetSpecificValueCaps(Feature, DeviceControlsPage, 0, BatteryStrength, values, ref count, descriptor) != Success)
            return null;

        for (int i = 0; i < count; i++)
        {
            ValueCaps value = values[i];
            if (value.LogicalMin < 0 || value.LogicalMax <= value.LogicalMin)
                continue;

            var report = new byte[caps.FeatureLength];
            report[0] = value.ReportId;
            uint raw;
            if (GetFeature(handle, report) && HidP_GetUsageValue(Feature, DeviceControlsPage, value.LinkCollection,
                BatteryStrength, out raw, descriptor, report, (uint)report.Length) == Success &&
                raw >= value.LogicalMin && raw <= value.LogicalMax)
            {
                long range = value.LogicalMax - value.LogicalMin;
                return (int)(((raw - value.LogicalMin) * 100L + range / 2) / range);
            }
        }
        return null;
    }

    internal static SafeFileHandle Open(string path, uint access = 0, uint flags = 0)
    {
        return CreateFileW(path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
    }

    internal static bool TryGetCaps(SafeFileHandle handle, out Caps caps)
    {
        caps = default(Caps);
        IntPtr descriptor;
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out descriptor))
            return false;
        try { return HidP_GetCaps(descriptor, out caps) == Success; }
        finally { HidD_FreePreparsedData(descriptor); }
    }

    internal static bool GetFeature(SafeFileHandle device, byte[] report)
    {
        return HidD_GetFeature(device, report, report.Length);
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct Caps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputLength;
        [FieldOffset(6)] public ushort OutputLength;
        [FieldOffset(8)] public ushort FeatureLength;
        [FieldOffset(60)] public ushort FeatureValues;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct ValueCaps
    {
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle device, [In, Out] byte[] report, int length);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr descriptor);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr descriptor);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(SafeFileHandle device, StringBuilder name, int bytes);
    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr descriptor, out Caps caps);
    [DllImport("hid.dll")]
    private static extern int HidP_GetSpecificValueCaps(int reportType, ushort page, ushort collection,
        ushort usage, [Out] ValueCaps[] values, ref ushort count, IntPtr descriptor);
    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort page, ushort collection,
        ushort usage, out uint value, IntPtr descriptor, byte[] report, uint length);
}
