using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Multiplayer;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Asset-backed synthetic coverage, not a substitute for live combat acceptance.
/// The source supplies a verified room and spawn; generated facts exercise every mode,
/// all seven hunters, eight occupants, weapons, alt forms, afflictions and a new life.</summary>
internal static class ReplayWorldCoverageCheck
{
    internal static int Run(string source, string? output)
    {
        string directory = output ?? Path.Combine(Path.GetTempPath(), "prime-world-coverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Headless.Enter();
            string room = source.StartsWith("room:", StringComparison.Ordinal) ? source[5..] : "TEST ARENA";
            Vector3 origin = new(0, 1, 0);
            if (source != "synthetic" && !source.StartsWith("room:", StringComparison.Ordinal))
            {
                using var seed = new PassiveReplayScene(source, new Vector2i(256, 192));
                bool found = false;
                do
                {
                    for (int slot = 0; slot < 8; slot++)
                        if (seed.State.TryGetPlayer(slot, out var player) && player.Health > 0)
                        { origin = player.Position; found = true; break; }
                    if (found) break;
                } while (seed.Step());
                if (!found || seed.State.Match is not { } match) throw new InvalidDataException("Source needs a room and an alive player.");
                room = match.RoomKey;
            }
            foreach (var option in Launcher.OfflineLaunch.Modes)
            {
                GameMode mode = option.Mode;
                string path = Path.Combine(directory, mode + ".ppdemo");
                Write(path, room, mode, origin);
                Console.WriteLine($"[replayworld] {mode}: eight actors (seven bots), all hunters, weapon/alt/affliction/death/respawn transitions");
                if (ReplayReplicaCheck.Run(path) != 0) return 1;
            }
            Console.WriteLine("[replayworld] PASS: all selectable multiplayer modes with detached restore, continuation and file/frozen-clip seeks.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine($"[replayworld] FAIL: {ex}"); return 1; }
        finally { if (output == null) Directory.Delete(directory, recursive: true); }
    }

    internal static void Write(string path, string room, GameMode mode, Vector3 origin)
    {
        const ushort matchId = 17;
        const ulong epoch = 19;
        var match = new MatchStatePacket
        {
            MatchId = matchId, AuthorityEpoch = epoch, RoomKey = room, NextRoomKey = "", Mode = (byte)mode,
            PlayerCount = 8, Flags = MatchStatePacket.FlagInProgress, PointGoal = MatchGoalRules.DefaultValue(mode),
            TimeRemaining = 600
        };
        var config = new SessionStatePacket
        {
            MatchId = matchId, AuthorityEpoch = epoch, Phase = SessionPhase.InMatch, MaxPlayers = 8, Revision = 1,
            WorldProfile = MatchWorldProfile.Resolve(8), OwnerSlot = 0,
            Match = new MatchDefinition { RoomKey = room, Mode = mode, Format = MatchFormat.Auto,
                TimeLimitSeconds = 600, PointGoal = match.PointGoal, AffinityWeapons = true, ShadowFreeze = true }
        };
        var roster = Network.RosterPacket.Create();
        roster.MatchId = matchId; roster.AuthorityEpoch = epoch; roster.Revision = 1; roster.Count = 8;
        roster.ContainsBots = true;
        for (byte slot = 0; slot < 8; slot++)
        {
            roster.Slots[slot] = slot; roster.Hunters[slot] = (byte)(slot % 7); roster.Colors[slot] = (byte)(slot % 4);
            roster.Teams[slot] = (sbyte)(slot % 2); roster.Generations[slot] = 1;
            if (slot == 0) roster.Names[slot] = "Actor 0";
            else
            {
                roster.Names[slot] = "BOT " + slot;
                roster.Flags[slot] = 1;
                roster.BotLevels[slot] = (byte)((slot - 1) % 4);
            }
        }
        byte[] MatchPacket()
        { byte[] p = new byte[1 + MatchStatePacket.Size]; p[0] = (byte)PacketType.MatchState; match.Write(p.AsSpan(1)); return p; }
        byte[] RosterPacket()
        { byte[] p = new byte[1 + Network.RosterPacket.Size]; p[0] = (byte)PacketType.Roster; roster.Write(p.AsSpan(1)); return p; }
        byte[] configuration = new byte[1 + SessionStatePacket.Size]; configuration[0] = (byte)PacketType.SessionState;
        config.Write(configuration.AsSpan(1));
        byte[] cosmetics = new byte[1 + PlayerEntity.SlotCapacity * CosmeticStatePacket.Size];
        cosmetics[0] = (byte)PacketType.CosmeticState;
        for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
        {
            var hunter = (Hunter)(slot % 7);
            var appearance = new Cosmetics.CosmeticAppearance(hunter, new(
                "skin." + hunter.ToString().ToLowerInvariant() + ".obsidian", "armor.inferno", "death.quantum"));
            CosmeticStatePacket.Create(slot, hunter, 1, matchId, epoch, 1, appearance)
                .Write(cosmetics.AsSpan(1 + slot * CosmeticStatePacket.Size));
        }
        using var writer = new ReplayWriterV3(path, new ReplayMetadata
        {
            RoomKey = room, Mode = mode, MapHash = ReplayMapIdentity.Compute(room),
            Bootstrap = new ReplayBootstrap { Packets = new List<byte[]> { MatchPacket(), configuration, RosterPacket(), cosmetics } }
        });
        for (uint frame = 0; frame <= 1800; frame++)
        {
            if (frame % 60 == 0)
            {
                match.TimeRemaining = 600 - frame / 60f; match.TimeElapsed = frame / 60f;
                writer.WriteRecord(frame, MatchPacket());
                roster.Revision++; writer.WriteRecord(frame, RosterPacket());
            }
            bool alt = frame is >= 750 and < 1050;
            bool dead = frame is >= 1602 and < 1662;
            ushort life = (ushort)(frame >= 1662 ? 2 : 1);
            Vector3 Position(int slot) => origin + new Vector3((slot % 4 - 1.5f) * 2, 0, (slot / 4) * 3);
            if (frame % 3 == 0)
            {
                byte[] snapshot = new byte[1 + SnapshotHeader.Size + 8 * PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
                snapshot[0] = (byte)PacketType.Snapshot;
                new SnapshotHeader { MatchId = matchId, AuthorityEpoch = epoch, Frame = frame, PlayerCount = 8,
                    Rng1 = 12345 + frame, Rng2 = 98765 + frame }.Write(snapshot.AsSpan(1));
                for (byte slot = 0; slot < 8; slot++)
                {
                    byte flags = PlayerState.FlagActive | PlayerState.FlagSpawned;
                    if (alt) flags |= PlayerState.FlagAltForm;
                    if (frame is >= 1200 and < 1260) flags |= PlayerState.FlagFrozen;
                    if (frame is >= 1300 and < 1360) flags |= PlayerState.FlagDisrupted;
                    if (frame is >= 1400 and < 1460) flags |= PlayerState.FlagBurning;
                    new PlayerState
                    {
                        SlotIndex = slot, SlotGeneration = 1, LifeId = life, Flags = flags,
                        Position = Position(slot), Facing = slot < 4 ? Vector3.UnitZ : -Vector3.UnitZ,
                        Health = (ushort)(dead ? 0 : frame >= 1500 && frame < 1602 ? 90 : 190),
                        CurrentWeapon = (byte)(slot % 8), Team = (byte)(slot % 2),
                        Deaths = (ushort)(frame >= 1602 ? 1 : 0), DamageEventId = (ushort)(frame >= 1602 ? 2 : frame >= 1500 ? 1 : 0),
                        AttackerSlot = (byte)((slot + 1) % 8), DamageBeam = (byte)(slot % 8),
                        Damage0 = frame < 1500 ? default : new DamageEvent { EventId = (ushort)(frame >= 1602 ? 2 : 1),
                            AttackerGeneration = 1, AttackerSlot = (byte)((slot + 1) % 8),
                            Beam = (byte)(slot % 8), Damage = 90, Direction = Vector3.UnitZ * .1f }
                    }.Write(snapshot.AsSpan(1 + SnapshotHeader.Size + slot * PlayerState.Size));
                }
                BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(snapshot.Length - NetHealthSync.HeaderSize), matchId);
                writer.WriteRecord(frame, snapshot);
            }
            if (frame % 6 == 0 && mode is GameMode.KillConfirmed or GameMode.KillConfirmedTeams or GameMode.Headhunter)
            {
                var world = new ReplayAuthorityWorld { MatchId = matchId, Epoch = epoch, Tick = frame,
                    Phase = MatchState.InProgress, MatchTime = 600 - frame / 60f, NextTokenId = frame < 1200 ? 2 : 3,
                    Tokens = frame is >= 300 and < 600 ? [new(1, 1, 1, 3, origin.AddY(1), 1200-(int)(frame-300))]
                        : frame is >= 1200 and < 1500 ? [new(2, 2, 0, 4, origin.AddX(3), 1200-(int)(frame-1200))] : [] };
                world.TokenStats[0] = mode == GameMode.Headhunter && frame is >= 600 and < 900 ? 3 : 0;
                world.TokenStats[24] = frame >= 600 ? 3 : 0;
                world.TokenStats[32] = frame >= 900 ? 3 : 0;
                foreach (byte[] packet in ReplayAuthorityWire.Packets(world)) writer.WriteRecord(frame, packet);
            }
            for (byte slot = 0; slot < 8; slot++)
            {
                bool fire = !dead && frame > 120 && frame % 30 < 10;
                IntentButtons buttons = dead ? 0 : IntentButtons.InPlayState;
                if (alt) buttons |= IntentButtons.AltFormState;
                if (fire) buttons |= alt ? IntentButtons.AltAttack : IntentButtons.Shoot;
                var presses = new InputEdgeHistory();
                if (fire && frame % 30 == 0) presses[0] = InputEdgeHistory.Encode((byte)(frame / 30), alt ? IntentButtons.AltAttack : IntentButtons.Shoot, 0);
                var intent = new IntentPacket { MatchId = matchId, AuthorityEpoch = epoch, SlotGeneration = 1, LifeId = life,
                    Frame = frame, Buttons = buttons, Presses = presses, HasState = true, AmmoUa = 999, AmmoMissiles = 99,
                    // FullSize is the modern event-capable wire contract. This
                    // fixture deliberately has no successfully authored shots;
                    // held buttons/presses must not invent fire events. Separate
                    // accepted-fire fixtures exercise actual current/recovered shots.
                    HasFireEvents = true,
                    WeaponSelect = slot, Aim = slot < 4 ? Vector3.UnitZ : -Vector3.UnitZ, Position = Position(slot),
                    AckFrame = slot == 0 ? frame : 0,
                    ChargeLevel = (byte)(frame % 90 == 0 ? 60 : 0), HomingTarget = IntentPacket.HomingTargetValid };
                byte[] packet = new byte[2 + IntentPacket.FullSize]; packet[0] = (byte)PacketType.SlotIntent; packet[1] = slot;
                intent.Write(packet.AsSpan(2)); writer.WriteRecord(frame, packet);
            }
        }
    }
}
