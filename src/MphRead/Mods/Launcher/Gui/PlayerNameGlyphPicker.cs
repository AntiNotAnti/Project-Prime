#if MPHREAD_AVALONIA
using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using MphRead.Mods.Network;
using MphRead.Text;

namespace MphRead.Mods.Launcher.Gui;

internal static class PlayerNameGlyphPicker
{
    public static Control Create(TextBox box)
    {
        var panel = new StackPanel { Spacing = 3 };
        var status = new TextBlock { FontSize = 11 };
        var button = new Avalonia.Controls.Button { Content = "GLYPHS", HorizontalAlignment = HorizontalAlignment.Left };
        var categories = new StackPanel { Spacing = 6, Width = 360 };
        var popup = new Popup { PlacementTarget = button, IsLightDismissEnabled = true,
            Child = new Border { Background = GuiTheme.PanelBrush, Padding = new Thickness(8),
                Child = new ScrollViewer { MaxHeight = 300, Content = categories } } };
        string[] labels = { "ASCII", "LATIN & SYMBOLS", "HIRAGANA", "KATAKANA", "JP / MISC" };
        string[] chars = Enumerable.Range(32, 95).Select(i => ((char)i).ToString())
            .Concat(MphGlyphMap.Extended.Where(c => c.Length == 1 && c != " ")).Distinct().ToArray();
        int Category(string c) => c[0] < 127 ? 0 : c[0] is >= '\u3041' and <= '\u3096' ? 2
            : c[0] is >= '\u30a1' and <= '\u30ff' ? 3 : c[0] < '\u3000' ? 1 : 4;
        for (int category = 0; category < labels.Length; category++)
        {
            categories.Children.Add(new TextBlock { Text = labels[category] });
            var row = new WrapPanel();
            foreach (string character in chars.Where(c => Category(c) == category))
            {
                var glyph = new Avalonia.Controls.Button { Content = character, MinWidth = 32, MinHeight = 32,
                    Padding = new Thickness(4) };
                glyph.Click += (_, _) =>
                {
                    string text = box.Text ?? "";
                    int start = Math.Min(box.SelectionStart, box.SelectionEnd);
                    int end = Math.Max(box.SelectionStart, box.SelectionEnd);
                    string candidate = text[..start] + character + text[end..];
                    if (PlayerNameCodec.ValidationError(candidate) != null) return;
                    box.Text = candidate;
                    box.CaretIndex = start + character.Length;
                    box.SelectionStart = box.SelectionEnd = box.CaretIndex;
                };
                row.Children.Add(glyph);
            }
            categories.Children.Add(row);
        }
        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        void Update()
        {
            string text = box.Text ?? "";
            status.Text = PlayerNameCodec.ValidationError(text) ?? $"{PlayerNameCodec.GlyphCount(PlayerNameCodec.Normalize(text))} / {PlayerNameCodec.MaxGlyphs}";
        }
        box.TextChanged += (_, _) => Update();
        Update();
        panel.Children.Add(status);
        panel.Children.Add(button);
        panel.Children.Add(popup);
        panel.Children.Add(PlayerNameNativePreview.Create(box));
        return panel;
    }
}
#endif
