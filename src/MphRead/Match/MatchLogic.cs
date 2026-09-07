using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Sound;

namespace MphRead
{
    /// <summary>Mode rules and standings for one scene. Comparison ordering and
    /// legacy tie behavior are preserved from the characterized baseline.</summary>
    public sealed class MatchLogic
    {
        private readonly Scene _scene;
        private readonly MatchRuntime _match;

        internal MatchLogic(Scene scene, MatchRuntime match)
        {
            _scene = scene;
            _match = match;
        }

        public void ProcessMode()
        {
            switch (_match.Rules.Mode)
            {
                case MatchMode.Battle:
                case MatchMode.TeamBattle: ModeStateBattle(); break;
                case MatchMode.Survival:
                case MatchMode.TeamSurvival: ModeStateSurvival(); break;
                case MatchMode.Capture: ModeStateCapture(); break;
                case MatchMode.Bounty:
                case MatchMode.TeamBounty: ModeStateBounty(); break;
                case MatchMode.Nodes:
                case MatchMode.TeamNodes: ModeStateNodes(); break;
                case MatchMode.Defender:
                case MatchMode.TeamDefender: ModeStateDefender(); break;
                case MatchMode.PrimeHunter: ModeStatePrimeHunter(); break;
                default: throw new InvalidOperationException("Unknown multiplayer mode.");
            }
        }

        private void EndIfPointGoalReached()
        {
            // Connected, the machine that keeps the score is the only one
            // allowed to decide the score has been reached; everybody else
            // learns it from the server. See NetMatchEnd.MayEndOnScore.
            if (_match.Rules.LegacyPointGoal <= 0 || !Mods.Network.NetMatchEnd.MayEndOnScore)
            {
                return;
            }
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.LoadFlags.TestFlag(LoadFlags.Active) && _match.TeamPoints[player.TeamIndex] >= _match.Rules.LegacyPointGoal)
                {
                    // deal with multiple nodes points on the same frame
                    _match.TeamPoints[player.TeamIndex] = _match.Rules.LegacyPointGoal;
                    _match.PendingEndReason = MatchEndReason.ScoreGoal;
                    _match.MatchTime = 0;
                    break;
                }
            }
        }

        public void ModeStateBattle()
        {
            EndIfPointGoalReached();
        }

        public void ModeStateSurvival()
        {
            _match.RadarPlayers = false;
            int playersAlive = 0;
            int botsAlive = 0;
            bool[] teamsAlive = new bool[2];
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.LoadFlags.TestFlag(LoadFlags.Active)
                    && (player.Health > 0 || _match.TeamDeaths[player.TeamIndex] <= _match.Rules.LegacyPointGoal))
                {
                    _match.Time[i] += _scene.FrameTime;
                    if (player.IsBot)
                    {
                        botsAlive++;
                    }
                    else
                    {
                        playersAlive++;
                    }
                    if (_match.Rules.Teams)
                    {
                        Debug.Assert(player.TeamIndex == 0 || player.TeamIndex == 1);
                        teamsAlive[player.TeamIndex] = true;
                    }
                }
            }
            if ((!_scene.IsHeadless && playersAlive == 0) || playersAlive + botsAlive < 2
                || _match.Rules.Teams && (!teamsAlive[0] || !teamsAlive[1]))
            {
                _match.PendingEndReason = MatchEndReason.Survival;
                _match.MatchTime = 0;
                for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                {
                    PlayerEntity player = PlayerEntity.Players[i];
                    if (player.LoadFlags.TestFlag(LoadFlags.Active)
                        && (player.Health > 0 || _match.TeamDeaths[player.TeamIndex] <= _match.Rules.LegacyPointGoal))
                    {
                        _match.Time[i] = -1; // MAX
                    }
                }
            }
            else if (playersAlive + botsAlive == 2 && PlayerEntity.PlayerCount > 2)
            {
                _match.RadarPlayers = true;
            }
        }

        public void ModeStateCapture()
        {
            EndIfPointGoalReached();
        }

        public void ModeStateBounty()
        {
            EndIfPointGoalReached();
        }

        public void ModeStateDefender()
        {
            if (!Mods.Network.NetMatchEnd.MayEndOnScore)
            {
                return;
            }
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.LoadFlags.TestFlag(LoadFlags.Active) && _match.TeamTime[player.TeamIndex] >= _match.Rules.LegacyTimeGoal)
                {
                    _match.PendingEndReason = MatchEndReason.ObjectiveTimeGoal;
                    _match.MatchTime = 0;
                    break;
                }
            }
        }

        public void ModeStateNodes()
        {
            EndIfPointGoalReached();
        }

        public void ModeStatePrimeHunter()
        {
            if (_match.PrimeHunter == -1)
            {
                return;
            }
            PlayerEntity player = PlayerEntity.Players[_match.PrimeHunter];
            if (!player.LoadFlags.TestFlag(LoadFlags.Active))
            {
                _match.PrimeHunter = -1;
                return;
            }
            if (_scene.FrameCount % (10 * 2) == 0) // todo: FPS stuff
            {
                player.TakeDamage(1, DamageFlags.NoDmgInvuln, direction: null, source: null);
            }
            if (_match.PrimeHunter != -1)
            {
                _match.Time[_match.PrimeHunter] += _scene.FrameTime;
                if (_match.Time[_match.PrimeHunter] >= _match.Rules.LegacyTimeGoal)
                {
                    _match.PendingEndReason = MatchEndReason.ObjectiveTimeGoal;
                    _match.MatchTime = 0;
                }
            }
        }

        public void UpdateState()
        {
            if (PlayerEntity.PlayerCount == 0)
            {
                return;
            }
            IReadOnlyList<PlayerEntity> players = PlayerEntity.Players;
            int[] prevTeamPoints = new int[PlayerEntity.SlotCapacity];
            int[] prevTeamDeaths = new int[PlayerEntity.SlotCapacity];
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                prevTeamPoints[i] = _match.TeamPoints[i];
                prevTeamDeaths[i] = _match.TeamDeaths[i];
                _match.TeamPoints[i] = 0;
                _match.TeamDeaths[i] = 0;
                _match.TeamKills[i] = 0;
                if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
                {
                    _match.TeamTime[i] = 0;
                }
            }
            for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
            {
                PlayerEntity player = players[i];
                if (!player.LoadFlags.TestFlag(LoadFlags.Initial) || player.TeamIndex == -1)
                {
                    continue;
                }
                _match.TeamPoints[player.TeamIndex] += _match.Points[i];
                _match.TeamDeaths[player.TeamIndex] += _match.Deaths[i];
                _match.TeamKills[player.TeamIndex] += _match.Kills[i];
                if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
                {
                    if (_match.TeamTime[player.TeamIndex] < _match.Time[i])
                    {
                        _match.TeamTime[player.TeamIndex] = _match.Time[i];
                    }
                }
                else if (_match.Rules.Mode == MatchMode.Defender || _match.Rules.Mode == MatchMode.TeamDefender)
                {
                    _match.Time[i] = _match.TeamTime[player.TeamIndex];
                }
            }
            if (_match.Rules.Mode == MatchMode.Battle || _match.Rules.Mode == MatchMode.TeamBattle || _match.Rules.Mode == MatchMode.Capture || _match.Rules.Mode == MatchMode.Bounty
                || _match.Rules.Mode == MatchMode.TeamBounty || _match.Rules.Mode == MatchMode.Nodes || _match.Rules.Mode == MatchMode.TeamNodes)
            {
                int teamPoints = _match.TeamPoints[PlayerEntity.Main.TeamIndex];
                if (teamPoints != prevTeamPoints[PlayerEntity.Main.TeamIndex] && teamPoints == _match.Rules.LegacyPointGoal - 1)
                {
                    Sfx.QueueStream(VoiceId.VOICE_ONE_KILL_TO_WIN, delay: 1);
                }
            }
            else if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
            {
                int opponents = 0;
                int lastTeam = -1;
                for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                {
                    PlayerEntity player = players[i];
                    if (!player.LoadFlags.TestAny(LoadFlags.Active))
                    {
                        continue;
                    }
                    if (player.Health > 0 || _match.TeamDeaths[player.TeamIndex] <= _match.Rules.LegacyPointGoal)
                    {
                        if (player.TeamIndex != PlayerEntity.Main.TeamIndex)
                        {
                            opponents++;
                            lastTeam = player.TeamIndex;
                        }
                    }
                    if (_match.TeamDeaths[player.TeamIndex] > _match.Rules.LegacyPointGoal && player.RespawnTimer == PlayerEntity.RespawnTime)
                    {
                        Sfx.QueueStream(VoiceId.VOICE_ELIMINATED);
                    }
                }
                if (PlayerEntity.Main.LoadFlags.TestAny(LoadFlags.Active) && opponents == 1 && lastTeam != -1)
                {
                    int teamDeaths = _match.TeamDeaths[lastTeam];
                    if (teamDeaths != prevTeamDeaths[lastTeam] && teamDeaths == _match.Rules.LegacyPointGoal)
                    {
                        Sfx.QueueStream(VoiceId.VOICE_ONE_KILL_TO_WIN, delay: 1);
                    }
                }
            }
            _match.ActivePlayers = 0;
            if (_match.Rules.Teams)
            {
                int a = 0;
                for (int t = 0; t < 2; t++)
                {
                    for (int p = 0; p < PlayerEntity.SlotCapacity; p++)
                    {
                        PlayerEntity player = players[p];
                        if (player.TeamIndex == t)
                        {
                            _match.Standings[p] = PlayerEntity.SlotCapacity - 1;
                            if (player.LoadFlags.TestFlag(LoadFlags.Active))
                            {
                                _match.ResultSlots[a++] = p;
                                _match.ActivePlayers++;
                            }
                        }
                    }
                }
            }
            else
            {
                int a = 0;
                for (int p = 0; p < PlayerEntity.SlotCapacity; p++)
                {
                    _match.Standings[p] = PlayerEntity.SlotCapacity - 1;
                    if (players[p].LoadFlags.TestFlag(LoadFlags.Active))
                    {
                        _match.ResultSlots[a++] = p;
                        _match.ActivePlayers++;
                    }
                }
            }
            for (int index = 0; index < _match.ActivePlayers; index++)
            {
                for (int nextIndex = index + 1; nextIndex < _match.ActivePlayers; nextIndex++)
                {
                    int slot = _match.ResultSlots[index];
                    int nextSlot = _match.ResultSlots[nextIndex];
                    int teamIndex = players[slot].TeamIndex;
                    int nextTeamIndex = players[nextSlot].TeamIndex;
                    // the game passes team_ids[wslot/nslot] instead of the player fields to CompareTeams
                    if (_match.Rules.Teams && teamIndex != nextTeamIndex && CompareTeams(teamIndex, nextTeamIndex) < 0
                        || ComparePlayers(slot, nextSlot) < 0)
                    {
                        _match.ResultSlots[index] = nextSlot;
                        _match.ResultSlots[nextIndex] = slot;
                    }
                }
            }
            if (_match.Rules.Teams)
            {
                int v47 = 0;
                int v48 = CompareTeams(0, 1);
                int[] v57 = new int[2];
                if (v48 <= 0)
                {
                    v57[0] = v48 != 0 ? 1 : 0;
                    v57[1] = 0;
                }
                else
                {
                    v57[0] = 0;
                    v57[1] = 1;
                }
                for (int i = 0; i < _match.ActivePlayers - 1; i++)
                {
                    int slot = _match.ResultSlots[i];
                    int nextSlot = _match.ResultSlots[i + 1];
                    int teamIndex = players[slot].TeamIndex;
                    _match.Standings[slot] = v57[teamIndex];
                    _match.TeamStandings[slot] = v47;
                    if (teamIndex != players[nextSlot].TeamIndex)
                    {
                        if (ComparePlayers(slot, nextSlot) != 0)
                        {
                            v47++;
                        }
                    }
                    else
                    {
                        v47 = 0;
                    }
                }
                int index = _match.ActivePlayers - 1;
                _match.Standings[index] = v57[players[_match.ResultSlots[index]].TeamIndex];
                _match.TeamStandings[index] = v47;
            }
            else
            {
                int index;
                int v47 = 0;
                for (index = 0; index < _match.ActivePlayers - 1; index++)
                {
                    int slot = _match.ResultSlots[index];
                    _match.Standings[slot] = v47;
                    if (ComparePlayers(slot, _match.ResultSlots[index + 1]) != 0)
                    {
                        v47 = index + 1;
                    }
                }
                _match.Standings[_match.ResultSlots[index]] = v47;
            }
            // todo: update license info
        }

        public int ComparePlayers(int slot1, int slot2)
        {
            int points1 = _match.Points[slot1];
            int points2 = _match.Points[slot2];
            float time1 = _match.Time[slot1];
            float time2 = _match.Time[slot2];
            if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
            {
                if (time1 == -1)
                {
                    time1 = Single.MaxValue;
                }
                if (time2 == -1)
                {
                    time2 = Single.MaxValue;
                }
            }
            int deaths1 = _match.Deaths[slot1];
            int deaths2 = _match.Deaths[slot2];
            int kills1 = _match.Kills[slot1];
            int kills2 = _match.Kills[slot2];
            if (_match.Rules.Mode == MatchMode.Battle || _match.Rules.Mode == MatchMode.TeamBattle)
            {
                if (points1 == points2 && deaths1 == deaths2)
                {
                    return 0;
                }
                if (points1 < points2 || points1 == points2 && deaths1 > deaths2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
            {
                if (time1 == time2 && deaths1 == deaths2)
                {
                    return 0;
                }
                if (time1 < time2 || time1 == time2 && deaths1 > deaths2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.Defender || _match.Rules.Mode == MatchMode.TeamDefender)
            {
                if (time1 == time2 && kills1 == kills2)
                {
                    return 0;
                }
                if (time1 < time2 || time1 == time2 && kills1 < kills2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.Capture || _match.Rules.Mode == MatchMode.Nodes || _match.Rules.Mode == MatchMode.TeamNodes
                || _match.Rules.Mode == MatchMode.Bounty || _match.Rules.Mode == MatchMode.TeamBounty)
            {
                if (points1 == points2 && kills1 == kills2)
                {
                    return 0;
                }
                if (points1 < points2 || points1 == points2 && kills1 < kills2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.PrimeHunter)
            {
                if (time1 == time2 && kills1 == kills2)
                {
                    return 0;
                }
                if (time1 < time2 || time1 == time2 && kills1 < kills2)
                {
                    return -1;
                }
                return 1;
            }
            return 0;
        }

        public int CompareTeams(int slot1, int slot2)
        {
            int points1 = _match.TeamPoints[slot1];
            int points2 = _match.TeamPoints[slot2];
            float time1 = _match.TeamTime[slot1];
            float time2 = _match.TeamTime[slot2];
            if (_match.Rules.Mode == MatchMode.Survival || _match.Rules.Mode == MatchMode.TeamSurvival)
            {
                if (time1 == -1)
                {
                    time1 = Single.MaxValue;
                }
                if (time2 == -1)
                {
                    time2 = Single.MaxValue;
                }
            }
            int deaths1 = _match.TeamDeaths[slot1];
            int deaths2 = _match.TeamDeaths[slot2];
            int kills1 = _match.TeamKills[slot1];
            int kills2 = _match.TeamKills[slot2];
            if (_match.Rules.Mode == MatchMode.TeamBattle)
            {
                if (points1 == points2 && deaths1 == deaths2)
                {
                    return 0;
                }
                if (points1 < points2 || points1 == points2 && deaths1 > deaths2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.TeamSurvival)
            {
                if (time1 == time2 && deaths1 == deaths2)
                {
                    return 0;
                }
                if (time1 < time2 || time1 == time2 && deaths1 > deaths2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.TeamDefender)
            {
                if (time1 == time2 && kills1 == kills2)
                {
                    return 0;
                }
                if (time1 < time2 || time1 == time2 && kills1 < kills2)
                {
                    return -1;
                }
                return 1;
            }
            if (_match.Rules.Mode == MatchMode.Capture || _match.Rules.Mode == MatchMode.TeamNodes || _match.Rules.Mode == MatchMode.TeamBattle)
            {
                if (points1 == points2 && kills1 == kills2)
                {
                    return 0;
                }
                if (points1 < points2 || points1 == points2 && kills1 < kills2)
                {
                    return -1;
                }
                return 1;
            }
            return 0;
        }
    }
}
