using System;
using System.Collections.Generic;
using MphRead.Mods.MapGen;

namespace MphRead.NetTest;

internal static class Q3MoverSourceTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool valid, string label)
        {
            checks++;
            if (!valid) throw new InvalidOperationException("Q3 MOVER SOURCE " + label);
        }
        Q3Model model = new(new[] { -64f, -32f, 0f }, new[] { 64f, 32f, 128f },
            0, 0, 0, 0);
        var data = new Dictionary<string, string>
        {
            ["classname"] = "func_bobbing", ["model"] = "*1",
            ["height"] = "35", ["speed"] = "4", ["phase"] = "0"
        };
        bool Read(out Q3MoverDescriptor mover, out string reason) =>
            Q3MoverSource.TryRead(data, model, 1, 0, 35, out mover, out reason);
        Check(Read(out var bob, out _), "bobbing source parses");
        Check(bob.Kind == Q3MoverKind.Bobbing && bob.ModelIndex == 1
            && MathF.Abs(bob.Distance - 1) < 0.001f, "world scale and model identity");
        Check(MathF.Abs(bob.ReferenceOffset(60).Y - 1) < 0.0001f,
            "quarter-cycle deterministic height");
        Check(bob.ReferenceOffset(60) == bob.ReferenceOffset(60),
            "identical 60Hz evaluation reproducible");
        data["classname"] = "func_door"; data["speed"] = "350"; data.Remove("height");
        Check(Read(out var door, out _), "source door parses");
        Check(door.ReferenceOffset(0) == OpenTK.Mathematics.Vector3.Zero,
            "door starts at canonical origin");
        Check(door.ReferenceOffset(360).LengthSquared <= door.Distance * door.Distance + 0.001f,
            "door never exceeds source travel bound");
        data["classname"] = "func_rotating"; data["speed"] = "90";
        Check(Read(out var rot, out _) && rot.Kind == Q3MoverKind.Rotating,
            "rotator source parses");
        Check(MathF.Abs(rot.ReferenceAngleDegrees(60) - 90f) < .00001f,
            "rotating model rate in 60Hz seconds");
        data["classname"] = "func_pendulum"; data["speed"] = "2"; data["height"] = "30";
        Check(Read(out var pendulum, out _) && pendulum.Kind == Q3MoverKind.Pendulum,
            "pendulum descriptor parses");
        Check(float.IsFinite(pendulum.ReferenceAngleDegrees(720)),
            "pendulum reference remains bounded");
        data["speed"] = "NaN"; Check(!Read(out _, out _), "nonfinite source rejects");
        data["speed"] = "-30"; Check(!Read(out _, out _), "negative speed rejects");
        data["speed"] = "4"; data["phase"] = "1000";
        Check(!Read(out _, out _), "out-of-range phase rejects");
        data["phase"] = "0"; data["spawnflags"] = "abc";
        Check(!Read(out _, out _), "invalid spawnflags rejects");
        data.Remove("spawnflags"); data["classname"] = "func_train";
        Check(!Read(out _, out _), "unknown mover fails closed");
        data["classname"] = "func_bobbing";
        Check(!Q3MoverSource.TryRead(data, model, 1, 0, 0, out _, out _),
            "zero import scale rejected");
        Check(!Q3MoverSource.TryRead(data, model, 0, 0, 35, out _, out _),
            "world model cannot be borrowed as moving entity");
        // Passenger and collision sweeps are immutable frame-based reference
        // math; never advance physical player ownership from a draw callback.
        var pose0 = Q3MoverPhysics.At(bob, 0);
        var pose60 = Q3MoverPhysics.At(bob, 60);
        var feet = new OpenTK.Mathematics.Vector3(0, bob.Maximum.Y, 0);
        Check(Q3MoverPhysics.TryCarry(bob, pose0, pose60, feet, .25f, .10f, out var lifted)
            && MathF.Abs(lifted.Y - feet.Y - 1f) < .0001f,
            "biped and alt-form feet can follow exact 60Hz bobbing height");
        Check(!Q3MoverPhysics.TryCarry(bob, pose0, pose60,
            feet + OpenTK.Mathematics.Vector3.UnitY * 3, .25f, .1f, out _),
            "mid-air passenger cannot attach to moving platform");
        Check(!Q3MoverPhysics.TryCarry(bob, pose0, pose60,
            feet + OpenTK.Mathematics.Vector3.UnitX * 500, .25f, .1f, out _),
            "distant player cannot be carried");
        Check(Q3MoverPhysics.SweptAabb(bob, pose0, pose60,
            new OpenTK.Mathematics.Vector3(0, bob.Maximum.Y + 2, 0),
            new OpenTK.Mathematics.Vector3(0, bob.Maximum.Y - 2, 0),
            .2f, out float impact) && impact is >= 0 and <= 1,
            "moving box collision broadphase uses swept relative motion");
        Check(!Q3MoverPhysics.SweptAabb(bob, pose0, pose60,
            new OpenTK.Mathematics.Vector3(100, 50, 100),
            new OpenTK.Mathematics.Vector3(100, 40, 100),
            .2f, out _), "nonintersecting moving hull fails closed");
        Check(!Q3MoverPhysics.TryCarry(bob, pose0, pose60,
            new OpenTK.Mathematics.Vector3(float.NaN), .25f, .1f, out _),
            "nonfinite player pose is refused");
        var pivot = (rot.Minimum + rot.Maximum) * 0.5f;
        var rotatingFeet = new OpenTK.Mathematics.Vector3(pivot.X + .25f, rot.Maximum.Y, pivot.Z);
        Check(Q3MoverPhysics.TryCarry(rot, Q3MoverPhysics.At(rot, 0),
            Q3MoverPhysics.At(rot, 60), rotatingFeet, .2f, .1f, out var spun)
            && MathF.Abs((spun - pivot).Length - (rotatingFeet - pivot).Length) < .0001f,
            "rotating platform applies rigid motion without expanding body radius");
        Check(!Q3MoverPhysics.SweptAabb(rot, Q3MoverPhysics.At(rot, 0),
            Q3MoverPhysics.At(rot, 60), rotatingFeet, rotatingFeet, .2f, out _),
            "rotating platform cannot use unsafe static-AABB authority approximation");
        Check(Q3MoverPhysics.At(bob, 60) == Q3MoverPhysics.At(bob, 60),
            "late join and replay frame replay yields identical deterministic pose");
        Console.WriteLine("Q3 MOVER SOURCE PASS " + checks + " checks");
        return 0;
    }
}
