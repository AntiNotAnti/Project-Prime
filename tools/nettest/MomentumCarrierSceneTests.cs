using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Editor;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Network;
using MphRead.Utility;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Serialized native platform, real entity collision and native waypoint motion.
// Reflection only reads the standing owner; it never installs carrier contact.
internal static class MomentumCarrierSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private readonly record struct Tick(Vector3 Player, Vector3 Speed, Vector3 Platform, bool Carried);
    private sealed record Capture(List<Tick> Idle, int CarriedTicks, int MovingCarryTicks,
        bool Exited, bool Resumed, long BypassedSteps);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline carrier fixture");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            Capture native = Trace(false), momentum = Trace(true);
            Check(native.Idle.Count == momentum.Idle.Count, "paired carrier trace length");
            for (int i = 0; i < native.Idle.Count; i++)
                Check(native.Idle[i] == momentum.Idle[i], "exact native carried trajectory " + i);
            Check(native.CarriedTicks > 30 && momentum.CarriedTicks > 30, "genuine sustained standing EntityCollision");
            Check(native.MovingCarryTicks > 20 && momentum.MovingCarryTicks > 20, "moving native platform actually transports player");
            Check(momentum.BypassedSteps == 0, "adapter never runs while already carried");
            Check(native.Exited && momentum.Exited && momentum.Resumed, "walk off carrier naturally and resume solver");
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "asset-backed offline native serialized platform with waypoint motion, real standing contact and keyboard walk-off; no transport/render/replay acceptance",
                    native = Summary(native), momentum = Summary(momentum) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM CARRIER PASS {_checks} assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM CARRIER FAIL " + error); return 1; }
    }

    private static object Summary(Capture capture) => new { capture.CarriedTicks, capture.MovingCarryTicks,
        capture.Exited, capture.Resumed, capture.BypassedSteps,
        ticks = capture.Idle.Select(t => new { player = new[] { t.Player.X, t.Player.Y, t.Player.Z },
            speed = new[] { t.Speed.X, t.Speed.Y, t.Speed.Z }, platform = new[] { t.Platform.X, t.Platform.Y, t.Platform.Z }, t.Carried }) };

    private static Capture Trace(bool momentum)
    {
        Scene scene = MomentumSceneTests.Load(Hunter.Samus, momentum, out var keyboard, "TEST MOMENTUM COLLISION");
        try
        {
            var player = scene.Players.Main;
            var editor = new PlatformEntityEditor { Id = 31000, ModelId = 0, ParentId = -1,
                Position = new Vector3(-48, 3, -10), Up = Vector3.UnitY, Facing = Vector3.UnitZ,
                Active = true, PositionCount = 2, ForwardSpeed = .025f, BackwardSpeed = .025f,
                Flags = PlatformFlags.NoRecoil, BeamId = -1, BeamHitMsgTarget = -1,
                PlayerColMsgTarget = -1, DeadMsgTarget = -1, ScanMsgTarget = -1,
                PositionOffset = Vector3.Zero, ReverseType = 0 };
            for (int i = 0; i < 10; i++)
            {
                editor.Positions.Add(i == 1 ? new Vector3(-48, 5, -10) : editor.Position);
                editor.Rotations.Add(new Vector4(0, 0, 0, 1));
            }
            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using var writer = new BinaryWriter(stream);
                typeof(Repack).GetMethod("WriteEntity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { editor, writer });
                bytes = stream.ToArray();
            }
            var platform = new PlatformEntity(Read.ReadStruct<PlatformEntityData>(bytes), "", scene);
            scene.AddEntity(platform);
            Check(platform.EntityCollision[0]?.Collision != null, "native model supplies genuine collision");
            player.Reposition(new Vector3(-48, 10, -10), Vector3.UnitZ, NodeRef.None);
            player.PrevPosition = player.Position; player.Speed = Vector3.Zero;
            var ownerField = typeof(PlayerEntity).GetField("_standingEntCol", Hidden)!;
            bool Carried() => ReferenceEquals(ownerField.GetValue(player), platform.EntityCollision[0]);
            Vector3 PlatformPosition() => platform.EntityCollision[0]!.Transform.Row3.Xyz;
            var idle = new List<Tick>();
            int carried = 0, movingCarry = 0; long bypassed = 0;
            Vector3 previousPlatform = PlatformPosition(), previousPlayer = player.Position;
            for (int i = 0; i < 240; i++)
            {
                bool beforeContact = Carried(); long before = scene.DeveloperMomentumSteps;
                scene.OnSimulationFrame();
                if (beforeContact) bypassed += scene.DeveloperMomentumSteps - before;
                bool contact = Carried();
                if (contact)
                {
                    carried++;
                    Check(player.Flags1.TestFlag(PlayerFlags1.Standing), "carrier contact comes from native standing collision");
                    if (PlatformPosition() != previousPlatform && player.Position != previousPlayer) movingCarry++;
                    Check(!player.ModMomentumEligible, "native standing owner excludes adapter");
                }
                idle.Add(new(player.Position, player.Speed, PlatformPosition(), contact));
                previousPlatform = PlatformPosition(); previousPlayer = player.Position;
            }
            Check(Carried(), "standing on moving platform before departure input");
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            setKey(keyboard, Keys.W, true);
            bool exited = false, resumed = false;
            for (int i = 0; i < 240; i++)
            {
                bool beforeContact = Carried(); long before = scene.DeveloperMomentumSteps;
                scene.OnSimulationFrame();
                if (beforeContact) Check(scene.DeveloperMomentumSteps == before, "departure remains native while carrier owns body");
                exited |= !Carried();
                resumed |= exited && scene.DeveloperMomentumSteps > before;
            }
            setKey(keyboard, Keys.W, false);
            return new(idle, carried, movingCarry, exited, resumed, bypassed);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
