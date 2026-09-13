using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Launcher.Theme;

/// <summary>Small, opt-in motion recipes for the launcher shell.</summary>
public enum PrimeMotionPreset
{
    Fade,
    Slide,
    FadeAndSlide
}

/// <summary>
/// The resolved values for one optional UI motion. Gameplay animation never
/// reads this type; it belongs only to the Avalonia launcher surface.
/// </summary>
public readonly record struct PrimeMotionSpec
{
    public PrimeMotionSpec(TimeSpan duration, Vector translation,
        double initialOpacity)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (initialOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(initialOpacity));
        if (Double.IsNaN(translation.X) || Double.IsNaN(translation.Y)
            || Double.IsInfinity(translation.X)
            || Double.IsInfinity(translation.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(translation));
        }

        Duration = duration;
        Translation = translation;
        InitialOpacity = initialOpacity;
    }

    public TimeSpan Duration { get; }
    public Vector Translation { get; }
    public Vector Offset => Translation;
    public double InitialOpacity { get; }
    public bool IsImmediate => Duration == TimeSpan.Zero;
    public bool IsEnabled => !IsImmediate;

    public static PrimeMotionSpec Immediate =>
        new(TimeSpan.Zero, Vector.Zero, 1);
}

/// <summary>
/// Lifecycle-safe opt-in transitions for shell controls.
///
/// Avalonia owns the render clock for these transitions. This helper does not
/// create a timer or a background loop. A lease restores the control's prior
/// transition/transform state and also tears itself down if the control leaves
/// the visual tree.
/// </summary>
public static class PrimeMotion
{
    public const int FastDurationMilliseconds = 160;
    public const int NormalDurationMilliseconds = 180;
    public const int MaximumDurationMilliseconds = 220;
    public const double DefaultTranslationDip = 8;

    public static TimeSpan FastDuration =>
        TimeSpan.FromMilliseconds(FastDurationMilliseconds);

    public static TimeSpan NormalDuration =>
        TimeSpan.FromMilliseconds(NormalDurationMilliseconds);

    public static TimeSpan MaximumDuration =>
        TimeSpan.FromMilliseconds(MaximumDurationMilliseconds);

    /// <summary>Current launcher preference, kept out of gameplay settings.</summary>
    public static bool ReducedMotion => LauncherPrefs.ReducedMotion;

    /// <summary>
    /// Resolve a recipe against the current launcher preference. The optional
    /// bool is useful for callers/tests that need a stable explicit choice.
    /// </summary>
    public static PrimeMotionSpec Resolve(PrimeMotionPreset preset =
        PrimeMotionPreset.FadeAndSlide, TimeSpan? duration = null,
        Vector? translation = null)
        => Resolve(LauncherPrefs.ReducedMotion, preset, duration, translation);

    public static PrimeMotionSpec Resolve(bool reducedMotion,
        PrimeMotionPreset preset = PrimeMotionPreset.FadeAndSlide,
        TimeSpan? duration = null, Vector? translation = null)
    {
        if (reducedMotion)
            return PrimeMotionSpec.Immediate;

        TimeSpan resolvedDuration = NormalizeDuration(duration);
        Vector resolvedTranslation = translation ?? preset switch
        {
            PrimeMotionPreset.Fade => Vector.Zero,
            PrimeMotionPreset.Slide => new Vector(0, DefaultTranslationDip),
            PrimeMotionPreset.FadeAndSlide => new Vector(0, DefaultTranslationDip),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        double initialOpacity = preset == PrimeMotionPreset.Slide ? 1 : 0;
        return new PrimeMotionSpec(resolvedDuration, resolvedTranslation,
            initialOpacity);
    }

    /// <summary>
    /// Creates the opacity transition used by an optional shell motion.
    /// Reduced motion returns null before allocating an Avalonia collection.
    /// </summary>
    public static Transitions? CreateTransitions(bool reducedMotion,
        TimeSpan? duration = null)
    {
        if (reducedMotion)
            return null;

        return CreateOpacityTransitions(NormalizeDuration(duration));
    }

    public static Transitions? CreateTransitions(TimeSpan? duration = null)
        => CreateTransitions(ReducedMotion, duration);

    /// <summary>Creates translation transitions for a TranslateTransform.</summary>
    public static Transitions? CreateTranslationTransitions(bool reducedMotion,
        TimeSpan? duration = null)
    {
        if (reducedMotion)
            return null;

        TimeSpan resolvedDuration = NormalizeDuration(duration);
        var transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            }
        };
        return transitions;
    }

    public static Transitions? CreateTranslationTransitions(
        TimeSpan? duration = null)
        => CreateTranslationTransitions(ReducedMotion, duration);

    /// <summary>
    /// Creates the short color transition shared by Prime buttons and tabs.
    /// The transition deliberately excludes geometry and transforms: hover
    /// and press feedback must never move controls or change their measured
    /// size. Reduced motion returns before allocating the collection.
    /// </summary>
    public static Transitions? CreateInteractiveTransitions(bool reducedMotion,
        TimeSpan? duration = null)
    {
        if (reducedMotion)
            return null;

        TimeSpan resolvedDuration = NormalizeDuration(duration ?? FastDuration);
        return new Transitions
        {
            new BrushTransition
            {
                Property = Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            },
            new BrushTransition
            {
                Property = Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            },
            new BrushTransition
            {
                Property = Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            }
        };
    }

    public static Transitions? CreateInteractiveTransitions(
        TimeSpan? duration = null)
        => CreateInteractiveTransitions(ReducedMotion, duration);

    /// <summary>
    /// Creates the matching surface transition for cards and status rows.
    /// Only paint properties are animated; border thickness, padding, and
    /// transforms remain fixed so selection feedback cannot reflow a route.
    /// </summary>
    public static Transitions? CreateSurfaceTransitions(bool reducedMotion,
        TimeSpan? duration = null)
    {
        if (reducedMotion)
            return null;

        TimeSpan resolvedDuration = NormalizeDuration(duration ?? FastDuration);
        return new Transitions
        {
            new BrushTransition
            {
                Property = Border.BackgroundProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            },
            new BrushTransition
            {
                Property = Border.BorderBrushProperty,
                Duration = resolvedDuration,
                Easing = new CubicEaseOut()
            }
        };
    }

    public static Transitions? CreateSurfaceTransitions(
        TimeSpan? duration = null)
        => CreateSurfaceTransitions(ReducedMotion, duration);

    /// <summary>
    /// Runs one low-opacity scan across a dedicated route overlay. It uses the
    /// same bounded transition system as every other Prime effect and creates
    /// no lease or animation objects when reduced motion is enabled.
    /// </summary>
    public static PrimeScanAccentLease? AnimateScan(Control control,
        double distance, bool? reducedMotion = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (reducedMotion ?? ReducedMotion)
        {
            control.Opacity = 0;
            return null;
        }
        if (!Double.IsFinite(distance) || distance <= 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        return new PrimeScanAccentLease(control, distance, MaximumDuration);
    }

    /// <summary>
    /// Starts an optional fade/translation when the control is attached. The
    /// returned lease should be held by the route/view owner until replacement.
    /// </summary>
    public static PrimeMotionLease? AnimateEntry(Control control,
        PrimeMotionPreset preset = PrimeMotionPreset.FadeAndSlide,
        bool? reducedMotion = null, TimeSpan? duration = null,
        Vector? translation = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        PrimeMotionSpec spec = Resolve(reducedMotion ?? ReducedMotion,
            preset, duration, translation);
        if (spec.IsImmediate)
        {
            // Do not allocate a transition collection, transform, timer, or
            // event handler for reduced-motion users. An existing transform is
            // unrelated to this optional effect and is intentionally kept.
            control.Opacity = 1;
            return null;
        }

        ITransform? previousTransform = control.RenderTransform;
        Transitions? previousControlTransitions = control.Transitions;
        double previousOpacity = control.Opacity;
        double? previousTranslationX = (previousTransform as TranslateTransform)?.X;
        double? previousTranslationY = (previousTransform as TranslateTransform)?.Y;
        TranslateTransform motionTransform = AttachTranslation(control,
            previousTransform, out Transform? transformContainer);
        Transitions? previousTransformTransitions = motionTransform.Transitions;
        Transitions visualTransitions = CreateOpacityTransitions(spec.Duration);
        Transitions transformTransitions = CreateTranslationTransitions(
            reducedMotion: false, duration: spec.Duration)!;

        // Set the starting values with transitions detached. This makes entry
        // deterministic even when a control is already attached or is reused.
        control.Transitions = null;
        motionTransform.Transitions = null;
        control.Opacity = spec.InitialOpacity;
        motionTransform.X = spec.Translation.X;
        motionTransform.Y = spec.Translation.Y;

        return new PrimeMotionLease(control, spec, previousTransform,
            previousControlTransitions, previousOpacity, motionTransform,
            transformContainer, previousTransformTransitions,
            visualTransitions, transformTransitions, previousTranslationX,
            previousTranslationY);
    }

    private static Transitions CreateOpacityTransitions(TimeSpan duration)
        => new()
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = new CubicEaseOut()
            }
        };

    private static TimeSpan NormalizeDuration(TimeSpan? requested)
    {
        if (requested is null)
            return NormalDuration;
        if (requested.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requested));
        if (requested.Value == TimeSpan.Zero)
            return TimeSpan.Zero;

        double milliseconds = Math.Clamp(requested.Value.TotalMilliseconds,
            FastDurationMilliseconds, MaximumDurationMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static TranslateTransform AttachTranslation(Control control,
        ITransform? previousTransform, out Transform? transformContainer)
    {
        if (previousTransform is TranslateTransform existing)
        {
            transformContainer = null;
            return existing;
        }

        var translation = new TranslateTransform();
        if (previousTransform is null)
        {
            transformContainer = null;
            control.RenderTransform = translation;
            return translation;
        }

        var group = new TransformGroup();
        if (previousTransform is TransformGroup existingGroup)
        {
            foreach (Transform child in existingGroup.Children)
                group.Children.Add(child);
        }
        else if (previousTransform is Transform transform)
        {
            group.Children.Add(transform);
        }
        else
        {
            // Avalonia exposes ITransform for the getter, while a mutable
            // Transform is required by TransformGroup.Children. Preserve the
            // safe, common path without guessing at an immutable transform.
            transformContainer = null;
            control.RenderTransform = translation;
            return translation;
        }
        group.Children.Add(translation);
        control.RenderTransform = group;
        transformContainer = group;
        return translation;
    }

    internal static void RestoreTransform(Control control,
        ITransform? previousTransform, Transform? transformContainer,
        TranslateTransform motionTransform)
    {
        if (transformContainer is TransformGroup group
            && ReferenceEquals(control.RenderTransform, group))
        {
            control.RenderTransform = previousTransform as Transform;
            return;
        }

        if (ReferenceEquals(control.RenderTransform, motionTransform))
            control.RenderTransform = previousTransform as Transform;
    }
}

/// <summary>Owns one optional Prime entry motion until its view is replaced.</summary>
public sealed class PrimeMotionLease : IDisposable
{
    private readonly Control _control;
    private readonly ITransform? _previousTransform;
    private readonly Transitions? _previousControlTransitions;
    private readonly double _previousOpacity;
    private readonly TranslateTransform _motionTransform;
    private readonly Transform? _transformContainer;
    private readonly Transitions? _previousTransformTransitions;
    private readonly Transitions _visualTransitions;
    private readonly Transitions _transformTransitions;
    private readonly double? _previousTranslationX;
    private readonly double? _previousTranslationY;
    private bool _started;
    private bool _disposed;

    internal PrimeMotionLease(Control control, PrimeMotionSpec spec,
        ITransform? previousTransform, Transitions? previousControlTransitions,
        double previousOpacity, TranslateTransform motionTransform,
        Transform? transformContainer,
        Transitions? previousTransformTransitions,
        Transitions visualTransitions, Transitions transformTransitions,
        double? previousTranslationX, double? previousTranslationY)
    {
        _control = control;
        Spec = spec;
        _previousTransform = previousTransform;
        _previousControlTransitions = previousControlTransitions;
        _previousOpacity = previousOpacity;
        _motionTransform = motionTransform;
        _transformContainer = transformContainer;
        _previousTransformTransitions = previousTransformTransitions;
        _visualTransitions = visualTransitions;
        _transformTransitions = transformTransitions;
        _previousTranslationX = previousTranslationX;
        _previousTranslationY = previousTranslationY;

        _control.DetachedFromVisualTree += DetachedFromVisualTree;
        if (_control.IsAttachedToVisualTree())
            Start();
        else
            _control.AttachedToVisualTree += AttachedToVisualTree;
    }

    public PrimeMotionSpec Spec { get; }
    public bool IsStarted => _started;
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _control.AttachedToVisualTree -= AttachedToVisualTree;
        _control.DetachedFromVisualTree -= DetachedFromVisualTree;

        if (ReferenceEquals(_control.Transitions, _visualTransitions))
            _control.Transitions = _previousControlTransitions;
        if (ReferenceEquals(_motionTransform.Transitions, _transformTransitions))
            _motionTransform.Transitions = _previousTransformTransitions;

        // A direct TranslateTransform is already the control's original
        // transform, so restore its coordinates before restoring the object.
        // A grouped transform gets a new motion child and does not need this.
        if (_previousTranslationX is double previousX
            && _previousTranslationY is double previousY
            && ReferenceEquals(_control.RenderTransform, _motionTransform))
        {
            _motionTransform.X = previousX;
            _motionTransform.Y = previousY;
        }

        PrimeMotion.RestoreTransform(_control, _previousTransform,
            _transformContainer, _motionTransform);
        _control.Opacity = _previousOpacity;
    }

    private void AttachedToVisualTree(object? sender,
        VisualTreeAttachmentEventArgs args)
    {
        _control.AttachedToVisualTree -= AttachedToVisualTree;
        Start();
    }

    private void DetachedFromVisualTree(object? sender,
        VisualTreeAttachmentEventArgs args)
        => Dispose();

    private void Start()
    {
        if (_disposed || _started)
            return;
        _started = true;
        _control.Transitions = _visualTransitions;
        _motionTransform.Transitions = _transformTransitions;
        _control.Opacity = 1;
        _motionTransform.X = 0;
        _motionTransform.Y = 0;
    }
}

/// <summary>Owns one non-repeating route scan accent.</summary>
public sealed class PrimeScanAccentLease : IDisposable
{
    private readonly Control _control;
    private readonly double _distance;
    private readonly ITransform? _previousTransform;
    private readonly Transitions? _previousControlTransitions;
    private readonly double _previousOpacity;
    private readonly TranslateTransform _translation;
    private readonly Transitions? _previousTransformTransitions;
    private readonly Transitions _opacityTransitions;
    private readonly Transitions _translationTransitions;
    private bool _started;
    private bool _disposed;

    internal PrimeScanAccentLease(Control control, double distance,
        TimeSpan duration)
    {
        _control = control;
        _distance = distance;
        _previousTransform = control.RenderTransform;
        _previousControlTransitions = control.Transitions;
        _previousOpacity = control.Opacity;
        _translation = new TranslateTransform();
        _previousTransformTransitions = _translation.Transitions;
        _opacityTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = new CubicEaseOut()
            }
        };
        _translationTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = duration,
                Easing = new CubicEaseOut()
            }
        };

        control.Transitions = null;
        control.RenderTransform = _translation;
        control.Opacity = 0.12;
        _translation.Y = 0;
        control.DetachedFromVisualTree += Detached;
        if (control.IsAttachedToVisualTree()) Start();
        else control.AttachedToVisualTree += Attached;
    }

    public bool IsStarted => _started;
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _control.AttachedToVisualTree -= Attached;
        _control.DetachedFromVisualTree -= Detached;
        if (ReferenceEquals(_control.Transitions, _opacityTransitions))
            _control.Transitions = _previousControlTransitions;
        if (ReferenceEquals(_translation.Transitions, _translationTransitions))
            _translation.Transitions = _previousTransformTransitions;
        if (ReferenceEquals(_control.RenderTransform, _translation))
            _control.RenderTransform = _previousTransform as Transform;
        _control.Opacity = _previousOpacity;
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _control.AttachedToVisualTree -= Attached;
        Start();
    }

    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
        => Dispose();

    private void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _control.Transitions = _opacityTransitions;
        _translation.Transitions = _translationTransitions;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _control.Opacity = 0;
            _translation.Y = _distance;
        }, DispatcherPriority.Render);
    }
}
