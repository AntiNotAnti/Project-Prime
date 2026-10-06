using System;

namespace MphRead.Mods.Render;

/// <summary>
/// A pyramid is evidence only for the extracted world and camera which produced
/// it in the current frame. Previous-frame depth may prioritize work, but must
/// never suppress a draw: even a stationary camera can observe a removed door.
/// </summary>
internal sealed class GpuOcclusionDepthPolicy
{
    private object? _world;
    private ulong _revision;
    private int _depthTexture;
    private int _width;
    private int _height;

    internal void Invalidate() => _world = null;

    internal void Capture(object world, ulong revision,
        int depthTexture, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(world);
        Invalidate();
        if (revision == 0 || depthTexture == 0 || width <= 0 || height <= 0)
            return;
        _world = world;
        _revision = revision;
        _depthTexture = depthTexture;
        _width = width;
        _height = height;
    }

    internal bool CanReject(object world, ulong revision,
        int depthTexture, int width, int height, bool identicalFinitePose) =>
        identicalFinitePose && _world != null
        && ReferenceEquals(_world, world) && _revision == revision
        && _depthTexture == depthTexture && _width == width && _height == height;
}
