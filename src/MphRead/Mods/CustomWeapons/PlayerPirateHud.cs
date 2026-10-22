using System;
using System.Globalization;
using MphRead.Mods;
using MphRead.Hud;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private bool ModDrawPirateAmmo(bool classic)
    {
        if (!ModPirateSelected) return false;
        if (IsAltForm || IsMorphing || IsUnmorphing) return true;
        var state = _scene.PirateBlaster!.Snapshot;
        float aspect = HudAspectFix;
        float left = classic ? Math.Clamp(_hudObjects.AmmoBarPosX + _objShiftX, 2 * aspect, Math.Max(2 * aspect, 256 - 72 * aspect)) : 256 - 72 * aspect;
        float top = classic ? Math.Clamp(_hudObjects.AmmoBarPosY + _objShiftY, 1, 170) : 170;
        using var layout = UseHudLayout(2, left, top);
        var style = HudProfiles.Runtime.Ammo;
        bool profile = HudProfiles.Runtime.Mode == HudMode.Custom;
        if (!profile || style.Background) _scene.DrawHudFlatBox(left, top, left + 70 * aspect, top + 20, new(0, 0, 0, .55f * Features.HudOpacity));
        var ink = new ColorRgba(255, 160, 80, 255);
        Span<char> buffer = stackalloc char[40];
        if ((!profile || style.Number) && buffer.TryWrite(CultureInfo.InvariantCulture, $"UA {state.Ua:00}   5 / BURST", out int written))
            DrawText2D(left + 68 * aspect, top + 1, Align.Right, 0, buffer[..written], ink, alpha: Features.HudOpacity, scale: .55f);
        if (!profile || style.Icon)
            DrawText2D(left + 2 * aspect, top + 10, Align.Left, 0, state.Marks > 0 ? "BLASTER / MARKED" : state.Tails ? "BLASTER / BURST" : state.Cooldown > 0 ? "BLASTER / WAIT" : "PIRATE BLASTER", ink, alpha: Features.HudOpacity, scale: .4f);
        if (!profile || style.Gauge)
            ProBar(left + 2 * aspect, top + 17, 66 * (profile ? style.GaugeScale : 1), profile ? style.GaugeThickness : 2,
                1 - state.Cooldown / 40f, new(1, .5f, .2f, Features.HudOpacity), profile && style.Vertical);
        return true;
    }
}
