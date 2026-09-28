#if MPHREAD_AVALONIA
using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead.Mods.Network;
using MphRead.Text;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Draw the same decoded 8x8 glyph tiles and advances as the in-game HUD.</summary>
internal sealed class PlayerNameNativePreview : Control
{
    private readonly TextBox _box;
    internal PlayerNameNativePreview(TextBox box)
    {
        _box = box;
        Height = 28;
        MinWidth = 180;
        ClipToBounds = true;
        box.TextChanged += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Font font = Font.Normal;
        if (font.CharacterData == null || font.Widths == null) return;
        double x = 0;
        double scale = Math.Min(2, Bounds.Width / Math.Max(1, MphRead.Mods.Render.PlayerNameLayout.Measure(PlayerNameCodec.Clamp(_box.Text))));
        foreach (var rune in PlayerNameCodec.Clamp(_box.Text).EnumerateRunes())
        {
            if (!PlayerNameCodec.TryMapUnicodeToGlyph(rune, out ushort code)) continue;
            int index = code - font.MinCharacter;
            if (index < 0 || index >= font.Widths.Count || index >= font.Offsets.Count || (index + 1) * 64 > font.CharacterData.Count) continue;
            if (code != 32)
                for (int y = 0; y < 8; y++)
                    for (int column = 0; column < 8; column++)
                        if (font.CharacterData[index * 64 + y * 8 + column] != 0)
                            context.DrawRectangle(GuiTheme.TextBrush, null,
                                new Rect(x + column * scale, (y + font.Offsets[index] + 2) * scale, scale, scale));
            x += font.Widths[index] * scale;
        }
    }

    internal static Control Create(TextBox box)
    {
        var panel = new StackPanel();
        try
        {
            if (Font.Normal.CharacterData == null && GameFiles.Ready) Extract.LoadRuntimeData();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // The editor remains usable without extracted font data.
        }
        panel.Children.Add(new TextBlock { FontSize = 10, Text = Font.Normal.CharacterData == null
            ? "IN-GAME PREVIEW: configure game files to load the native font" : "IN-GAME PREVIEW" });
        if (Font.Normal.CharacterData != null) panel.Children.Add(new PlayerNameNativePreview(box));
        return panel;
    }
}
#endif
