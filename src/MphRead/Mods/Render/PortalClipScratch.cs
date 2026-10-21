using System;
using System.Collections.Generic;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Owned by one room/view. A caller consumes Result before starting its next clip,
// including a recursive portal walk; no ancestor retains a scratch-array alias.
internal sealed class PortalClipScratch
{
    internal const int Capacity = 14;
    private readonly Vector3[] _first = new Vector3[Capacity];
    private readonly Vector3[] _second = new Vector3[Capacity];
    internal readonly Vector3[] Result = new Vector3[Capacity];

    // -1 means the caller must abandon culling, never silently truncate a polygon.
    internal int Clip(FrustumInfo frustum, IReadOnlyList<Vector3> points)
    {
        if (points.Count > Capacity || frustum.Count <= 0
            || frustum.Count > frustum.Planes.Length) return -1;
        if (points.Count == 0) return 0;
        int count = points.Count;
        for (int j = 0; j < count; j++) _first[j] = points[j];
        Vector3[] input = _first;
        for (int i = 0; i < frustum.Count; i++)
        {
            Vector3[] output = i == frustum.Count - 1 ? Result
                : ReferenceEquals(input, _first) ? _second : _first;
            Vector4 plane = frustum.Planes[i].Plane;
            int written = 0;
            float distance = Vector3.Dot(input[0], plane.Xyz) - plane.W;
            bool inside = distance >= 0;
            for (int j = 0; j < count; j++)
            {
                Vector3 point = input[j];
                Vector3 next = input[j == count - 1 ? 0 : j + 1];
                if (inside)
                {
                    if (written == output.Length) return -1;
                    output[written++] = point;
                }
                float nextDistance = Vector3.Dot(next, plane.Xyz) - plane.W;
                bool nextInside = nextDistance >= 0;
                if (inside != nextInside)
                {
                    if (written == output.Length) return -1;
                    output[written++] = point + (next - point) * (-distance / (nextDistance - distance));
                }
                distance = nextDistance;
                inside = nextInside;
            }
            if (written == 0) return 0;
            count = written;
            input = output;
        }
        return count;
    }
}
