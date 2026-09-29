using System;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
public static class EnhancedHunterChecks
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        _checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    public static int Run()
    {
        try
        {
            NetSession.Stop();
            var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
            scene.GameState.Mode = GameMode.Battle;
            foreach (bool affinity in new[] { false, true })
            foreach (bool enhanced in new[] { false, true })
            {
                var match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle,
                    AffinityWeapons = affinity, EnhancedHunters = enhanced };
                match.ApplyModifiers(scene.GameState);
                Check(scene.GameState.AffinityWeapons == affinity && scene.GameState.EnhancedHunters == enhanced, "independent scene rules");
                var session = new SessionStatePacket { Match = match, MaxPlayers = 8, Phase = SessionPhase.InMatch,
                    WorldProfile = MatchWorldProfile.Resolve(8), MatchId = 1, AuthorityEpoch = 1 };
                byte[] bytes = new byte[SessionStatePacket.Size]; session.Write(bytes);
                Check(SessionStatePacket.TryRead(bytes, out var read) && read.Match.EnhancedHunters == enhanced
                    && read.Match.AffinityWeapons == affinity, "lobby rule codec");
                var packet = new MatchStatePacket { RuleBits = (ushort)match.Rules, RoomKey = match.RoomKey,
                    Flags = MatchStatePacket.RuleFlags(1, affinity) };
                bytes = new byte[MatchStatePacket.Size]; packet.Write(bytes);
                var restored = MatchStatePacket.Read(bytes);
                Check(restored.EnhancedHunters == enhanced && restored.AffinityWeapons == affinity, "match rule codec");
            }
            Check(!new MenuSettings().EnhancedHunters.Equals("on"), "enhanced rule defaults off");
            scene.GameState.EnhancedHunters = true;
            for (int i = 0; i < scene.Players.Items.Count; i++)
            {
                var p = scene.Players.Items[i]; p.Hunter = (Hunter)i; p.Health = 99;
                p.LoadFlags |= LoadFlags.Active | LoadFlags.Spawned;
                EnhancedHunters.OnPlayerSpawn(p);
            }
            var owner = scene.Players.Items[0]; var target = scene.Players.Items[1];
            owner.Hunter = Hunter.Weavel; EnhancedHunters.ResetPlayer(owner);
            for (int i = 0; i < 5; i++) EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Battlehammer, true, false, false);
            Check(owner.EnhancedState.ValueA == 3, "direct Battlehammer caps Siege at three");
            EnhancedHunters.OnEnterAlt(owner);
            Check(owner.EnhancedState.ValueA == 0 && owner.EnhancedState.ValueB == 3
                && owner.EnhancedState.TimerB == 150, "full charge transfers to rounds and overclock");
            EnhancedHunters.OnExitAlt(owner);
            Check(owner.EnhancedState.ValueB == 0 && owner.EnhancedState.TimerB == 0, "unmorph consumes unused siege");
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Battlehammer, false, false, false);
            Check(owner.EnhancedState.ValueA == 0, "splash earns no siege");
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.VoltDriver, true, true, false);
            Check(owner.EnhancedState.TargetSlot == byte.MaxValue, "wrong weapon earns no mark");
            scene.GameState.EnhancedHunters = false;
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Battlehammer, true, false, false);
            Check(owner.EnhancedState.ValueA == 0, "disabled rule cannot earn state");
            scene.GameState.EnhancedHunters = true;
            owner.Hunter = Hunter.Kanden; EnhancedHunters.ResetPlayer(owner);
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.VoltDriver, true, true, false);
            Check(EnhancedHunters.Target(owner) == target && owner.EnhancedState.TimerA == 180, "charged Volt establishes life-fenced rod");
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.VoltDriver, true, false, false);
            Check(owner.EnhancedState.ValueA == 1, "direct Volt adds overload progress");
            EnhancedHunters.OnPlayerSpawn(target);
            Check(EnhancedHunters.Target(owner) == null && owner.EnhancedState.TargetSlot == byte.MaxValue, "respawn invalidates incoming marks");
            owner.Hunter = Hunter.Trace; EnhancedHunters.ResetPlayer(owner);
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Imperialist, true, false, true);
            Check((owner.EnhancedState.Flags & 1) == 1 && owner.EnhancedState.TimerA == 240, "Perfect Mark duration");
            owner.EnhancedState.TimerA = 1; EnhancedHunters.OnPlayerFrame(owner);
            Check(owner.EnhancedState.TargetSlot == byte.MaxValue, "mark expiry");
            owner.Hunter = Hunter.Noxus; EnhancedHunters.ResetPlayer(owner);
            for (int i = 0; i < 4; i++) EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Judicator, true, false, false);
            Check(owner.EnhancedState.ValueA == 3 && owner.EnhancedState.TimerA == 90, "Frost cap and refresh");
            scene.GameState.Teams = true; owner.TeamIndex = target.TeamIndex = 0;
            EnhancedHunters.ResetPlayer(owner);
            EnhancedHunters.OnConfirmedHit(owner, target, BeamType.Judicator, true, false, false);
            Check(owner.EnhancedState.ValueA == 0, "friendly-fire gate prevents enhanced status");
            scene.GameState.Teams = false;
            owner.Hunter = Hunter.Guardian;
            Check(!EnhancedHunters.Enabled(owner), "Guardian is excluded");
            PacketChecks();
            Console.WriteLine($"ENHANCED PASS {_checks} checks; fast ceiling {SnapshotFast.MaximumEncodedSize} bytes");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.Stop(); }
    }
    private static void PacketChecks()
    {
        byte[] canonical = new byte[NetConfig.MaxSnapshotSize];
        var header = new SnapshotHeader { Frame = 100, MatchId = 1, AuthorityEpoch = 1, PlayerCount = 8 };
        header.Write(canonical);
        int at = SnapshotHeader.Size;
        for (byte i = 0; i < 8; i++)
        {
            var state = new PlayerState { SlotIndex = i, SlotGeneration = 1, LifeId = 1,
                Enhanced = new EnhancedHunterNetState { TargetSlot = (byte)((i + 1) % 8), TargetLifeId = 1,
                    TargetGeneration = 1, ValueA = 3, ValueB = 3, TimerA = 255, TimerB = 255 } };
            state.Write(canonical.AsSpan(at)); at += PlayerState.Size;
        }
        NetMatchTimeSync.Write(canonical.AsSpan(at)); at += NetMatchTimeSync.Size;
        int healthAt = at;
        BinaryPrimitives.WriteUInt16LittleEndian(canonical.AsSpan(at), 1);
        canonical[at + 2] = NetHealthSync.MaxSpawns; at += NetHealthSync.HeaderSize;
        for (int i = 0; i < NetHealthSync.MaxSpawns; i++)
        { BinaryPrimitives.WriteInt16LittleEndian(canonical.AsSpan(at), (short)i); at += NetHealthSync.EntrySize; }
        var world = new EnhancedHunterWorld();
        for (int i = 0; i < 16; i++) world.Zones[i] = new EnhancedZone { Type = EnhancedZoneType.MagmaPool,
            OwnerSlot = (byte)(i / 2), OwnerLifeId = 1, OwnerGeneration = 1, RemainingFrames = 135,
            Position = new Vector3(i, 0, i), Radius = 1.6f };
        at += world.Write(canonical.AsSpan(at));
        Check(NetHealthSync.Validate(canonical.AsSpan(healthAt, at - healthAt)), "maximum world validates");
        var lanes = new NetReplicationLanes(); lanes.Prepare(canonical.AsSpan(0, at));
        Check(lanes.FastLength + NetHeader.Size <= 1200 && lanes.WorldLength + NetHeader.Size <= 1200
            && lanes.SlowLength + NetHeader.Size <= 1200, "all maximum lanes fit realtime budget");
        Check(SnapshotFast.MaximumEncodedSize + WorldBootstrapIdentity.Size + 5 <= 1200, "fast bootstrap fits budget");
        var receiver = new NetReplicationReceiver();
        Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 1, 1), "slow lane accepted");
        Check(receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 1, 1), "world lane accepted");
        Check(!receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 1, 1), "duplicate world rejected");
        byte[] restored = new byte[NetConfig.MaxSnapshotSize];
        int length = receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), restored, 1, 1);
        Check(length == at && canonical.AsSpan(0, at).SequenceEqual(restored.AsSpan(0, length)), "all enhanced players and zones round-trip");
        world.Zones[0] = default;
        int zoneAt = healthAt + NetHealthSync.HeaderSize + NetHealthSync.MaxSpawns * NetHealthSync.EntrySize;
        int nextLength = zoneAt + world.Write(canonical.AsSpan(zoneAt));
        header.Frame++; header.Write(canonical); lanes.Prepare(canonical.AsSpan(0, nextLength));
        Check(lanes.SendWorld, "zone removal publishes immediately");
        byte[] legacy = new byte[104]; legacy[0] = (byte)PacketType.MatchState;
        Check(!MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(legacy, 28)[1..]).EnhancedHunters, "legacy replay opts out");
        byte[] invalid = new byte[1 + EnhancedHunterWorld.ZoneSize]; invalid[0] = 1; invalid[1] = 255;
        Check(!EnhancedHunterWorld.Validate(invalid), "invalid zone rejected");
    }
}
