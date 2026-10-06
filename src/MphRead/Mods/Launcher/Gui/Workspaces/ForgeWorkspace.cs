#if MPHREAD_AVALONIA
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>The game owns this launch surface; the desktop Studio owns map authoring.</summary>
internal sealed class ForgeWorkspace : UserControl
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Func<string?, bool, string?> _launch;
    private bool _browsing;
    public event EventHandler? Closed;

    internal ForgeWorkspace(Func<string?, bool, string?>? launch = null)
    {
        _launch = launch ?? LaunchStudio;
        var body = PrimeChrome.Stack(
            PrimeChrome.Title("FORGE"),
            PrimeChrome.Text("Create and edit maps in Project Prime Studio. Keep Project Prime open for local playtests and Community publishing."));
#if MPHREAD_SHELL && !ANDROID
        Add("OPEN MAP STUDIO", "forge.open", () => Open(null, false), primary: true);
        Add("OPEN MAP PROJECT", "forge.project", () => _ = BrowseAsync());
        Add("RESTORE STUDIO SESSION", "forge.recover", () => Open(null, true));
#else
        body.Children.Add(PrimeChrome.Text("Map authoring is available in the desktop release."));
#endif
        body.Children.Add(_status);
        Add("BACK", "forge.back", () => Closed?.Invoke(this, EventArgs.Empty));
        Content = new PrimePanel(body);
        void Add(string label, string id, Action action, bool primary = false)
        {
            var button = new PrimeButton(label, action, primary: primary);
            ControllerNav.Identify(button, id, initial: primary);
            body.Children.Add(button);
        }
    }

    private static string? LaunchStudio(string? path, bool recover)
    {
#if MPHREAD_SHELL && !ANDROID
        return StudioIntegration.StudioApplicationLauncher.TryOpen(path, recover, out string? error)
            ? null : error ?? "Project Prime Studio could not start.";
#else
        return "Install the desktop release to use Project Prime Studio.";
#endif
    }

    internal void Open(string? path, bool recover)
    {
        string? error = _launch(path, recover);
        _status.Text = error ?? "Opening Project Prime Studio";
        _status.Foreground = error == null ? PrimeTheme.TextSecondaryBrush : Brushes.Orange;
    }

    private async Task BrowseAsync()
    {
        if (_browsing) return;
        _browsing = true;
        try
        {
#if MPHREAD_SHELL && !ANDROID
            if (!NativeFilePicker.Available)
            {
                _status.Text = "Open Map Studio and use File → Open Map Project to choose a map.";
                return;
            }
            string? path = await NativeFilePicker.OpenFile("Open map project", "Project Prime map project", "json");
            if (path != null) Open(path, false);
#endif
        }
        catch (Exception error) { _status.Text = error.Message; _status.Foreground = Brushes.Orange; }
        finally { _browsing = false; }
    }
}
#endif
