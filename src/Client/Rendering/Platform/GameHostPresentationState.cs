using System;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// The native game host's presentation contract.  SDL reports a logical
/// client rectangle and a drawable framebuffer rectangle in different units;
/// keeping both names in the contract prevents an Avalonia consumer from
/// guessing a scaling factor.  Position is likewise explicitly a screen
/// coordinate in logical window units.
/// </summary>
internal readonly record struct GameHostPresentationState(
    Vector2i LogicalClientSizeUnits,
    Vector2i FramebufferSizePixels,
    Vector2i WindowPositionScreenUnits,
    bool IsVisible,
    bool IsMinimized,
    bool IsFocused,
    bool ActivationDeferred,
    bool IsFullscreen = false)
{
    public Vector2i LogicalSizeUnits => LogicalClientSizeUnits;

    public bool HasValidGeometry => LogicalClientSizeUnits.X > 0
        && LogicalClientSizeUnits.Y > 0
        && FramebufferSizePixels.X > 0
        && FramebufferSizePixels.Y > 0;
}

/// <summary>
/// Event-driven state reducer used by <see cref="SdlGameHost"/> and by
/// headless presentation tests.  Invalid/zero resize reports never erase the
/// last valid geometry; callers observe minimization separately and can keep a
/// logical overlay mode alive while its native window is hidden.
/// </summary>
internal sealed class GameHostPresentationTracker
{
    private Vector2i _lastLogicalSizeUnits;
    private Vector2i _lastFramebufferSizePixels;
    private Vector2i _lastWindowPositionScreenUnits;
    private GameHostPresentationState _current;

    public GameHostPresentationTracker(Vector2i logicalSizeUnits,
        Vector2i framebufferSizePixels, Vector2i windowPositionScreenUnits,
        bool isVisible = false, bool isMinimized = false, bool isFocused = false,
        bool activationDeferred = false, bool isFullscreen = false)
    {
        if (!IsValid(logicalSizeUnits) || !IsValid(framebufferSizePixels))
            throw new ArgumentOutOfRangeException(nameof(logicalSizeUnits),
                "Presentation geometry must be positive.");
        _lastLogicalSizeUnits = logicalSizeUnits;
        _lastFramebufferSizePixels = framebufferSizePixels;
        _lastWindowPositionScreenUnits = windowPositionScreenUnits;
        _current = Build(isVisible, isMinimized, isFocused, activationDeferred,
            isFullscreen);
    }

    public GameHostPresentationState Current => _current;

    public event Action<GameHostPresentationState>? Changed;

    public void UpdateGeometry(Vector2i logicalSizeUnits,
        Vector2i framebufferSizePixels, Vector2i windowPositionScreenUnits)
    {
        if (IsValid(logicalSizeUnits)) _lastLogicalSizeUnits = logicalSizeUnits;
        if (IsValid(framebufferSizePixels)) _lastFramebufferSizePixels = framebufferSizePixels;
        if (windowPositionScreenUnits.X != int.MinValue
            && windowPositionScreenUnits.Y != int.MinValue)
        {
            _lastWindowPositionScreenUnits = windowPositionScreenUnits;
        }
        Publish(_current.IsVisible, _current.IsMinimized, _current.IsFocused,
            _current.ActivationDeferred, _current.IsFullscreen);
    }

    public void UpdateVisibility(bool isVisible, bool isMinimized)
        => Publish(isVisible, isMinimized, _current.IsFocused,
            _current.ActivationDeferred, _current.IsFullscreen);

    public void UpdateFocus(bool isFocused, bool activationDeferred)
        => Publish(_current.IsVisible, _current.IsMinimized, isFocused,
            activationDeferred, _current.IsFullscreen);

    public void Update(bool isVisible, bool isMinimized, bool isFocused,
        bool activationDeferred, Vector2i logicalSizeUnits,
        Vector2i framebufferSizePixels, Vector2i windowPositionScreenUnits,
        bool? isFullscreen = null)
    {
        if (IsValid(logicalSizeUnits)) _lastLogicalSizeUnits = logicalSizeUnits;
        if (IsValid(framebufferSizePixels)) _lastFramebufferSizePixels = framebufferSizePixels;
        if (windowPositionScreenUnits.X != int.MinValue
            && windowPositionScreenUnits.Y != int.MinValue)
        {
            _lastWindowPositionScreenUnits = windowPositionScreenUnits;
        }
        Publish(isVisible, isMinimized, isFocused, activationDeferred,
            isFullscreen ?? _current.IsFullscreen);
    }

    private void Publish(bool isVisible, bool isMinimized, bool isFocused,
        bool activationDeferred, bool isFullscreen)
    {
        GameHostPresentationState next = Build(isVisible, isMinimized, isFocused,
            activationDeferred, isFullscreen);
        if (next == _current) return;
        _current = next;
        Changed?.Invoke(next);
    }

    private GameHostPresentationState Build(bool isVisible, bool isMinimized,
        bool isFocused, bool activationDeferred, bool isFullscreen)
        => new(_lastLogicalSizeUnits, _lastFramebufferSizePixels,
            _lastWindowPositionScreenUnits, isVisible, isMinimized, isFocused,
            activationDeferred, isFullscreen);

    private static bool IsValid(Vector2i value) => value.X > 0 && value.Y > 0;
}
