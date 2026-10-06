using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Rendering;

namespace ProjectPrime.Studio;

public sealed class App : Application
{
    private static StudioPaths? _paths;
    private static StudioOpenRequest? _request;
    private static Action<StudioWindow>? _ready;
    private static string? _startupError;
    internal static void Configure(StudioPaths paths, StudioOpenRequest request, Action<StudioWindow> ready)
        => (_paths, _request, _ready) = (paths, request, ready);
    internal static void ConfigureStartupError(string error) => _startupError = error;
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_startupError is { } error)
            {
                Window failure = new() { Title = "Project Prime Studio · Startup error", Width = 600, SizeToContent = SizeToContent.Height, MinHeight = 180, CanResize = false };
                StackPanel content = new() { Margin = new Thickness(24), Spacing = 20 };
                content.Children.Add(new TextBlock { Text = "Studio could not complete the launch request", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap, MaxWidth = 540 });
                Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
                close.Click += (_, _) => failure.Close();
                content.Children.Add(close);
                failure.Content = content;
                desktop.MainWindow = failure;
                base.OnFrameworkInitializationCompleted();
                return;
            }
            StudioPaths paths = _paths ?? StudioPaths.CreateDefault();
            StudioSettingsStore store = new(paths);
            StudioOpenRequest request = _request ?? new(Guid.NewGuid(),StudioOpenKind.Home);
            StudioGraphicsHost.Initialize(request.SafeMode);
            StudioWindow window = new(paths, store.LoadSettings(),request,store.LoadSession());
            desktop.MainWindow = window;
            window.Opened += (_, _) => _ready?.Invoke(window);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
