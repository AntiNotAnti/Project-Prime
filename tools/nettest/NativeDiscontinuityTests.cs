// Real room/models + production native interactions -> actual authority snapshot
// bytes and rewind capture -> observer presentation. No invented accepted poses.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class NativeDiscontinuityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Require(bool good, string name)
    { if (!good) throw new InvalidOperationException(name); _checks++; }
    private static void Session(string name, object value) => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void ReadFrame(double frame) => typeof(NetSmoothing).GetField("_readFrame", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, frame);
    private record Sample(uint Frame, PlayerState State);

    public static int Run(string room)
    {
        _checks = 0;
        Require(ServerSim.Available(out string reason), reason);
        var wire = new List<Sample>();
        void Sink(ReadOnlySpan<byte> bytes)
        {
            var header = SnapshotHeader.Read(bytes);
            for (int i = 0; i < header.PlayerCount; i++)
            {
                var state = PlayerState.Read(bytes.Slice(SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size));
                if (state.SlotIndex == 1) wire.Add(new(header.Frame, state));
            }
        }
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
        for (byte i = 0; i < 2; i++)
        { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)(i == 1 ? Hunter.Weavel : Hunter.Samus); roster.Names[i] = "BOUNDARY" + i; }
        var sim = new ServerSim();
        Require(sim.Start(room, GameMode.Battle, 2, Sink, () => { }, roster: roster), "asset room started");
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            GameState.MatchTime = 3600; GameState.PointGoal = 10000;
            void Publish()
            {
                int before = wire.Count; NetSession.BroadcastSnapshot();
                Require(wire.Count > before, "actual published snapshot contains target");
            }
            bool spawning = true;
            void Step()
            { sim.Step(); Require(sim.StepFailures == 0, "native step did not throw"); if (!spawning) Publish(); }
            for (int i = 0; i < 120; i++) Step();
            spawning = false;
            var player = scene.Players.Items[1];
            Require(player.Health > 0 && player.ModIsInPlay && player.Hunter == Hunter.Weavel, "native live target spawned");
            Sample Capture()
            {
                Publish(); var sample = wire[^1];
                Require(sample.State.Position == player.Position && sample.State.Speed == player.Speed, "post-event native pose/speed serialized exactly");
                Require(NetUnlagged.TryHistoricalPose(player, sample.Frame, out var pose) && pose.Position == sample.State.Position,
                    "same snapshot frame names current native rewind pose");
                return sample;
            }

            // Fixture initial placement only; native teleporter performs the event.
            // Invisible native entity avoids camera/GL dependence. Destination is
            // another map spawn, not a per-frame position override.
            Vector3 source = player.Position;
            Vector3 destination = scene.Players.Items[0].Position;
            Require((destination - source).Length > 4, "native spawn pair is a hard discontinuity");
            var header = new EntityDataHeader((ushort)EntityType.Teleporter, 30000, source, Vector3.UnitY, Vector3.UnitZ);
            var teleData = new TeleporterEntityData(header, 0, 0, 8, 1, 1, null, destination.ToVector3Fx(), null);
            var teleporter = new TeleporterEntity(teleData, "", scene, forceMultiplayer: true);
            teleporter.Initialize();
            // Native pads start already-triggered. Exit, process once, then enter.
            player.Reposition(source + new Vector3(8, 0, 0), Vector3.UnitZ, player.NodeRef);
            Step(); teleporter.Process(); player.Reposition(source, Vector3.UnitZ, player.NodeRef); Step();
            var beforeTeleport = Capture();
            teleporter.Process(); Step();
            Require((player.Position - beforeTeleport.State.Position).Length > 4, "native teleporter actually changed world position");
            var afterTeleport = Capture();
            Boundary(player, beforeTeleport, afterTeleport, hard: true, form: false);

            // Volume contact runs JumpPadEntity.Process and ActivateJumpPad, not
            // a test-only velocity write. Native launch is continuous motion:
            // do NOT demand a snap merely because the audio/event sequence changes.
            Vector3 padPos = player.Position;
            var volume = new RawCollisionVolume(Vector3.UnitY.ToVector3Fx(), new Vector3Fx(0, 0, 0),
                new Fixed(Fixed.ToInt(2)), new Fixed(Fixed.ToInt(3)));
            var padHeader = new EntityDataHeader((ushort)EntityType.JumpPad, 30001, padPos, Vector3.UnitY, Vector3.UnitZ);
            var padData = new JumpPadEntityData(padHeader, -1, volume, Vector3.UnitY.ToVector3Fx(),
                new Fixed(Fixed.ToInt(.3f)), 0, 20, 1, 0, 0, TriggerFlags.PlayerBiped);
            var pad = new JumpPadEntity(padData, "", scene); pad.Initialize();
            var beforePad = Capture(); pad.Process();
            Require(player.Speed.Y > 0 && player.ModJumpPadAudioEventId != beforePad.State.JumpPadEventId, "native contact launched and advanced canonical pad sequence");
            Step(); var launch = Capture(); Step(); var afterPad = Capture();
            Require(afterPad.State.Position != launch.State.Position, "native simulation moved launched body");
            Boundary(player, launch, afterPad, hard: false, form: false);

            // Native damage endpoint with an explicit direction proves impulse
            // installation and snapshot order. It is not a physical projectile
            // collision test and must be described that way.
            var beforeDamage = Capture();
            player.TakeDamage(1, DamageFlags.IgnoreInvuln, new Vector3(.2f, .1f, 0), scene.Players.Items[0]);
            Step(); var damaged = Capture();
            Require(damaged.State.Health < beforeDamage.State.Health && damaged.State.Speed != beforeDamage.State.Speed,
                "native finalized damage installed real health/impulse before snapshot");
            Step(); var afterDamage = Capture();
            Boundary(player, damaged, afterDamage, hard: false, form: false);

            // Real native transition plus post-event serialized phase/form.
            var beforeForm = Capture(); player.ModStartFormSwitch(); Step();
            var transition = Capture();
            Require(player.IsMorphing && transition.State.Form.Phase == FormPhase.Morphing, "native form transition captured at entry");
            for (int i = 0; i < 100 && player.IsMorphing; i++) Step();
            var settled = Capture();
            Require(player.IsAltForm && settled.State.Form.Phase == FormPhase.Alt, "native form settled");
            // Adjacent source frames around actual FlagAltForm edge should be
            // selected from wire, not synthesized or renumbered. Locate a native
            // edge and assert held lower state only when adjacent frames exist.
            bool found = false;
            for (int i = 1; i < wire.Count; i++)
                if (wire[i].Frame == wire[i-1].Frame + 1 && ((wire[i].State.Flags ^ wire[i-1].State.Flags) & PlayerState.FlagAltForm) != 0)
                { Boundary(player, wire[i-1], wire[i], hard: false, form: true); found = true; break; }
            Require(found, "actual adjacent snapshot form boundary available");
            Console.WriteLine($"NATIVE DISCONTINUITIES: {_checks} checks PASS (teleport, pad, directed native damage, Weavel form; snapshots/history/presentation)");
            return 0;
        }
        finally { sim.Stop(); NetSmoothing.Reset(); }
    }

    private static void Boundary(PlayerEntity player, Sample a, Sample b, bool hard, bool form)
    {
        Require(b.Frame == a.Frame + 1, "native event brackets are adjacent authority frames");
        var role = NetSession.Role; bool authority = NetSession.IsAuthority;
        try
        {
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false);
            NetSmoothing.Reset();
            long now = Stopwatch.GetTimestamp();
            NetSmoothing.Record(a.Frame, new[] { a.State }, now);
            NetSmoothing.Record(b.Frame, new[] { b.State }, now + Stopwatch.Frequency / 60);
            ReadFrame(a.Frame + .5);
            Require(NetSmoothing.Sample(player.SlotIndex, out var halfway, out _), "native snapshots available to observer");
            Vector3 expected = hard || form ? a.State.Position : (a.State.Position + b.State.Position) * .5f;
            Require((halfway - expected).Length < .0001f, "native discontinuity holds lower pose; continuous impulse interpolates only real endpoints");
            ReadFrame(b.Frame);
            Require(NetSmoothing.Sample(player.SlotIndex, out var final, out _) && final == b.State.Position,
                "authority upper boundary presents exact new native position");
        }
        finally { Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority); NetSmoothing.Reset(); }
    }
}
