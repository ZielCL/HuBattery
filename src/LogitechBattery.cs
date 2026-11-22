using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class LogitechBattery
{
    private const ushort DeviceNameAndType = 0x0005;
    private const ushort UnifiedBattery = 0x1004;
    private const ushort BatteryStatus = 0x1000;
    private const byte Mouse = 0x03;
    private const byte Trackball = 0x05;
    private const byte ChargePercentageSupported = 0x02;
    internal const int ReportLength = 20;
    internal const byte ReportId = 0x11;

    internal static IEnumerable<MouseReading> Read(string[] paths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            string receiver = ReceiverName(path);
            if (receiver == null)
                continue;

            using (SafeFileHandle handle = HidBattery.Open(path, 0xC0000000, 0x40000000))
            {
                HidBattery.Caps caps;
                if (!HidBattery.TryGetCaps(handle, out caps) || caps.UsagePage != 0xFF00 || caps.Usage != 2 ||
                    caps.InputLength != ReportLength || caps.OutputLength != ReportLength)
                    continue;

                Guid? container = DeviceNodes.ContainerForHidPath(path);
                string key = container.HasValue ? container.Value.ToString() : path;
                if (!found.Add(key))
                    continue;

                using (var channel = new LogitechChannel(handle))
                {
                    var protocol = new Protocol(channel.Exchange);
                    for (byte slot = 1; slot <= 6; slot++)
                    {
                        MouseReading reading = protocol.ReadSlot(slot, key, receiver);
                        if (reading != null)
                            yield return reading;
                    }
                }
            }
        }
    }

    internal static string ReceiverName(string path)
    {
        const string prefix = @"\\?\HID#VID_046D&PID_";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (path.IndexOf("&MI_02&", StringComparison.OrdinalIgnoreCase) != prefix.Length + 4 &&
             path.IndexOf("&MI_02#", StringComparison.OrdinalIgnoreCase) != prefix.Length + 4))
            return null;
        switch (path.Substring(prefix.Length, 4).ToUpperInvariant())
        {
            case "C548": return "Logi Bolt";
            case "C52B":
            case "C532": return "Unifying";
            default: return null;
        }
    }

    internal sealed class Protocol
    {
        private readonly Func<byte[], byte[]> exchange;
        private int softwareId = 1;

        internal Protocol(Func<byte[], byte[]> exchange)
        {
            this.exchange = exchange;
        }

        internal MouseReading ReadSlot(byte slot, string receiverId, string connection)
        {
            byte device = FindFeature(slot, DeviceNameAndType);
            if (device == 0)
                return null;

            byte[] kind = Request(slot, device, 2);
            if (kind == null || (kind[4] != Mouse && kind[4] != Trackball))
                return null;

            int? percent = ReadBattery(slot);
            if (!percent.HasValue)
                return null;

            string name = ReadName(slot, device) ?? (kind[4] == Trackball ? "Trackball Logitech" : "Mouse Logitech");
            return new MouseReading(receiverId + ":" + slot, name, percent.Value, connection);
        }

        private int? ReadBattery(byte slot)
        {
            byte feature = FindFeature(slot, UnifiedBattery);
            if (feature != 0)
            {
                byte[] capabilities = Request(slot, feature, 0);
                if (capabilities != null && (capabilities[5] & ChargePercentageSupported) != 0)
                {
                    byte[] status = Request(slot, feature, 1);
                    if (status != null && status[4] <= 100)
                        return status[4];
                }
            }

            feature = FindFeature(slot, BatteryStatus);
            if (feature == 0)
                return null;
            byte[] legacy = Request(slot, feature, 0);
            return legacy != null && legacy[4] > 0 && legacy[4] <= 100 ? (int?)legacy[4] : null;
        }

        private string ReadName(byte slot, byte feature)
        {
            byte[] response = Request(slot, feature, 0);
            if (response == null || response[4] == 0 || response[4] > 96)
                return null;

            var name = new byte[response[4]];
            for (int offset = 0; offset < name.Length; offset += ReportLength - 4)
            {
                byte[] fragment = Request(slot, feature, 1, (byte)offset);
                if (fragment == null)
                    return null;
                int length = Math.Min(ReportLength - 4, name.Length - offset);
                Array.Copy(fragment, 4, name, offset, length);
            }
            string value = Encoding.UTF8.GetString(name).Trim('\0', ' ');
            return value.Length == 0 ? null : value;
        }

        private byte FindFeature(byte slot, ushort id)
        {
            byte[] response = Request(slot, 0, 0, (byte)(id >> 8), (byte)id);
            return response == null || response[4] == 0xFF ? (byte)0 : response[4];
        }

        private byte[] Request(byte slot, byte feature, int function, params byte[] parameters)
        {
            var report = new byte[ReportLength];
            report[0] = ReportId;
            report[1] = slot;
            report[2] = feature;
            report[3] = (byte)((function << 4) | softwareId);
            softwareId = softwareId % 7 + 1;
            Array.Copy(parameters, 0, report, 4, parameters.Length);
            return exchange(report);
        }
    }
}
