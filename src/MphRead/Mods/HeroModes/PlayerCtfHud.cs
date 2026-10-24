using System;
using MphRead.Hud;
using MphRead.Mods.HeroModes;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Hud;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Presentation state only. Timers and scores are always read from current simulation state.
    private string? _ctfEventText;
    private ulong _ctfEventUntil;
    internal void ModSetCtfEvent(string text)
    {
        _ctfEventText = text;
        _ctfEventUntil = _scene.FrameCount + 150;
    }

    internal string ModCtfRespawnText => Health == 0 && _respawnTimer > 0
        ? $"RESPAWN IN {(_respawnTimer + 59) / 60}" : "";

    private string CtfFlagStatus(int team)
    {
        foreach (var flag in _scene.GetOctolithFlagEntities())
        {
            if (flag.Data.TeamId != team) continue;
            if (flag.Carrier != null) return "CARRIED";
            if (flag.AtBase) return "HOME";
            // Do not display an unsynchronized client prediction of a server return timer.
            if (!NetObjectiveSync.IsClient(_scene) && flag.CtfState.ReturnProgress > 0)
                return $"RETURNING {Math.Clamp((int)(flag.CtfState.ReturnProgress / CtfRules.ReturnSeconds * 100), 0, 100)}%";
            return "DROPPED";
        }
        return "UNAVAILABLE";
    }

    private string CtfActionText()
    {
        if (Health <= 0 || !ModIsInPlay || IsAltForm || IsMorphing || IsUnmorphing) return "";
        if (ModCarryingCtfFlag) return $"[{ModCtfBindingLabel()}] THROW FLAG  |  SPEED +20%";
        foreach (var flag in _scene.GetOctolithFlagEntities())
            if (flag.CanPickupCtf(this)) return $"[{ModCtfBindingLabel()}] PICK UP FLAG";
        return "";
    }

    private void ModDrawCtfHud()
    {
        // Exactly one draw per value, no per-tick message queues. Every element is
        // a normal HUD-profile element with its own visibility, font, position and color.
        using (UseHudLayout(5, 0, 0))
            DrawText2D(0, 0, Align.Left, 0, $"RED {_scene.GameState.TeamPoints[0]}   BLUE {_scene.GameState.TeamPoints[1]}   / {_scene.GameState.PointGoal}", scale: .58f);
        using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds,"match.ctfFlags"), 0, 0))
        {
            DrawText2D(0, 0, Align.Left, 0, $"RED FLAG  {CtfFlagStatus(0)}", scale: .48f);
            DrawText2D(0, 8, Align.Left, 0, $"BLUE FLAG  {CtfFlagStatus(1)}", scale: .48f);
        }
        string respawn = ModCtfRespawnText;
        if (respawn.Length != 0)
            using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds,"match.respawn"), 128, 150)) DrawText2D(128, 150, Align.Center, 0, respawn, scale: .7f);
        string action = CtfActionText();
        if (action.Length != 0)
            using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds,"match.flagAction"), 128, 176)) DrawText2D(128, 176, Align.Center, 0, action, scale: .5f);
        if (_ctfEventText != null && _scene.FrameCount < _ctfEventUntil)
            using (UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds,"match.ctfEvents"), 128, 52)) DrawText2D(128, 52, Align.Center, 0, _ctfEventText, scale: .6f);
    }

    private void ModDrawCtfScoreboard()
    {
        using var layout = UseHudLayout(Array.IndexOf(HudProfileDefaults.ElementIds,"match.ctfScoreboard"), 0, 0);
        // The scoreboard owns its objective header and countdown. The gameplay
        // overlay is not drawn underneath, including when a dead player holds Tab.
        _scene.DrawHudFlatBox(5, 3, 251, 187, new Vector4(.025f, .045f, .07f, .93f));
        DrawText2D(128, 7, Align.Center, 0, "CAPTURE THE FLAG", scale: .85f);
        string time = _scene.GameState.MatchTime < 0 ? "NO TIME LIMIT"
            : FormatTime(TimeSpan.FromSeconds(Math.Max(0, _scene.GameState.MatchTime)));
        DrawText2D(128, 20, Align.Center, 0, $"TIME {time}   |   FIRST TO {_scene.GameState.PointGoal}", scale: .55f);
        DrawText2D(12, 32, Align.Left, 0, "HUNTER / PLAYER", scale: .48f);
        DrawText2D(162, 32, Align.Center, 0, "CAP", scale: .48f);
        DrawText2D(188, 32, Align.Center, 0, "KILLS", scale: .48f);
        DrawText2D(214, 32, Align.Center, 0, "DEATHS", scale: .48f);
        DrawText2D(239, 32, Align.Center, 0, "PING", scale: .48f);
        float y = 45;
        for (int team = 0; team < 2; team++)
        {
            string label = team == 0 ? "RED" : "BLUE";
            _scene.DrawHudFlatBox(9, y - 2, 247, y + 8, new Vector4(.14f, .19f, .24f, .55f));
            DrawText2D(12, y, Align.Left, 0, $"{label}  {_scene.GameState.TeamPoints[team]} / {_scene.GameState.PointGoal}", scale: .65f);
            // Keep flag status in the team header, immediately left of CAP.
            // Numeric columns and the profile's typography/color stay unchanged.
            string status = CtfFlagStatus(team);
            string flagLabel = $"Flag: {status[0]}{status[1..].ToLowerInvariant()}";
            DrawText2D(150, y, Align.Right, 0, flagLabel, scale: .5f);
            y += 12;
            for (int i = 0; i < _scene.GameState.ActivePlayers; i++)
            {
                int slot = _scene.GameState.ResultSlots[i];
                var player = _scene.Players.Items[slot];
                if (player.TeamIndex != team) continue;
                string name = (player.IsMainPlayer ? "> " : "") + _scene.GameState.Nicknames[slot];
                // Names cannot invade the numeric columns. Native and custom fonts
                // share the existing profile scale; limit pathological nicknames too.
                if (name.Length > 24) name = name[..23] + "~";
                DrawText2D(12, y, Align.Left, 0, name, scale: .57f);
                DrawText2D(162, y, Align.Center, 0, _scene.GameState.OctolithScores[slot].ToString(), scale: .58f);
                DrawText2D(188, y, Align.Center, 0, _scene.GameState.Kills[slot].ToString(), scale: .58f);
                DrawText2D(214, y, Align.Center, 0, _scene.GameState.Deaths[slot].ToString(), scale: .58f);
                int ping = NetSession.Active && slot < NetSession.SlotPing.Length ? NetSession.SlotPing[slot] : 0;
                DrawText2D(239, y, Align.Center, 0, ping > 0 ? Math.Min(ping, 999).ToString() : "--", scale: .5f);
                y += 11;
            }
            y += 5;
        }
        string footer = ModCtfRespawnText;
        if (footer.Length == 0) footer = "Own flag must be home to capture";
        DrawText2D(128, 177, Align.Center, 0, footer, scale: .55f);
    }
}
