using System;
using System.Text;
using MphRead.Hud;
using MphRead.Mods.Chat;
using MphRead.Mods.Network;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

/// <summary>Private replay presentation; all identity and health comes from the replica.</summary>
internal sealed class KillcamHud
{
    private readonly Scene _scene;
    private readonly HudObjectInstance _font;
    private readonly HudObjectInstance? _playerNameFont;
#if MPHREAD_AVALONIA
    private readonly KillcamText? _text;
#endif
    private static readonly ColorRgba White = new(239, 247, 255, 255);
    private static readonly ColorRgba Muted = new(157, 179, 199, 255);
    internal KillcamHud(Scene scene)
    {
        _scene = scene; _font = new(ChatFont.Cell, ChatFont.Cell);
        _font.SetPaletteData(new ColorRgba[] { new(), new(255, 255, 255, 255) }, scene);
        _font.SetCharacterData(ChatFont.Pixels, scene); _font.Enabled = true;
        if (Font.Normal.CharacterData != null && Font.Normal.Widths != null && Font.Normal.Offsets != null)
        {
            _playerNameFont = new HudObjectInstance(width: 8, height: 8);
            var palette = new ColorRgba[16];
            for (int i = 1; i < palette.Length; i++) palette[i] = new ColorRgba(255, 255, 255, 255);
            _playerNameFont.SetPaletteData(palette, scene);
            _playerNameFont.SetCharacterData(Font.Normal.CharacterData, scene);
            _playerNameFont.Enabled = true;
        }
#if MPHREAD_AVALONIA
        try { _text = new KillcamText(scene); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Console.WriteLine("[killcam] Smooth text unavailable: " + ex.Message); }
#endif
    }
    internal void Draw(bool final, string killer, string victim, string weapon, byte weaponId,
        bool headshot, int health, float progress, float killProgress, float secondsToKill,
        float impactStrength, int finalFrame, int finalLength, bool reduceMotion)
    {
        FinalImpactStyle style = FinalKillPresentation.Impact(weaponId);
        Vector4 accent = final ? ImpactAccent(style) : new(.28f, .84f, 1, 1);
        if (final && impactStrength > 0 && !reduceMotion)
            DrawFinalImpact(style, headshot, impactStrength, accent);
        Card(10, 10, 84, 29, accent);
        Box(16, 17, 18, 19, accent);
        Text(22, 12, final ? "FINAL ELIMINATION" : "ELIMINATION REPLAY", .62f, White, 81);
        Text(22, 22, "RECORDED PERSPECTIVE", .43f, Muted, 81);
        Card(212, 10, 246, 26, accent);
        Text(218, 13, secondsToKill > 0 ? $"-{secondsToKill:0.0}s" : $"+{-secondsToKill:0.0}s", .85f, White, 243);

        Card(10, 148, 170, 181, accent);
        Card(174, 148, 246, 181, accent);
        Text(17, 151, final ? "FINAL KILL" : "KILLED BY", .53f, Muted, 164);
        PlayerNameText(17, 159, killer, 1.12f, White, 164);
        Text(17, 173, weapon + (headshot ? "  /  HEADSHOT" : ""), .57f, White, 164);
        Text(181, 152, "ELIMINATED", .48f, Muted, 240);
        PlayerNameText(181, 160, victim, .8f, White, 240);
        Text(181, 173, $"ATTACKER HP  {health}", .43f, Muted, 240);
        Box(10, 183, 246, 184, new(.55f, .72f, .85f, .22f));
        Box(10, 183, 10 + 236 * Math.Clamp(progress, 0, 1), 184, accent);
        float killX = 10 + 236 * Math.Clamp(killProgress, 0, 1);
        Box(killX - .45f, 181.5f, killX + .45f, 185, new(1, 1, 1, .9f));
        Text(10, 186, "REPLAY", .38f, Muted, 55);
        Text(174, 186, "FIRE / BACK TO SKIP", .43f, White, 246);
        if (final)
        {
            // A short slate shutter hides a distant historical camera cut,
            // and hands a quiet, dark frame back to the native scoreboard.
            // It is draw-only: never changes scene lighting or replay timing.
            float cover = FinalKillPresentation.TransitionCover(finalFrame, finalLength);
            if (cover > 0)
                Box(0, 0, 256, 192, new(.025f, .045f, .075f, .92f * cover));
        }
    }

    private static Vector4 ImpactAccent(FinalImpactStyle style) => style switch
    {
        FinalImpactStyle.Precision => new(.46f, .92f, 1f, 1f),
        FinalImpactStyle.Electrical => new(.66f, .57f, 1f, 1f),
        FinalImpactStyle.Explosive => new(1f, .69f, .30f, 1f),
        FinalImpactStyle.Cryo => new(.44f, .92f, .94f, 1f),
        FinalImpactStyle.Thermal => new(1f, .46f, .28f, 1f),
        _ => new(1f, .76f, .37f, 1f)
    };

    private void DrawFinalImpact(FinalImpactStyle style, bool headshot, float strength, Vector4 ink)
    {
        float alpha = Math.Clamp(strength, 0f, 1f) * .27f;
        Vector4 color = new(ink.X, ink.Y, ink.Z, alpha);
        // HUD-space geometry is inexpensive, deterministic and cannot be
        // mistaken for an actual server projectile or collision response.
        switch (style)
        {
            case FinalImpactStyle.Precision:
                Box(94, 93.5f, 122, 94.15f, color);
                Box(134, 93.5f, 162, 94.15f, color);
                Box(127.65f, 73, 128.35f, 88, color);
                Box(127.65f, 100, 128.35f, 115, color);
                Box(102, 66, 103, 76, color);
                Box(153, 112, 154, 122, color);
                break;
            case FinalImpactStyle.Electrical:
                for (int i = 0; i < 4; i++)
                {
                    float y = 67 + i * 14;
                    Box(43 + i * 8, y, 47 + i * 8, y + 1.5f, color);
                    Box(199 - i * 8, 154 - y, 204 - i * 8, 155.5f - y, color);
                }
                break;
            case FinalImpactStyle.Explosive:
                for (int i = 0; i < 3; i++)
                {
                    float radius = 12 + i * 13 + (1f - strength) * 11f;
                    Outline(128 - radius, 94 - radius, 128 + radius, 94 + radius,
                        new(ink.X, ink.Y, ink.Z, alpha / (i + 1)));
                }
                break;
            case FinalImpactStyle.Cryo:
                for (int i = 0; i < 4; i++)
                {
                    float inset = i * 7f;
                    Box(91 + inset, 68 + inset, 94 + inset, 80 + inset, color);
                    Box(162 - inset, 108 - inset, 165 - inset, 120 - inset, color);
                }
                break;
            case FinalImpactStyle.Thermal:
                for (int i = 0; i < 3; i++)
                {
                    float inset = i * 7f;
                    Box(65 + inset, 132 - inset, 191 - inset, 134 - inset,
                        new(ink.X, ink.Y, ink.Z, alpha / (i + 1)));
                }
                break;
            default:
                Outline(99 - 10 * (1 - strength), 67 - 10 * (1 - strength),
                    157 + 10 * (1 - strength), 121 + 10 * (1 - strength), color);
                break;
        }
        if (headshot)
        {
            Vector4 white = new(.87f, .97f, 1f, alpha * .55f);
            Box(126, 91, 130, 93, white);
            Box(127.4f, 88, 128.6f, 96, white);
        }
    }

    private void Outline(float x1, float y1, float x2, float y2, Vector4 color)
    {
        Box(x1, y1, x2, y1 + .8f, color);
        Box(x1, y2 - .8f, x2, y2, color);
        Box(x1, y1, x1 + .8f, y2, color);
        Box(x2 - .8f, y1, x2, y2, color);
    }

    private void Card(float left, float top, float right, float bottom, Vector4 accent)
    {
        // Soft slate glass with cut corners and a fine highlight, never an opaque black block.
        const int bands = 12;
        float height = (bottom - top) / bands;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            float inset = i is 0 or bands - 1 ? 1.4f : 0;
            Box(left + inset, top + i * height, right - inset, top + (i + 1) * height,
                new(.08f + t * .025f, .15f + t * .03f, .22f + t * .035f, .88f - t * .16f));
        }
        Box(left + 2, top, right - 2, top + .35f, new(accent.X, accent.Y, accent.Z, .5f));
        Box(left, top + 3, left + .65f, bottom - 3, new(accent.X, accent.Y, accent.Z, .65f));
    }
    private void Box(float x, float y, float right, float bottom, Vector4 color)
        => _scene.DrawHudFlatBox(x, y, right, bottom, color);
    private void PlayerNameText(float x, float y, string text, float scale, ColorRgba color, float right)
    {
        Font font = Font.Normal;
        string name = PlayerNameCodec.Clamp(text);
        if (_playerNameFont == null || font.CharacterData == null || font.Widths == null || font.Offsets == null)
        {
            Text(x, y, name, scale, color, right);
            return;
        }

        // Match the smooth killcam type's roughly 12-unit cap height while using
        // the exact native glyph atlas that defines the accepted player-name repertoire.
        float nativeScale = scale * 1.5f;
        _playerNameFont.Alpha = 1;
        foreach (var rune in name.EnumerateRunes())
        {
            if (!PlayerNameCodec.TryMapUnicodeToGlyph(rune, out ushort code)) continue;
            int index = code - font.MinCharacter;
            if (index < 0 || index >= font.Widths.Count || index >= font.Offsets.Count
                || (index + 1) * 64 > font.CharacterData.Count)
            {
                continue;
            }
            float width = font.Widths[index] * nativeScale;
            if (x + width > right) break;
            if (code != ' ')
            {
                _playerNameFont.PositionX = x / 256f;
                _playerNameFont.PositionY = (y - 1 + font.Offsets[index] * nativeScale) / 192f;
                _playerNameFont.SetData(index, color, _scene);
                _scene.DrawHudObject(_playerNameFont, mode: 1, scale: nativeScale);
            }
            x += width;
        }
    }

    private void Text(float x, float y, string text, float scale, ColorRgba color, float right)
    {
#if MPHREAD_AVALONIA
        if (_text != null) { _text.Draw(x, y - 1, text, scale, color.Equals(Muted) ? .72f : 1, right); return; }
#endif
        _font.Alpha = 1;
        foreach (char ch in text)
        {
            int glyph = ChatFont.Index(ch);
            if (glyph < 0) continue;
            float width = ChatFont.Widths[glyph] * scale;
            if (x + width > right) break;
            _font.PositionX = x / 256; _font.PositionY = y / 192;
            _font.SetData(glyph, color, _scene);
            _scene.DrawHudObject(_font, mode: 1, scale: scale);
            x += width;
        }
    }
}
