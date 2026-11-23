using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class ControllerBattery
{
    internal static IEnumerable<BatteryReading> Read(string[] paths)
    {
        foreach (string path in paths)
        {
            using (SafeFileHandle handle = HidBattery.Open(path))
            {
                var attributes = new Attributes { Size = Marshal.SizeOf(typeof(Attributes)) };
                HidBattery.Caps caps;
                if (handle.IsInvalid || !HidD_GetAttributes(handle, ref attributes) || attributes.Vendor != 0x054C ||
                    !HidBattery.TryGetCaps(handle, out caps) || caps.UsagePage != 1 || caps.Usage != 5 ||
                    (caps.InputLength != 64 && caps.InputLength != 78))
                    continue;

                string name = SonyModel(attributes.Product);
                if (name == null)
                    continue;
                BatteryReading reading = ReadSony(path, name, attributes.Product, caps.InputLength);
                if (reading != null)
                    yield return reading;
            }
        }

        for (uint index = 0; index < 4; index++)
        {
            XInputBattery sample;
            if (XInputGetBatteryInformation(index, 0, out sample) != 0)
                continue;
            string level = XInputLevel(sample.Type, sample.Level);
            if (level != null)
                yield return new BatteryReading("xinput:" + index, "Mando XInput " + (index + 1), level, "XInput");
        }
    }

    private static BatteryReading ReadSony(string path, string name, ushort product, int length)
    {
        using (SafeFileHandle handle = HidBattery.Open(path, 0x80000000, 0x40000000))
        {
            if (handle.IsInvalid)
                return null;
            int? percent = null;
            using (var channel = new HidReportChannel(handle))
            {
                byte[] report = channel.Receive(length, bytes => (percent = DecodeSony(product, bytes)).HasValue, 150);
                if (report == null)
                    return null;
                Guid? container = DeviceNodes.ContainerForHidPath(path);
                return new BatteryReading(container.HasValue ? container.Value.ToString() : path, name, percent.Value,
                    report[0] == 1 ? "USB" : "Bluetooth", PeripheralKind.Controller, true);
            }
        }
    }

    private static string SonyModel(ushort product)
    {
        switch (product)
        {
            case 0x05C4:
            case 0x09CC: return "DualShock 4";
            case 0x0CE6: return "DualSense";
            case 0x0DF2: return "DualSense Edge";
            default: return null;
        }
    }

    internal static string XInputLevel(byte type, byte level)
    {
        if (type != 2 && type != 3)
            return null;
        switch (level)
        {
            case 0: return "Vacía";
            case 1: return "Baja";
            case 2: return "Media";
            case 3: return "Alta";
            default: return null;
        }
    }

    internal static int? DecodeSony(ushort product, byte[] report)
    {
        bool dualSense = product == 0x0CE6 || product == 0x0DF2;
        if (!dualSense && product != 0x05C4 && product != 0x09CC)
            return null;
        bool usb = report.Length == 64 && report[0] == 1;
        if (!usb && (report.Length != 78 || report[0] != (dualSense ? 0x31 : 0x11) || !ValidBluetoothCrc(report)))
            return null;
        int status = report[dualSense ? (usb ? 53 : 54) : (usb ? 30 : 32)];
        int level = status & 0x0F;
        if (dualSense)
        {
            int state = status >> 4;
            if (state == 2)
                return 100;
            if (state > 1 || level > 10)
                return null;
        }
        else
        {
            if (level == 11 && (status & 0x10) != 0)
                return 100;
            if (level > 10)
                return null;
        }
        return Math.Min(level * 10 + 5, 100);
    }

    private static bool ValidBluetoothCrc(byte[] report)
    {
        int end = report.Length - 4;
        uint crc = AddCrcByte(0xFFFFFFFF, 0xA1);
        for (int index = 0; index < end; index++)
            crc = AddCrcByte(crc, report[index]);
        return ~crc == BitConverter.ToUInt32(report, end);
    }

    private static uint AddCrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
        return crc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        internal int Size;
        internal ushort Vendor;
        internal ushort Product;
        internal ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBattery
    {
        internal byte Type;
        internal byte Level;
    }

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetBatteryInformation(uint index, byte deviceType, out XInputBattery sample);
}
