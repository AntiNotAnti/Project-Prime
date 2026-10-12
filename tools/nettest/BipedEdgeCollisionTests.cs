using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Exercise the native response, not a duplicate of its normalization arithmetic.
internal static class BipedEdgeCollisionTests
{
    private static int _checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine($"BIPED EDGE PASS {description}");
    }
    private static bool Finite(Vector3 value)
        => Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

    private static void Contact(PlayerEntity player, Vector3 offset, string name,
        Vector3? plane = null, Vector3? previous = null, Vector3? speed = null, Vector3? expectedDirection = null)
    {
        float radius = Fixed.ToFloat(player.Values.BipedColRadius);
        float bottom = Fixed.ToFloat(player.Values.MinPickupHeight);
        float top = Fixed.ToFloat(player.Values.MaxPickupHeight);
        Check(top > bottom + 0.75f, "fixture edge intersects upper biped capsule");
        // A nonzero vertical edge intersects the capsule above its bottom cap.
        // Its X/Z projection is exactly the body's center in the zero case.
        var edge = new CollisionResult
        {
            Field0 = 1,
            EdgePoint1 = new Vector3(0, bottom + 0.75f, 0),
            EdgePoint2 = new Vector3(0, top + 0.5f, 0),
            Plane = new Vector4(plane ?? Vector3.UnitX, 0)
        };
        player.Position = offset;
        player.PrevPosition = previous ?? new Vector3(radius, 0, 0);
        player.Speed = speed ?? new Vector3(-0.1f, 0.2f, 0.025f);
        player.PrevSpeed = player.Speed;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!
            .SetValue(player, PlayerFlags1.None);
        player.HandleCollision(edge);
        Check(Finite(player.Position) && Finite(player.Speed), $"{name}: native contact preserves finite spatial state; position={player.Position} speed={player.Speed}");
        float horizontal = new Vector2(player.Position.X, player.Position.Z).Length;
        Check(MathF.Abs(horizontal - radius) < 0.0001f, $"{name}: native response separates by capsule radius");
        Check(MathF.Abs(player.Position.Y) < 0.0001f, $"{name}: lateral edge preserves height");
        if (expectedDirection is { } direction)
            Check((player.Position - direction.Normalized() * radius).Length < 0.0001f, $"{name}: degenerate contact honors available separating direction");
        if (offset != Vector3.Zero)
        {
            Vector3 expected = offset.Normalized() * radius;
            Check((player.Position - expected).Length < 0.0001f, $"{name}: ordinary response preserves contact direction");
        }
    }

    private static void CapContact(PlayerEntity player, float offset, string name)
    {
        float bottom = Fixed.ToFloat(player.Values.MinPickupHeight);
        float radius = Fixed.ToFloat(player.Values.BipedColRadius);
        // The nonzero vertical edge ends exactly at the center of the native
        // 0.5-unit bottom cap. This exercises the other zero-distance branch.
        var edge = new CollisionResult
        {
            Field0 = 1,
            EdgePoint1 = new Vector3(0, bottom - 0.5f, 0),
            EdgePoint2 = new Vector3(0, bottom + 0.5f, 0),
            Plane = new Vector4(0, 1, 0, 0)
        };
        player.Position = new Vector3(offset, 0, 0);
        player.PrevPosition = player.Position;
        player.Speed = player.PrevSpeed = Vector3.Zero;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!
            .SetValue(player, PlayerFlags1.None);
        player.HandleCollision(edge);
        Check(Finite(player.Position) && Finite(player.Speed), $"{name}: cap contact preserves finite spatial state");
        Vector3 expected = offset == 0 ? new Vector3(0, MathF.Min(0.5f, radius), 0) : new Vector3(0.5f, 0, 0);
        Check((player.Position - expected).Length < 0.0001f, $"{name}: cap response retains native penetration and vertical step limit; position={player.Position}");
    }

    public static int Run(string profile, string room)
    {
        _checks = 0;
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        try
        {
            for (int i = 0; i < 120; i++) sim.Step();
            var player = PlayerEntity.Players[0];
            Check(player.Hunter == Hunter.Samus, "fixture uses native Samus values");
            float halfRadius = Fixed.ToFloat(player.Values.BipedColRadius) / 2;
            Contact(player, new Vector3(halfRadius, 0, 0), "positive X neighbor");
            Contact(player, new Vector3(-halfRadius, 0, 0), "negative X neighbor");
            Contact(player, new Vector3(0, 0, halfRadius), "positive Z neighbor");
            Contact(player, Vector3.Zero, "exact edge center", expectedDirection: Vector3.UnitX);
            Contact(player, Vector3.Zero, "negative contact plane", plane: -Vector3.UnitX, expectedDirection: -Vector3.UnitX);
            Contact(player, Vector3.Zero, "diagonal contact plane", plane: new Vector3(-2, 4, 3), expectedDirection: new Vector3(-2, 0, 3));
            Contact(player, Vector3.Zero, "previous-position fallback", plane: Vector3.Zero,
                previous: -Vector3.UnitZ, expectedDirection: -Vector3.UnitZ);
            Contact(player, Vector3.Zero, "opposing-speed fallback", plane: Vector3.Zero,
                previous: Vector3.Zero, speed: -Vector3.UnitZ, expectedDirection: Vector3.UnitZ);
            Contact(player, Vector3.Zero, "stationary deterministic fallback", plane: Vector3.Zero,
                previous: Vector3.Zero, speed: Vector3.Zero, expectedDirection: Vector3.UnitX);
            CapContact(player, 0.125f, "bottom-cap neighbor");
            CapContact(player, 0, "exact bottom-cap center");
            Check(sim.StepFailures == 0, "scene fixture has no simulation failures");
            Console.WriteLine($"PASS: {_checks} biped edge collision assertions ({profile})");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); }
    }
}
