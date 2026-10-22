using System;
using MphRead.Hud;
using MphRead.Mods;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        /// <summary>Make the native winner camera visibly distinct from the
        /// normal Game Over frame. Draws on the gameplay HUD, so offline bot
        /// matches show this even when no network replay is available.</summary>
        private void ModDrawFinalCinematicHud()
        {
            SceneGameState game = _scene.GameState;
            if (!game.Multiplayer || game.MatchState != MatchState.GameOver) return;

            float elapsed = Math.Clamp(SceneGameState.MatchFinalCameraSeconds - game.MatchTime,
                0f, SceneGameState.MatchFinalCameraSeconds);
            float progress = elapsed / SceneGameState.MatchFinalCameraSeconds;
            float appear = Math.Clamp(elapsed / 0.3f, 0f, 1f);
            float fadeOut = Math.Clamp((SceneGameState.MatchFinalCameraSeconds - elapsed) / 0.3f, 0f, 1f);
            float alpha = MathF.Min(appear, fadeOut);
            Vector4 frame = new(0.025f, 0.045f, 0.075f, 0.84f * alpha);
            Vector4 accent = new(0.32f, 0.85f, 1f, 0.86f * alpha);
            _scene.DrawHudFlatBox(0, 0, 256, 14, frame);
            _scene.DrawHudFlatBox(0, 165, 256, 192, frame);
            _scene.DrawHudFlatBox(10, 14, 10 + 236f * progress, 14.9f, accent);
            _scene.DrawHudFlatBox(20, 164, 236, 164.6f, accent);
            DrawText2D(16, 4, Align.Left, 0, "PROJECT PRIME", _killFeedInk, scale: 0.48f);
            DrawText2D(240, 4, Align.Right, 0, "FINAL CINEMATIC", _killFeedSpecial, scale: 0.42f);

            bool tie = game.IsResultTie;
            int winnerSlot = game.ResultSlots.Length > 0 ? game.ResultSlots[0] : -1;
            bool hasWinner = !tie && winnerSlot >= 0 && winnerSlot < game.Nicknames.Length
                && winnerSlot < _scene.Players.Items.Count
                && _scene.Players.Items[winnerSlot] != null;
            string title = tie ? "MATCH DRAW" : hasWinner ? "MATCH WINNER" : "MATCH COMPLETE";
            DrawText2D(16, 169, Align.Left, 0, title, _killFeedSpecial, scale: 0.48f);
            if (hasWinner)
            {
                DrawPlayerName(16, 177.5f, Align.Left, 0,
                    game.Nicknames[winnerSlot], _killFeedInk, scale: 0.67f, maxWidth: 190);
            }
            else
            {
                DrawText2D(16, 177.5f, Align.Left, 0,
                    "FINAL STANDINGS NEXT", _killFeedInk, scale: 0.52f);
            }
            DrawText2D(241, 179, Align.Right, 0,
                "SCOREBOARD NEXT", _killFeedSpecial, scale: 0.32f);
        }

        /// <summary>
        /// Full post-match table, styled like the live kill feed: no opaque
        /// slab, thin separators, team-coloured names, and a narrow gold rail
        /// on the local player's row.
        /// </summary>
        private void ModDrawPostMatchReport()
        {
            if (!_scene.GameState.Multiplayer
                || _scene.GameState.MatchState != MatchState.Ending
                || _scene.GameState.MatchTime < 0)
            {
                return;
            }

            bool networked = NetSession.Active;
            int count = networked
                ? NetSession.PostMatchReportCount
                : Math.Clamp(_scene.GameState.ActivePlayers, 0, PlayerEntity.SlotCapacity);

            // The modal results/map panel no longer occupies the right side.
            // Keep the authoritative table fully visible at every HUD aspect.
            float aspect = HudAspectFix;
            const float left = 4f;
            const float right = 252f;
            float width = right - left;

            const float titleY = 20f;
            const float headerY = 34f;
            const float firstRowY = 44f;
            const float rowHeight = 15.6f;
            const float contentHeight = 13.5f;

            DrawText2D(left + 1.8f * aspect, titleY, Align.Left, 0,
                (NetSession.MatchContainsBots ? "PRACTICE - BOTS USED" : "POST-MATCH REPORT"), _killFeedInk, scale: 0.48f);
            DrawText2D(right - 1.2f * aspect, titleY + 0.5f, Align.Right, 0,
                count > 0 ? $"{count} PLAYER{(count == 1 ? "" : "S")}" : "RESULTS",
                _killFeedSpecial, scale: 0.34f);

            if (NetSession.MatchContainsBots)
                DrawText2D(left + 1.8f * aspect, titleY + 5f, Align.Left, 0,
                    "Hunter License progression disabled", _killFeedSpecial, scale: 0.23f);
            Vector4 titleRule = _killFeedRule;
            titleRule.W *= 1.15f;
            _scene.DrawHudFlatBox(left, titleY + 8.2f, right, titleY + 8.55f, titleRule);

            if (networked && count == 0)
            {
                DrawText2D(left + width / 2f, 82f, Align.Center, 0,
                    "SYNCING RESULTS...", _killFeedSpecial, scale: 0.43f);
                ModDrawPostMatchIntroShutter();
                return;
            }

            float kdX = left + width * 0.52f;
            float accX = left + width * 0.67f;
            float dmgX = left + width * 0.86f;
            DrawText2D(left + 7f * aspect, headerY, Align.Left, 0,
                "PLAYER", _killFeedSpecial, scale: 0.31f);
            DrawText2D(kdX, headerY, Align.Center, 0,
                "K/D", _killFeedSpecial, scale: 0.31f);
            DrawText2D(accX, headerY, Align.Center, 0,
                "ACC", _killFeedSpecial, scale: 0.31f);
            DrawText2D(dmgX, headerY, Align.Center, 0,
                "DAMAGE DEALT", _killFeedSpecial, scale: 0.31f);

            int localSlot = networked && NetSession.LocalSlot >= 0
                ? NetSession.LocalSlot
                : _scene.Players.MainPlayerIndex;
            int rows = Math.Min(count, PlayerEntity.SlotCapacity);
            SceneGameState state = _scene.GameState;

            for (int i = 0; i < rows; i++)
            {
                int slot;
                int team;
                string name;
                if (networked)
                {
                    slot = NetSession.PostMatchReportSlot(i);
                    team = NetSession.PostMatchReportTeam(i);
                    name = NetSession.PostMatchReportName(i);
                }
                else
                {
                    slot = state.ResultSlots[i];
                    if ((uint)slot >= PlayerEntity.SlotCapacity)
                    {
                        continue;
                    }
                    PlayerEntity player = _scene.Players.Items[slot];
                    team = player.TeamIndex;
                    name = state.Nicknames[slot];
                }
                if ((uint)slot >= PlayerEntity.SlotCapacity)
                {
                    continue;
                }

                float y = firstRowY + i * rowHeight;
                bool local = slot == localSlot;

                Vector4 rule = _killFeedRule;
                rule.W *= local ? 1.45f : 1f;
                if (i == 0)
                {
                    Vector4 topRule = _killFeedRule;
                    topRule.W *= 0.75f;
                    _scene.DrawHudFlatBox(left, y - 1f, right, y - 0.7f, topRule);
                }
                _scene.DrawHudFlatBox(left, y + contentHeight, right,
                    y + contentHeight + 0.34f, rule);
                if (local)
                {
                    _scene.DrawHudFlatBox(left, y + 0.25f,
                        left + 0.9f * aspect, y + contentHeight - 0.35f,
                        _killFeedLocalRule);
                }

                ColorRgba nameColor = state.Teams && team >= 0
                    ? TeamVisuals.Get(team).Color
                    : local ? _killFeedLocal : _killFeedInk;

                DrawText2D(left + 2f * aspect, y + 0.8f, Align.Left, 0,
                    $"#{i + 1}", _killFeedSpecial, scale: 0.31f);
                DrawPlayerName(left + 8.2f * aspect, y + 0.35f, Align.Left, 0,
                    name, nameColor, scale: 0.43f,
                    maxWidth: Math.Max(12, (kdX - left) / aspect - 23.2f));

                int shots = Math.Max(0, state.ShotsFired[slot]);
                int hits = Math.Min(shots, Math.Max(0, state.ShotsHit[slot]));
                int accuracy = shots == 0 ? 0
                    : (int)Math.Round(hits * 100.0 / shots);

                DrawText2D(kdX, y + 0.55f, Align.Center, 0,
                    $"{state.Kills[slot]}/{state.Deaths[slot]}",
                    _killFeedInk, scale: 0.40f);
                DrawText2D(accX, y + 0.55f, Align.Center, 0,
                    $"{accuracy}%", _killFeedInk, scale: 0.40f);
                DrawText2D(dmgX, y + 0.55f, Align.Center, 0,
                    state.MatchDamageDealt[slot].ToString(),
                    _killFeedInk, scale: 0.40f);

                string objective = Mods.Multiplayer.MatchObjectiveReport.Text(state, slot);
                DrawText2D(right - 1.2f * aspect, y + 7.15f, Align.Right, 0,
                    objective.Length > 0 ? objective : $"DAMAGE TAKEN {state.MatchDamageTaken[slot]}   HS {state.HeadshotKills[slot]}   BEST {state.LongestKillStreak[slot]}",
                    _killFeedSpecial, scale: 0.29f);
            }
            ModDrawPostMatchIntroShutter();
        }

        /// <summary>
        /// The six-second scoreboard has finished, but the shared server can
        /// still be completing its replay allowance or map handoff. Display
        /// a minimal native transition status instead of extending the table
        /// beyond the player's requested results window.
        /// </summary>
        private void ModDrawPostMatchHandoff()
        {
            string action = NetSession.PersistentLobby ? "RETURNING TO LOBBY"
                : NetSession.Active ? "PREPARING NEXT ARENA"
                : "RETURNING TO MATCH MENU";
            DrawText2D(128, 85, Align.Center, 0, action, _killFeedInk, scale: 0.65f);
            DrawText2D(128, 99, Align.Center, 0,
                NetSession.Active ? "SESSION CONNECTED" : "MATCH COMPLETE",
                _killFeedSpecial, scale: 0.37f);
        }

        /// <summary>Fade out of the final replay's quiet slate shutter into
        /// the native results. This never prolongs the six-second scoreboard
        /// or changes its authoritative statistics.</summary>
        private void ModDrawPostMatchIntroShutter()
        {
            float remaining = _scene.GameState.MatchTime;
            // Harness screenshots may deliberately extend the results clock.
            // Do not paint an eternal opaque slate over their native table.
            if (remaining < 0 || remaining > SceneGameState.MatchEndingSeconds) return;
            float elapsed = SceneGameState.MatchEndingSeconds - remaining;
            float fade = Mods.Launcher.LauncherPrefs.ReduceMotion ? 0.08f : 0.17f;
            float cover = 1f - Math.Clamp(elapsed / fade, 0f, 1f);
            if (cover > 0)
                _scene.DrawHudFlatBox(0, 0, 256, 192,
                    new Vector4(0.025f, 0.045f, 0.075f, 0.92f * cover));
        }

    }
}
