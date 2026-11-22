using System;
using System.Collections.Generic;
using Microsoft.Win32.SafeHandles;

internal static class UsbHeadsetBattery
{
    private const string UsbHidPrefix = @"\\?\HID#";
    private const int IdLength = 17;

    internal enum Protocol { Nova7, Nova7Steps, Nova5, CloudAlpha, CloudAlpha2, CloudIII, VoidElite }

    private sealed class Model
    {
        internal readonly string Name;
        internal readonly Protocol Reader;
        internal readonly ushort Page;
        internal readonly ushort? Usage;
        internal readonly byte[] Request;

        internal Model(string name, Protocol reader, ushort page, ushort? usage, byte[] request)
        {
            Name = name;
            Reader = reader;
            Page = page;
            Usage = usage;
            Request = request;
        }
    }

    private static readonly Dictionary<string, Model> Models = BuildCatalog();

    private static Dictionary<string, Model> BuildCatalog()
    {
        var models = new Dictionary<string, Model>(StringComparer.OrdinalIgnoreCase);
        AddNova(models, "Arctis Nova 7", Protocol.Nova7, 0x22A1);
        AddNova(models, "Arctis Nova 7", Protocol.Nova7Steps, 0x2202);
        AddNova(models, "Arctis Nova 7 Gen 2", Protocol.Nova7, 0x227E);
        AddNova(models, "Arctis Nova 7X", Protocol.Nova7, 0x2258, 0x229E, 0x22AD, 0x22A5);
        AddNova(models, "Arctis Nova 7X", Protocol.Nova7Steps, 0x2206, 0x22A4);
        AddNova(models, "Arctis Nova 7P", Protocol.Nova7, 0x22A7, 0x2298);
        AddNova(models, "Arctis Nova 7P", Protocol.Nova7Steps, 0x220A);
        AddNova(models, "Arctis Nova 5", Protocol.Nova5, 0x2232);
        AddNova(models, "Arctis Nova 5X", Protocol.Nova5, 0x2253);
        Add(models, 0x03F0, new Model("HyperX Cloud Alpha Wireless", Protocol.CloudAlpha, 0xFF43, 0x0202,
            new byte[] { 0x21, 0xBB, 0x0B }), 0x098D);
        Add(models, 0x03F0, new Model("HyperX Cloud Alpha 2", Protocol.CloudAlpha2, 0xFF13, 0xFF00,
            new byte[] { 0x50, 0x02 }), 0x08BE);
        Add(models, 0x03F0, new Model("HyperX Cloud III Wireless", Protocol.CloudIII, 0xFF13, 1,
            new byte[] { 0x66, 0x89 }), 0x05B7, 0x0C9D);
        Add(models, 0x1B1C, new Model("Corsair Void Elite Wireless", Protocol.VoidElite, 0xFFC5, 1,
            new byte[] { 0xC9, 0x64 }), 0x0A55);
        return models;
    }

    private static void AddNova(Dictionary<string, Model> models, string name, Protocol reader, params ushort[] products)
    {
        Add(models, 0x1038, new Model("SteelSeries " + name, reader, 0xFFC0, null,
            new byte[] { 0, 0xB0 }), products);
    }

    private static void Add(Dictionary<string, Model> models, ushort vendor, Model model, params ushort[] products)
    {
        foreach (ushort product in products)
            models.Add("VID_" + vendor.ToString("X4") + "&PID_" + product.ToString("X4"), model);
    }

    internal static IEnumerable<BatteryReading> Read(string[] paths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            int end = UsbHidPrefix.Length + IdLength;
            if (path.Length <= end || !path.StartsWith(UsbHidPrefix, StringComparison.OrdinalIgnoreCase) ||
                (path[end] != '#' && path[end] != '&'))
                continue;

            string usbId = path.Substring(UsbHidPrefix.Length, IdLength);
            Model model;
            if (!Models.TryGetValue(usbId, out model))
                continue;
            Guid? container = DeviceNodes.ContainerForHidPath(path);
            string id = container.HasValue ? container.Value.ToString() : path;
            if (found.Contains(id))
                continue;

            using (SafeFileHandle handle = HidBattery.Open(path, 0xC0000000, 0x40000000))
            {
                HidBattery.Caps caps;
                if (!HidBattery.TryGetCaps(handle, out caps) || caps.UsagePage != model.Page ||
                    (model.Usage.HasValue && caps.Usage != model.Usage.Value) || caps.InputLength < 3)
                    continue;

                int? percent = Query(handle, caps, model);
                if (percent.HasValue && found.Add(id))
                    yield return new BatteryReading(id, model.Name, percent.Value, "Receptor USB", PeripheralKind.Headphones);
            }
        }
    }

    private static int? Query(SafeFileHandle handle, HidBattery.Caps caps, Model model)
    {
        using (var channel = new HidReportChannel(handle))
        {
            bool sent = caps.OutputLength >= model.Request.Length &&
                channel.Send(BuildReport(model.Request, caps.OutputLength));
            if (!sent && model.Reader == Protocol.CloudIII && caps.FeatureLength >= 62)
                sent = HidBattery.SetFeature(handle, BuildReport(model.Request, caps.FeatureLength));
            if (!sent)
                return null;

            int? percent = null;
            channel.Receive(caps.InputLength, reply => (percent = Decode(reply, model.Reader)).HasValue, 400);
            return percent;
        }
    }

    private static byte[] BuildReport(byte[] prefix, int length)
    {
        var report = new byte[length];
        Array.Copy(prefix, report, prefix.Length);
        return report;
    }

    internal static int? Decode(byte[] reply, Protocol protocol)
    {
        switch (protocol)
        {
            case Protocol.CloudAlpha:
                return reply.Length >= 4 && reply[0] == 0x21 && reply[1] == 0xBB &&
                    reply[2] == 0x0B && reply[3] <= 100 ? (int?)reply[3] : null;
            case Protocol.CloudAlpha2:
                return VendorMouseBattery.DecodeHyperX(reply);
            case Protocol.CloudIII:
                return reply.Length >= 5 && reply[0] == 0x66 && (reply[1] == 0x89 || reply[1] == 0x0D) &&
                    (reply[2] != 0 || reply[3] != 0) && reply[4] <= 100 ? (int?)reply[4] : null;
            case Protocol.VoidElite:
                int value = reply.Length >= 5 ? reply[2] & 0x7F : 0;
                return reply.Length >= 5 && reply[0] == 0x64 && reply[3] == 0xB1 &&
                    (reply[4] == 1 || reply[4] == 2 || reply[4] == 4 || reply[4] == 5) && value <= 100
                    ? (int?)value : null;
            default:
                return DecodeNova(reply, protocol);
        }
    }

    private static int? DecodeNova(byte[] reply, Protocol protocol)
    {
        int offset = reply.Length > 0 && reply[0] == 0 ? 1 : 0;
        if (reply.Length < offset + 4 || reply[offset] != 0xB0)
            return null;

        int state = reply[offset + 1];
        if (protocol == Protocol.Nova5)
            return state != 0 && state != 2 && reply[offset + 3] <= 100 ? (int?)reply[offset + 3] : null;

        if (state != 3 || reply[offset + 3] == 0)
            return null;
        int level = reply[offset + 2];
        if (protocol == Protocol.Nova7Steps)
            return level <= 4 ? (int?)(level * 25) : null;
        return level <= 100 ? (int?)level : null;
    }
}
