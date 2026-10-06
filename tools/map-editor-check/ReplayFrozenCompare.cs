using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

// Read-only diagnostic for staged/synchronous continuation against accepted-fact
// live capture. Detached field strings identify differences without retaining Scenes.
internal static class ReplayFrozenCompare
{
    internal static void Run(string source)
    {
        Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
        var size = new Vector2i(256, 192); var recorder = new ReplayRecorder();
        using var capture = new ReplayLiveWorld(recorder);
        var reference = new Dictionary<uint, (string Hash, string Presentation, Dictionary<string, string> Fields)>();
        using var reader = DemoReader.Open(source)!;
        foreach (var packet in reader.Metadata!.Bootstrap.Packets.OrderBy(p => p[0] == (byte)PacketType.MatchState ? 0 : 1))
            ReplayLiveCaptureCheck.Accept(recorder, packet, 0);
        DemoRecord? pending = reader.ReadNext();
        uint frame = 0;
        while (pending != null)
        {
            while (pending is { } record && record.Frame <= frame)
            { ReplayLiveCaptureCheck.Accept(recorder, record.Data, frame); pending = reader.ReadNext(); }
            capture.Advance(frame, size);
            if (capture.LastError != null) throw new InvalidDataException(capture.LastError);
            if (frame >= 1200 && capture.World is { } world)
                reference[frame] = (ReplayStateHash.Compute(world.Scene, frame), world.Scene.ReplayPresentationHash(frame), Snapshot(world.Scene));
            frame++;
        }
        uint end = frame - 1, start = end - 250;
        if (!recorder.Timeline.TryFreeze(start, end, out var clip) || clip == null) throw new InvalidDataException("No frozen comparison clip.");
        using (clip)
        {
            foreach (bool staged in new[] { false, true })
            {
                using var job = staged ? ReplayPreparationJob.Clip(clip) : null;
                using var prepared = job?.WaitCompleted();
                using var player = staged ? new PassiveReplayPlayer(prepared!, size, ReplayPlayerOptions.Linear)
                    : new PassiveReplayPlayer(clip, size, ReplayPlayerOptions.Linear);
                uint baseline = clip.RestorePoint.RecordingFrame;
                Compare(player, baseline, staged ? "staged restore" : "sync restore");
                var timeout = Stopwatch.StartNew();
                while (!player.Ready)
                { if (timeout.Elapsed.TotalSeconds > 30) throw new TimeoutException(); player.Update(24, 1); Thread.Sleep(1); }
                Compare(player, player.Current.Session.CurrentFrame, staged ? "staged first visible" : "sync first visible");
                player.Update(); Compare(player, player.Current.Session.CurrentFrame, staged ? "staged next" : "sync next");
            }
            void Compare(PassiveReplayPlayer player, uint compared, string label)
            {
                string hash = ReplayStateHash.Compute(player.Current.Scene, compared);
                string presentation = player.Current.Scene.ReplayPresentationHash(compared);
                var expected = reference[compared];
                Console.WriteLine($"[{label}] frame={compared} gameplay={hash == expected.Hash} presentation={presentation == expected.Presentation}; hash={hash}, expected={expected.Hash}");
                if (hash == expected.Hash && presentation == expected.Presentation) return;
                var actual = Snapshot(player.Current.Scene);
                foreach (string key in expected.Fields.Keys.Concat(actual.Keys).Distinct().Order())
                {
                    expected.Fields.TryGetValue(key, out string? before); actual.TryGetValue(key, out string? after);
                    if (before != after) Console.WriteLine($"[{label}] {key}: expected {before ?? "<missing>"}, actual {after ?? "<missing>"}");
                }
            }
        }
    }
    private static Dictionary<string, string> Snapshot(Scene scene)
    {
        var values = new Dictionary<string, string>();
        void Add(object target, string prefix, params string[] names)
        {
            foreach (string name in names)
            {
                var type = target.GetType(); object? value = null;
                while (type != null)
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    var property = type.GetProperty(name, flags); var field = type.GetField(name, flags);
                    if (property != null) { value = property.GetValue(target); break; }
                    if (field != null) { value = field.GetValue(target); break; }
                    type = type.BaseType;
                }
                if (value is Array array)
                { for (int i = 0; i < array.Length; i++) values[prefix + name + "[" + i + "]"] = Format(array.GetValue(i)); }
                else values[prefix + name] = Format(value);
            }
        }
        Add(scene.GameState, "game.", "Mode", "MatchState", "MatchTime", "PrimeHunter", "NextTokenId", "TokenCarried", "TokenConfirms",
            "TokenDenies", "TokensCollected", "TokensBanked", "LargestBank", "ActiveHardpointId", "HardpointTicksRemaining",
            "Points", "Kills", "Deaths", "TeamPoints", "TeamKills", "TeamDeaths", "Time", "TeamTime");
        Add(scene.Random, "rng.", "Rng1", "Rng2");
        for (int slot = 0; slot < 8; slot++) Add(scene.Players.Items[slot], $"p{slot}.", "LoadFlags", "Hunter", "TeamIndex", "Position", "FacingVector",
            "Speed", "Health", "CurrentWeapon", "IsAltForm", "IsMorphing", "IsUnmorphing", "RespawnTimer", "DeathCountdown", "TimeSinceShot", "OctolithFlag");
        int ordinal = 0;
        foreach (var entity in scene.Entities)
        {
            if (entity is not (OctolithFlagEntity or NodeDefenseEntity or BeamProjectileEntity or BombEntity or ItemSpawnEntity or ItemInstanceEntity)) continue;
            string prefix = $"entity[{ordinal++}].";
            Add(entity, prefix, "Type", "Id", "Position");
            if (entity is BeamProjectileEntity) Add(entity, prefix, "Owner", "Target", "ModLaunchFrame", "ModLaunchMatch", "ModLaunchAuthority",
                "ModLaunchGeneration", "ModLaunchLife", "Beam", "BeamKind", "Flags", "Velocity", "Acceleration", "SpawnPosition", "Direction",
                "Age", "Lifespan", "Speed", "Homing", "Damage", "HeadshotDamage", "SplashDamage", "SplashRadius", "CylinderRadius",
                "ModContinuousPhase", "ModHasSharedContinuousPhase");
            if (entity is BombEntity) Add(entity, prefix, "Owner", "BombType", "Flags", "Countdown", "BombIndex", "Radius", "SelfRadius", "Damage", "EnemyDamage");
            if (entity is ItemSpawnEntity) Add(entity, prefix, "ModHealthState");
            if (entity is ItemInstanceEntity) Add(entity, prefix, "ItemType", "TokenId", "TokenVictimSlot", "TokenTeam", "TokenValue", "ParentId", "Owner", "DespawnTimer");
            if (entity is OctolithFlagEntity) Add(entity, prefix, "AtBase", "Carrier");
            if (entity is NodeDefenseEntity) Add(entity, prefix, "CurrentTeam", "OccupyingTeam", "Progress", "Contested", "InProgress", "CapturedPlayer", "OccupiedBy");
        }
        return values;
    }
    private static string Format(object? value) => value switch
    {
        null => "null",
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " (0x" + BitConverter.SingleToInt32Bits(f).ToString("X8") + ")",
        EntityBase entity => entity is PlayerEntity player ? "Player slot " + player.SlotIndex : entity.Type + " id " + entity.Id,
        _ => value.ToString() ?? "null"
    };
}
