using System;
using MphRead.Mods.Cosmetics;

namespace MphRead.Mods.Launcher;

/// <summary>Local visual state only; never changes a Hunter identity or cosmetic loadout.</summary>
public readonly record struct LauncherHunterPreviewSnapshot(SkinContext Mode, float Yaw,
    float Zoom, bool LoopDeath, int DeathRequest)
{
    public bool CompareNative { get; init; }

    public bool IsValid => Mode is SkinContext.Biped or SkinContext.ViewModel or SkinContext.AltForm
        && Single.IsFinite(Yaw) && Yaw >= -MathF.PI && Yaw < MathF.PI
        && Single.IsFinite(Zoom) && Zoom is >= 0.8f and <= 1.2f && DeathRequest >= 0;
}

/// <summary>Owner-thread preview controls with the same zoom/death semantics as HunterStand.</summary>
public sealed class LauncherHunterPreviewController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private SkinContext _mode;
    private float _yaw;
    private int _deathRequest;
    private float _zoom = 1f;
    private bool _loopDeath, _compareNative, _disposed;

    public LauncherHunterPreviewSnapshot Current()
    {
        Check();
        return new(_mode, _yaw, _zoom, _loopDeath, _deathRequest)
        { CompareNative = _compareNative };
    }

    public bool SelectMode(int mode)
    {
        Check();
        if (mode is not (0 or 1 or 2)) return false;
        // The original customization owner resets the camera and death request
        // when changing between Hunter, weapon and alternate-form previews.
        Reset();
        _compareNative = false;
        _mode = (SkinContext)mode;
        return true;
    }

    public bool Rotate(int direction)
    {
        Check();
        if (direction is not (0 or 1)) return false;
        return RotateBy((direction == 0 ? -1 : 1) * (MathF.PI / 12));
    }

    public bool RotateBy(float radians)
    {
        Check();
        if (!Single.IsFinite(radians)) return false;
        _yaw = (_yaw + radians) % MathF.Tau;
        if (_yaw >= MathF.PI) _yaw -= MathF.Tau;
        else if (_yaw < -MathF.PI) _yaw += MathF.Tau;
        return true;
    }

    public bool Zoom(int direction)
    {
        Check();
        return direction is 0 or 1 && ZoomBy(direction == 0 ? -.05f : .05f);
    }

    public bool ZoomBy(float delta)
    {
        Check();
        if (!Single.IsFinite(delta)) return false;
        float next = Math.Clamp(_zoom + delta, .8f, 1.2f);
        if (next == _zoom) return false;
        _zoom = next;
        return true;
    }

    public bool PreviewDeath()
    {
        Check();
        if (_deathRequest == Int32.MaxValue) return false;
        _compareNative = false;
        _mode = SkinContext.Biped;
        _deathRequest++;
        return true;
    }

    public bool ToggleLoopDeath()
    {
        Check();
        if (!_loopDeath && !PreviewDeath()) return false;
        _loopDeath = !_loopDeath;
        return true;
    }

    public bool ToggleCompareNative()
    {
        Check();
        Reset();
        _compareNative = !_compareNative;
        return true;
    }

    public void ClearComparison()
    {
        Check();
        _compareNative = false;
    }

    public bool Reset()
    {
        Check();
        // HunterStand.ResetPreview preserves the chosen form and loop toggle.
        _yaw = 0;
        _zoom = 1f;
        _deathRequest = 0;
        return true;
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Hunter preview is owned by the UI thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Check();
        _disposed = true;
    }
}
