using System;
using System.Linq;
using System.Buffers.Binary;
using MphRead.Mods.Multiplayer;
using MphRead.Entities;
using MphRead.Mods.Chat;

namespace MphRead.Mods.Network;

/// <summary>Asset-free lifecycle contracts. Objective interactions require the scene checks.</summary>
public static class GameModeCheck
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(description);
    }

    public static int Run()
    {
        try
        {
            _checks = 0;
            GameMode[] modes = { GameMode.Battle, GameMode.BattleTeams, GameMode.Survival,
                GameMode.SurvivalTeams, GameMode.Capture, GameMode.Bounty, GameMode.BountyTeams,
                GameMode.Nodes, GameMode.NodesTeams, GameMode.Defender, GameMode.DefenderTeams,
                GameMode.PrimeHunter, GameMode.Relic, GameMode.Hardpoint, GameMode.HardpointTeams, GameMode.GunGame, GameMode.KillConfirmed, GameMode.KillConfirmedTeams, GameMode.Headhunter, GameMode.OneInTheChamber };
            Check(MatchTypeCatalog.GameTypes.All(type => !type.Label.Contains("teams", StringComparison.OrdinalIgnoreCase)),
                "player-facing game types do not duplicate team variants");
            MatchTypeDefinition battleType = MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.Battle)];
            Check(battleType.Resolve(MatchFormat.FreeForAll) == GameMode.Battle
                && battleType.Resolve(MatchFormat.Auto) == GameMode.BattleTeams,
                "Battle resolves FFA and Teams through matchup");
            MatchTypeDefinition nodesType = MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.NodesTeams)];
            Check(nodesType.Resolve(MatchFormat.TwoVsTwo) == GameMode.NodesTeams, "Nodes team matchup resolves legacy internal mode");
            MatchTypeDefinition confirmedType = MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.KillConfirmed)];
            Check(confirmedType.Resolve(MatchFormat.FreeForAll) == GameMode.KillConfirmed
                && confirmedType.Resolve(MatchFormat.Auto) == GameMode.KillConfirmedTeams,
                "Kill Confirmed supports both FFA and Teams");
            MatchTypeDefinition captureType = MatchTypeCatalog.GameTypes[MatchTypeCatalog.BaseModeIndex(GameMode.Capture)];
            Check(MatchTypeCatalog.NormalizeFormat(captureType, MatchFormat.FreeForAll) == MatchFormat.Auto,
                "Capture coerces invalid FFA matchup to Teams");
            NetChat.Clear();
            NetChat.Remember(new ChatPacket { Kind = ChatPacket.KindSystem, Name = "TRACE", Text = "joined" });
            Check(NetChat.History[^1] == "TRACE joined", "named system chat retains player name");
            NetChat.Clear();
            Check(HardpointRules.Next(new[] { 9, 1, 5 }, null, -1) == 1, "Hardpoint starts at smallest entity ID");
            Check(HardpointRules.Next(new[] { 9, 1, 5 }, null, 9) == 1, "Hardpoint ID order wraps");
            Check(HardpointRules.Next(new[] { 9, 1, 5 }, new[] { 5, 9 }, -1) == 5, "custom Hardpoint order overrides IDs");
            Check(HardpointRules.Next(new[] { 9, 1, 5 }, new[] { 5, 9 }, 9) == 1, "unspecified Hardpoints follow custom order");
            var state = new SceneGameState(new ScenePlayerRegistry());
            foreach (GameMode previous in modes)
            foreach (GameMode next in modes)
            {
                state.ConfigureMatchMode(previous, true);
                state.PointGoal = 23;
                state.TimeGoal = 47;
                state.MatchTime = 123;
                state.ConfigureMatchMode(next, false);
                Check(state.Mode == next && state.Teams == state.IsTeamMode(next), $"{previous} -> {next}: mode and teams");
                Check(state.ModeState.Method.Name == "ModeState" + next.ToString().Replace("Teams", ""),
                    $"{previous} -> {next}: objective handler");
                Check(state.PointGoal == 23 && state.TimeGoal == 47 && state.MatchTime == 123,
                    $"{previous} -> {next}: authoritative goals retained");
                state.ConfigureMatchMode(next, true);
                Check((MatchGoalRules.UsesTimeTarget(next) ? state.TimeGoal : state.PointGoal) == MatchGoalRules.DefaultValue(next),
                    next + ": default victory condition");
            }
            var report = PostMatchReportPacket.Create(); report.MatchId = 7; report.Count = 1;
            report.Slots[0] = 2; report.Generations[0] = 1; report.Teams[0] = 1;
            report.Names[0] = "OBJECTIVE"; report.ObjectiveA[0]=3; report.ObjectiveB[0]=4; report.ObjectiveC[0]=5; report.ObjectiveD[0]=6;
            byte[] reportBytes = new byte[PostMatchReportPacket.Size]; report.Write(reportBytes);
            Check(reportBytes.Length <= NetConfig.MaxPayloadSize && PostMatchReportPacket.TryRead(reportBytes, out var reportRoundtrip)
                && reportRoundtrip.ObjectiveA[0]==3 && reportRoundtrip.ObjectiveD[0]==6, "objective report fits transport and retains statistics");
            foreach (int protocol in new[] { 27, 28, 29, 30 })
            {
                byte[] oldReport = new byte[4 + 8 * PostMatchReportPacket.LegacyEntrySize]; oldReport[0] = (byte)PacketType.PostMatchReport;
                reportBytes.AsSpan(0, 3).CopyTo(oldReport.AsSpan(1));
                for (int i = 0; i < 8; i++)
                    reportBytes.AsSpan(3 + i * PostMatchReportPacket.EntrySize, PostMatchReportPacket.LegacyEntrySize)
                        .CopyTo(oldReport.AsSpan(4 + i * PostMatchReportPacket.LegacyEntrySize));
                var upgraded = ReplayIdentityCompatibility.Convert(oldReport, protocol);
                Check(PostMatchReportPacket.TryRead(upgraded[1..], out var historical)
                    && historical.Names[0] == "OBJECTIVE" && historical.ObjectiveA[0] == 0,
                    $"protocol {protocol} report supplies neutral objective defaults");
            }
            var chamberMode = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.OneInTheChamber,
                PointGoal = 99, TimeLimitSeconds = 600 }.NormalizeLegacy();
            Check(chamberMode.PointGoal == 2 && chamberMode.TimeLimitSeconds == 0, "Chamber fixes three lives and last-survivor victory");
            chamberMode.ApplyModifiers(state);
            Check(state.OneInTheChamber && state.PointGoal == 2, "Chamber mode implies its weapon rules");
            Check(!MatchModifierRules.Validate(chamberMode with { Fiesta = true }, out _), "Chamber mode rejects Fiesta");
            Check(!MatchModifierRules.Validate(chamberMode with { InstaGib = true }, out _), "Chamber mode rejects Insta-Gib");
            Check(!MatchModifierRules.Validate(chamberMode with { NoImperialist = true }, out _), "Chamber mode requires Imperialist");
            var chamberSession = new SessionStatePacket { Match = chamberMode, MaxPlayers = 8 };
            byte[] chamberBytes = new byte[SessionStatePacket.Size]; chamberSession.Write(chamberBytes);
            Check(SessionStatePacket.TryRead(chamberBytes, out var chamberRead) && chamberRead.Match.Mode == GameMode.OneInTheChamber,
                "Chamber mode roundtrips through session state");
            state.ConfigureMatchMode(GameMode.InstaGib, true);
            Check(state.Mode == GameMode.Battle && state.InstaGib && state.ModeState.Method.Name == "ModeStateBattle",
                "legacy Insta-Gib normalizes to Battle plus modifier");
            state.ConfigureMatchMode(GameMode.SinglePlayer, true);
            Check(!state.Teams && state.ModeState.Method.Name == "ModeStateAdventure", "adventure restores its handler");

            // Deliberately seed every slot, including slots 4-7 and multidimensional weapon statistics.
            Array[] roundArrays = { state.Points, state.TeamPoints, state.Kills, state.TeamKills,
                state.Deaths, state.TeamDeaths, state.Time, state.TeamTime, state.Standings,
                state.TeamStandings, state.ResultSlots, state.BeamDamageMax, state.BeamDamageDealt,
                state.DamageCount, state.AltDamageCount, state.ShotsFired, state.ShotsHit,
                state.MatchDamageDealt, state.MatchDamageTaken, state.LongestKillStreak,
                state.KillStreak, state.Suicides, state.FriendlyKills, state.HeadshotKills,
                state.OctolithScores, state.OctolithDrops, state.OctolithStops, state.NodesCaptured,
                state.NodesLost, state.KillsAsPrime, state.PrimesKilled, state.BeamKills,
                state.TokenCarried, state.TokenConfirms, state.TokenDenies, state.TokensCollected, state.TokensBanked, state.LargestBank, state.ObjectiveSeconds, state.ObjectivePickups, state.ObjectiveContests,
                state.StageSeconds, state.FastestStageSeconds };
            foreach (Array array in roundArrays)
            {
                if (array is int[] integers) Array.Fill(integers, 17);
                else if (array is float[] times) Array.Fill(times, 17f);
                else for (int i = 0; i < PlayerEntity.SlotCapacity; i++)
                    for (int j = 0; j < 9; j++) state.BeamKills[i, j] = 17;
            }
            for (int slot = 0; slot < 8; slot++)
            for (uint life = 1; life <= 32; life++)
            {
                var pair = SpawnLoadoutRules.Fiesta(12, life, slot, false);
                Check(pair.First != pair.Second && pair == SpawnLoadoutRules.Fiesta(12, life, slot, false), "Fiesta deterministically selects distinct weapons");
                var noImpPair = SpawnLoadoutRules.Fiesta(12, life, slot, true);
                Check(noImpPair.First != BeamType.Imperialist && noImpPair.Second != BeamType.Imperialist, "Fiesta respects No Imp");
            }
            Check(!MatchModifierRules.Validate(new MatchDefinition { OneInTheChamber = true, InstaGib = true }, out _), "Chamber rejects Insta-Gib");
            Check(!MatchModifierRules.Validate(new MatchDefinition { OneInTheChamber = true, NoImperialist = true }, out _), "Chamber rejects No Imp");
            Check(!MatchModifierRules.Validate(new MatchDefinition { Mode = GameMode.GunGame, Fiesta = true }, out _), "Gun Game rejects Fiesta");
            state.ConfigureMatchMode(GameMode.Capture, true);
            state.FriendlyFire = state.AffinityWeapons = state.InstaGib = state.LowTier = true;
            state.NoImperialist = state.ShadowFreeze = state.SpawnProtection = true;
            state.PrimeHunter = 7; state.ActiveHardpointId = 42; state.HardpointTicksRemaining = 1500;
            state.ActivePlayers = 8;
            state.ForceEndGame = true;
            state.MatchState = MatchState.Ending;
            state.ResetRoundState();
            foreach (Array array in roundArrays)
                Check(array.Cast<object>().All(value => Convert.ToDouble(value) == 0), "round array fully reset");
            Check(state.PrimeHunter == -1 && state.ActiveHardpointId == -1 && state.HardpointTicksRemaining == 0 && state.ActivePlayers == 0 && !state.ForceEndGame
                && state.MatchState == MatchState.InProgress, "ownership and match end state reset");
            Check(state.Mode == GameMode.Capture && state.Teams && state.PointGoal == 5 && state.MatchTime == 900
                && state.FriendlyFire && state.AffinityWeapons && state.InstaGib && state.LowTier
                && state.NoImperialist && state.ShadowFreeze && state.SpawnProtection, "round reset retains configuration");
            state.ResetRoundState();
            Check(state.PrimeHunter == -1 && state.MatchState == MatchState.InProgress, "round reset is idempotent");
            Check(!CareerMatchEligibility.IsStandard(new MatchDefinition { Mode = GameMode.Battle }, true),
                "bot rounds cannot earn career rating");
            Check(!CareerMatchEligibility.IsStandard(new MatchDefinition { Mode = GameMode.InstaGib }, false),
                "legacy Insta-Gib is a custom career rule");
            var match = new MatchDefinition { RoomKey = "CTF1_FAULT LINE", Mode = GameMode.Capture,
                OctolithAutoReset = true, LowTier = true, PointGoal = 5, HideOpponentHealth = true };
            var session = new SessionStatePacket { Match = match, MaxPlayers = 8, Phase = SessionPhase.Lobby,
                RuleFlags = LobbyRuleFlags.RequireReady | LobbyRuleFlags.LockTeams };
            byte[] bytes = new byte[SessionStatePacket.Size]; session.Write(bytes);
            Check(SessionStatePacket.TryRead(bytes, out var restored) && restored.Match == match
                && restored.RequireReady && restored.LockTeams, "separate lobby/modifier wire fields round trip");
            foreach (var modifierMatch in new[] { match with { Fiesta = true, EnhancedHunters = true }, match with { OneInTheChamber = true, EnhancedHunters = true } })
            {
                var modifierSession = session; modifierSession.Match = modifierMatch; modifierSession.Write(bytes);
                Check(SessionStatePacket.TryRead(bytes, out var modRead) && modRead.Match == modifierMatch, "spawn modifier roundtrips session wire");
            }
            restored.Match.ApplyModifiers(state);
            Check(state.OctolithReset, "authoritative auto reset applies");
            Check(!MatchModifierRules.Validate(match with { Mode = GameMode.Battle }, out _), "auto reset rejects non-Octolith modes");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SessionStatePacket.Protocol28Size), 1u << 31);
            Check(!SessionStatePacket.TryRead(bytes, out _), "unknown modifier bits are rejected");
            byte[] old = new byte[1 + SessionStatePacket.Protocol28Size]; old[0] = (byte)PacketType.SessionState;
            session.Write(bytes); bytes.AsSpan(0, SessionStatePacket.Protocol28Size).CopyTo(old.AsSpan(1));
            BinaryPrimitives.WriteUInt16LittleEndian(old.AsSpan(15), 8 | 16 | 32 | 64 | 2048);
            Check(SessionStatePacket.TryRead(ReplayIdentityCompatibility.Convert(old, 28)[1..], out restored)
                && restored.RequireReady && restored.LockTeams && restored.AllowJoinInProgress
                && restored.Match.LowTier && restored.Match.HideOpponentHealth && !restored.Match.OctolithAutoReset,
                "protocol 28 recordings translate independent lobby and modifier bits");
            foreach (int protocol in new[] { 29, 30 })
            {
                BinaryPrimitives.WriteUInt16LittleEndian(old.AsSpan(15), 8 | 16 | 32 | 64 | 2048 | 8192);
                Check(SessionStatePacket.TryRead(ReplayIdentityCompatibility.Convert(old, protocol)[1..], out restored)
                    && restored.Match.EnhancedHunters && restored.Match.LowTier && restored.RequireReady,
                    $"protocol {protocol} retains Enhanced Hunters with separated rule bits");
                var oldMatch = new MatchStatePacket { RuleBits = 8192, RoomKey = "MP1 SANCTORUS", NextRoomKey = "" };
                byte[] oldMatchBytes = new byte[1 + MatchStatePacket.Size]; oldMatchBytes[0] = (byte)PacketType.MatchState;
                oldMatch.Write(oldMatchBytes.AsSpan(1));
                Check(MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(oldMatchBytes, protocol)[1..]).EnhancedHunters,
                    $"protocol {protocol} match state translates Enhanced Hunters");
            }
            byte[] packet = new byte[1 + SessionStatePacket.Size]; packet[0] = (byte)PacketType.SessionState;
            session.Write(packet.AsSpan(1));
            var replica = new ReplayReplicaState(); replica.Accept(packet, 0);
            var checkpoint = new ReplayReplicaState(); checkpoint.RestoreCheckpoint(replica.CaptureCheckpoint());
            Check(checkpoint.Configuration?.Match.OctolithAutoReset == true, "replay checkpoint retains auto reset");
            Console.WriteLine($"[gamemodecheck] PASS {_checks} lifecycle checks (asset-free; not objective simulation)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("[gamemodecheck] FAIL " + ex); return 1; }
    }
}
