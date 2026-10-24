using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Oddball;

/// <summary>Uses the engine's native next-hop tables; no generated geometry or new gameplay navigation.</summary>
internal sealed class OddballMapNavigation
{
    private readonly Scene _scene;
    private readonly IReadOnlyList<NodeData3> _nodes;
    private readonly float[] _red;
    private readonly float[] _blue;
    private readonly List<int>[] _edges;
    private static bool Biped(NodeData3 n) => n.NodeType is not (NodeType.AltForm or NodeType.Hazard);
    internal OddballMapNavigation(Scene scene, IReadOnlyList<NodeData3> nodes, Vector3[] red, Vector3[] blue)
    {
        _scene = scene; _nodes = nodes; _edges = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            _edges[i] = new(); var n = nodes[i]; int consumed = 0, offset = n.Index1;
            // Same run-length pairs used by PlayerAi.Func213A0A4. Index2 is
            // a visibility/adjacency list, not the complete routing table.
            while (consumed < nodes.Count && offset >= 0 && offset + 1 < n.Values.Count)
            {
                int run = n.Values[offset], hop = n.Values[offset + 1];
                if (run <= 0) break;
                if (hop < nodes.Count && hop != i && Biped(nodes[hop]) && !_edges[i].Contains(hop)) _edges[i].Add(hop);
                consumed += run; offset += 2;
            }
        }
        _red = Distances(red); _blue = Distances(blue);
    }
    private float[] Distances(Vector3[] starts)
    {
        float[] distance = Enumerable.Repeat(float.PositiveInfinity, _nodes.Count).ToArray();
        var open = new PriorityQueue<int, float>();
        foreach (var start in starts)
        {
            int root = Enumerable.Range(0, _nodes.Count).Where(i => Biped(_nodes[i]))
                .OrderBy(i => Vector3.DistanceSquared(start, _nodes[i].Position)).DefaultIfEmpty(-1).First();
            if (root < 0) continue;
            float cost = Vector3.Distance(start, _nodes[root].Position);
            if (cost < distance[root]) { distance[root] = cost; open.Enqueue(root, cost); }
        }
        while (open.TryDequeue(out int at, out float cost))
        {
            if (cost > distance[at]) continue;
            foreach (int next in _edges[at])
            {
                float value = cost + Math.Max(.01f, Vector3.Distance(_nodes[at].Position, _nodes[next].Position));
                if (value < distance[next]) { distance[next] = value; open.Enqueue(next, value); }
            }
        }
        return distance;
    }
    internal bool TryCost(Vector3 point, out float red, out float blue)
    {
        red = blue = 0;
        foreach (int i in Enumerable.Range(0, _nodes.Count).Where(i => Biped(_nodes[i]))
            .OrderBy(i => Vector3.DistanceSquared(point, _nodes[i].Position)).Take(4))
        {
            if (!Single.IsFinite(_red[i]) || !Single.IsFinite(_blue[i])) continue;
            CollisionResult hit = default;
            if (CollisionDetection.CheckBetweenPoints(point.AddY(1), _nodes[i].Position.AddY(.5f), TestFlags.Players, _scene, ref hit)) continue;
            float last = Vector3.Distance(point, _nodes[i].Position);
            red = _red[i] + last; blue = _blue[i] + last; return true;
        }
        return false;
    }
}
