#if MPHREAD_AVALONIA
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed class PrimePanel : Border
    {
        public PrimePanel(Control content, bool raised = false)
        {
            Background = raised ? PrimeTheme.PanelRaisedBrush : PrimeTheme.PanelBrush;
            BorderBrush = PrimeTheme.BorderBrush;
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(PrimeMetrics.PanelPadding);
            CornerRadius = new CornerRadius(3);
            Child = content;
        }
    }

    /// <summary>
    /// Artwork-backed presentation surface for top-level destinations.
    /// Art is optional: the gradient and border still produce a finished card
    /// before thumbnails exist on a fresh install.
    /// </summary>
    internal sealed class PrimeHeroPanel : Border
    {
        public Image Art { get; }

        public PrimeHeroPanel(Control content, IImage? art = null, double minHeight = 190)
        {
            MinHeight = minHeight;
            Background = PrimeTheme.BackgroundDeepBrush;
            BorderBrush = PrimeTheme.BorderBrightBrush;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            ClipToBounds = true;

            Art = new Image
            {
                Source = art,
                Stretch = Stretch.UniformToFill,
                Opacity = art == null ? 0 : 0.74,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var shade = new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.7, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(235, PrimeTheme.BackgroundDeep.R,
                            PrimeTheme.BackgroundDeep.G, PrimeTheme.BackgroundDeep.B), 0),
                        new GradientStop(Color.FromArgb(190, PrimeTheme.Background.R,
                            PrimeTheme.Background.G, PrimeTheme.Background.B), 0.52),
                        new GradientStop(Color.FromArgb(74, PrimeTheme.BackgroundDeep.R,
                            PrimeTheme.BackgroundDeep.G, PrimeTheme.BackgroundDeep.B), 1)
                    }
                }
            };

            var lowerShade = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 96,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, PrimeTheme.BackgroundDeep.R,
                            PrimeTheme.BackgroundDeep.G, PrimeTheme.BackgroundDeep.B), 0),
                        new GradientStop(Color.FromArgb(218, PrimeTheme.BackgroundDeep.R,
                            PrimeTheme.BackgroundDeep.G, PrimeTheme.BackgroundDeep.B), 1)
                    }
                }
            };

            var layers = new Grid();
            layers.Children.Add(Art);
            layers.Children.Add(shade);
            layers.Children.Add(lowerShade);
            layers.Children.Add(new Border
            {
                Padding = new Thickness(22, 18),
                Child = content
            });
            Child = layers;
        }

        public void SetArt(IImage? art, double opacity = 0.74)
        {
            Art.Source = art;
            Art.Opacity = art == null ? 0 : opacity;
        }
    }

    internal enum PrimeStateKind
    {
        Neutral,
        Loading,
        Empty,
        Warning,
        Error,
        Success
    }

    /// <summary>
    /// Shared no-data / loading / warning / recovery surface.
    ///
    /// It is deliberately static: the desktop shell rasterizes Avalonia into
    /// an off-screen texture, so a spinner that animates forever would turn an
    /// otherwise idle screen into a permanent full-surface redraw.
    /// </summary>
    internal sealed class PrimeStatePanel : Border
    {
        private readonly TextBlock _eyebrow;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;
        private readonly StackPanel _actions;

        public PrimeStateKind Kind { get; private set; }

        public PrimeStatePanel(PrimeStateKind kind, string title, string detail,
            params Control[] actions)
        {
            Background = PrimeTheme.PanelBrush;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Padding = new Thickness(18, 16);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Center;

            _eyebrow = PrimeChrome.Eyebrow("");
            _title = PrimeChrome.Title(title);
            _detail = PrimeChrome.Text(detail, PrimeTypography.BodySmall,
                PrimeTheme.TextSecondaryBrush);
            _actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };
            foreach (Control action in actions)
                _actions.Children.Add(action);
            _actions.IsVisible = actions.Length > 0;

            Child = PrimeChrome.Stack(_eyebrow, _title, _detail, _actions);
            Set(kind, title, detail);
        }

        public void Set(PrimeStateKind kind, string title, string detail,
            bool visible = true, bool showActions = true)
        {
            Kind = kind;
            IsVisible = visible;
            _title.Text = title;
            _detail.Text = detail;
            _actions.IsVisible = showActions && _actions.Children.Count > 0;

            (string label, IBrush brush) = kind switch
            {
                PrimeStateKind.Loading => ("IN PROGRESS", PrimeTheme.HighlightBrush),
                PrimeStateKind.Empty => ("NOTHING HERE YET", PrimeTheme.TextSecondaryBrush),
                PrimeStateKind.Warning => ("ATTENTION", PrimeTheme.WarningBrush),
                PrimeStateKind.Error => ("ACTION REQUIRED", PrimeTheme.DangerBrush),
                PrimeStateKind.Success => ("READY", PrimeTheme.GreenBrush),
                _ => ("STATUS", PrimeTheme.HighlightBrush)
            };
            _eyebrow.Text = label;
            _eyebrow.Foreground = brush;
            BorderBrush = brush;
        }
    }

    // Keep the tested pointer, touch and controller behavior of the shared buttons.
    internal class PrimeButton : HubNavButton
    {
        public PrimeButton(string text, Action? action = null, bool primary = false,
            bool danger = false, bool compact = false)
            : base(text, primary: primary, compact: true,
                accent: danger ? PrimeTheme.Danger : null)
        {
            UseTacticalStyle(compact: compact);
            if (action != null) Click += (_, _) => action();
        }

        public PrimeButton(string text, string detail, Action? action = null,
            bool primary = false, bool danger = false, bool compact = false)
            : base(text, detail, primary: primary, compact: compact,
                accent: danger ? PrimeTheme.Danger : null)
        {
            UseTacticalStyle(compact: compact);
            if (action != null) Click += (_, _) => action();
        }
    }

    internal sealed class PrimeTabButton : PrimeButton
    {
        public PrimeTabButton(string text, Action action) : base(text, action)
        {
            UseTacticalStyle(tab: true);
        }
    }

    internal sealed class PrimeBadge : Border
    {
        public PrimeBadge(string text, IBrush? color = null)
        {
            Background = PrimeTheme.PanelHighlightBrush;
            Padding = new Thickness(8, 4);
            CornerRadius = new CornerRadius(2);
            Child = PrimeChrome.Text(text, PrimeTypography.DataSmall,
                color ?? PrimeTheme.HighlightBrush, data: true);
        }
    }

    internal sealed class PrimeStatBar : StackPanel
    {
        public PrimeStatBar(string label, double value, double maximum, IBrush? color = null)
        {
            Spacing = 6;
            Children.Add(PrimeChrome.Text(label, PrimeTypography.DataSmall, data: true));
            Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(1, maximum),
                Value = Math.Clamp(value, 0, Math.Max(1, maximum)),
                Height = 6,
                Foreground = color ?? PrimeTheme.PrimaryBrush,
                Background = PrimeTheme.PanelHighlightBrush
            });
        }
    }

    internal static class PrimeChrome
    {
        public static TextBlock Text(string text, double size = PrimeTypography.Body,
            IBrush? color = null, bool data = false) => new()
        {
            Text = text,
            FontSize = size,
            Foreground = color ?? PrimeTheme.TextBrush,
            FontFamily = data ? PrimeTypography.Data : PrimeTypography.Ui,
            TextWrapping = TextWrapping.Wrap
        };

        public static TextBlock Title(string text) => new()
        {
            Text = text,
            FontSize = PrimeTypography.HeadingLarge,
            FontWeight = FontWeight.Bold,
            Foreground = PrimeTheme.TextBrush,
            FontFamily = PrimeTypography.Display,
            TextWrapping = TextWrapping.Wrap
        };

        public static TextBlock HeroTitle(string text) => new()
        {
            Text = text,
            FontSize = PrimeTypography.DisplayLarge,
            FontWeight = FontWeight.Bold,
            Foreground = PrimeTheme.TextBrush,
            FontFamily = PrimeTypography.Display,
            TextWrapping = TextWrapping.Wrap
        };

        public static TextBlock Eyebrow(string text, IBrush? color = null) => new()
        {
            Text = text,
            FontSize = PrimeTypography.DataSmall,
            FontWeight = FontWeight.Bold,
            Foreground = color ?? PrimeTheme.HighlightBrush,
            FontFamily = PrimeTypography.Data,
            TextWrapping = TextWrapping.Wrap
        };

        public static StackPanel Stack(params Control[] children)
        {
            var stack = new StackPanel { Spacing = PrimeMetrics.PanelGap };
            foreach (var child in children) stack.Children.Add(child);
            return stack;
        }

        public static Grid Columns(string widths, params Control[] children)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(widths),
                ColumnSpacing = PrimeMetrics.PanelGap
            };
            for (int i = 0; i < children.Length; i++)
            {
                Grid.SetColumn(children[i], i);
                grid.Children.Add(children[i]);
            }
            return grid;
        }
    }
}
#endif
