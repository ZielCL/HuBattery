using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

internal static class RazerBattery
{
    private const int ReportLength = 91;
    private const byte PowerClass = 0x07;
    private const byte GetBattery = 0x80;
    private static readonly Regex ControlInterface = new Regex(
        @"^\\\\\?\\hid#vid_1532&pid_([0-9a-f]{4})(?:&mi_00(?:&col[0-9a-f]{2})*)?#",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Dictionary<ushort, Model> Models = BuildCatalog();

    internal static IEnumerable<BatteryReading> Read(string[] paths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.OrderBy(path => path.IndexOf("&col", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            Match match = ControlInterface.Match(path);
            Model model;
            if (!match.Success || !Models.TryGetValue(
                ushort.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), out model))
                continue;

            Guid? container = DeviceNodes.ContainerForHidPath(path);
            string key = container.HasValue ? container.Value.ToString() : "Razer:" + match.Groups[1].Value;
            if (found.Contains(key))
                continue;

            int? percent = Query(path, model.Transaction);
            if (percent.HasValue)
            {
                found.Add(key);
                yield return new BatteryReading(key, "Razer " + model.Name, percent.Value, "USB");
            }
        }
    }

    private sealed class Model
    {
        internal readonly string Name;
        internal readonly byte Transaction;
        internal Model(string name, byte transaction)
        {
            Name = name;
            Transaction = transaction;
        }
    }

    private static Dictionary<ushort, Model> BuildCatalog()
    {
        var models = new Dictionary<ushort, Model>();
        Add(models, "Atheris", 0x1F, 0x0062);
        Add(models, "Basilisk Mobile", 0x1F, 0x00D3, 0x00D4);
        Add(models, "Basilisk Ultimate", 0x1F, 0x0086, 0x0088);
        Add(models, "Basilisk V3 Pro", 0x1F, 0x00AA, 0x00AB);
        Add(models, "Basilisk V3 Pro 35K", 0x1F, 0x00CC, 0x00CD, 0x00D6, 0x00D7);
        Add(models, "Basilisk V3 X HyperSpeed", 0x1F, 0x00B9);
        Add(models, "Basilisk X HyperSpeed", 0xFF, 0x0083);
        Add(models, "Cobra HyperSpeed", 0x1F, 0x00DA, 0x00DB);
        Add(models, "Cobra Pro", 0x1F, 0x00AF, 0x00B0);
        Add(models, "DeathAdder V2 Pro", 0x3F, 0x007C, 0x007D);
        Add(models, "DeathAdder V2 X HyperSpeed", 0x1F, 0x009C);
        Add(models, "DeathAdder V3 HyperSpeed", 0x1F, 0x00C4, 0x00C5);
        Add(models, "DeathAdder V3 Pro", 0x1F, 0x00B6, 0x00B7, 0x00C2, 0x00C3);
        Add(models, "DeathAdder V4 Pro", 0x1F, 0x00BE, 0x00BF);
        Add(models, "HyperPolling Wireless Dongle", 0x1F, 0x00B3);
        Add(models, "Lancehead", 0x3F, 0x0059, 0x005A);
        Add(models, "Lancehead Wireless", 0x1F, 0x006F, 0x0070);
        Add(models, "Mamba 2012", 0xFF, 0x0024, 0x0025);
        Add(models, "Mamba Chroma", 0xFF, 0x0044, 0x0045);
        Add(models, "Mamba Wireless", 0x3F, 0x0072, 0x0073);
        Add(models, "Naga Epic", 0xFF, 0x001F);
        Add(models, "Naga Epic Chroma", 0xFF, 0x003E, 0x003F);
        Add(models, "Naga Pro", 0x1F, 0x008F, 0x0090);
        Add(models, "Naga V2 HyperSpeed", 0x1F, 0x00B4);
        Add(models, "Naga V2 Pro", 0x1F, 0x00A7, 0x00A8);
        Add(models, "Orochi V2", 0x1F, 0x0094);
        Add(models, "Ouroboros", 0xFF, 0x0032);
        Add(models, "Pro Click", 0x1F, 0x0077, 0x0080);
        Add(models, "Pro Click Mini", 0x1F, 0x009A);
        Add(models, "Pro Click V2", 0x1F, 0x00D0, 0x00D1);
        Add(models, "Pro Click V2 Vertical Edition", 0x1F, 0x00C7, 0x00C8);
        Add(models, "Viper Mini SE", 0x1F, 0x009E, 0x009F);
        Add(models, "Viper Ultimate", 0xFF, 0x007A, 0x007B);
        Add(models, "Viper V2 Pro", 0x1F, 0x00A5, 0x00A6);
        Add(models, "Viper V3 HyperSpeed", 0x1F, 0x00B8);
        Add(models, "Viper V3 Pro", 0x1F, 0x00C0, 0x00C1);
        Add(models, "Viper V3 Pro SE", 0x1F, 0x00DE, 0x00DF);
        Add(models, "Viper V4 Pro", 0x1F, 0x00E5, 0x00E6);
        return models;
    }

    private static void Add(Dictionary<ushort, Model> models, string name, byte transaction, params ushort[] products)
    {
        var model = new Model(name, transaction);
        foreach (ushort id in products)
            models.Add(id, model);
    }

    private static int? Query(string path, byte transaction)
    {
        using (SafeFileHandle handle = HidBattery.Open(path))
        {
            if (handle.IsInvalid)
                return null;

            byte[] request = new byte[ReportLength];
            request[2] = transaction;
            request[6] = 2;
            request[7] = PowerClass;
            request[8] = GetBattery;
            request[89] = Checksum(request);

            if (!HidBattery.SetFeature(handle, request))
                return null;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Thread.Sleep(25);
                byte[] response = new byte[ReportLength];
                if (!HidBattery.GetFeature(handle, response))
                    return null;

                if (response[1] == 2 && response[2] == transaction &&
                    response[3] == 0 && response[4] == 0 &&
                    response[6] == 2 && response[7] == PowerClass &&
                    response[8] == GetBattery && response[89] == Checksum(response))
                    return (response[10] * 100 + 127) / 255;
            }
            return null;
        }
    }

    private static byte Checksum(byte[] report)
    {
        byte value = 0;
        for (int i = 3; i < 89; i++)
            value ^= report[i];
        return value;
    }

}
