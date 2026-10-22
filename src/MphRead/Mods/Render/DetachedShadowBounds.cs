using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

internal static class DetachedShadowBounds
{
    // Decode source vertices, rather than assuming an authored node box bounds
    // every skinned vertex. Unknown/over-budget geometry is retained, not culled.
    internal static bool Read(Model model, out Vector3 min, out Vector3 max)
    {
        min = new(float.PositiveInfinity); max = new(float.NegativeInfinity);
        int remaining = 65536, vertices = 0;
        foreach (var list in model.RenderInstructionLists)
        {
            Vector3 p = Vector3.Zero;
            foreach (var instruction in list)
            {
                if (--remaining < 0) return false;
                uint a = instruction.Arguments.Count == 0 ? 0 : instruction.Arguments[0];
                static float S16(uint v) => (short)(v & 65535) / 4096f;
                static int S10(uint v) => (int)(v << 22) >> 22;
                switch (instruction.Code)
                {
                case InstructionCode.VTX_16:
                    if (instruction.Arguments.Count < 2) return false;
                    p = new(S16(a), S16(a >> 16), S16(instruction.Arguments[1])); break;
                case InstructionCode.VTX_10: p = new(S10(a) / 64f, S10(a >> 10) / 64f, S10(a >> 20) / 64f); break;
                case InstructionCode.VTX_XY: p.X = S16(a); p.Y = S16(a >> 16); break;
                case InstructionCode.VTX_XZ: p.X = S16(a); p.Z = S16(a >> 16); break;
                case InstructionCode.VTX_YZ: p.Y = S16(a); p.Z = S16(a >> 16); break;
                case InstructionCode.VTX_DIFF: p += new Vector3(S10(a), S10(a >> 10), S10(a >> 20)) / 4096f; break;
                case InstructionCode.NOP: case InstructionCode.BEGIN_VTXS: case InstructionCode.END_VTXS:
                case InstructionCode.MTX_RESTORE: case InstructionCode.COLOR: case InstructionCode.DIF_AMB:
                case InstructionCode.NORMAL: case InstructionCode.TEXCOORD: continue;
                default: return false;
                }
                min = Vector3.ComponentMin(min, p); max = Vector3.ComponentMax(max, p); vertices++;
            }
        }
        return vertices > 0;
    }

    internal static bool Overlaps(RenderItem item, Matrix4 light)
    {
        if (!item.RetainedGpuVisibilityEligible) return true;
        // The union under every palette matrix also encloses weighted
        // combinations (all accepted weights are nonnegative).
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int matrix = 0; matrix < System.Math.Max(1, item.MatrixStackCount); matrix++)
        {
            Matrix4 transform = item.Transform;
            if (item.MatrixStackCount > 0)
            {
                int i = matrix * 16; float[] s = item.MatrixStack;
                transform = new(s[i],s[i+1],s[i+2],s[i+3],s[i+4],s[i+5],s[i+6],s[i+7],
                    s[i+8],s[i+9],s[i+10],s[i+11],s[i+12],s[i+13],s[i+14],s[i+15]);
            }
            for (int c = 0; c < 8; c++)
            {
                Vector3 p = new((c & 1) == 0 ? item.RetainedBoundsMin.X : item.RetainedBoundsMax.X,
                    (c & 2) == 0 ? item.RetainedBoundsMin.Y : item.RetainedBoundsMax.Y,
                    (c & 4) == 0 ? item.RetainedBoundsMin.Z : item.RetainedBoundsMax.Z);
                p = Matrix.Vec3MultMtx4(p, transform);
                min = Vector3.ComponentMin(min,p); max = Vector3.ComponentMax(max,p);
            }
        }
        return Entities.RoomEntity.ShadowCasterOverlapsLightXY(min,max,light);
    }
}
