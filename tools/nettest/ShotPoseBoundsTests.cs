using System;
using MphRead;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ShotPoseBoundsTests
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string name)
        {
            count++;
            if (!condition) throw new InvalidOperationException("SHOT POSE " + name);
        }
        var shot = new FireEvent(1, 99, 98, 0, FireEventKind.PressFire,
            (byte)BeamType.Imperialist, 0, 0,
            FireEvent.FlagPose, new Vector3(4, 2, 1),
            Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ);
        var intent = new IntentPacket { Frame = 100, FireEventCount = 1 };
        bool Valid(FireEvent fire)
        {
            intent.FireEvents[0] = fire;
            return NetFireEvents.Validate(intent);
        }

        Check(Valid(shot), "canonical unit shot accepted");
        Check(Valid(shot with { Direction = new Vector3(1.5f, 0, 0) }),
            "modest source quantization tolerated");
        Check(!Valid(shot with { Direction = new Vector3(4, 0, 0) }),
            "oversized finite direction rejected");
        Check(!Valid(shot with { Aim = new Vector3(5, 0, 0) }),
            "oversized aim rejected");
        Check(!Valid(shot with { View = new Vector3(5, 0, 0) }),
            "oversized view rejected");
        Check(!Valid(shot with { Direction = Vector3.Zero }),
            "zero direction rejected");
        Check(!Valid(shot with { Aim = new Vector3(float.PositiveInfinity, 0, 0) }),
            "nonfinite aim rejected");
        Check(!Valid(shot with { Origin = new Vector3(float.NaN, 0, 0) }),
            "nonfinite origin rejected");
        Check(!Valid(shot with { Origin = new Vector3(100001f, 0, 0) }),
            "out-of-range source origin rejected");
        Check(Valid(shot with { PoseFlags = 0, Origin = default, Direction = default, Aim = default,
            View = default }), "legacy shot with no pose keeps compatibility");
        Check(!Valid(shot with { PoseFlags = (byte)(FireEvent.FlagPose | FireEvent.FlagReticle),
            Reticle = new Vector2(1.5f, 0.5f) }), "invalid reticle rejected");
        Check(Valid(shot with { PoseFlags = (byte)(FireEvent.FlagPose | FireEvent.FlagReticle),
            Reticle = new Vector2(0.5f, 0.5f) }), "valid reticle accepted");

        Console.WriteLine("SHOT POSE BOUNDS PASS " + count + " checks");
        return 0;
    }
}
