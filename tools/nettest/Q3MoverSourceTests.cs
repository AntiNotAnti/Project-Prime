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
        Console.WriteLine("Q3 MOVER SOURCE PASS " + checks + " checks");
        return 0;
    }
}
