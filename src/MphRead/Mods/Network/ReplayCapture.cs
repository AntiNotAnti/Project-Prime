using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    internal static class ReplayCapture
    {
        internal static ReplayRecorder Recorder { get; } = new();
        internal static ReplayLiveWorld WorldCapture { get; private set; } = new(Recorder);
        private static readonly byte[] Snapshot = new byte[NetConfig.MaxSnapshotSize];
        private static int _snapshotLength;
        private static uint _historyFrames;
        private static string? _room;
        private static ulong _mapHash;
        private static readonly PlayerState[] Previous = new PlayerState[RosterPacket.MaxSlots];
        private static readonly ReplayAuthorityWire AuthorityWire = new();
        private static readonly ReplayAuthorityCaptureScratch AuthorityScratch = new();
        internal static ReplayAuthorityWorld? LatestAuthorityWorld { get; private set; }
        private static ReplayKillIdentity? _authorityKill;
        private static bool _worldCaptureFailed;
        private sealed record DropIdentity(int Value);
        private static ConditionalWeakTable<ItemInstanceEntity, DropIdentity> DropIdentities = new();
        private static int _nextDropIdentity;
        private static int IdentifyDrop(ItemInstanceEntity item)
            => DropIdentities.GetValue(item, _ => new(checked(++_nextDropIdentity))).Value;
        private static readonly bool[] Known = new bool[RosterPacket.MaxSlots];

        static ReplayCapture()
        {
            Recorder.Resetting += () =>
            {
                AuthorityWire.Reset(); LatestAuthorityWorld = null; _authorityKill = null; _worldCaptureFailed = false;
                Array.Clear(Known); DropIdentities = new(); _nextDropIdentity = 0;
            };
        }

        internal static bool NeedsWorld => (!Headless.Active && (Mods.Launcher.LauncherPrefs.KillCamEnabled
            || Mods.Launcher.LauncherPrefs.FinalKillCamEnabled || DemoClip.Active))
            || DemoClip.IsSaving || DemoRecorder.IsRecording || ServerReplayRecorder.IsRecording;

        internal static void AfterSimulation(Scene scene)
        {
            if (scene.Services.IsReplica) return;
            using var perf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.Capture);
            DemoClip.Tick(scene.Size);
            if (DemoPlayback.IsActive || !NetSession.Active || !scene.GameState.Multiplayer) return;
            if (LatestAuthorityWorld is { } receivedWorld) NetObjectiveSync.Apply(scene, receivedWorld);
            uint historyFrames = (uint)Math.Max(45, DemoClip.Seconds + DemoClip.PostRollSeconds) * 60;
            if (historyFrames != _historyFrames)
            {
                Recorder.Timeline.SetHistoryFrames(historyFrames);
                _historyFrames = historyFrames;
            }
            if (NetSession.IsAuthority && !_worldCaptureFailed
                && (NetSession.NetFrame % 6 == 0 || LatestAuthorityWorld?.Phase != scene.GameState.MatchState))
            {
                try
                {
                    using var authorityPerf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.AuthorityCapture);
                    var world = AuthorityScratch.Capture(scene, NetSession.CurrentMatchId, NetSession.AuthorityEpoch, NetSession.NetFrame, IdentifyDrop);
                    ClassifyEnd(scene, world);
                    AcceptWorld(world, AuthorityScratch.Encode(world), send: true);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                { _worldCaptureFailed = true; Console.WriteLine("[replay] Authority world capture unavailable: " + ex.Message); }
            }
            WorldCapture.SetEnabled(NeedsWorld);
            WorldCapture.Advance(NetSession.NetFrame, scene.Size);
            DemoRecorder.Tick();
            ServerReplayRecorder.Tick();
        }

        internal static void ReleaseWorld()
        {
            DemoClip.CompletePending(WorldCapture.World?.Scene.Size ?? new OpenTK.Mathematics.Vector2i(256, 192));
            WorldCapture.Dispose(); WorldCapture = new(Recorder);
            Recorder.Reset();
        }

        public static void Reset()
        {
            _snapshotLength = 0; _room = null; _mapHash = 0;
            Array.Clear(Known); AuthorityWire.Reset(); LatestAuthorityWorld = null; _authorityKill = null; _worldCaptureFailed = false;
            Recorder.Reset();
        }

        internal static void ObserveSnapshot(SnapshotHeader header, ReadOnlySpan<PlayerState> players,
            ReadOnlySpan<byte> time, ReadOnlySpan<byte> world)
        {
            if (DemoPlayback.IsActive) return;
            int length = 1 + SnapshotHeader.Size + players.Length * PlayerState.Size + time.Length + world.Length;
            if (length > Snapshot.Length) return;
            Snapshot[0] = (byte)PacketType.Snapshot; header.Write(Snapshot.AsSpan(1));
            for (int i = 0; i < players.Length; i++) players[i].Write(Snapshot.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size));
            int at = 1 + SnapshotHeader.Size + players.Length * PlayerState.Size;
            time.CopyTo(Snapshot.AsSpan(at)); world.CopyTo(Snapshot.AsSpan(at + time.Length));
            _snapshotLength = length;
        }

        public static void Observe(ReadOnlySpan<byte> packet)
        {
            if (DemoPlayback.IsActive || packet.Length < 1) return;
            if ((PacketType)packet[0] == PacketType.Snapshot && packet.Length <= Snapshot.Length)
            {
                packet.CopyTo(Snapshot);
                _snapshotLength = packet.Length;
            }
        }

        public static ReplayMetadata Capture(ReplayType type, bool hashMap = true)
        {
            var packets = new List<byte[]>();
            var players = new List<ReplayPlayerInfo>();
            MatchStatePacket? match = NetSession.ServerMatch;
            string room = match?.RoomKey ?? "";
            if (_room != room || _mapHash == 0)
            {
                ulong hash = hashMap && room.Length > 0 ? ReplayMapIdentity.Compute(room) : 0;
                _room = room;
                _mapHash = hash;
            }
            if (NetSession.ServerSession is { } session)
            {
                byte[] packet = new byte[1 + SessionStatePacket.Size];
                packet[0] = (byte)PacketType.SessionState; session.Write(packet.AsSpan(1));
                packets.Add(packet);
            }
            if (match is { } state)
            {
                byte[] packet = new byte[1 + MatchStatePacket.Size];
                packet[0] = (byte)PacketType.MatchState; state.Write(packet.AsSpan(1));
                packets.Add(packet);
            }
            // Use the current wire roster instead of reconstructing an obsolete
            // packet shape. This carries stream identity, slot generations, teams
            // and lobby state, all of which playback needs before the first snapshot.
            RosterPacket roster = NetSession.LobbyRoster();
            for (int i = 0; i < roster.Count; i++)
            {
                players.Add(new(roster.Slots[i], roster.Hunters[i], roster.Teams[i], roster.Names[i], roster.IsBot(i), roster.BotLevels[i]));
            }
            byte[] rosterBytes = new byte[1 + RosterPacket.Size];
            rosterBytes[0] = (byte)PacketType.Roster; roster.Write(rosterBytes.AsSpan(1));
            packets.Add(rosterBytes);
            if (match is { } cosmeticMatch)
            {
                byte[] cosmetics = new byte[1 + Entities.PlayerEntity.SlotCapacity * CosmeticStatePacket.Size];
                cosmetics[0] = (byte)PacketType.CosmeticState;
                int size = NetCosmetics.Live.Write(cosmetics.AsSpan(1), cosmeticMatch.MatchId, cosmeticMatch.AuthorityEpoch);
                if (size > 0) packets.Add(cosmetics.AsSpan(0, size + 1).ToArray());
            }
            if (_snapshotLength > 0) packets.Add(Snapshot.AsSpan(0, _snapshotLength).ToArray());
            return new ReplayMetadata
            {
                Type = type, RoomKey = room, Mode = (GameMode)(match?.Mode ?? 0), MapHash = _mapHash,
                Players = players, Bootstrap = new ReplayBootstrap { Packets = packets }
            };
        }

        internal static void AcceptedChat(ChatPacket chat)
        {
            if (DemoPlayback.IsActive || !Recorder.HasMatch) return;
            Span<byte> bytes = stackalloc byte[1 + ChatPacket.Size];
            bytes[0] = (byte)PacketType.Chat;
            chat.Write(bytes[1..]);
            Recorder.AcceptChat(bytes, NetSession.NetFrame);
        }

        internal static void AcceptedShotFact(in ReplayShotFact fact)
        {
            if (DemoPlayback.IsActive || !NetSession.IsAuthority
                || fact.MatchId != NetSession.CurrentMatchId
                || fact.AuthorityEpoch != NetSession.AuthorityEpoch)
            {
                return;
            }
            Span<byte> payload = stackalloc byte[ReplayShotFactPacket.Size];
            ReplayShotFactPacket.Write(fact, payload);
            if (!Recorder.AcceptShotFact(payload, NetSession.NetFrame)) return;
            NetSession.SendReplayShotFact(payload);
        }

        internal static void AcceptedShotFact(ReadOnlySpan<byte> payload, uint frame)
        {
            if (DemoPlayback.IsActive) return;
            Recorder.AcceptShotFact(payload, frame);
        }

        internal static void AcceptedCosmetics()
        {
            if (DemoPlayback.IsActive || NetSession.ServerMatch is not { } match) return;
            Span<byte> bytes = stackalloc byte[1 + Entities.PlayerEntity.SlotCapacity * CosmeticStatePacket.Size];
            bytes[0] = (byte)PacketType.CosmeticState;
            int length = NetCosmetics.Live.Write(bytes[1..], match.MatchId, match.AuthorityEpoch);
            if (length > 0) Recorder.AcceptCosmetics(bytes[..(length + 1)], NetSession.NetFrame);
        }

        internal static void AcceptedMatch(in MatchStatePacket state)
        {
            if (DemoPlayback.IsActive) return;
            Recorder.AcceptMatch(state, NetSession.NetFrame);
            if (NetSession.ServerSession is { } configuration)
                Recorder.AcceptConfiguration(configuration, NetSession.NetFrame);
        }

        internal static void AcceptedConfiguration(in SessionStatePacket state)
        {
            if (!DemoPlayback.IsActive) Recorder.AcceptConfiguration(state, NetSession.NetFrame);
        }

        internal static void AcceptedIntent(int slot, in IntentPacket intent)
        {
            if (!DemoPlayback.IsActive) Recorder.AcceptIntent(slot, intent, NetSession.NetFrame);
        }

        internal static void AcceptedRoster(in RosterPacket roster)
        {
            if (!DemoPlayback.IsActive) Recorder.AcceptRoster(roster, NetSession.NetFrame);
        }

        internal static void AcceptedSnapshot(ReadOnlySpan<byte> packet, uint tick)
        {
            if (!DemoPlayback.IsActive) Recorder.AcceptSnapshot(packet, NetSession.NetFrame, tick);
        }

        internal static void AcceptWorldPacket(ReadOnlySpan<byte> payload)
        {
            if (AuthorityWire.Accept(payload, NetSession.CurrentMatchId, NetSession.AuthorityEpoch) is { } world) AcceptWorld(world);
        }
        private static void AcceptWorld(ReplayAuthorityWorld world) => AcceptWorld(world, world.Encode());
        private static void AcceptWorld(ReplayAuthorityWorld world, ReadOnlySpan<byte> encoded, bool send = false)
        {
            var old = LatestAuthorityWorld;
            if (old != null && (old.MatchId != world.MatchId || old.Epoch != world.Epoch)) old = null;
            Recorder.AcceptWorld(world, NetSession.NetFrame, encoded, send);
            void Marker(ReplayMarkerKind kind, int actor = 255, int target = 255, int value = 0)
                => Recorder.Marker(NetSession.NetFrame, world.Tick, new(kind, (byte)actor, (byte)target, value));
            if (old != null)
            {
                foreach (var token in world.Tokens.AsSpan(0, world.TokenCount < 0 ? world.Tokens.Length : world.TokenCount))
                    if (token.Id >= old.NextTokenId) Marker(ReplayMarkerKind.TokenSpawn, token.Victim, value: token.Value);
                if (world.ActiveHardpointId != old.ActiveHardpointId)
                    Marker(ReplayMarkerKind.HardpointChanged, value: world.ActiveHardpointId);
                if (NetSession.ServerMatch?.Mode is (byte)GameMode.Hardpoint or (byte)GameMode.HardpointTeams)
                    foreach (var node in world.Nodes.AsSpan(0, world.NodeCount < 0 ? world.Nodes.Length : world.NodeCount))
                        foreach (var previous in old.Nodes.AsSpan(0, old.NodeCount < 0 ? old.Nodes.Length : old.NodeCount))
                            if (node.Id == world.ActiveHardpointId && node.Id == previous.Id
                                && node.Team >= 0 && node.Team != previous.Team)
                                Marker(ReplayMarkerKind.HardpointCaptured, node.Capturer.Slot, value: node.Id);
                if (NetSession.ServerMatch?.Mode == (byte)GameMode.Relic)
                    foreach (var flag in world.Flags.AsSpan(0, world.FlagCount < 0 ? world.Flags.Length : world.FlagCount))
                        foreach (var previous in old.Flags.AsSpan(0, old.FlagCount < 0 ? old.Flags.Length : old.FlagCount))
                            if (flag.Id == previous.Id && flag.Carrier != previous.Carrier)
                            {
                                if (previous.Carrier.Slot < 8) Marker(ReplayMarkerKind.RelicDrop, previous.Carrier.Slot, value: flag.Id);
                                if (flag.Carrier.Slot < 8) Marker(ReplayMarkerKind.RelicPickup, flag.Carrier.Slot, value: flag.Id);
                            }
                if (world.Prime != old.Prime) Marker(ReplayMarkerKind.PrimeChange, world.Prime.Slot, old.Prime.Slot);
                for (int i = 0; i < 8; i++)
                {
                    if (world.TokenStats[8+i] > old.TokenStats[8+i]) Marker(ReplayMarkerKind.TokenConfirmed, i, value: world.TokenStats[8+i]);
                    if (world.TokenStats[16+i] > old.TokenStats[16+i]) Marker(ReplayMarkerKind.TokenDenied, i, value: world.TokenStats[16+i]);
                    if (world.TokenStats[32+i] > old.TokenStats[32+i]) Marker(ReplayMarkerKind.TokenBanked, i, value: world.TokenStats[32+i] - old.TokenStats[32+i]);
                    if (NetSession.ServerMatch?.Mode == (byte)GameMode.GunGame && world.TeamPoints[i] > old.TeamPoints[i])
                        Marker(ReplayMarkerKind.GunGameAdvance, i, value: world.TeamPoints[i]);
                    if (world.FlagScores[i] > old.FlagScores[i]) Marker(ReplayMarkerKind.FlagCapture, i, value: world.FlagScores[i]);
                    if (world.NodesCaptured[i] > old.NodesCaptured[i]) Marker(ReplayMarkerKind.NodeCapture, i, value: world.NodesCaptured[i]);
                    int goal = NetSession.ServerMatch?.PointGoal ?? 0;
                    if (goal > 1 && world.TeamPoints[i] == goal - 1 && old.TeamPoints[i] < goal - 1)
                        Marker(ReplayMarkerKind.MatchPoint, value: i);
                }
                if (old.EndCause == ReplayEndCause.None && world.EndCause != ReplayEndCause.None)
                    Recorder.Marker(NetSession.NetFrame, world.Tick, new(ReplayMarkerKind.MatchEnd,
                        world.EndingKill?.KillerSlot ?? 255, world.EndingKill?.VictimSlot ?? 255,
                        (int)world.EndCause, world.EndingKill));
            }
            LatestAuthorityWorld = world;
        }
        private static void ClassifyEnd(Scene scene, ReplayAuthorityWorld world)
        {
            if (world.Phase == MatchState.InProgress) return;
            if (LatestAuthorityWorld is { EndCause: not ReplayEndCause.None } previous
                && previous.MatchId == world.MatchId && previous.Epoch == world.Epoch)
            { world.EndCause = previous.EndCause; world.EndingKill = previous.EndingKill; return; }
            world.EndCause = ReplayEndCause.Other;
            if (scene.GameState.ForceEndGame) return;
            bool combat = scene.GameState.Mode is GameMode.Battle or GameMode.BattleTeams
                or GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber;
            if (combat && _authorityKill is { } kill && kill.MatchId == world.MatchId && kill.AuthorityEpoch == world.Epoch
                && world.Tick >= kill.ServerTick && world.Tick - kill.ServerTick <= 1
                && (!scene.GameState.Teams || scene.Players.Items[kill.KillerSlot].TeamIndex != scene.Players.Items[kill.VictimSlot].TeamIndex)
                && (scene.GameState.Mode is GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber
                    && scene.GameState.TeamDeaths[scene.Players.Items[kill.VictimSlot].TeamIndex] > scene.GameState.PointGoal
                    || scene.GameState.Mode is GameMode.Battle or GameMode.BattleTeams
                    && scene.GameState.TeamPoints[scene.Players.Items[kill.KillerSlot].TeamIndex] >= scene.GameState.PointGoal))
            { world.EndCause = ReplayEndCause.Kill; world.EndingKill = kill; }
            else if (LatestAuthorityWorld is { Phase: MatchState.InProgress, MatchTime: > 0 and <= 0.12f })
                world.EndCause = ReplayEndCause.Time;
            else if (!combat) world.EndCause = ReplayEndCause.Objective;
        }

        public static void Event(ReplayEventType type, int actor = -1, int target = -1, int value = 0)
        {
            if (!NetSession.Active || DemoPlayback.IsActive) return;
            byte Actor(int slot) => slot is >= 0 and < RosterPacket.MaxSlots ? (byte)slot : byte.MaxValue;
            ReplayMarkerKind? marker = type switch
            {
                ReplayEventType.PlayerSpawn => ReplayMarkerKind.Spawn,
                ReplayEventType.PlayerDeath => ReplayMarkerKind.Death,
                ReplayEventType.Damage => ReplayMarkerKind.Damage,
                ReplayEventType.ScoreChanged => ReplayMarkerKind.Score,
                ReplayEventType.PlayerJoined => ReplayMarkerKind.Join,
                ReplayEventType.PlayerLeft => ReplayMarkerKind.Leave,
                // Objective markers are derived once from accepted authority
                // counters below, never duplicated by speculative entity contacts.
                ReplayEventType.MatchEnded => ReplayMarkerKind.MatchEnd,
                ReplayEventType.MatchStarted => ReplayMarkerKind.MatchStart,
                _ => null
            };
            if (marker is { } kind) Recorder.Marker(NetSession.NetFrame,
                Recorder.Timeline.LastServerTick ?? NetSession.NetFrame, new(kind, Actor(actor), Actor(target), value));
        }

        // Called where authoritative state is accepted, after normal validation. Annotations
        // describe confirmed transitions, never inferred projectile hits or local predictions.
        public static void AcceptedState(in PlayerState state, uint? authoritativeFrame = null, Scene? authorityScene = null)
        {
            int slot = state.SlotIndex;
            if (DemoPlayback.IsActive || slot >= Known.Length) return;
            if (Known[slot] && Previous[slot].SlotGeneration == state.SlotGeneration)
            {
                var old = Previous[slot];
                if (old.Points != state.Points) Event(ReplayEventType.ScoreChanged, slot, value: state.Points);
                if ((old.Flags & PlayerState.FlagSpawned) == 0 && (state.Flags & PlayerState.FlagSpawned) != 0)
                    Event(ReplayEventType.PlayerSpawn, slot);
                if (state.Deaths > old.Deaths)
                {
                    Event(ReplayEventType.PlayerDeath, slot, state.AttackerSlot);
                    if (state.AttackerSlot < RosterPacket.MaxSlots && state.AttackerSlot != slot)
                        Event(ReplayEventType.Kill, state.AttackerSlot, slot);
                    ushort attackerGeneration = 0;
                    for (int i = 0; i < PlayerState.DamageHistory; i++)
                    {
                        var damage = state.EventAt(i);
                        if (damage.EventId == state.DamageEventId && damage.AttackerSlot == state.AttackerSlot)
                        { attackerGeneration = damage.AttackerGeneration; break; }
                    }
                    uint tick = authoritativeFrame ?? NetSession.NetFrame;
                    ushort attackerLife = NetDamage.ReplayAttackerLife(state, attackerGeneration);
                    var identity = new ReplayKillIdentity(NetSession.CurrentMatchId, NetSession.AuthorityEpoch,
                        tick, state.DamageEventId, state.AttackerSlot, attackerGeneration,
                        state.SlotIndex, state.SlotGeneration, state.LifeId, attackerLife);
                    // A later-life snapshot or a jump in cumulative deaths does not
                    // identify the exact death. Keep the coarse Studio annotation,
                    // but never advertise it as a fenced killcam candidate.
                    if (state.LifeId == old.LifeId && state.Health == 0
                        && state.Deaths == old.Deaths + 1 && attackerGeneration != 0
                        && state.AttackerSlot < RosterPacket.MaxSlots && state.AttackerSlot != slot)
                    {
                        var marker = new ReplayMarker(ReplayMarkerKind.Kill,
                            state.AttackerSlot, state.SlotIndex, Kill: identity,
                            Weapon: state.DamageBeam, DamageFlags: state.DamageFlags);
                        if (authorityScene != null && !authorityScene.Services.IsReplica && NetSession.IsAuthority)
                            authorityScene.MatchEvents.ParityDiagnostics.Legacy(tick, Mods.MatchEvents.MatchSemanticEventType.PlayerKilled,
                                new(state.AttackerSlot, attackerGeneration, attackerLife),
                                new(state.SlotIndex, state.SlotGeneration, state.LifeId), state.DamageBeam == byte.MaxValue ? (int)BeamType.Platform : state.DamageBeam,
                                (state.DamageFlags & (byte)MphRead.Entities.DamageFlags.Headshot) != 0);
                        Recorder.Marker(NetSession.NetFrame, tick, marker);
                        if (NetSession.IsAuthority) _authorityKill = identity;
                        Mods.KillCam.NoteKill(marker, NetSession.NetFrame);
                    }
                }
                if (old.DamageEventId != state.DamageEventId)
                {
                    Event(ReplayEventType.Damage, state.AttackerSlot, slot, Math.Max(0, old.Health - state.Health));
                    if ((state.DamageFlags & (byte)MphRead.Entities.DamageFlags.Headshot) != 0 && state.AttackerSlot < 8)
                    {
                        if (authorityScene != null && !authorityScene.Services.IsReplica && NetSession.IsAuthority)
                        {
                            var damage = state.EventAt(0);
                            for (int i = 0; i < PlayerState.DamageHistory; i++)
                                if (state.EventAt(i).EventId == state.DamageEventId) { damage = state.EventAt(i); break; }
                            authorityScene.MatchEvents.ParityDiagnostics.Legacy(authoritativeFrame ?? NetSession.NetFrame,
                                Mods.MatchEvents.MatchSemanticEventType.Headshot,
                                new(state.AttackerSlot, damage.AttackerGeneration, NetDamage.ReplayAttackerLife(state, damage.AttackerGeneration)),
                                new(state.SlotIndex, state.SlotGeneration, state.LifeId), state.DamageBeam == byte.MaxValue ? (int)BeamType.None : state.DamageBeam, true);
                        }
                        Recorder.Marker(NetSession.NetFrame, authoritativeFrame ?? NetSession.NetFrame,
                            new(ReplayMarkerKind.Headshot, state.AttackerSlot, state.SlotIndex,
                                Weapon: state.DamageBeam, DamageFlags: state.DamageFlags));
                    }
                }
            }
            Previous[slot] = state; Known[slot] = true;
        }
    }
}
