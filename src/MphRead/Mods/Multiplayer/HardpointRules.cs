using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Multiplayer;

internal static class HardpointRules
{
    internal const int RotationTicks = 60 * 60;

    internal static int Next(Scene scene, int current)
    {
        var ids = new List<int>();
        foreach (var node in scene.GetNodeDefenseEntities()) ids.Add(node.Id);
        var order = CustomRooms.Definitions.FirstOrDefault(map => map.Name == scene.Room?.Meta.Name)?.HardpointOrder;
        return Next(ids, order, current);
    }

    internal static int Next(IEnumerable<int> nodeIds, IList<int>? order, int current)
    {
        var ids = nodeIds.ToList();
        ids.Sort((left, right) =>
        {
            int a = order?.IndexOf(left) ?? -1, b = order?.IndexOf(right) ?? -1;
            int compare = (a < 0 ? int.MaxValue : a).CompareTo(b < 0 ? int.MaxValue : b);
            return compare != 0 ? compare : left.CompareTo(right);
        });
        return ids.Count == 0 ? -1 : ids[(ids.IndexOf(current) + 1) % ids.Count];
    }
}
