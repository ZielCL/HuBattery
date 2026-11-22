using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32.SafeHandles;

internal static class VendorMouseBattery
{
    private enum Protocol { Aerox, Rival, HyperX, Sinowealth, Corsair, Compx }

    private sealed class Model
    {
        internal readonly string Name;
        internal readonly Protocol Reader;
        internal readonly string Connection;

        internal Model(string name, Protocol reader, string connection = "USB 2,4 GHz")
        {
            Name = name;
            Reader = reader;
            Connection = connection;
        }
    }

    private const string UsbHidPrefix = @"\\?\HID#";
    private const int IdLength = 17;
    private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase)
    {
        { "VID_1038&PID_1838", new Model("SteelSeries Aerox 3 Wireless", Protocol.Aerox) },
        { "VID_1038&PID_1878", new Model("SteelSeries Aerox 3 Wireless CS2", Protocol.Aerox) },
        { "VID_1038&PID_1852", new Model("SteelSeries Aerox 5 Wireless", Protocol.Aerox) },
        { "VID_1038&PID_185C", new Model("SteelSeries Aerox 5 Wireless Destiny 2", Protocol.Aerox) },
        { "VID_1038&PID_1860", new Model("SteelSeries Aerox 5 Wireless Diablo IV", Protocol.Aerox) },
        { "VID_1038&PID_1858", new Model("SteelSeries Aerox 9 Wireless", Protocol.Aerox) },
        { "VID_1038&PID_1874", new Model("SteelSeries Aerox 9 Wireless WoW", Protocol.Aerox) },
        { "VID_1038&PID_1830", new Model("SteelSeries Rival 3 Wireless", Protocol.Rival) },
        { "VID_1038&PID_1872", new Model("SteelSeries Rival 3 Wireless Gen 2", Protocol.Rival) },
        { "VID_03F0&PID_0F98", new Model("HyperX Pulsefire Haste 2 Wireless", Protocol.HyperX) },
        { "VID_03F0&PID_0D97", new Model("HyperX Pulsefire Haste 2 Wireless", Protocol.HyperX) },
        { "VID_258A&PID_2022", new Model("Glorious Model O Wireless", Protocol.Sinowealth) },
        { "VID_373E&PID_001E", new Model("LAMZU Maya X 8K", Protocol.Sinowealth) },
        { "VID_1B1C&PID_1B7F", new Model("Corsair Dark Core RGB Pro SE", Protocol.Corsair) },
        { "VID_1B1C&PID_2B2A", new Model("Corsair Sabre V2 Pro Ultralight", Protocol.Compx) },
        { "VID_3770&PID_0200", new Model("Hitscan Hyperlight", Protocol.Compx) },
        { "VID_3554&PID_F5F7", new Model("Scyrox V8", Protocol.Compx) },
        { "VID_3554&PID_F5F4", new Model("Scyrox V8", Protocol.Compx) },
        { "VID_3554&PID_F5F6", new Model("Scyrox V8", Protocol.Compx, "Cable USB") },
        { "VID_3554&PID_F508", new Model("Pulsar X2 V2 Mini", Protocol.Compx) },
        { "VID_3710&PID_5406", new Model("Pulsar X2 CrazyLight Mini", Protocol.Compx) },
        { "VID_3554&PID_F58A", new Model("ATK VXE R1 Pro Max", Protocol.Compx) },
        { "VID_373B&PID_101A", new Model("ATK X1 Pro", Protocol.Compx) },
        { "VID_3554&PID_F505", new Model("VGN Dragonfly 4K", Protocol.Compx) }
    };

    internal static IEnumerable<MouseReading> Read(string[] paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (!path.StartsWith(UsbHidPrefix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= UsbHidPrefix.Length + IdLength)
                continue;

            string usbId = path.Substring(UsbHidPrefix.Length, IdLength);
            char separator = path[UsbHidPrefix.Length + IdLength];
            Model model;
            if ((separator != '#' && separator != '&') || !Models.TryGetValue(usbId, out model))
                continue;

            Guid? container = DeviceNodes.ContainerForHidPath(path);
            string id = container.HasValue ? container.Value.ToString() : usbId;
            if (seen.Contains(id))
                continue;

            HidBattery.Caps caps;
            using (SafeFileHandle handle = HidBattery.Open(path))
            {
                if (!HidBattery.TryGetCaps(handle, out caps) || !IsBatteryCollection(model.Reader, usbId, caps))
                    continue;

                if (model.Reader == Protocol.Sinowealth)
                {
                    int? level = ReadSinowealth(handle);
                    if (level.HasValue && seen.Add(id))
                        yield return new MouseReading(id, model.Name, level.Value, model.Connection);
                    continue;
                }
            }

            using (SafeFileHandle handle = HidBattery.Open(path, 0xC0000000, 0x40000000))
            {
                if (handle.IsInvalid)
                    continue;
                using (var channel = new HidReportChannel(handle))
                {
                    int? level = ReadOutputBattery(channel, caps, model.Reader);
                    if (level.HasValue && seen.Add(id))
                        yield return new MouseReading(id, model.Name, level.Value, model.Connection);
                }
            }
        }
    }

    private static bool IsBatteryCollection(Protocol reader, string usbId, HidBattery.Caps caps)
    {
        switch (reader)
        {
            case Protocol.Sinowealth:
                return caps.FeatureLength == 65 &&
                    (usbId.EndsWith("001E", StringComparison.OrdinalIgnoreCase)
                        ? caps.UsagePage == 0xFFFF && caps.Usage == 0
                        : caps.UsagePage == 0xFFA0 || caps.UsagePage == 0xFFFF);
            case Protocol.HyperX:
                return caps.UsagePage == 0xFF00 && caps.Usage == 1 &&
                    caps.InputLength == 64 && caps.OutputLength == 64;
            case Protocol.Corsair:
                return caps.UsagePage == 0xFF42 && caps.Usage == 1 && Is64ByteChannel(caps);
            case Protocol.Compx:
                return caps.UsagePage == 0xFF02 && caps.Usage == 2 &&
                    caps.InputLength >= 17 && caps.OutputLength >= 17;
            default:
                return caps.UsagePage == 0xFFC0 && Is64ByteChannel(caps);
        }
    }

    private static bool Is64ByteChannel(HidBattery.Caps caps)
    {
        return (caps.InputLength == 64 || caps.InputLength == 65) &&
            (caps.OutputLength == 64 || caps.OutputLength == 65);
    }

    private static int? ReadSinowealth(SafeFileHandle handle)
    {
        var request = new byte[65];
        request[3] = 2;
        request[4] = 2;
        request[6] = 0x83;
        if (!HidBattery.SetFeature(handle, request))
            return null;

        Thread.Sleep(100);
        var response = new byte[request.Length];
        return HidBattery.GetFeature(handle, response) ? DecodeSinowealth(response) : null;
    }

    internal static int? DecodeSinowealth(byte[] response)
    {
        if (response == null)
            return null;
        int offset = PayloadOffset(response);
        return response.Length >= offset + 8 && response[offset] == 0xA1 &&
            response[offset + 5] == 0x83 && response[offset + 7] <= 100
            ? (int?)response[offset + 7] : null;
    }

    private static int? ReadOutputBattery(HidReportChannel channel, HidBattery.Caps caps, Protocol protocol)
    {
        var request = new byte[caps.OutputLength];
        if (protocol == Protocol.HyperX)
        {
            request[0] = 0x52;
            request[1] = 1;
            byte[] link = channel.Exchange(request, caps.InputLength, reply => reply[0] == 0x53 && reply[1] == 1);
            if (link != null && link[2] == 0)
                return null;
            Thread.Sleep(10);
            request[0] = 0x50;
            request[1] = 2;
            return Ask(channel, caps.InputLength, request, DecodeHyperX);
        }

        if (protocol == Protocol.Corsair)
        {
            int start = caps.OutputLength == 65 ? 1 : 0;
            request[start] = 9;
            request[start + 1] = 2;
            request[start + 2] = 0x0F;
            int? first = Ask(channel, caps.InputLength, request, DecodeCorsair, 5500);
            if (!first.HasValue)
                return null;
            int? second = Ask(channel, caps.InputLength, request, DecodeCorsair, 1500);
            return first == second ? first : null;
        }

        if (protocol == Protocol.Compx)
        {
            request[0] = 8;
            request[1] = 4;
            request[16] = CompxChecksum(request);
            return Ask(channel, caps.InputLength, request, DecodeCompx);
        }

        request[1] = protocol == Protocol.Aerox ? (byte)0xD2 : (byte)0xAA;
        if (protocol == Protocol.Rival)
            request[2] = 1;
        return Ask(channel, caps.InputLength, request,
            response => DecodeSteelSeries(response, protocol == Protocol.Aerox));
    }

    private static int? Ask(HidReportChannel channel, int length, byte[] request,
        Func<byte[], int?> decode, int timeoutMs = 300)
    {
        int? level = null;
        channel.Exchange(request, length, reply => (level = decode(reply)).HasValue, timeoutMs);
        return level;
    }

    internal static int? DecodeSteelSeries(byte[] response, bool aerox)
    {
        int offset = PayloadOffset(response);
        if (response.Length < offset + 2 || response[offset] != (aerox ? 0xD2 : 0xAA))
            return null;

        int raw = response[offset + 1];
        if (!aerox)
            return raw <= 100 ? (int?)raw : null;
        int steps = raw & 0x7F;
        if (steps == 0 || steps > 100)
            return null;
        return steps <= 21 ? (steps - 1) * 5 : steps;
    }

    internal static int? DecodeHyperX(byte[] response)
    {
        return response.Length >= 3 && response[0] == 0x51 && response[1] == 2 && response[2] <= 100
            ? (int?)response[2] : null;
    }

    internal static int? DecodeCorsair(byte[] response)
    {
        int offset = PayloadOffset(response);
        if (response.Length < offset + 5 || response[offset] != 1 ||
            response[offset + 1] != 2 || response[offset + 2] != 0)
            return null;
        int tenths = response[offset + 3] | (response[offset + 4] << 8);
        return tenths > 0 && tenths <= 1000 ? (int?)(tenths / 10) : null;
    }

    internal static int? DecodeCompx(byte[] response)
    {
        return response.Length >= 17 && response[0] == 8 && response[1] == 4 && response[2] == 0 &&
            response[6] <= 100 && response[16] == CompxChecksum(response) ? (int?)response[6] : null;
    }

    private static byte CompxChecksum(byte[] bytes)
    {
        int checksum = 0x55;
        for (int i = 0; i < 16; i++)
            checksum -= bytes[i];
        return unchecked((byte)checksum);
    }

    private static int PayloadOffset(byte[] response)
    {
        return response.Length > 0 && response[0] == 0 ? 1 : 0;
    }
}
