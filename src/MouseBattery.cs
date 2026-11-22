using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class MouseReading
{
    internal readonly string Id;
    internal readonly string Name;
    internal readonly int Percent;
    internal readonly string Connection;

    internal MouseReading(string id, string name, int percent, string connection)
    {
        Id = id;
        Name = name;
        Percent = percent;
        Connection = connection;
    }
}

internal static class MouseBattery
{
    internal static MouseReading[] Read()
    {
        string[] paths = DeviceNodes.HidPaths();
        return RazerBattery.Read(paths).Concat(LogitechBattery.Read(paths)).Concat(ReadBluetooth()).Concat(HidBattery.Read(paths))
            .GroupBy(reading => reading.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reading => reading.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(reading => reading.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<MouseReading> ReadBluetooth()
    {
        var mice = new Dictionary<Guid, string>();
        var mouseNodes = new List<Tuple<uint, Guid>>();
        foreach (uint node in DeviceNodes.Mice())
        {
            uint bluetooth = DeviceNodes.Ancestors(node).FirstOrDefault(DeviceNodes.IsBluetooth);
            if (bluetooth == 0)
                continue;

            Guid? container = DeviceNodes.ContainerFor(bluetooth) ?? DeviceNodes.ContainerFor(node);
            if (!container.HasValue)
                continue;

            mice[container.Value] = DeviceNodes.FriendlyName(bluetooth) ?? "Mouse Bluetooth";
            mouseNodes.Add(Tuple.Create(node, container.Value));
        }
        if (mice.Count == 0)
            yield break;

        foreach (uint node in DeviceNodes.BluetoothNodes())
        {
            Guid? id = DeviceNodes.ContainerFor(node);
            if (id.HasValue && mice.ContainsKey(id.Value))
                mouseNodes.Add(Tuple.Create(node, id.Value));
        }
        foreach (var group in mouseNodes.GroupBy(entry => entry.Item2))
        {
            int? percent = group.Select(entry => DeviceNodes.BatteryFor(entry.Item1)).FirstOrDefault(value => value.HasValue);
            if (percent.HasValue)
                yield return new MouseReading(group.Key.ToString(), mice[group.Key], percent.Value, "Bluetooth");
        }
    }
}
