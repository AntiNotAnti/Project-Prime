using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Optional diagnostic overlay. Detaching it stops polling and Dispose releases its providers.</summary>
public sealed class StudioPerformanceHud : UserControl, IDisposable
{
    private readonly StudioPerformanceCollector _collector;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _text;
    private bool _disposed;
    private bool _attached;
    public StudioPerformanceSnapshot? Snapshot { get; private set; }
    public bool IsSampling => _timer.IsEnabled;
    public string DisplayText => _text.Text ?? "";

    public StudioPerformanceHud(StudioJobManager jobs, Func<IStudioDocument?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Name = "StudioPerformanceHud";
        _collector = new(jobs);
        _collector.Register("Graphics lifetime", StudioPerformanceSources.Graphics);
        _collector.Register("Active document", () => StudioPerformanceSources.Document(current()));
        _text = new TextBlock
        {
            Name = "StudioPerformanceMetrics", FontSize = 11, LineHeight = 17,
            Foreground = new SolidColorBrush(Color.Parse("#DDEAF5")),
            TextWrapping = TextWrapping.Wrap
        };
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EE101827")),
            BorderBrush = new SolidColorBrush(Color.Parse("#477089")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8, Children =
                {
                    new TextBlock { Text = "PERFORMANCE", FontSize = 11, FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#79C9EF")) },
                    new ScrollViewer { MaxHeight = 620, Content = _text }
                }
            }
        };
        Width = 340;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _attached = true;
        if (!_disposed && IsEffectivelyVisible) { RefreshSnapshot(); _timer.Start(); }
    }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    { _attached = false; _timer.Stop(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || !_attached || _disposed) return;
        if (IsEffectivelyVisible) { RefreshSnapshot(); _timer.Start(); }
        else _timer.Stop();
    }
    private void OnTick(object? sender, EventArgs args)
    { if (IsEffectivelyVisible) RefreshSnapshot(); }
    public void RefreshSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        Snapshot = _collector.Capture();
        _text.Text = StudioPerformanceText.Format(Snapshot);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Tick -= OnTick;
        AttachedToVisualTree -= OnAttached; DetachedFromVisualTree -= OnDetached;
        _collector.Dispose(); Snapshot = null;
    }
}
