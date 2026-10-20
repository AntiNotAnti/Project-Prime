using System;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-free numerical/profile metadata contracts. Actual contact, damage and
// arbitration are covered separately by the admitted SelfSplashTests scene.
internal static class Quake3AdvancedRulesTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static void Reject(Vector3 origin, Vector3 body, float radius)
    {
        bool refused = false;
        try { Quake3MovementProfileV2.RocketImpulse(20, origin, body, radius); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        Check(refused, "nonfinite geometry or nonpositive radius rejected");
    }
    internal static int Run()
    {
        try
        {
            _checks = 0;
            NumericalContract(); AllocationContract(); RecordedProfileContract();
            Console.WriteLine($"QUAKE3 ADVANCED RULES PASS {_checks} assertions; asset-free rocket/profile contracts, no gameplay qualification");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("QUAKE3 ADVANCED RULES FAIL " + error); return 1; }
    }
    private static void NumericalContract()
    {
        Check(Quake3MovementProfileV1.ProfileVersion == 1 && Quake3MovementProfileV2.ProfileVersion == 2
            && MovementModelRules.ProfileVersion(MovementModel.Quake3) == 2,
            "current Q3 rule selects profile two while historical profile one remains immutable");
        Check(Quake3MovementProfileV2.RocketImpulsePerDamage == .025f
            && Quake3MovementProfileV2.MaximumRocketImpulse == 1.2f
            && Quake3MovementProfileV2.RocketBodyAimHeight == .5f,
            "reviewed native-unit rocket constants");
        Check(Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, Vector3.Zero, 4)
            == new Vector3(0, .5f, 0), "coincident blast uses upward half-unit impulse for twenty damage");
        Check(Quake3MovementProfileV2.RocketImpulse(0, Vector3.Zero, Vector3.Zero, 4) == Vector3.Zero,
            "zero damage supplies no impulse");
        Check(Quake3MovementProfileV2.RocketImpulse(uint.MaxValue, Vector3.Zero, Vector3.Zero, 4)
            == new Vector3(0, 1.2f, 0), "maximum uint damage saturates at finite reviewed bound");
        Vector3 centre = Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, Vector3.Zero, 4);
        Vector3 half = Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, Vector3.UnitX * 2, 4);
        Check(MathF.Abs(half.Length - centre.Length / 2) < .000001f,
            "half-radius body receives half-strength impulse independently of damage curve");
        Check(half.X > 0 && half.Y > 0 && half.Z == 0 && MathF.Abs(half.X / half.Y - 4) < .000001f,
            "radial aim uses actual horizontal displacement and half-unit body height");
        Check(Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, Vector3.UnitX * 4, 4) == Vector3.Zero
            && Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, Vector3.UnitX * 5, 4) == Vector3.Zero,
            "radius boundary and outside body receive no impulse");
        Vector3 above = Quake3MovementProfileV2.RocketImpulse(20, Vector3.UnitY * 2, Vector3.Zero, 4);
        Check(above.Y < 0 && above.X == 0 && above.Z == 0,
            "blast above body preserves full downward radial component");
        Vector3 singular = Quake3MovementProfileV2.RocketImpulse(20, Vector3.UnitY * .5f, Vector3.Zero, 4);
        Check(Finite(singular) && singular.Y > 0 && singular.X == 0 && singular.Z == 0,
            "zero-length body aim has finite upward fallback");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (Vector3 bad in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
            { Reject(bad, Vector3.Zero, 4); Reject(Vector3.Zero, bad, 4); }
            Reject(Vector3.Zero, Vector3.Zero, invalid);
        }
        Reject(Vector3.Zero, Vector3.Zero, 0); Reject(Vector3.Zero, Vector3.Zero, -1);
        foreach (uint damage in new uint[] { 0, 1, 20, 100, ushort.MaxValue, uint.MaxValue })
        foreach (float radius in new[] { float.Epsilon, .25f, 1f, 100f, float.MaxValue })
        foreach (Vector3 body in new[] { Vector3.Zero, new Vector3(1, -2, 3), new Vector3(float.MaxValue), new Vector3(-float.MaxValue) })
        {
            Vector3 impulse = Quake3MovementProfileV2.RocketImpulse(damage, -body, body, radius);
            Check(Finite(impulse) && impulse.Length <= 1.200001f,
                "finite extreme input produces finite impulse within shared network candidate bound");
        }
        var input = new MomentumInput(new Vector2(-1, 1), Vector3.UnitZ, -Vector3.UnitX,
            Quake3MovementProfileV1.GroundSpeed, false, 1f / 60);
        Vector3 incoming = new(4, -.125f, 7);
        Check(Quake3MovementProfileV2.Tuning == Quake3MovementProfileV1.Tuning
            && Quake3MovementProfileV2.Step(incoming, input) == Quake3MovementProfileV1.Step(incoming, input),
            "profile two retains exact immutable profile-one horizontal solver");
    }
    private static void AllocationContract()
    {
        Vector3 sum = Vector3.Zero;
        for (int i = 0; i < 10000; i++)
            sum += Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, new Vector3(1, .5f, 1), 4);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
            sum += Quake3MovementProfileV2.RocketImpulse(20, Vector3.Zero, new Vector3(1, .5f, 1), 4);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && Finite(sum), "warmed rocket impulse hot path allocates zero bytes");
    }
    private static void RecordedProfileContract()
    {
        const string room = "MP3 PROVING GROUND";
        foreach (byte protocol in new byte[] { 60, 61, 62 })
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3 })
        {
            var match = new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = room,
                Mode = (byte)GameMode.Battle, RuleBits = (uint)MovementModelRules.Modifier(model) };
            byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
            match.Write(packet.AsSpan(1));
            var metadata = new ReplayMetadata { ProtocolVersion = protocol, RoomKey = room,
                Bootstrap = new ReplayBootstrap { Packets = new[] { packet } } };
            int expected = model == MovementModel.Quake3 ? protocol >= 62 ? 2 : 1
                : MovementModelRules.ProfileVersion(model);
            Check(metadata.RecordedMovementModel == model && metadata.RecordedMovementProfileVersion == expected,
                $"actual recorded protocol {protocol} {model} bootstrap reports historical profile {expected}");
        }
        Check(new ReplayMetadata { ProtocolVersion = 62, RoomKey = room }.RecordedMovementProfileVersion == null,
            "missing recorded movement identity remains unknown");
    }
}
