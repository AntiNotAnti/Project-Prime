using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Oddball;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private static ColorRgba OddInk
    {
        get { var c=HudProfiles.Runtime.AccentInk; return new ColorRgba((byte)(c.X*255),(byte)(c.Y*255),(byte)(c.Z*255),255); }
    }
    private static int OddLayout(string id) => Array.IndexOf(HudProfileDefaults.ElementIds, id);
    internal string ModOddballRespawnText => Health == 0 && _respawnTimer > 0
        ? $"RESPAWN IN {(_respawnTimer + 59) / 60}" : "";

    private string OddBinding(bool fire)
    {
        var control = fire ? Controls.Shoot : Controls.Zoom;
        string key = control.Type switch
        {
            ButtonType.Key => control.Key.ToString(),
            ButtonType.Mouse => "Mouse " + control.MouseButton,
            ButtonType.ScrollUp => "Wheel up", ButtonType.ScrollDown => "Wheel down", _ => fire ? "Fire" : "Zoom / ADS"
        };
        var pad = PadBindings.Get(fire ? PadAction.Shoot : PadAction.Zoom);
        return pad == GamepadButtons.None ? key : key + " / " + PadBindings.Describe(pad);
    }

    private PlayerEntity? OddCarrier()
    {
        foreach (var flag in _scene.GetOctolithFlagEntities()) return flag.Carrier;
        return null;
    }
    private string OddStatus(int team = -1)
    {
        var match = _scene.GameState.Oddball;
        if (match == null) return "Unavailable";
        if (match.Winner >= 0) return "Match over";
        if (match.PauseTicks > 0) return "Round over";
        if (match.SpawnTicks > 0) return $"Spawns {(match.SpawnTicks + 59) / 60}s";
        var carrier = OddCarrier();
        if (carrier != null)
            return team < 0 ? (carrier.TeamIndex == 0 ? "Red team" : "Blue team")
                : carrier.TeamIndex == team ? "Held" : "Enemy";
        return "Loose";
    }
    private string OddAction()
    {
        if (Health <= 0 || !ModIsInPlay || IsAltForm || IsMorphing || IsUnmorphing
            || _scene.GameState.Oddball?.CanPickUp != true) return "";
        if (ModOddballCarrying) return $"[{OddBinding(false)}] DROP  |  [{OddBinding(true)}] MELEE";
        foreach (var flag in _scene.GetOctolithFlagEntities())
        {
            if (flag.Carrier != null || (flag.Position - Position.AddY(1)).LengthSquared > 2.1f * 2.1f) continue;
            CollisionResult wall = default;
            if (!CollisionDetection.CheckBetweenPoints(Position.AddY(1), flag.Position, TestFlags.None, _scene, ref wall))
                return $"[{OddBinding(false)}] PICK UP BALL";
        }
        return "";
    }
    private void ModDrawOddballHud()
    {
        var match = _scene.GameState.Oddball;
        if (match == null || ShowScoreboard || _scene.GameState.MatchState != MatchState.InProgress) return;
        using (UseHudLayout(5, 4, 12))
            DrawText2D(4, 12, Align.Left, 0, $"RED {match.Score(0)}   BLUE {match.Score(1)}   / {OddballMatch.Goal}", color: OddInk, scale: .58f);
        using (UseHudLayout(OddLayout("match.oddballStatus"), 4, 24))
        {
            DrawText2D(4, 24, Align.Left, 0, $"BALL: {OddStatus().ToUpperInvariant()}", color: OddInk, scale: .48f);
            DrawText2D(4, 32, Align.Left, 0, $"ROUNDS {match.RoundsWon[0]} - {match.RoundsWon[1]}   |   ROUND {match.RoundNumber}", color: OddInk, scale: .48f);
            if (match.PauseTicks > 0)
                DrawText2D(4, 40, Align.Left, 0, $"NEXT ROUND IN {(match.PauseTicks + 59) / 60}", color: OddInk, scale: .48f);
        }
        string respawn = ModOddballRespawnText;
        if (respawn.Length > 0)
            using (UseHudLayout(OddLayout("match.respawn"), 128, 150))
                DrawText2D(128, 150, Align.Center, 0, respawn, color: OddInk, scale: .7f);
        string action = OddAction();
        if (action.Length > 0)
            using (UseHudLayout(OddLayout("match.oddballAction"), 128, 176))
                DrawText2D(128, 176, Align.Center, 0, action, color: OddInk, scale: .5f);
        if (match.EventTicks > 0 && match.EventText.Length > 0)
            using (UseHudLayout(OddLayout("match.oddballEvents"), 128, 52))
                DrawText2D(128, 52, Align.Center, 0, match.EventText, color: OddInk, scale: .6f);
    }

    private void ModDrawOddballScoreboard()
    {
        ModDrawUnifiedScoreboard();
    }
}
