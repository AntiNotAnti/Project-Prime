using MphRead.Entities;

namespace MphRead
{
    public partial class Scene
    {
        private BeamProjectileEntity[]? _forceFieldLockProjectiles;

        // Locks share the original 64-projectile capacity within their owning scene.
        internal BeamProjectileEntity[] GetForceFieldLockProjectiles()
            => _forceFieldLockProjectiles ??= SceneSetup.CreateBeamList(64, this);

        internal void ResetForceFieldLockProjectiles() => _forceFieldLockProjectiles = null;
    }
}
