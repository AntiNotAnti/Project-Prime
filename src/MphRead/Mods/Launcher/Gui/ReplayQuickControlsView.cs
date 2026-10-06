#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Game Theatre transport. Authoring lives in the independent Studio workspace.</summary>
internal sealed class ReplayQuickControlsView : UserControl
{
    public event EventHandler? Closed;
    public event EventHandler? ResumeRequested;
    private readonly Slider _position = new() { Minimum = 0, Maximum = 1, SmallChange = 60, LargeChange = 600 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PrimeButton _play;
    private readonly ComboBox _rates = new() { ItemsSource = new[] { "0.25×", "0.5×", "1×", "2×", "4×" } };
    private readonly ComboBox _camera = new() { ItemsSource = new[] { "First person", "Chase", "Free", "Orbit" } };
    private readonly DispatcherTimer _timer;
    private bool _refreshing;

    internal ReplayQuickControlsView()
    {
        Focusable = true;
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        body.Children.Add(PrimeChrome.Title("REPLAY"));
        body.Children.Add(_status);
        body.Children.Add(_position);
        _play = new PrimeButton("PAUSE", () => { ReplayController.TogglePause(); Refresh(); }, primary: true);
        body.Children.Add(PrimeChrome.Columns("*,*,*", new PrimeButton("−10 SECONDS", () => Jump(-600)), _play,
            new PrimeButton("+10 SECONDS", () => Jump(600))));
        body.Children.Add(PrimeChrome.Columns("*,*", new PrimeButton("RESTART", () => ReplayController.Restart()),
            new PrimeButton("STEP FRAME", () => ReplayController.StepForward())));
        _rates.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _rates.SelectedIndex >= 0)
                ReplayController.SetPlaybackRate(ReplayController.Rates[_rates.SelectedIndex]);
        };
        body.Children.Add(new TextBlock { Text = "Playback speed" });
        body.Children.Add(_rates);
        _camera.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _camera.SelectedIndex < 0) return;
            ReplayCamera.Director = false;
            ReplayCamera.PlayTrack = false;
            ReplayCamera.SetMode((ReplayCameraMode)_camera.SelectedIndex);
        };
        body.Children.Add(new TextBlock { Text = "Camera" });
        body.Children.Add(_camera);
        body.Children.Add(PrimeChrome.Columns("*,*", new PrimeButton("PREVIOUS PLAYER", SpectatorMode.CyclePrevious),
            new PrimeButton("NEXT PLAYER", SpectatorMode.CycleNext)));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
#if !ANDROID
        body.Children.Add(new PrimeButton("EDIT IN REPLAY STUDIO", () =>
        {
            if (DemoPlayback.LogicalPath is not { } path) { error.Text = "Open a replay first."; return; }
            error.Text = StudioIntegration.StudioApplicationLauncher.TryOpen(path, false, out string? problem)
                ? "Opening Project Prime Studio" : problem;
        }));
#endif
        body.Children.Add(error);
        body.Children.Add(PrimeChrome.Columns("*,*", new PrimeButton("BACK TO THEATRE", () => Closed?.Invoke(this, EventArgs.Empty)),
            new PrimeButton("FULLSCREEN", () => ResumeRequested?.Invoke(this, EventArgs.Empty))));
        Content = new ScrollViewer { Content = body };
        _position.PropertyChanged += (_, e) =>
        {
            if (!_refreshing && e.Property == Slider.ValueProperty)
                ReplayController.Seek((uint)Math.Clamp(Math.Round(_position.Value), 0, ReplayController.DurationFrames), resume: false);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { Refresh(); _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        KeyDown += (_, e) =>
        {
            if (e.Source is TextBox or ComboBox || e.KeyModifiers != KeyModifiers.None) return;
            if (e.Key == Key.Space) { ReplayController.TogglePause(); e.Handled = true; }
            else if (e.Key == Key.Left) { Jump(-60); e.Handled = true; }
            else if (e.Key == Key.Right) { Jump(60); e.Handled = true; }
        };
    }

    private static void Jump(int frames) => ReplayController.Seek((uint)Math.Clamp(
        (long)ReplayController.CurrentFrame + frames, 0, ReplayController.DurationFrames), resume: false);

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            _position.Maximum = Math.Max(1, ReplayController.DurationFrames);
            _position.Value = ReplayController.CurrentFrame;
            _status.Text = $"{ReplayHud.Time(ReplayController.CurrentFrame)} / {ReplayHud.Time(ReplayController.DurationFrames)} · {ReplayController.State} · {ReplayController.PlaybackRate:0.##}×";
            _play.Label = ReplayController.IsPaused || ReplayController.AtEnd ? "PLAY" : "PAUSE";
            _rates.SelectedIndex = Array.IndexOf(ReplayController.Rates, ReplayController.PlaybackRate);
            _camera.SelectedIndex = (int)ReplayCamera.Mode;
        }
        finally { _refreshing = false; }
    }
}
#endif
