#if !MPHREAD_SERVER
namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    private bool _surfaceConfigured;
    private bool _surfaceNeedsRecreation;

    private void RecordSurfaceRecovery(NativeGraphicsOutcome outcome)
    {
        if (outcome == NativeGraphicsOutcome.SurfaceLost)
            _surfaceNeedsRecreation = true;
        if (outcome is NativeGraphicsOutcome.Outdated or NativeGraphicsOutcome.SurfaceLost
            or NativeGraphicsOutcome.DeviceLost)
            _surfaceConfigured = false;
    }

    private bool PrepareSurfaceForAcquire()
    {
        if (_device.IsLost) return false;
        if (_surfaceNeedsRecreation)
        {
            if(!_ownsDevice)
                throw new System.InvalidOperationException("Studio native surface was lost. Retry its viewport to recreate presentation.");
            _device.RecreateSurface();
            _surfaceNeedsRecreation = false;
            QuerySurfaceFormat();
        }
        if (!_surfaceConfigured) ConfigureSurface();
        return _surfaceConfigured && !_device.IsLost;
    }
}
#endif
