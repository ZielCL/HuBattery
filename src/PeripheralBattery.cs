using System;
using System.Collections.Generic;
using System.Linq;

internal enum PeripheralKind { Mouse, Headphones }

internal sealed class BatteryReading
{
    internal readonly string Id;
    internal readonly string Name;
    internal readonly int Percent;
    internal readonly string Connection;
    internal readonly PeripheralKind Kind;

    internal BatteryReading(string id, string name, int percent, string connection,
        PeripheralKind kind = PeripheralKind.Mouse)
    {
        Id = id;
        Name = name;
        Percent = percent;
        Connection = connection;
        Kind = kind;
    }
}

internal static class PeripheralBattery
{
    internal static BatteryReading[] Read()
    {
        string[] paths = DeviceNodes.HidPaths();
        return RazerBattery.Read(paths).Concat(LogitechBattery.Read(paths)).Concat(VendorMouseBattery.Read(paths))
            .Concat(UsbHeadsetBattery.Read(paths)).Concat(ReadBluetooth()).Concat(HidBattery.Read(paths))
            .GroupBy(reading => reading.Kind + ":" + reading.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reading => reading.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(reading => reading.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<BatteryReading> ReadBluetooth()
    {
        var mice = new Dictionary<Guid, string>();
        var bluetooth = new Dictionary<Guid, List<uint>>();
        foreach (uint node in DeviceNodes.BluetoothNodes())
            AddNode(bluetooth, DeviceNodes.ContainerFor(node), node);

        foreach (uint node in DeviceNodes.Mice())
        {
            uint parent = DeviceNodes.Ancestors(node).FirstOrDefault(DeviceNodes.IsBluetooth);
            if (parent == 0)
                continue;

            Guid? container = DeviceNodes.ContainerFor(parent) ?? DeviceNodes.ContainerFor(node);
            if (!container.HasValue)
                continue;

            mice[container.Value] = DeviceNodes.FriendlyName(parent) ?? "Mouse Bluetooth";
            AddNode(bluetooth, container, node);
        }
        if (bluetooth.Count == 0)
            yield break;

        var endpoints = new Dictionary<Guid, string>();
        foreach (uint node in DeviceNodes.AudioEndpoints())
        {
            Guid? id = DeviceNodes.ContainerFor(node);
            if (id.HasValue && bluetooth.ContainsKey(id.Value))
                endpoints[id.Value] = DeviceNodes.FriendlyName(node);
        }

        foreach (var entry in bluetooth)
        {
            var battery = entry.Value.Select(node => new { Node = node, Level = DeviceNodes.BatteryFor(node) })
                .FirstOrDefault(reading => reading.Level.HasValue);
            if (battery == null)
                continue;

            string name;
            PeripheralKind kind;
            if (mice.TryGetValue(entry.Key, out name))
            {
                kind = PeripheralKind.Mouse;
            }
            else
            {
                bool hasEndpoint = endpoints.TryGetValue(entry.Key, out name);
                if (!hasEndpoint && !entry.Value.Any(node =>
                    IsBluetoothAudio(DeviceNodes.InstanceId(node)) || DeviceNodes.HasAudioCategory(node)))
                    continue;

                uint device = entry.Value.FirstOrDefault(node => IsBluetoothDevice(DeviceNodes.InstanceId(node)));
                name = (device == 0 ? null : DeviceNodes.FriendlyName(device)) ?? name ??
                    DeviceNodes.FriendlyName(battery.Node) ?? "Audio Bluetooth";
                kind = PeripheralKind.Headphones;
            }

            yield return new BatteryReading(entry.Key.ToString(), name, battery.Level.Value, "Bluetooth", kind);
        }
    }

    private static void AddNode(Dictionary<Guid, List<uint>> nodes, Guid? container, uint node)
    {
        if (!container.HasValue)
            return;
        List<uint> group;
        if (!nodes.TryGetValue(container.Value, out group))
            nodes[container.Value] = group = new List<uint>();
        group.Add(node);
    }

    private static bool IsBluetoothDevice(string id)
    {
        return id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBluetoothAudio(string id)
    {
        if (id.StartsWith("BTHHFENUM\\", StringComparison.OrdinalIgnoreCase))
            return true;
        const string classicPrefix = "BTHENUM\\{0000";
        const string lePrefix = "BTHLEDEVICE\\{0000";
        const string suffix = "-0000-1000-8000-00805F9B34FB}";
        bool lowEnergy = id.StartsWith(lePrefix, StringComparison.OrdinalIgnoreCase);
        int start = lowEnergy ? lePrefix.Length : classicPrefix.Length;
        if ((!lowEnergy && !id.StartsWith(classicPrefix, StringComparison.OrdinalIgnoreCase)) ||
            id.Length < start + 4 + suffix.Length ||
            string.Compare(id, start + 4, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;
        string service = id.Substring(start, 4).ToUpperInvariant();
        return lowEnergy ? service == "184E" || service == "1850" :
            service == "110B" || service == "1108" || service == "111E";
    }
}
