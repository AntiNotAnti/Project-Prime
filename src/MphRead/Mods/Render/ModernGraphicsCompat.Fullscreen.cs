#if !MPHREAD_SERVER
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private static readonly float[] _retainedFullscreenVertices =
        BuildRetainedFullscreenVertices();
    private static readonly int[] _retainedFullscreenIndices =
        { 0, 1, 2, 2, 1, 3 };

    private NativeGeometry? _retainedFullscreenGeometry;
    private long _retainedFullscreenDraws;

    internal static long RetainedFullscreenDraws =>
        _current?._retainedFullscreenDraws ?? 0;

    /// <summary>
    /// Draw the canonical clip-space fullscreen quad directly through the
    /// backend-native core path. This keeps the current shader, textures,
    /// uniforms, blend/depth state and render target exactly as configured by
    /// the caller, while avoiding LegacyGeometryBatch construction every frame.
    /// </summary>
    internal static bool TryDrawRetainedFullscreenQuad()
    {
        if (_current == null)
            return false;
        return Current.TryDrawRetainedFullscreenQuadCore();
    }

    private bool TryDrawRetainedFullscreenQuadCore()
    {
        ModernProgramKind kind = CurrentProgramKind();
        if (_wireframe || kind is not (
            ModernProgramKind.Rtt
            or ModernProgramKind.Shift
            or ModernProgramKind.Cel
            or ModernProgramKind.PlayerOutline
            or ModernProgramKind.ToneMap
            or ModernProgramKind.PostProcess))
        {
            return false;
        }

        if (_retainedFullscreenGeometry == null
            || _retainedFullscreenGeometry.Vertex == null
            || _retainedFullscreenGeometry.Index == null)
        {
            bool drawingList = _drawingList;
            _drawingList = true;
            try
            {
                _retainedFullscreenGeometry = PrepareGeometry(
                    _retainedFullscreenVertices,
                    _retainedFullscreenIndices);
            }
            finally
            {
                _drawingList = drawingList;
            }
        }

        DrawCoreIndexed(
            _retainedFullscreenVertices,
            _retainedFullscreenIndices,
            PrimitiveTopology.TriangleList,
            kind,
            retainedGeometry: _retainedFullscreenGeometry);
        _retainedFullscreenDraws++;
        return true;
    }

    private static float[] BuildRetainedFullscreenVertices()
    {
        var vertices =
            new float[LegacyGeometryBatch.FloatsPerVertex * 4];
        WriteBlitVertex(vertices, 0, 1f, 1f, 1f, 1f);
        WriteBlitVertex(vertices, 1, -1f, 1f, 0f, 1f);
        WriteBlitVertex(vertices, 2, 1f, -1f, 1f, 0f);
        WriteBlitVertex(vertices, 3, -1f, -1f, 0f, 0f);
        return vertices;
    }
}
#endif
