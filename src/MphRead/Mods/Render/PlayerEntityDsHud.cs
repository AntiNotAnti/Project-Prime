using System;
using MphRead.Hud;
using MphRead.Mods.Combat;
using MphRead.Mods.Input;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

// Authored around the DS's native 256x192 coordinate system. The new scene
// viewport performs the 4:3 or 5:3 stretch; all touch targets use the same
// native coordinates and stay on the lower screen. No extracted asset imports.
public partial class PlayerEntity
{
    private static readonly Vector4 DsNavy = new(.015f, .035f, .075f, 1);
    private static readonly Vector4 DsPanel = new(.026f, .077f, .138f, 1);
    private static readonly Vector4 DsInk = new(.61f, .86f, .98f, .81f);
    private static readonly Vector4 DsMuted = new(.23f, .47f, .66f, .44f);
    private static readonly ColorRgba DsText = new(215, 235, 250, 255);
    private static readonly ColorRgba DsWarning = new(255, 116, 112, 255);

    private void DsBox(float x, float y, float width, float height, Vector4 color) =>
        _scene.DrawHudFlatBox(x, y, x + width, y + height, color);

    private void DsLine(float x1, float y1, float x2, float y2, Vector4 color, float thick = 1)
    {
        float width = _scene.Size.X / 256f, height = _scene.Size.Y / 192f;
        _scene.DrawFlatLine(x1 / 256f, y1 / 192f, Vector2.Zero,
            new Vector2((x2 - x1) * width, (y1 - y2) * height),
            Math.Max(.7f, thick * height), color);
    }

    private void DsRing(float x, float y, float radius, Vector4 color, float thick = 1)
    {
        float unit = _scene.Size.Y / 192f;
        _scene.DrawFlatRing(x / 256f, y / 192f, Vector2.Zero,
            radius * unit, Math.Max(1f, thick * unit), color);
    }

    private void DsDot(float x, float y, float radius, Vector4 color)
    {
        _scene.DrawFlatDisc(x / 256f, y / 192f, Vector2.Zero,
            radius * _scene.Size.Y / 192f, color);
    }

    private void DsLabel(float x, float y, string value, float scale = .55f,
        ColorRgba? ink = null)
    {
        DrawText2D(x, y, Align.Left, 0, value.AsSpan(), ink ?? DsText, scale: scale);
    }

    private bool DsWeaponIcon(BeamType beam, float centerX, float centerY, float extent)
    {
        int index = (int)beam;
        if ((uint)index >= (uint)_weaponListIcons.Length
            || index > (int)BeamType.OmegaCannon
            || _weaponListIcons[index] is not HudObjectInstance icon)
            return false;
        IconBounds bounds = _weaponListIconBounds[index];
        float scaling = extent / Math.Max(1, Math.Max(bounds.Width, bounds.Height));
        float oldX = icon.PositionX, oldY = icon.PositionY, oldAlpha = icon.Alpha;
        try
        {
            icon.PositionX = (centerX - bounds.CentreX * scaling * HudAspectFix) / 256f;
            icon.PositionY = (centerY - bounds.CentreY * scaling) / 192f;
            icon.Alpha = Features.HudOpacity;
            _scene.DrawHudObject(icon, mode: 1, scale: scaling);
            return true;
        }
        finally
        {
            icon.PositionX = oldX; icon.PositionY = oldY; icon.Alpha = oldAlpha;
        }
    }

    internal void ModDrawDsTopScreen()
    {
        if (!IsMainPlayer || _scene.GameState.MenuPause) return;

        // Confirmed semantic UI keeps its normal authoritative source. Only
        // weapon selection/radar are relocated to the lower LCD.
        ModDrawChat();
        ModDrawKillFeed();
        ModDrawVote();
        ModDrawEndScreen();
        ModDrawPostMatchReport();
        if (ShowScoreboard)
        {
            DrawMatchTime();
            DrawScoreboard();
            return;
        }
        if (_scene.GameState.MatchState != MatchState.InProgress) return;

        DsBox(14, 10, 228, 1, DsMuted);
        DsLine(14, 12, 14, 24, DsInk, .7f);
        DsLine(242, 12, 242, 24, DsInk, .7f);
        DsLabel(18, 13, "PROJECT PRIME", .46f);

        if (ModHudHealthVisible)
        {
            int hp = ModHudHealth;
            float fraction = Math.Clamp(hp / (float)Math.Max(1, HealthMax), 0f, 1f);
            float blink = DsLayout.HealthBlink(hp, _scene.GlobalElapsedTime);
            Vector4 health = hp <= 99
                ? new Vector4(1f, .36f, .34f, .92f * blink) : DsInk;
            DsBox(74, 14, 99, 4, new Vector4(.13f, .20f, .28f, .76f));
            if (fraction > 0) DsBox(74, 14, 99 * fraction, 4, health);
            DsLabel(175, 11, hp.ToString(), .65f, hp <= 99 ? DsWarning : DsText);
        }

        if (!IsAltForm && !IsMorphing && !IsUnmorphing)
        {
            WeaponInfo info = EquipInfo.Weapon;
            int cost = ModAmmoCostForWeapon(CurrentWeapon, info);
            int amount = ModAmmoForWeapon(CurrentWeapon, info);
            int shots = cost > 0 && amount >= 0 ? amount / cost : -1;
            DsWeaponIcon(CurrentWeapon, 216, 160, 25);
            DsLabel(188, 171, shots < 0 ? "--" : shots.ToString(), .8f);
            DsLine(189, 183, 244, 183, DsInk, .75f);
        }

        if (Mods.RenderOptions.ShowFps) DrawFps();
        if (Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen) || ScanVisor) return;

        Vector2 reticle = GetReticlePresentationPosition();
        var profile = HudProfiles.Runtime.CrosshairFor((int)CurrentWeapon, _hudZoom);
        bool native = HudProfiles.Runtime.Mode == HudMode.Custom && profile.Native;
        if (Features.CustomCrosshair && !native)
            _scene.DrawCustomCrosshair(GetCrosshairColor(), reticle.X, reticle.Y,
                (int)CurrentWeapon, _hudZoom);
        else
        {
            _targetCircleInst.PositionX = reticle.X;
            _targetCircleInst.PositionY = reticle.Y;
            _targetCircleInst.Alpha = Features.ReticleOpacity * (native ? profile.Opacity : 1);
            _scene.DrawHudObject(_targetCircleInst,
                scale: native ? profile.NativeScale : Crosshair.Scale);
        }

        // Only authoritative hit-confirmation is allowed to light this marker.
        float confirmation = Mods.Network.NetHitPrediction.MarkerEnabled
            ? Mods.Network.NetHitPrediction.MarkerAlpha : 0;
        if (confirmation > 0)
            _scene.DrawHitMarker(new Vector4(1f, 1f, 1f, confirmation),
                reticle.X, reticle.Y);
    }

    internal void ModDrawDsBottomScreen()
    {
        if (!IsMainPlayer || _scene.GameState.MenuPause) return;
        DsBox(0, 0, 256, 192, DsNavy);
        DsBox(7, 7, 242, 178, DsPanel);
        DsBox(11, 10, 234, 1, DsMuted);
        DsBox(11, 182, 234, 1, DsMuted);

        // Fine, low-opacity rails give depth without v19's unverified bitmap
        // assets and without another GL texture/decode per picture.
        for (int i = 0; i < 7; i++)
        {
            float y = 24 + i * 20;
            DsLine(18, y, 238, y, new Vector4(.24f, .47f, .65f, .065f), .4f);
        }
        DsLine(18, 18, 18, 174, DsMuted);
        DsLine(238, 18, 238, 174, DsMuted);
        DsLabel(20, 157, "TACTICAL LINK", .53f);
        DsDrawRadar();

        foreach (StylusZone.Button original in StylusZone.Buttons)
        {
            StylusZone.Button b = StylusZone.EffectiveButton(original);
            float halfWidth = b.Round ? b.Width / 2f / DsLayout.HorizontalStretch : b.Width / 2f;
            float halfHeight = b.Height / 2f;
            bool active = StylusZone.Contact && StylusZone.Region == b.Region;
            Vector4 accent = active ? new Vector4(.92f, .95f, 1, 1) : DsInk;
            if (b.Round)
            {
                DsRing(b.X, b.Y, halfHeight, accent, .9f);
                DsDot(b.X, b.Y, halfHeight * .78f,
                    new Vector4(.065f, .20f, .32f, active ? .8f : .28f));
            }
            else
            {
                DsBox(b.X - halfWidth, b.Y - halfHeight, halfWidth * 2, halfHeight * 2,
                    new Vector4(.08f, .19f, .31f, active ? .72f : .4f));
                DsLine(b.X - halfWidth, b.Y - halfHeight,
                    b.X + halfWidth, b.Y - halfHeight, accent, .9f);
                DsLine(b.X - halfWidth, b.Y + halfHeight,
                    b.X + halfWidth, b.Y + halfHeight, accent, .9f);
            }
            if (b.Region == StylusRegion.PowerBeam)
                DsWeaponIcon(BeamType.PowerBeam, b.X, b.Y, 17);
            else if (b.Region == StylusRegion.Missile)
                DsWeaponIcon(BeamType.Missile, b.X, b.Y, 17);
            else if (b.Region == StylusRegion.Weapons)
            {
                BeamType selected = CurrentWeapon is BeamType.PowerBeam or BeamType.Missile
                    ? _weaponSlots[2] : CurrentWeapon;
                if (selected != BeamType.None) DsWeaponIcon(selected, b.X, b.Y, 19);
            }
            else if (b.Region == StylusRegion.WeaponSelect)
                DsLabel(b.X - 12, b.Y - 2, "WPN", .5f);
            else if (b.Region == StylusRegion.AltForm)
                DsLabel(b.X - 10, b.Y - 2, "ALT", .5f);
        }

        if (Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen))
        {
            float scale = ModPlaceWeaponSelect();
            for (int i = 0; i < 6; i++)
            {
                _scene.DrawHudObject(_selectBoxInsts[i], mode: 1, scale: scale);
                _scene.DrawHudObject(_weaponSelectInsts[i], mode: 1, scale: scale);
            }
        }
        // A moving pen cursor is always rendered on its owning LCD.
        if (!Mods.PauseMenu.Open) DrawStylusCursor();
    }

    private void DsDrawRadar()
    {
        if (!Radar.Enabled || _scene.GameState.MatchState != MatchState.InProgress
            || _scene.CameraSequences.Current?.IsIntro == true) return;

        float scale = DsLayout.RadarSize, radius = 22 * scale;
        Vector2 center = DsLayout.RadarCenter / 3f;
        DsRing(center.X, center.Y, radius, DsInk, .65f);
        DsRing(center.X, center.Y, radius * .64f, DsMuted, .55f);
        DsRing(center.X, center.Y, radius * .3f, DsMuted, .45f);
        DsLine(center.X - radius / DsLayout.HorizontalStretch, center.Y,
            center.X + radius / DsLayout.HorizontalStretch, center.Y, DsMuted, .55f);
        DsLine(center.X, center.Y - radius, center.X, center.Y + radius, DsMuted, .55f);
        DsDot(center.X, center.Y, 2.3f, DsInk);
        var style = HudProfiles.Runtime.Radar;
        var basis = HudRadarProjection.BuildBasis(RadarVector(CameraInfo.Facing),
            style.Orientation);
        var origin = RadarVector(RadarPresentedPosition());
        float radiusPixels = radius * _scene.Size.Y / 192f;
        foreach (var contact in CollectRadarContacts(style))
        {
            if (!HudRadarProjection.Project(contact, origin, basis, style,
                    radiusPixels, out var point)) continue;
            float x = center.X + point.Position.X * 256f / _scene.Size.X;
            float y = center.Y - point.Position.Y * 192f / _scene.Size.Y;
            var color = contact.Color;
            DsDot(x, y, Math.Clamp(1.75f * scale, 1f, 4f),
                new Vector4(color.X, color.Y, color.Z, color.W * contact.Alpha));
        }
    }
}
