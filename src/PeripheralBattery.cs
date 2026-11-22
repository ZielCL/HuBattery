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
            string name;
            PeripheralKind kind;
            if (mice.TryGetValue(entry.Key, out name))
            {
                kind = PeripheralKind.Mouse;
            }
            else
            {
                bool hasEndpoint = endpoints.TryGetValue(entry.Key, out name);
                if (!hasEndpoint && !entry.Value.Any(node => IsBluetoothAudio(DeviceNodes.InstanceId(node))))
                    continue;

                uint device = entry.Value.FirstOrDefault(node => IsBluetoothDevice(DeviceNodes.InstanceId(node)));
                name = (device == 0 ? null : DeviceNodes.FriendlyName(device)) ?? name ?? "Audio Bluetooth";
                kind = PeripheralKind.Headphones;
            }

            int? percent = entry.Value.Select(DeviceNodes.BatteryFor).FirstOrDefault(value => value.HasValue);
            if (percent.HasValue)
                yield return new BatteryReading(entry.Key.ToString(), name, percent.Value, "Bluetooth", kind);
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
        const string prefix = "BTHENUM\\{0000";
        const string suffix = "-0000-1000-8000-00805F9B34FB}";
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || id.Length < prefix.Length + 4 + suffix.Length)
            return false;
        string service = id.Substring(prefix.Length, 4);
        return string.Compare(id, prefix.Length + 4, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
            (service.Equals("110B", StringComparison.OrdinalIgnoreCase) || service == "1108" ||
             service.Equals("111E", StringComparison.OrdinalIgnoreCase));
    }
}
