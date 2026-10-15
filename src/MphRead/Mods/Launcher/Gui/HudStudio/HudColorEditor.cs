using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Validated HSV, RGB, hex, swatch and optional alpha editor. Callbacks are
/// supplied by the owning HUD field at construction time, not looked up from
/// the currently selected inspector after a later controller navigation.
/// </summary>
internal sealed class HudColorEditor : StackPanel
{
    internal static readonly string[] Swatches = {
        "#FFFFFF","#D9D9D9","#999999","#555555","#222222","#000000",
        "#FF0000","#FF6B6B","#FF8800","#FFC857","#FFFF00","#FFF1A8",
        "#00FF00","#80FF80","#00AA55","#00FFFF","#80FFFF","#00AACC",
        "#0088FF","#80BFFF","#0000FF","#8888FF","#8800FF","#C080FF",
        "#FF00FF","#FF80FF","#FF0088","#FF99BB","#56B4E9","#D55E00"
    };
    // Session-only palettes avoid modifying profile state on edit or creating
    // another persistence file. Bounded to avoid accumulating every edit.
    private static readonly List<string> Recents = new();
    private static readonly List<string> Favorites = new();

    private readonly TextBox _hex;
    private readonly Border _sample;
    private readonly Slider _hue, _saturation, _brightness;
    private readonly NumericUpDown _red, _green, _blue;
    private readonly NumericUpDown? _alpha;
    private readonly WrapPanel _recentSwatches = new() { MaxWidth = 260 };
    private readonly WrapPanel _favoriteSwatches = new() { MaxWidth = 260 };
    private readonly TextBlock _message = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Action<string> _changed;
    private readonly Action<float>? _opacityChanged;
    private bool _updating;
    private string _value;
    private float _opacity;

    internal string Hex => _value;
    internal float Opacity => _opacity;

    internal HudColorEditor(string label, string value, Action<string> changed,
        float opacity = 1f, Action<float>? opacityChanged = null)
    {
        Spacing = 6;
        _changed = changed;
        _opacityChanged = opacityChanged;
        _value = TryNormalize(value, out string valid) ? valid : "#FFFFFF";
        _opacity = float.IsFinite(opacity) ? Math.Clamp(opacity, 0f, 1f) : 1f;

        Children.Add(new TextBlock { Text = label + " color" });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _sample = new Border
        {
            Width = 28, Height = 28,
            Background = Brush.Parse(_value),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
        };
        _hex = new TextBox { Text = _value, PlaceholderText = "#RRGGBB", Width = 112 };
        _hex.LostFocus += (_, _) => Apply(_hex.Text);
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Apply(_hex.Text); e.Handled = true; }
        };
        header.Children.Add(_sample);
        header.Children.Add(_hex);
        Children.Add(header);

        _hue = new Slider { Minimum = 0, Maximum = 360, SmallChange = 1, LargeChange = 15 };
        _saturation = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        _brightness = new Slider { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        Children.Add(HsvRow("Hue", _hue));
        Children.Add(HsvRow("Saturation", _saturation));
        Children.Add(HsvRow("Value", _brightness));
        foreach (Slider slider in new[] { _hue, _saturation, _brightness })
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == Slider.ValueProperty && !_updating)
                    Apply(HudColorMath.ToHex(HudColorMath.FromHsv(
                        (float)_hue.Value,
                        (float)_saturation.Value / 100f,
                        (float)_brightness.Value / 100f)));
            };

        var rgbRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _red = RgbField("R", rgbRow);
        _green = RgbField("G", rgbRow);
        _blue = RgbField("B", rgbRow);
        Children.Add(rgbRow);
        foreach (NumericUpDown number in new[] { _red, _green, _blue })
            number.ValueChanged += (_, _) =>
            {
                if (_updating || !_red.Value.HasValue || !_green.Value.HasValue || !_blue.Value.HasValue)
                    return;
                Apply(HudColorMath.ToHex(new HudRgb(
                    (byte)_red.Value.Value, (byte)_green.Value.Value, (byte)_blue.Value.Value)));
            };

        if (opacityChanged != null)
        {
            var alphaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            alphaRow.Children.Add(new TextBlock
            {
                Text = "Alpha", Width = 64, VerticalAlignment = VerticalAlignment.Center
            });
            _alpha = new NumericUpDown
            {
                Minimum = 0, Maximum = 1, Value = (decimal)_opacity,
                Increment = .01m, FormatString = "0.00", Width = 84
            };
            _alpha.ValueChanged += (_, _) =>
            {
                if (!_updating && _alpha.Value is decimal value)
                    ApplyOpacity((float)value);
            };
            alphaRow.Children.Add(_alpha);
            Children.Add(alphaRow);
        }

        var swatches = new WrapPanel { MaxWidth = 260 };
        foreach (string hex in Swatches)
        {
            var button = Swatch(hex);
            button.Click += (_, _) => Apply(hex);
            swatches.Children.Add(button);
        }
        Children.Add(new Expander
        {
            Header = "Color swatches", Content = swatches, IsExpanded = false
        });

        var collections = new StackPanel { Spacing = 6 };
        collections.Children.Add(new TextBlock { Text = "Recent colors" });
        collections.Children.Add(_recentSwatches);
        var favoritesHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        favoritesHeader.Children.Add(new TextBlock
        {
            Text = "Favorites", VerticalAlignment = VerticalAlignment.Center
        });
        favoritesHeader.Children.Add(new PrimeButton("Toggle current favorite", () =>
        {
            if (!Favorites.Remove(_value))
            {
                Favorites.Insert(0, _value);
                if (Favorites.Count > 12) Favorites.RemoveAt(Favorites.Count - 1);
            }
            RefreshCollections();
        }, compact: true));
        collections.Children.Add(favoritesHeader);
        collections.Children.Add(_favoriteSwatches);
        Children.Add(new Expander { Header = "Recent / favorites", Content = collections });
        RefreshCollections();

        var clipboard = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        clipboard.Children.Add(new PrimeButton("Copy", async () =>
        {
            try
            {
#if MPHREAD_SHELL
                WriteSystemClipboard(_value);
                _message.Text = "Color copied.";
#else
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                {
                    await cb.SetTextAsync(_value);
                    _message.Text = "Color copied.";
                }
                else _message.Text = "System clipboard is unavailable.";
#endif
                await System.Threading.Tasks.Task.CompletedTask;
            }
            catch (Exception) { _message.Text = "Clipboard access was refused."; }
        }, compact: true));
        clipboard.Children.Add(new PrimeButton("Paste", async () =>
        {
            try
            {
#if MPHREAD_SHELL
                Apply(ReadSystemClipboard());
#else
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                    Apply(await cb.TryGetTextAsync());
                else _message.Text = "System clipboard is unavailable.";
#endif
                await System.Threading.Tasks.Task.CompletedTask;
            }
            catch (Exception) { _message.Text = "Clipboard access was refused."; }
        }, compact: true));
        Children.Add(clipboard);
        Children.Add(_message);
        SyncInputs();
    }

    private static StackPanel HsvRow(string name, Slider slider)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = name, Width = 76, VerticalAlignment = VerticalAlignment.Center
        });
        slider.Width = 140;
        row.Children.Add(slider);
        return row;
    }

    private static NumericUpDown RgbField(string name, Panel row)
    {
        row.Children.Add(new TextBlock
        {
            Text = name, VerticalAlignment = VerticalAlignment.Center
        });
        var number = new NumericUpDown
        {
            Minimum = 0, Maximum = 255, Value = 0,
            Increment = 1m, FormatString = "0", Width = 52
        };
        row.Children.Add(number);
        return number;
    }

    private static Avalonia.Controls.Button Swatch(string hex)
    {
        var button = new Avalonia.Controls.Button
        {
            Width = 32, Height = 32, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(2), Margin = new Thickness(1),
            Content = new Border
            {
                Background = Brush.Parse(hex), Width = 24, Height = 24,
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(button, "Choose " + hex);
        ToolTip.SetTip(button, hex);
        return button;
    }

    private void RefreshCollections()
    {
        void Refresh(WrapPanel target, List<string> source)
        {
            target.Children.Clear();
            foreach (string color in source)
            {
                var swatch = Swatch(color);
                swatch.Click += (_, _) => Apply(color);
                target.Children.Add(swatch);
            }
        }
        Refresh(_recentSwatches, Recents);
        Refresh(_favoriteSwatches, Favorites);
    }

    private void SyncInputs()
    {
        if (!HudColorMath.TryParseHex(_value, out _, out HudRgb rgb)) return;
        _updating = true;
        try
        {
            HudHsv hsv = HudColorMath.ToHsv(rgb);
            _hue.Value = hsv.Hue;
            _saturation.Value = hsv.Saturation * 100f;
            _brightness.Value = hsv.Value * 100f;
            _red.Value = rgb.R;
            _green.Value = rgb.G;
            _blue.Value = rgb.B;
            if (_alpha != null) _alpha.Value = (decimal)_opacity;
            _hex.Text = _value;
            _sample.Background = Brush.Parse(_value);
            _sample.Opacity = .25 + .75 * _opacity;
        }
        finally { _updating = false; }
    }

#if MPHREAD_SHELL
    private static unsafe string? ReadSystemClipboard()
        => OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetClipboardString(null);
    private static unsafe void WriteSystemClipboard(string text)
        => OpenTK.Windowing.GraphicsLibraryFramework.GLFW.SetClipboardString(null, text);
#endif

    internal static bool TryNormalize(string? text, out string color)
    {
        bool ok = HudColorMath.TryParseHex(text, out color, out _);
        return ok;
    }

    internal bool Apply(string? text)
    {
        if (!TryNormalize(text, out string color))
        {
            _message.Text = "Use #RGB or #RRGGBB. The previous color is unchanged.";
            return false;
        }
        bool changed = color != _value;
        _value = color;
        SyncInputs();
        _message.Text = "";
        if (changed)
        {
            Recents.Remove(color);
            Recents.Insert(0, color);
            if (Recents.Count > 12) Recents.RemoveAt(Recents.Count - 1);
            RefreshCollections();
            _changed(color);
        }
        return true;
    }

    internal bool ApplyOpacity(float opacity)
    {
        if (_opacityChanged == null || !float.IsFinite(opacity)) return false;
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (MathF.Abs(opacity - _opacity) <= .000001f) return true;
        _opacity = opacity;
        SyncInputs();
        _opacityChanged(opacity);
        return true;
    }
}
