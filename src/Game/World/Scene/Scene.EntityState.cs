using MphRead.Entities;

namespace MphRead;

public partial class Scene
{
    private BeamProjectileEntity[]? _platformBeams;
    private ushort _nextItemRotation;

    // Platforms share a bounded pool within a room, never across scenes.
    internal BeamProjectileEntity[] PlatformBeams =>
        _platformBeams ??= SceneSetup.CreateBeamList(64, this);

    internal void ResetPlatformBeams() => _platformBeams = null;

    internal float NextItemRotation()
    {
        float rotation = _nextItemRotation / (float)0x10000 * 360f;
        _nextItemRotation = unchecked((ushort)(_nextItemRotation + 0x2000));
        return rotation;
    }
}
