// Asset-backed fixed-step movement kernel checks. Initial placement and trigger
// metadata are fixture seams; every measured trajectory uses ProcessMovement
// and native collision. Full input, snapshots and replay seeking are out of scope.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class Q3TraversalSceneTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly Vector3 Origin = new(0, 1000, 0);
    private static int _checks;
    private static void Require(bool good, string message)
    { if (!good) throw new InvalidOperationException(message); _checks++; }
    private static object? Call(PlayerEntity p, string name, params object[] args) =>
        typeof(PlayerEntity).GetMethod(name, Private)!.Invoke(p, args);
    private static void Field(PlayerEntity p, string name, object value)
    {
        FieldInfo field = typeof(PlayerEntity).GetField(name, Private)!;
        field.SetValue(p, field.FieldType.IsPrimitive ? Convert.ChangeType(value, field.FieldType) : value);
    }
    private static T Read<T>(PlayerEntity p, string name) =>
        (T)typeof(PlayerEntity).GetField(name, Private)!.GetValue(p)!;
    private static void Session(string name, object value) =>
        typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static void Flags(PlayerEntity p, PlayerFlags1 value) =>
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(p, value);
    private static Vector3 Feet(PlayerEntity p) => p.IsAltForm
        ? p.Volume.SpherePosition - Vector3.UnitY * p.Volume.SphereRadius : p.Position;
    private static void Place(PlayerEntity p, Vector3 feet)
    {
        CollisionVolume volume = Read<CollisionVolume>(p, "_volumeUnxf");
        Vector3 offset = p.IsAltForm ? volume.SpherePosition - Vector3.UnitY * volume.SphereRadius : Vector3.Zero;
        p.Reposition(feet - offset, Vector3.UnitZ, NodeRef.None);
        p.PrevPosition = p.Position;
        Field(p, "_volume", CollisionVolume.Move(volume, p.Position));
        p.Speed = Vector3.Zero; p.Acceleration = Vector3.Zero;
        Field(p, "_accelerationTimer", 0);
        Flags(p, p.Flags1 & ~(PlayerFlags1.Standing | PlayerFlags1.Grounded | PlayerFlags1.UsedJumpPad
            | PlayerFlags1.Boosting | PlayerFlags1.Morphing | PlayerFlags1.Unmorphing));
    }
    private static MapQ3Trigger Trigger(string kind, Vector3 center, Vector3 destination, int cooldown = 120) => new()
    {
        Kind = kind, SourceEntity = 30000, Min = new[] { center.X - .05f, center.Y - .05f, center.Z - .05f },
        Max = new[] { center.X + .05f, center.Y + .05f, center.Z + .05f },
        Destination = new[] { destination.X, destination.Y, destination.Z }, Gravity = 90, Yaw = 90,
        CooldownFrames = cooldown
    };
    private static void Bind(Scene scene, PlayerEntity p, params MapQ3Trigger[] triggers)
    {
        typeof(Q3Traversal).GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(Scene), typeof(string), typeof(System.Collections.Generic.IReadOnlyList<MapQ3Trigger>) }, null)!
            .Invoke(null, new object[] { scene, scene.Room!.Meta.Name, triggers });
        Call(p, "ModResetQ3Traversal");
    }
    private static void Move(PlayerEntity p, bool keepPrevious = false)
    {
        if (!keepPrevious) p.PrevPosition = p.Position;
        Call(p, "ProcessMovement");
        Require(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z), "finite native trajectory");
    }
    internal static int Run(string room)
    {
        _checks = 0;
        try
        {
            Require(ServerSim.Available(out string reason), reason);
            foreach (Hunter hunter in Hunters) RunHunter(room, hunter);
            Console.WriteLine($"Q3 TRAVERSAL SCENE: {_checks} checks PASS; seven hunters, biped/alt, 60 native fixed steps each; swept feet, boost, teleport cooldown/reset and traversal appendix roundtrip.");
            Console.WriteLine("Scope: asset-backed private movement kernel and native form activation; no full input/network or full replay-seek claim.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Q3 TRAVERSAL SCENE FAIL: " + ex); return 1; }
    }
    private static void RunHunter(string room, Hunter hunter)
    {
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        var roster = RosterPacket.Create();
        roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)hunter; roster.Names[0] = "Q3 " + hunter;
        var sim = new ServerSim();
        Require(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "native room start");
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Private)!.GetValue(sim)!;
            scene.Players.MainPlayerIndex = 0;
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            for (int i = 0; i < 120; i++) sim.Step();
            Require(sim.StepFailures == 0, "native spawn steps");
            Session(nameof(NetSession.Role), NetRole.Host); Session(nameof(NetSession.LocalSlot), 0);
            var p = scene.Players.Items[0];
            Require(p.Hunter == hunter && p.Health > 0 && p.ModIsInPlay && !p.IsBot, "live owned native hunter " + hunter);
            Trajectory(scene, p);
            // Exercise actual form startup/animation/volume transition before
            // sampling the stable alt movement kernel.
            Bind(scene, p); Place(p, Origin);
            p.ModStartFormSwitch();
            for (int i = 0; i < 180 && (!p.IsAltForm || p.IsMorphing); i++) sim.Step();
            Require(sim.StepFailures == 0 && p.IsAltForm && !p.IsMorphing, "native stable alt " + hunter);
            Trajectory(scene, p);
            SweptFeet(scene, p);
            if (hunter == Hunter.Samus) { Lifecycle(scene, p); ObserveOwnerProof(scene, p); RejectInvalidState(scene, p); }
            Console.WriteLine($"Q3 TRAVERSAL {hunter}: biped and native stable alt trajectory PASS");
        }
        finally { sim.Stop(); }
    }
    private static void Trajectory(Scene scene, PlayerEntity p)
    {
        var push = Trigger("push", Origin, Origin + new Vector3(60, 180, 30));
        Bind(scene, p, push); Place(p, Origin);
        Vector3 start = p.Position;
        bool interruptedBoost = p.Hunter == Hunter.Samus && p.IsAltForm;
        if (interruptedBoost)
        {
            Flags(p, p.Flags1 | PlayerFlags1.Boosting);
            p.Acceleration = new Vector3(.3f, 0, 0); Field(p, "_accelerationTimer", 4);
        }
        for (int step = 1; step <= 60; step++)
        {
            Move(p);
            if (step == 1 && interruptedBoost)
                Require(!p.Flags1.TestFlag(PlayerFlags1.Boosting) && p.Acceleration == Vector3.Zero
                    && Read<ushort>(p, "_accelerationTimer") == 0, "pad contact interrupts existing native boost");
            // Independent physical contract: a 90-unit/s^2 launch to an apex
            // 180 units up takes two seconds; 60 half-steps are one second.
            Vector3 expectedSpeed = new(1, 6 - .05f * step, .5f);
            Require((p.Speed - expectedSpeed).Length < .003f, $"{p.Hunter} alt={p.IsAltForm} gravity/velocity step {step}: {p.Speed}");
            Require(!p.Flags1.TestFlag(PlayerFlags1.Standing), "trajectory remains above native room geometry");
        }
        Require((p.Position - start - new Vector3(30, 134.25f, 15)).Length < .03f, "one-second target trajectory displacement");
        byte[] before = Appendix(p);
        Call(p, "ModResetQ3Traversal");
        using (var reader = new BinaryReader(new MemoryStream(before))) Call(p, "ModReadQ3Traversal", reader);
        Require(Appendix(p).SequenceEqual(before), "checkpoint traversal appendix restores launch and cooldown bytes");
        Move(p);
        Require(MathF.Abs(p.Speed.Y - 2.95f) < .003f, "restored launch continues native gravity next step");
    }
    private static byte[] Appendix(PlayerEntity p)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) Call(p, "ModWriteQ3Traversal", writer);
        return stream.ToArray();
    }
    private static void SweptFeet(Scene scene, PlayerEntity p)
    {
        Bind(scene, p, Trigger("push", Origin, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin + new Vector3(2, 0, 0));
        p.PrevPosition = p.Position - new Vector3(4, 0, 0);
        Move(p, keepPrevious: true);
        Require(p.Speed.Y > 5.9f, "swept feet crossing launches although both endpoints are outside");
        Bind(scene, p, Trigger("push", Origin + Vector3.UnitY * 1.5f, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin); Move(p);
        Require(p.Speed.Y <= 0 && Read<int>(p, "_q3LaunchFrames") == 0, "torso overlap without feet contact does not launch");
    }
    private static void RejectInvalidState(Scene scene, PlayerEntity p)
    {
        var unsafeDestination = new Vector3(0, scene.Room!.Meta.KillHeight - 10, 0);
        Bind(scene, p, Trigger("teleport", Origin, unsafeDestination));
        Place(p, Origin); p.Speed = new Vector3(.2f, .3f, .4f);
        Vector3 position = p.Position, speed = p.Speed;
        Call(p, "ModProcessQ3Triggers");
        Require(p.Position == position && p.Speed == speed, "teleport destination below kill height preserves native position and speed");
        byte[] valid = Appendix(p);
        void Reject(byte[] bytes, string name)
        {
            bool rejected = false;
            try
            {
                using var reader = new BinaryReader(new MemoryStream(bytes));
                Call(p, "ModReadQ3Traversal", reader);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
            { rejected = true; }
            Require(rejected, name);
            Require(Appendix(p).SequenceEqual(valid), "rejected appendix preserves valid traversal state");
        }
        byte[] invalidGravity = (byte[])valid.Clone();
        BitConverter.GetBytes(float.NaN).CopyTo(invalidGravity, 0);
        Reject(invalidGravity, "NaN traversal checkpoint gravity rejected");
        byte[] invalidCount = (byte[])valid.Clone();
        BitConverter.GetBytes(4097).CopyTo(invalidCount, 12);
        Reject(invalidCount, "mismatched/oversized traversal checkpoint trigger count rejected");
    }
    private static void ObserveOwnerProof(Scene scene, PlayerEntity p)
    {
        Bind(scene, p, Trigger("push", Origin, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin - Vector3.UnitX * 2);
        Session(nameof(NetSession.LocalSlot), 1);
        try
        {
            Require(NetSession.IsAuthority && NetHooks.IsPuppet(p), "authority observational fixture is a remote owned body");
            Vector3 position = p.Position, speed = p.Speed;
            Vector3 reported = position + Vector3.UnitX * 4;
            var audio = p.ModJumpPadAudioEventId;
            Call(p, "ModObserveQ3ReportedTraversal", reported, 100u);
            var first = p.ModJumpPadAudioEventId;
            Require(first != audio && Read<int[]>(p, "_q3Cooldowns")[0] == 120,
                "reported swept owner contact advances canonical pad event once");
            Call(p, "ModObserveQ3ReportedTraversal", reported, 100u);
            Require(p.ModJumpPadAudioEventId == first && Read<int[]>(p, "_q3Cooldowns")[0] == 120,
                "duplicate reported frame does not repeat event or age cooldown");
            Call(p, "ModObserveQ3ReportedTraversal", reported, 99u);
            Require(p.ModJumpPadAudioEventId == first && Read<int[]>(p, "_q3Cooldowns")[0] == 120,
                "stale reported frame is fenced");
            Call(p, "ModObserveQ3ReportedTraversal", reported, 101u);
            Require(p.ModJumpPadAudioEventId == first && Read<int[]>(p, "_q3Cooldowns")[0] == 119,
                "new owner report ages cooldown without repeating contact");
            Call(p, "ModObserveQ3ReportedTraversal", reported, 220u);
            Require(p.ModJumpPadAudioEventId != first, "owner pad proof can reactivate after cooldown expires");
            Require(p.Position == position && p.Speed == speed && Read<float>(p, "_q3LaunchGravity") == 0,
                "authority observation never authors competing movement");
        }
        finally { Session(nameof(NetSession.LocalSlot), 0); }
    }
    private static void Lifecycle(Scene scene, PlayerEntity p)
    {
        Bind(scene, p, Trigger("push", Origin, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin); Move(p);
        Flags(p, p.Flags1 | PlayerFlags1.Boosting);
        p.Acceleration = new Vector3(.3f, 0, 0); Field(p, "_accelerationTimer", 4);
        Move(p);
        Require(Read<float>(p, "_q3LaunchGravity") == 0, "active boost acceleration interrupts Q3 launch gravity");
        Bind(scene, p, Trigger("push", Origin, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin); Move(p);
        p.ActivateJumpPad(null!, new Vector3(0, 1, 0), 2);
        Require(Read<float>(p, "_q3LaunchGravity") == 0 && Read<int>(p, "_q3LaunchFrames") == 0,
            "native pad supersedes Q3 flight without retaining source gravity");
        Bind(scene, p, Trigger("push", Origin, Origin + new Vector3(60, 180, 30)));
        Place(p, Origin); Move(p);
        p.Teleport(p.Position + new Vector3(0, 0, 5), Vector3.UnitZ, NodeRef.None);
        Require(Read<float>(p, "_q3LaunchGravity") == 0 && Read<int>(p, "_q3LaunchFrames") == 0,
            "native teleport ends Q3 flight without retaining source gravity");
        var destination = Origin + new Vector3(20, 0, 0);
        var source = Trigger("teleport", Origin, destination, 3);
        var returnGate = Trigger("teleport", destination, Origin, 3); returnGate.SourceEntity = 30001;
        Bind(scene, p, source, returnGate); Place(p, Origin);
        Call(p, "ModProcessQ3Triggers");
        Require((Feet(p) - destination).Length < .001f && (p.FacingVector - Vector3.UnitX).Length < .001f,
            "teleport uses destination feet and authored yaw");
        Require(p.Speed == Vector3.Zero && p.Acceleration == Vector3.Zero && p.PrevPosition == p.Position,
            "teleport clears momentum and resets sweep origin");
        Require(Read<int[]>(p, "_q3Cooldowns").All(x => x == 3), "teleport fences every destination gate");
        Call(p, "ModProcessQ3Triggers"); Call(p, "ModProcessQ3Triggers");
        Require((Feet(p) - destination).Length < .001f, "destination gate cannot bounce before cooldown expiry");
        Call(p, "ModProcessQ3Triggers");
        Require((Feet(p) - Origin).Length < .001f, "destination gate activates on cooldown expiry");
        Call(p, "ModResetQ3Traversal");
        Require(Read<int[]>(p, "_q3Cooldowns").Length == 0 && Read<int>(p, "_q3LaunchFrames") == 0,
            "lifecycle reset clears traversal cache and flight state");
        Call(p, "ModProcessQ3Triggers");
        Require((Feet(p) - destination).Length < .001f, "lifecycle reset reloads bound metadata and permits fresh traversal");
    }
}
