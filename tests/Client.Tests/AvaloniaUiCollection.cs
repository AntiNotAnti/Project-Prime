using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MphRead.Tests.Client.ClientTestAppBuilder))]

namespace MphRead.Tests.Client;

/// <summary>
/// Avalonia owns one application and UI dispatcher per test process. Keep the
/// extracted client control tests on one deterministic non-parallel fixture.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaUiCollection
{
    public const string Name = "Avalonia UI";
}

/// <summary>Headless Avalonia setup owned by the extracted client tests.</summary>
internal static class ClientTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestClientApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}

internal sealed class TestClientApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        base.Initialize();
    }
}

[CollectionDefinition("Match baseline globals", DisableParallelization = true)]
public sealed class MatchBaselineCollection { }
