using System;
using System.Collections.Generic;
using System.Linq;
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
    private const byte ShortReportId = 0x10;
    private const int ShortReportLength = 7;

    internal static IEnumerable<BatteryReading> Read(string[] paths)
    {
        foreach (var receiver in paths.Where(path => ReceiverName(path) != null)
            .GroupBy(ReceiverKey, StringComparer.OrdinalIgnoreCase))
        {
            string name = ReceiverName(receiver.First());
            string shortPath = null;
            string longPath = null;
            foreach (string path in receiver)
            {
                using (SafeFileHandle handle = HidBattery.Open(path))
                {
                    HidBattery.Caps caps;
                    if (!HidBattery.TryGetCaps(handle, out caps) || caps.UsagePage != 0xFF00)
                        continue;
                    if (caps.Usage == 1 && caps.InputLength == ShortReportLength && caps.OutputLength == ShortReportLength)
                        shortPath = shortPath ?? path;
                    if (caps.Usage == 2 && caps.InputLength == ReportLength && caps.OutputLength == ReportLength)
                        longPath = longPath ?? path;
                }
            }

            using (SafeFileHandle longHandle = OpenChannel(longPath))
            using (SafeFileHandle shortHandle = OpenChannel(shortPath))
            using (var longChannel = longHandle == null ? null : new HidReportChannel(longHandle))
            using (var shortChannel = shortHandle == null ? null : new HidReportChannel(shortHandle))
            {
                if (longChannel == null)
                    continue;

                var protocol = new Protocol(request => Exchange(longChannel, request));
                bool legacy = shortChannel != null && (name == "Unifying" || name == "Nano");
                int slots = name == "Nano" ? 2 : 6;
                for (byte slot = 1; slot <= slots; slot++)
                {
                    BatteryReading reading = protocol.ReadSlot(slot, receiver.Key, name);
                    if (reading == null && legacy)
                        reading = ReadLegacySlot(slot, receiver.Key, name, request => shortChannel.Send(request)
                            ? longChannel.Receive(ReportLength, reply => ReplyMatches(reply, ReportId, request))
                            : null, request => shortChannel.Exchange(request, ShortReportLength,
                                reply => ReplyMatches(reply, ShortReportId, request)));
                    if (reading != null)
                        yield return reading;
                }
            }
        }
    }

    private static SafeFileHandle OpenChannel(string path)
    {
        if (path == null)
            return null;
        SafeFileHandle handle = HidBattery.Open(path, 0xC0000000, 0x40000000);
        if (!handle.IsInvalid)
            return handle;
        handle.Dispose();
        return null;
    }

    private static string ReceiverKey(string path)
    {
        Guid? container = DeviceNodes.ContainerForHidPath(path);
        if (container.HasValue)
            return container.Value.ToString();

        // Windows uses the final element of the instance segment to distinguish HID collections.
        string[] parts = path.Split('#');
        int interfaceStart = parts[1].IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
        int collection = parts[2].LastIndexOf('&');
        return parts[1].Substring(0, interfaceStart + 6) + "#" +
            (collection < 0 ? parts[2] : parts[2].Substring(0, collection));
    }

    internal static string ReceiverName(string path)
    {
        const string prefix = @"\\?\HID#VID_046D&PID_";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || path.Length < prefix.Length + 11)
            return null;
        string interfaceTag = path.Substring(prefix.Length + 4, 7).ToUpperInvariant();
        if (interfaceTag[6] != '#' && interfaceTag[6] != '&')
            return null;
        string number = interfaceTag.Substring(0, 6);
        switch (path.Substring(prefix.Length, 4).ToUpperInvariant())
        {
            case "C548": return number == "&MI_02" ? "Logi Bolt" : null;
            case "C52B":
            case "C532": return number == "&MI_02" ? "Unifying" : null;
            case "C539": case "C53A": case "C53D": case "C53F":
            case "C541": case "C545": case "C547": case "C54D":
                return number == "&MI_02" ? "LIGHTSPEED" : null;
            case "C518": case "C51A": case "C51B": case "C521": case "C525": case "C526":
            case "C52E": case "C52F": case "C531": case "C534": case "C535": case "C537":
                return number == "&MI_01" ? "Nano" : null;
            default: return null;
        }
    }

    internal static byte[] Exchange(HidReportChannel channel, byte[] request)
    {
        byte[] reply = channel.Exchange(request, ReportLength, received => ReplyMatches(received, ReportId, request) ||
            (received[0] == ReportId && received[1] == request[1] && received[2] == 0xFF &&
             received[3] == request[2] && received[4] == request[3]));
        return reply != null && ReplyMatches(reply, ReportId, request) ? reply : null;
    }

    private static bool ReplyMatches(byte[] reply, byte reportId, byte[] request)
    {
        return reply[0] == reportId && reply[1] == request[1] && reply[2] == request[2] && reply[3] == request[3];
    }

    internal static BatteryReading ReadLegacySlot(byte slot, string id, string connection,
        Func<byte[], byte[]> readPairing, Func<byte[], byte[]> readRegister)
    {
        byte selector = (byte)(0x20 + slot - 1);
        byte[] paired = readPairing(new byte[] { ShortReportId, 0xFF, 0x83, 0xB5, selector, 0, 0 });
        if (paired == null || paired[4] != selector)
            return null;
        int kind = paired[11] & 15;
        if (kind != 2 && kind != 8)
            return null;

        byte[] charge = readRegister(new byte[] { ShortReportId, slot, 0x81, 0x0D, 0, 0, 0 });
        if (charge == null || charge[4] > 100)
            return null;
        string name = kind == 8 ? "Trackball Logitech" : "Mouse Logitech";
        return new BatteryReading(id + ":" + slot, name + " " + slot, charge[4], connection);
    }

    internal sealed class Protocol
    {
        private readonly Func<byte[], byte[]> exchange;
        private int softwareId = 1;

        internal Protocol(Func<byte[], byte[]> exchange)
        {
            this.exchange = exchange;
        }

        internal BatteryReading ReadSlot(byte slot, string receiverId, string connection)
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
            return new BatteryReading(receiverId + ":" + slot, name, percent.Value, connection);
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
