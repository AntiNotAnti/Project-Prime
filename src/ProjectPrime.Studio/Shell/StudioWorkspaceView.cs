using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ProjectPrime.Studio.Shell;

internal static class StudioWorkspaceView
{
    internal static Control Create(IStudioDocument document, string heading, string description)
    {
        StackPanel body = new() { Spacing = 20, MaxWidth = 670, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(35) };
        body.Children.Add(new TextBlock { Text = "PROJECT PRIME STUDIO", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#79C9EF")) });
        body.Children.Add(new TextBlock { Text = heading, FontSize = 30, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 });
        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#213044")), CornerRadius = new CornerRadius(6), Padding = new Thickness(18),
            Child = new TextBlock
            {
                Text = document.Path is null ? "Open a source file from the File menu to inspect its location, size and SHA-256 identity. The existing creator tools remain available in Project Prime during migration."
                    : "Source inspection only. This tab does not parse, edit, build or play this file. Its bytes remain owned by the canonical map and replay implementations.",
                TextWrapping = TextWrapping.Wrap, LineHeight = 22
            }
        });
        if (document.Path is { } path)
            body.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brushes.LightGray });
        return new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }
}
