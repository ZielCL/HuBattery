using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class HidBattery
{
    internal const int Success = 0x00110000;
    private const int Input = 0;
    private const int Feature = 2;
    private const ushort DeviceControlsPage = 0x06;
    private const ushort BatteryStrength = 0x20;
    private const ushort BatterySystemPage = 0x85;
    private const ushort RelativeCharge = 0x64;

    internal static IEnumerable<BatteryReading> Read(string[] paths)
    {
        Dictionary<Guid, string> audio = null;
        foreach (string path in paths)
        {
            using (SafeFileHandle handle = Open(path))
            {
                IntPtr descriptor;
                if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out descriptor))
                    continue;

                int? percent;
                Guid? container = null;
                PeripheralKind kind = PeripheralKind.Mouse;
                string audioName = null;
                try
                {
                    Caps caps;
                    if (HidP_GetCaps(descriptor, out caps) != Success)
                        continue;
                    if (caps.UsagePage != 1 || caps.Usage != 2)
                    {
                        if (!path.StartsWith(@"\\?\HID#VID_", StringComparison.OrdinalIgnoreCase))
                            continue;
                        container = DeviceNodes.ContainerForHidPath(path);
                        if (!container.HasValue)
                            continue;
                        if (audio == null)
                            audio = DeviceNodes.UsbAudioDevices();
                        if (!audio.TryGetValue(container.Value, out audioName))
                            continue;
                        kind = PeripheralKind.Headphones;
                    }
                    bool isUsbAudio = kind == PeripheralKind.Headphones;
                    percent = ReadFeatureBattery(handle, descriptor, caps, isUsbAudio);
                    if (!percent.HasValue && isUsbAudio)
                        percent = ReadInputBattery(path, descriptor, caps);
                }
                finally { HidD_FreePreparsedData(descriptor); }
                if (!percent.HasValue)
                    continue;

                var product = new StringBuilder(128);
                string name = HidD_GetProductString(handle, product, product.Capacity * 2)
                    ? product.ToString().Trim() : "";
                container = container ?? DeviceNodes.ContainerForHidPath(path);
                string id = container.HasValue ? container.Value.ToString() : path;
                bool isAudio = kind == PeripheralKind.Headphones;
                string fallback = isAudio ? audioName ?? "Audio USB" : "Mouse HID";
                yield return new BatteryReading(id, name.Length == 0 ? fallback : name, percent.Value,
                    isAudio ? "USB HID estándar" : "HID estándar", kind);
            }
        }
    }

    private static int? ReadFeatureBattery(SafeFileHandle handle, IntPtr descriptor, Caps caps, bool isUsbAudio)
    {
        if (caps.FeatureLength == 0 || caps.FeatureValues == 0)
            return null;

        foreach (BatteryField field in BatteryFields(descriptor, Feature, caps.FeatureValues, isUsbAudio))
        {
            var report = new byte[caps.FeatureLength];
            report[0] = field.Value.ReportId;
            if (!GetFeature(handle, report))
                continue;
            int? percent = DecodeBattery(descriptor, Feature, field, report);
            if (percent.HasValue)
                return percent;
        }
        return null;
    }

    private static int? ReadInputBattery(string path, IntPtr descriptor, Caps caps)
    {
        if (caps.InputLength == 0 || caps.InputValues == 0)
            return null;
        var fields = new List<BatteryField>(BatteryFields(descriptor, Input, caps.InputValues, true));
        if (fields.Count == 0)
            return null;

        // Read only reports the device already sends. Some HID devices do not support GET_INPUT_REPORT.
        using (SafeFileHandle handle = Open(path, 0x80000000, 0x40000000))
        {
            if (handle.IsInvalid)
                return null;
            using (var channel = new HidReportChannel(handle))
            {
                int? percent = null;
                channel.Receive(caps.InputLength, report =>
                {
                    foreach (BatteryField field in fields)
                    {
                        percent = DecodeBattery(descriptor, Input, field, report);
                        if (percent.HasValue)
                            return true;
                    }
                    return false;
                }, 150);
                return percent;
            }
        }
    }

    private static IEnumerable<BatteryField> BatteryFields(IntPtr descriptor, int type, ushort count, bool isUsbAudio)
    {
        if (isUsbAudio)
            foreach (BatteryField field in FindFields(descriptor, type, count, BatterySystemPage, RelativeCharge))
                yield return field;
        foreach (BatteryField field in FindFields(descriptor, type, count, DeviceControlsPage, BatteryStrength))
            yield return field;
    }

    private static IEnumerable<BatteryField> FindFields(IntPtr descriptor, int type, ushort count, ushort page, ushort usage)
    {
        var values = new ValueCaps[count];
        if (HidP_GetSpecificValueCaps(type, page, 0, usage, values, ref count, descriptor) != Success)
            yield break;
        for (int i = 0; i < count; i++)
        {
            ValueCaps value = values[i];
            if (value.LogicalMin >= 0 && value.LogicalMax > value.LogicalMin)
                yield return new BatteryField { Page = page, Usage = usage, Value = value };
        }
    }

    private static int? DecodeBattery(IntPtr descriptor, int type, BatteryField field, byte[] report)
    {
        uint raw;
        ValueCaps value = field.Value;
        if (report[0] != value.ReportId || HidP_GetUsageValue(type, field.Page, value.LinkCollection,
            field.Usage, out raw, descriptor, report, (uint)report.Length) != Success)
            return null;
        return BatteryPercent(raw, value.LogicalMin, value.LogicalMax, field.Page == BatterySystemPage);
    }

    internal static int? BatteryPercent(uint raw, int min, int max, bool isPercent)
    {
        if (min < 0 || max <= min || raw < min || raw > max)
            return null;
        if (isPercent)
            return raw <= 100 ? (int?)raw : null;
        long range = max - min;
        return (int)(((raw - min) * 100L + range / 2) / range);
    }

    private struct BatteryField
    {
        internal ushort Page;
        internal ushort Usage;
        internal ValueCaps Value;
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

    internal static bool SetFeature(SafeFileHandle device, byte[] report)
    {
        return HidD_SetFeature(device, report, report.Length);
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct Caps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputLength;
        [FieldOffset(6)] public ushort OutputLength;
        [FieldOffset(8)] public ushort FeatureLength;
        [FieldOffset(48)] public ushort InputValues;
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
    private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] report, int length);
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
