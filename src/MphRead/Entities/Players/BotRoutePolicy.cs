using System.Collections.Generic;
using MphRead.Formats;

namespace MphRead.Entities;

/// <summary>
/// Bounds-checked, allocation-free lookup over the native directed routing
/// table. No edges, teleport moves or physics shortcuts are invented.
/// </summary>
internal static class BotRoutePolicy
{
    internal static int NextHopIndex(IReadOnlyList<ushort> values,
        int offset, int destinationId, int nodeCount)
    {
        if (values == null || nodeCount <= 0 || offset < 0
            || destinationId < 0 || destinationId >= nodeCount)
            return -1;
        int covered = 0;
        for (int i = 0; i < nodeCount && offset + 1 < values.Count; i++, offset += 2)
        {
            int count = values[offset];
            int hop = values[offset + 1];
            if (count <= 0 || count > nodeCount - covered || hop >= nodeCount)
                return -1;
            if (destinationId < covered + count)
                return hop;
            covered += count;
        }
        return -1;
    }

    internal static bool TryNextHop(IReadOnlyList<NodeData3> nodes,
        NodeData3 source, NodeData3 destination, out NodeData3? next)
    {
        next = null;
        if (nodes == null || source == destination || source.Values.Count == 0)
            return false;
        int index = NextHopIndex(source.Values, source.Index1, destination.Id, nodes.Count);
        if (index < 0 || index >= nodes.Count || nodes[index] == source)
            return false;
        next = nodes[index];
        return true;
    }
}
