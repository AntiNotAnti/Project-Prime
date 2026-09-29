using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct ReplayActorRef(byte Slot, ushort Generation, ushort Life)
{
    internal static ReplayActorRef Capture(PlayerEntity? player)
    {
        if (player == null) return new(255, 0, 0);
        ushort generation = NetPlayerLifecycle.Generation(player.SlotIndex), life = NetPlayerLifecycle.Get(player.SlotIndex);
        return generation == 0 || life == 0 ? new(255, 0, 0) : new((byte)player.SlotIndex, generation, life);
    }
    internal PlayerEntity? Resolve(Scene scene, ReplayReplicaState state) => Slot < 8 && state.MatchesLife(Slot, Generation, Life)
        ? scene.Players.Items[Slot] : null;
}
internal readonly record struct ReplayChamberState(ReplayActorRef Actor, ushort Ammo, uint AcknowledgedFrame);
internal readonly record struct ReplayFlagState(int Id, Vector3 Position, ReplayActorRef Carrier,
    ReplayActorRef LastCarrier, bool AtBase, bool Grounded, float ResetTimer, float Gravity);
internal readonly record struct ReplayNodeState(int Id, sbyte Team, sbyte Occupying, byte Occupants,
    ReplayActorRef Capturer, float Progress, float ScoreTimer, float BlinkTimer, float Rotation, float Spin,
    bool Contested, bool InProgress);
internal readonly record struct ReplayPickupState(short Id, HealthSpawnState State);
internal readonly record struct ReplayDropState(ItemType Type, Vector3 Position, int DespawnTimer, int Identity = 1);
internal readonly record struct ReplayDoorState(int Id, DoorFlags Flags, bool Portal, bool Collision, bool ConnectorInactive);
internal readonly record struct ReplayTokenState(int Id, byte Victim, sbyte Team, int Value, Vector3 Position, int Timer);
internal enum ReplayEndCause : byte { None, Time, Kill, Objective, Other }

/// <summary>Versioned value contract for authority-only state that protocol 16's player
/// snapshot cannot describe. Replay applies all facts; live clients apply objective facts only.</summary>
internal sealed class ReplayAuthorityWorld
{
    internal const int MaximumBytes = 48 * 1024, MaximumEntities = 512;
    internal const int ModeTailSize = 8 + 8 * 11 + 4 + 48 * 4 + 2 + 32 * 4;
    internal int[] ObjectiveStats { get; set; } = new int[32];
    internal int NextTokenId { get; set; } = 1;
    internal int[] TokenStats { get; set; } = new int[48];
    internal ReplayTokenState[] Tokens { get; set; } = [];
    internal int TokenCount = -1;
    internal ushort MatchId { get; set; }
    internal ulong Epoch { get; set; }
    internal uint Tick { get; set; }
    internal ReplayActorRef Prime { get; set; } = new(255, 0, 0);
    internal MatchState Phase { get; set; }
    internal int ActiveHardpointId { get; set; } = -1;
    internal int HardpointTicksRemaining { get; set; }
    internal float MatchTime { get; set; }
    internal int[] TeamPoints { get; set; } = new int[8];
    internal int[] FlagScores { get; set; } = new int[8];
    internal int[] NodesCaptured { get; set; } = new int[8];
    internal ReplayChamberState[] Chamber { get; set; } = EmptyChamber();
    private static ReplayChamberState[] EmptyChamber()
    {
        var values = new ReplayChamberState[8];
        Array.Fill(values, new ReplayChamberState(new(255, 0, 0), 0, 0));
        return values;
    }
    internal static ReplayChamberState CaptureChamber(Scene scene, int slot)
    {
        var player = scene.Players.Items[slot];
        if (!scene.GameState.OneInTheChamber || player.Health == 0 || !player.LoadFlags.TestFlag(LoadFlags.Active))
            return new(new(255, 0, 0), 0, 0);
        if (scene.Services is ReplaySceneServices replay && replay.State.TryGetPlayer(slot, out var recorded))
            return new(new((byte)slot, recorded.SlotGeneration, recorded.LifeId),
                (ushort)Math.Clamp(player.ModAmmo.Ua, 0, ushort.MaxValue), replay.State.AuthorityWorld?.Chamber[slot].AcknowledgedFrame ?? 0);
        uint ack = NetSession.RemoteIntentValid[slot] ? NetSession.RemoteIntents[slot].Frame : NetSession.NetFrame;
        return new(ReplayActorRef.Capture(player), (ushort)Math.Clamp(player.ModAmmo.Ua, 0, ushort.MaxValue), ack);
    }
    internal ReplayFlagState[] Flags { get; set; } = [];
    internal ReplayNodeState[] Nodes { get; set; } = [];
    internal ReplayPickupState[] Pickups { get; set; } = [];
    internal ReplayDropState[] Drops { get; set; } = [];
    internal ReplayDoorState[] Doors { get; set; } = [];
    internal int FlagCount = -1, NodeCount = -1, PickupCount = -1, DropCount = -1, DoorCount = -1;
    internal ReplayEndCause EndCause { get; set; }
    internal ReplayKillIdentity? EndingKill { get; set; }

    internal static ReplayAuthorityWorld Capture(Scene scene, ushort matchId, ulong epoch, uint tick, Func<ItemInstanceEntity, int>? dropIdentity = null)
    {
        var tokens = new List<ReplayTokenState>();
        var flags = new List<ReplayFlagState>(); var nodes = new List<ReplayNodeState>();
        var pickups = new List<ReplayPickupState>(); var drops = new List<ReplayDropState>(); var doors = new List<ReplayDoorState>();
        foreach (var entity in scene.Entities)
        {
            switch (entity)
            {
                case OctolithFlagEntity flag: flags.Add(flag.CaptureReplayAuthority()); break;
                case NodeDefenseEntity node: nodes.Add(node.CaptureReplayAuthority()); break;
                case ItemSpawnEntity pickup: pickups.Add(new(checked((short)pickup.Id), pickup.ModHealthState)); break;
                case ItemInstanceEntity { TokenId: > 0, DespawnTimer: > 0 } token:
                    tokens.Add(CaptureToken(token)); break;
                case ItemInstanceEntity { TokenId: 0, Owner: null, DespawnTimer: not 0 } drop:
                    drops.Add(new(drop.ItemType, drop.Position, drop.DespawnTimer, dropIdentity?.Invoke(drop) ?? drops.Count + 1)); break;
                case DoorEntity door: doors.Add(new(door.Id, door.Flags, door.Portal?.Active == true,
                    door.ConnectorCollision?.Active == true, door.ConnectorInactive)); break;
            }
        }
        int prime = scene.GameState.PrimeHunter;
        var chamber = EmptyChamber();
        for (int slot = 0; slot < 8; slot++) chamber[slot] = CaptureChamber(scene, slot);
        var tokenStats = new int[48]; CaptureTokenStats(scene.GameState, tokenStats);
        var objectiveStats = new int[32]; CaptureObjectiveStats(scene.GameState, objectiveStats);
        return new() { ObjectiveStats = objectiveStats, NextTokenId = scene.GameState.NextTokenId, Tokens = tokens.ToArray(), TokenStats = tokenStats, MatchId = matchId, Epoch = epoch, Tick = tick,
            Prime = ReplayActorRef.Capture(prime is >= 0 and < 8 ? scene.Players.Items[prime] : null),
            Phase = scene.GameState.MatchState, MatchTime = scene.GameState.MatchTime,
            ActiveHardpointId = scene.GameState.ActiveHardpointId, HardpointTicksRemaining = scene.GameState.HardpointTicksRemaining, Chamber = chamber,
            TeamPoints = (int[])scene.GameState.TeamPoints.Clone(), FlagScores = (int[])scene.GameState.OctolithScores.Clone(),
            NodesCaptured = (int[])scene.GameState.NodesCaptured.Clone(), Flags = flags.ToArray(), Nodes = nodes.ToArray(),
            Pickups = pickups.ToArray(), Drops = drops.ToArray(), Doors = doors.ToArray() };
    }
    internal byte[] Encode()
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        Encode(w);
        return stream.ToArray();
    }
    internal void Encode(BinaryWriter w)
    {
        using var perf = ReplayPerfTelemetry.Measure(ReplayPerfOperation.AuthorityEncode);
        long start = w.BaseStream.Position;
        w.Write((byte)4); w.Write(MatchId); w.Write(Epoch); w.Write(Tick);
        Actor(Prime); w.Write((byte)Phase); w.Write(MatchTime); w.Write((byte)EndCause); w.Write(EndingKill.HasValue);
        if (EndingKill is { } kill)
        {
            w.Write(kill.MatchId); w.Write(kill.AuthorityEpoch); w.Write(kill.ServerTick); w.Write(kill.EventId);
            w.Write(kill.KillerSlot); w.Write(kill.KillerGeneration); w.Write(kill.VictimSlot); w.Write(kill.VictimGeneration); w.Write(kill.VictimLifeId);
        }
        Scores(TeamPoints); Scores(FlagScores); Scores(NodesCaptured);
        Count(FlagCount < 0 ? Flags.Length : FlagCount); foreach (var v in Flags.AsSpan(0, FlagCount < 0 ? Flags.Length : FlagCount))
        { w.Write(v.Id); Vector(v.Position); Actor(v.Carrier); Actor(v.LastCarrier); w.Write(v.AtBase); w.Write(v.Grounded); w.Write(v.ResetTimer); w.Write(v.Gravity); }
        Count(NodeCount < 0 ? Nodes.Length : NodeCount); foreach (var v in Nodes.AsSpan(0, NodeCount < 0 ? Nodes.Length : NodeCount))
        { w.Write(v.Id); w.Write(v.Team); w.Write(v.Occupying); w.Write(v.Occupants); Actor(v.Capturer); w.Write(v.Progress); w.Write(v.ScoreTimer); w.Write(v.BlinkTimer); w.Write(v.Rotation); w.Write(v.Spin); w.Write(v.Contested); w.Write(v.InProgress); }
        Count(PickupCount < 0 ? Pickups.Length : PickupCount); foreach (var v in Pickups.AsSpan(0, PickupCount < 0 ? Pickups.Length : PickupCount))
        { w.Write(v.Id); w.Write(v.State.Available); w.Write(v.State.Active); w.Write(v.State.Cooldown); w.Write(v.State.SpawnCount); w.Write(v.State.PickerSlot); }
        Count(DropCount < 0 ? Drops.Length : DropCount); foreach (var v in Drops.AsSpan(0, DropCount < 0 ? Drops.Length : DropCount)) { w.Write(v.Identity); w.Write((byte)v.Type); Vector(v.Position); w.Write(v.DespawnTimer); }
        Count(DoorCount < 0 ? Doors.Length : DoorCount); foreach (var v in Doors.AsSpan(0, DoorCount < 0 ? Doors.Length : DoorCount))
        { w.Write(v.Id); w.Write((uint)v.Flags); w.Write(v.Portal); w.Write(v.Collision); w.Write(v.ConnectorInactive); }
        w.Write(ActiveHardpointId); w.Write(HardpointTicksRemaining);
        if (Chamber.Length != 8) throw Invalid();
        foreach (var ammo in Chamber) { Actor(ammo.Actor); w.Write(ammo.Ammo); w.Write(ammo.AcknowledgedFrame); }
        if (ObjectiveStats.Length != 32) throw Invalid();
        foreach (int value in ObjectiveStats) w.Write(value);
        w.Write(NextTokenId);
        if (TokenStats.Length != 48) throw Invalid();
        foreach (int value in TokenStats) w.Write(value);
        int tokenCount = TokenCount < 0 ? Tokens.Length : TokenCount;
        if (tokenCount > Multiplayer.TokenRules.MaximumTokens) throw Invalid();
        Count(tokenCount);
        foreach (var token in Tokens.AsSpan(0, tokenCount))
        { w.Write(token.Id); w.Write(token.Victim); w.Write(token.Team); w.Write(token.Value); Vector(token.Position); w.Write(token.Timer); }
        if (w.BaseStream.Position - start > MaximumBytes) throw Invalid();
        void Scores(int[] values) { if (values.Length != 8) throw Invalid(); foreach (int value in values) w.Write(value); }
        void Count(int n) { if (n > MaximumEntities) throw Invalid(); w.Write((ushort)n); }
        void Actor(ReplayActorRef v) { w.Write(v.Slot); w.Write(v.Generation); w.Write(v.Life); }
        void Vector(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    }
    internal static ReplayAuthorityWorld Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw Invalid();
        using var stream = new MemoryStream(bytes.ToArray(), false); using var r = new BinaryReader(stream);
        try
        {
            byte version = r.ReadByte(); if (version is < 1 or > 4) throw Invalid();
            ushort match = r.ReadUInt16(); ulong epoch = r.ReadUInt64(); uint tick = r.ReadUInt32();
            var prime = Actor(); var phase = (MatchState)r.ReadByte(); float time = Float();
            var cause = (ReplayEndCause)r.ReadByte(); ReplayKillIdentity? kill = null;
            if (Bool()) kill = new(r.ReadUInt16(), r.ReadUInt64(), r.ReadUInt32(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt16(), r.ReadByte(), r.ReadUInt16(), r.ReadUInt16());
            if (!Enum.IsDefined(phase) || !Enum.IsDefined(cause) || match == 0
                || (cause == ReplayEndCause.Kill) != kill.HasValue
                || kill is { } k && (k.MatchId != match || k.AuthorityEpoch != epoch || k.KillerSlot >= 8 || k.VictimSlot >= 8
                    || k.KillerGeneration == 0 || k.VictimGeneration == 0 || k.VictimLifeId == 0 || k.ServerTick > tick)) throw Invalid();
            int[] points = Scores(), flagscores = Scores(), captured = Scores();
            var flags = new ReplayFlagState[Count()]; var ids = new HashSet<int>();
            for (int i = 0; i < flags.Length; i++)
            { int id = Id(ids); flags[i] = new(id, Vector(), Actor(), Actor(), Bool(), Bool(), Float(), Float()); }
            var nodes = new ReplayNodeState[Count()]; ids.Clear();
            for (int i = 0; i < nodes.Length; i++)
            { int id = Id(ids); sbyte team = Team(), occupying = Team(); nodes[i] = new(id, team, occupying, r.ReadByte(), Actor(), Float(), Float(), Float(), Float(), Float(), Bool(), Bool()); }
            var pickups = new ReplayPickupState[Count()]; ids.Clear();
            for (int i = 0; i < pickups.Length; i++)
            {
                short id = r.ReadInt16(); if (!ids.Add(id)) throw Invalid();
                pickups[i] = new(id, new(Bool(), Bool(), r.ReadUInt16(), r.ReadUInt16(), Team()));
            }
            var drops = new ReplayDropState[Count()]; ids.Clear();
            for (int i = 0; i < drops.Length; i++)
            {
                int identity = version >= 2 ? r.ReadInt32() : i + 1;
                var type = (ItemType)r.ReadByte(); Vector3 position = Vector(); int timer = r.ReadInt32();
                if ((uint)type >= 22 || timer is < -1 or > 1000000 || identity is < 1 or > int.MaxValue - 2 || !ids.Add(identity)) throw Invalid();
                drops[i] = new(type, position, timer, identity);
            }
            var doors = new ReplayDoorState[Count()]; ids.Clear();
            for (int i = 0; i < doors.Length; i++) doors[i] = new(Id(ids), (DoorFlags)r.ReadUInt32(), Bool(), Bool(), Bool());
            int hardpoint = version >= 3 ? r.ReadInt32() : -1;
            int hardpointTicks = version >= 3 ? r.ReadInt32() : 0;
            var chamber = EmptyChamber();
            if (version >= 3)
                for (int slot = 0; slot < 8; slot++)
                {
                    chamber[slot] = new(Actor(), r.ReadUInt16(), r.ReadUInt32());
                    if (chamber[slot].Actor.Slot != 255 && chamber[slot].Actor.Slot != slot) throw Invalid();
                }
            var objectiveStats = new int[32];
            if (version >= 4) for (int i = 0; i < 32; i++) objectiveStats[i] = r.ReadInt32();
            int nextTokenId = version >= 4 ? r.ReadInt32() : 1;
            var tokenStats = new int[48];
            if (version >= 4)
                for (int i = 0; i < tokenStats.Length; i++)
                { tokenStats[i] = r.ReadInt32(); if (tokenStats[i] < 0) throw Invalid(); }
            var tokens = new ReplayTokenState[version >= 4 ? Count() : 0]; ids.Clear();
            if (nextTokenId < 1 || tokens.Length > Multiplayer.TokenRules.MaximumTokens) throw Invalid();
            for (int i = 0; i < tokens.Length; i++)
            {
                int id = r.ReadInt32(); byte victim = r.ReadByte(); sbyte team = r.ReadSByte();
                int value = r.ReadInt32(); var position = Vector(); int timer = r.ReadInt32();
                if (id < 1 || id >= nextTokenId || !ids.Add(id) || victim >= 8 || team is < 0 or > 7
                    || value is < 1 or > 100000 || timer is < 1 or > Multiplayer.TokenRules.LifetimeTicks) throw Invalid();
                tokens[i] = new(id, victim, team, value, position, timer);
            }
            if (hardpoint < -1 || hardpointTicks is < 0 or > Multiplayer.HardpointRules.RotationTicks
                || hardpoint >= 0 && !Array.Exists(nodes, node => node.Id == hardpoint)) throw Invalid();
            if (stream.Position != stream.Length) throw Invalid();
            return new() { ObjectiveStats = objectiveStats, NextTokenId = nextTokenId, TokenStats = tokenStats, Tokens = tokens, MatchId = match, Epoch = epoch, Tick = tick, Prime = prime, Phase = phase, MatchTime = time,
                EndCause = cause, EndingKill = kill, TeamPoints = points, FlagScores = flagscores, NodesCaptured = captured,
                Flags = flags, Nodes = nodes, Pickups = pickups, Drops = drops, Doors = doors,
                ActiveHardpointId = hardpoint, HardpointTicksRemaining = hardpointTicks, Chamber = chamber };
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Truncated authoritative replay world.", ex); }
        int Count() { int n = r.ReadUInt16(); if (n > MaximumEntities) throw Invalid(); return n; }
        int Id(HashSet<int> ids) { int id = r.ReadInt32(); if (!ids.Add(id)) throw Invalid(); return id; }
        bool Bool() { byte v = r.ReadByte(); if (v > 1) throw Invalid(); return v != 0; }
        sbyte Team() { sbyte v = r.ReadSByte(); if (v is < -1 or >= 8) throw Invalid(); return v; }
        float Float() { float v = r.ReadSingle(); if (!float.IsFinite(v) || Math.Abs(v) > 1000000) throw Invalid(); return v; }
        Vector3 Vector() => new(Float(), Float(), Float());
        ReplayActorRef Actor()
        {
            var v = new ReplayActorRef(r.ReadByte(), r.ReadUInt16(), r.ReadUInt16());
            if (v.Slot != 255 && (v.Slot >= 8 || v.Generation == 0) || v.Slot == 255 && (v.Generation != 0 || v.Life != 0)) throw Invalid();
            return v;
        }
        int[] Scores() { var values = new int[8]; for (int i = 0; i < 8; i++) values[i] = r.ReadInt32(); return values; }
    }
    internal void Apply(Scene scene, ReplayReplicaState state)
    {
        if (!scene.Services.IsReplica) throw new InvalidOperationException("Replay world facts require a private scene.");
        foreach (var ammo in Chamber)
            ammo.Actor.Resolve(scene, state)?.ApplyChamberAmmo(ammo.Ammo, ammo.AcknowledgedFrame);
        scene.GameState.ActiveHardpointId = ActiveHardpointId;
        scene.GameState.HardpointTicksRemaining = HardpointTicksRemaining;
        scene.GameState.PrimeHunter = Prime.Resolve(scene, state)?.SlotIndex ?? -1;
        TeamPoints.CopyTo(scene.GameState.TeamPoints, 0); FlagScores.CopyTo(scene.GameState.OctolithScores, 0);
        NodesCaptured.CopyTo(scene.GameState.NodesCaptured, 0);
        foreach (var flag in Flags)
            if (scene.TryGetEntity(flag.Id, out var entity) && entity is OctolithFlagEntity target) target.ApplyReplayAuthority(flag, state);
        foreach (var node in Nodes)
            if (scene.TryGetEntity(node.Id, out var entity) && entity is NodeDefenseEntity target) target.ApplyReplayAuthority(node, state);
        foreach (var door in Doors)
            if (scene.TryGetEntity(door.Id, out var entity) && entity is DoorEntity target)
            {
                target.Flags = door.Flags; target.ConnectorInactive = door.ConnectorInactive;
                if (target.Portal != null) target.Portal.Active = door.Portal;
                if (target.ConnectorCollision != null) target.ConnectorCollision.Active = door.Collision;
            }
        var existing = new Dictionary<int, ItemInstanceEntity>();
        foreach (var item in scene.GetItemInstanceEntities())
            if (item.Owner == null && item.TokenId == 0)
            {
                if (item.Id >= -1) item.DespawnTimer = 0; // discard contact-inferred drops
                else existing.Add(-2 - item.Id, item);
            }
        foreach (var drop in Drops)
        {
            existing.Remove(drop.Identity, out var item);
            if (item != null && item.ItemType != drop.Type) throw new InvalidDataException("Replay drop identity changed type.");
            if (item == null)
            { item = ItemInstanceEntity.CreateReplayDrop(drop, scene); scene.AddEntity(item); }
            item.Position = drop.Position; item.DespawnTimer = drop.DespawnTimer;
        }
        foreach (var item in existing.Values) item.DespawnTimer = 0;
        ApplyTokens(scene);
    }
    internal static void CaptureObjectiveStats(SceneGameState state, int[] values)
    {
        for (int slot = 0; slot < 8; slot++)
        {
            var (a,b,c,d) = Multiplayer.MatchObjectiveReport.Values(state, slot);
            values[slot*4]=a; values[slot*4+1]=b; values[slot*4+2]=c; values[slot*4+3]=d;
        }
    }
    internal static ReplayTokenState CaptureToken(ItemInstanceEntity token) =>
        new(token.TokenId, (byte)token.TokenVictimSlot, (sbyte)token.TokenTeam, token.TokenValue, token.Position, token.DespawnTimer);
    internal static void CaptureTokenStats(SceneGameState state, int[] values)
    {
        state.TokenCarried.CopyTo(values, 0); state.TokenConfirms.CopyTo(values, 8); state.TokenDenies.CopyTo(values, 16);
        state.TokensCollected.CopyTo(values, 24); state.TokensBanked.CopyTo(values, 32); state.LargestBank.CopyTo(values, 40);
    }
    internal void ApplyTokens(Scene scene)
    {
        var state = scene.GameState; state.NextTokenId = NextTokenId;
        for (int slot = 0; slot < 8; slot++)
        {
            float time = state.Time[slot]; // continuous mode time belongs to the player snapshot
            Multiplayer.MatchObjectiveReport.Apply(state, slot, ObjectiveStats[slot*4], ObjectiveStats[slot*4+1],
                ObjectiveStats[slot*4+2], ObjectiveStats[slot*4+3]);
            state.Time[slot] = time;
        }
        Array.Copy(TokenStats, 0, state.TokenCarried, 0, 8); Array.Copy(TokenStats, 8, state.TokenConfirms, 0, 8);
        Array.Copy(TokenStats, 16, state.TokenDenies, 0, 8); Array.Copy(TokenStats, 24, state.TokensCollected, 0, 8);
        Array.Copy(TokenStats, 32, state.TokensBanked, 0, 8); Array.Copy(TokenStats, 40, state.LargestBank, 0, 8);
        foreach (var item in scene.GetItemInstanceEntities())
            if (item.TokenId > 0) item.DespawnTimer = 0;
        foreach (var token in Tokens.AsSpan(0, TokenCount < 0 ? Tokens.Length : TokenCount))
        {
            ItemInstanceEntity? target = null;
            foreach (var item in scene.GetItemInstanceEntities())
                if (item.TokenId == token.Id) { target = item; break; }
            if (target == null)
            {
                target = new ItemInstanceEntity(new(token.Position, ItemType.ArtifactKey, token.Timer),
                    scene.Room!.GetNodeRefByPosition(token.Position), scene) { TokenId = token.Id };
                scene.AddEntity(target);
            }
            target.TokenId = token.Id; target.TokenVictimSlot = token.Victim; target.TokenTeam = token.Team;
            target.TokenValue = token.Value; target.Position = token.Position; target.DespawnTimer = token.Timer;
        }
    }
    private static InvalidDataException Invalid() => new("Invalid authoritative replay world.");
}
