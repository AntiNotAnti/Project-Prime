using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class PrimeTitleScreenView : UserControl, IDisposable
{
    internal const double PortraitAspectThreshold = 1.25;
    internal static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(650);
    private static readonly Uri BackgroundUri = new(
        "avares://ProjectPrime.Client.Presentation/Assets/project-prime-title-screen.png");
    private static int _assetFailureLogged;

    private readonly bool _reducedMotion;
    private readonly bool _allowBlink;
    private readonly DispatcherTimer _blinkTimer;
    private readonly CancellationTokenSource _dispose = new();
    private Bitmap? _bitmap;
    private PrimeTitleScreenPhase _phase;
    private bool _blinkBright = true;
    private bool _attached;
    private bool _disposed;

    public PrimeTitleScreenView(PrimeTitleScreenPhase phase = PrimeTitleScreenPhase.Loading,
        bool? reducedMotion = null, bool allowBlink = true,
        Func<Stream>? openBackground = null)
    {
        _phase = phase;
        _reducedMotion = reducedMotion ?? LauncherPrefs.ReducedMotion;
        _allowBlink = allowBlink;
        InitializeComponent();
        Thickness safe = PrimeLayoutMetrics.ResolveSafeArea(default);
        PromptArea.Margin = new Thickness(safe.Left + 8, 24,
            safe.Right + 8, Math.Max(54, safe.Bottom + 30));
        PrimeAccessibility.SetName(this, "Project Prime title screen");
        PrimeAccessibility.SetName(LoadingText, "Project Prime is initializing");
        PrimeAccessibility.SetName(PromptText, "Continue to Project Prime");
        LoadBackground(openBackground);
        _blinkTimer = new DispatcherTimer { Interval = BlinkInterval };
        _blinkTimer.Tick += BlinkTick;
        AttachedToVisualTree += Attached;
        DetachedFromVisualTree += Detached;
        SizeChanged += (_, e) => ApplyBackgroundStretch(e.NewSize);
        ApplyPhase();
    }

    internal bool HasBackgroundImage => _bitmap is not null;
    internal bool BlinkRunning => _blinkTimer.IsEnabled;
    internal Stretch BackgroundStretch => BackgroundImage.Stretch;
    internal PrimeTitleScreenPhase Phase => _phase;
    internal string Prompt => PromptText.Text ?? String.Empty;
    internal Thickness PromptMargin => PromptArea.Margin;

    internal void SetPhase(PrimeTitleScreenPhase phase)
    {
        if (_disposed || (int)phase < (int)_phase) return;
        _phase = phase;
        ApplyPhase();
    }

    internal void SetInputPrompt(PrimeInputDevice device, ControllerFamily family)
    {
        PromptText.Text = device switch
        {
            PrimeInputDevice.Touch => "TAP TO CONTINUE",
            PrimeInputDevice.Gamepad => "PRESS ANY BUTTON TO CONTINUE",
            _ => "PRESS ANY KEY TO CONTINUE"
        };
        PrimeAccessibility.SetStatus(PromptText, PromptText.Text);
        if (_phase == PrimeTitleScreenPhase.Ready)
            PrimeAccessibility.SetStatus(this,
                $"Project Prime is ready. {PromptText.Text}.");
    }

    internal async Task FadeOutAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        SetPhase(PrimeTitleScreenPhase.Dismissing);
        StopBlink();
        PrimeMotionSpec spec = PrimeMotion.Resolve(_reducedMotion,
            PrimeMotionPreset.Fade, PrimeMotion.NormalDuration);
        if (spec.IsImmediate)
        {
            Opacity = 0;
            return;
        }

        Transitions = PrimeMotion.CreateTransitions(reducedMotion: false,
            PrimeMotion.NormalDuration);
        Opacity = 0;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _dispose.Token);
        await Task.Delay(spec.Duration, linked.Token).ConfigureAwait(false);
    }

    private void LoadBackground(Func<Stream>? openBackground)
    {
        try
        {
            using Stream stream = (openBackground ?? (() => AssetLoader.Open(BackgroundUri)))();
            _bitmap = new Bitmap(stream);
            BackgroundImage.Source = _bitmap;
            FallbackPanel.IsVisible = false;
        }
        catch (Exception error)
        {
            BackgroundImage.Source = null;
            FallbackPanel.IsVisible = true;
            if (Interlocked.Exchange(ref _assetFailureLogged, 1) == 0)
                Debug.WriteLine($"Project Prime title artwork could not be loaded: {error.Message}");
        }
    }

    private void ApplyBackgroundStretch(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        BackgroundImage.Stretch = size.Width / size.Height < PortraitAspectThreshold
            ? Stretch.Uniform : Stretch.UniformToFill;
    }

    private void ApplyPhase()
    {
        bool ready = _phase == PrimeTitleScreenPhase.Ready;
        LoadingText.IsVisible = _phase == PrimeTitleScreenPhase.Loading;
        PromptText.IsVisible = ready;
        PrimeAccessibility.SetStatus(this, ready
            ? $"Project Prime is ready. {PromptText.Text}."
            : _phase == PrimeTitleScreenPhase.Loading
                ? "Project Prime is initializing."
                : "Opening Project Prime.");
        if (ready) StartBlinkIfEligible();
        else StopBlink();
    }

    private void Attached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
    {
        _attached = true;
        StartBlinkIfEligible();
    }

    private void Detached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        StopBlink();
    }

    private void StartBlinkIfEligible()
    {
        if (_disposed || _reducedMotion || !_allowBlink
            || _phase != PrimeTitleScreenPhase.Ready
            || !_attached)
        {
            PromptText.Opacity = 1;
            return;
        }
        if (!_blinkTimer.IsEnabled) _blinkTimer.Start();
    }

    private void StopBlink()
    {
        _blinkTimer.Stop();
        _blinkBright = true;
        PromptText.Opacity = 1;
    }

    private void BlinkTick(object? sender, EventArgs args)
    {
        if (_disposed || _phase != PrimeTitleScreenPhase.Ready)
        {
            StopBlink();
            return;
        }
        _blinkBright = !_blinkBright;
        PromptText.Opacity = _blinkBright ? 1 : 0.42;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispose.Cancel();
        StopBlink();
        AttachedToVisualTree -= Attached;
        DetachedFromVisualTree -= Detached;
        _bitmap?.Dispose();
        _bitmap = null;
        BackgroundImage.Source = null;
        _dispose.Dispose();
    }
}
