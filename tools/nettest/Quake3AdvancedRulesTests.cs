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
            CapsuleContractV3(); SolverContractV3(); AllocationContractV3();
            Console.WriteLine($"QUAKE3 ADVANCED RULES PASS {_checks} assertions; asset-free rocket/profile contracts, no gameplay qualification");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("QUAKE3 ADVANCED RULES FAIL " + error); return 1; }
    }
    private static void NumericalContract()
    {
        Check(Quake3MovementProfileV1.ProfileVersion == 1 && Quake3MovementProfileV2.ProfileVersion == 2
            && MovementModelRules.ProfileVersion(MovementModel.Quake3) == 3,
            "current Q3 rule selects profile three while historical profiles one and two remain immutable");
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
        foreach (byte protocol in new byte[] { 60, 61, 62, 70, 71 })
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3 })
        {
            var match = new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = room,
                Mode = (byte)GameMode.Battle, RuleBits = (uint)MovementModelRules.Modifier(model) };
            byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
            match.Write(packet.AsSpan(1));
            var metadata = new ReplayMetadata { ProtocolVersion = protocol, RoomKey = room,
                Bootstrap = new ReplayBootstrap { Packets = new[] { packet } } };
            int expected = model == MovementModel.Quake3 ? protocol >= 71 ? 3 : protocol >= 62 ? 2 : 1
                : MovementModelRules.ProfileVersion(model);
            Check(metadata.RecordedMovementModel == model && metadata.RecordedMovementProfileVersion == expected,
                $"actual recorded protocol {protocol} {model} bootstrap reports historical profile {expected}");
        }
        Check(new ReplayMetadata { ProtocolVersion = 62, RoomKey = room }.RecordedMovementProfileVersion == null,
            "missing recorded movement identity remains unknown");
    }

    private static void Near(float actual, float expected, string label, float tolerance = .000002f)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance,
            $"{label}: {actual} != {expected}");

    private static void RejectCapsuleV3(Vector3 origin, Vector3 body,
        float bottom = -.5f, float top = 1.1f, float bodyRadius = .5f)
    {
        bool distanceRefused = false, impulseRefused = false;
        try { Quake3MovementProfileV3.BlastDistance(origin, body, bottom, top, bodyRadius); }
        catch (ArgumentOutOfRangeException) { distanceRefused = true; }
        try { Quake3MovementProfileV3.RocketImpulse(20, origin, body, 4, bottom, top, bodyRadius); }
        catch (ArgumentOutOfRangeException) { impulseRefused = true; }
        Check(distanceRefused && impulseRefused, "V3 distance and impulse reject the same invalid capsule geometry");
    }

    private static void CapsuleContractV3()
    {
        Check(Quake3MovementProfileV3.ProfileVersion == 3
            && Quake3MovementProfileV3.GroundSpeed == Quake3MovementProfileV2.GroundSpeed
            && Quake3MovementProfileV3.Tuning == Quake3MovementProfileV2.Tuning,
            "V3 selects capsule blast geometry while retaining exact V2 movement coefficients");
        // Standing capsule has radius .5 and centreline y=0..0.6. These
        // expected distances come from side/cap offsets and a 3-4-5 triangle.
        foreach (var sample in new[] {
            (Origin: Vector3.Zero, Distance: 0f),
            (Origin: new Vector3(0, .3f, 0), Distance: 0f),
            (Origin: new Vector3(.3f, .3f, .4f), Distance: 0f),
            (Origin: new Vector3(1, .3f, 0), Distance: .5f),
            (Origin: new Vector3(0, -1, 0), Distance: .5f),
            (Origin: new Vector3(0, 2.1f, 0), Distance: 1f),
            (Origin: new Vector3(1.5f, -2, 0), Distance: 2f),
            (Origin: new Vector3(1.5f, 2.6f, 0), Distance: 2f) })
        {
            Near(Quake3MovementProfileV3.BlastDistance(sample.Origin, Vector3.Zero), sample.Distance,
                "V3 hand-computed standing capsule distance");
            Vector3 translation = new(3, -2, 5);
            Near(Quake3MovementProfileV3.BlastDistance(sample.Origin + translation, translation), sample.Distance,
                "V3 capsule distance is translation invariant");
        }
        // A minimum-height crouched capsule is a sphere centred at body.y.
        Near(Quake3MovementProfileV3.BlastDistance(new Vector3(0, 1.1f, 0), Vector3.Zero, -.5f, .5f, .5f), .6f,
            "shortened capsule exposes the expected space above the crouched head");
        Near(Quake3MovementProfileV3.BlastDistance(new Vector3(1.5f, 2, 0), Vector3.Zero, -.5f, .5f, .5f), 2f,
            "crouched spherical cap retains hand-computed diagonal distance");
        float crouchedTop = TraversalStance.Top(-.5f, 1.1f, .5f, true);
        Near(Quake3MovementProfileV3.BlastDistance(new Vector3(0, 2.1f, 0), Vector3.Zero,
            -.5f, crouchedTop, .5f), 2.1f - crouchedTop,
            "accepted proportional crouch height sets the actual upper blast boundary");
        Check(Quake3MovementProfileV3.RocketImpulse(0, Vector3.Zero, Vector3.Zero, 4) == Vector3.Zero,
            "V3 zero base splash supplies no physical impulse");
        Check(Quake3MovementProfileV3.RocketImpulse(20, Vector3.Zero, Vector3.Zero, 4) == new Vector3(0, .5f, 0),
            "V3 inside-capsule blast receives full upward physical strength");
        Check(Quake3MovementProfileV3.RocketImpulse(20, Vector3.UnitY * .5f, Vector3.Zero, 4)
            == new Vector3(0, .5f, 0), "V3 zero-length aim direction has the reviewed upward fallback");
        Vector3 half = Quake3MovementProfileV3.RocketImpulse(20, new Vector3(-2.5f, 0, 0), Vector3.Zero, 4);
        Near(half.Length, .25f, "capsule surface distance of two gives half strength in radius four");
        Near(half.X / half.Y, 5, "V3 radial direction retains actual displacement and existing body aim offset");
        Vector3 above = Quake3MovementProfileV3.RocketImpulse(20, new Vector3(0, 2.1f, 0), Vector3.Zero, 4);
        Near(above.Y, -.375f, "V3 upper-cap distance attenuates a downward blast without flipping direction");
        foreach (float x in new[] { -4.5f, -5f })
            Check(Quake3MovementProfileV3.RocketImpulse(20, new Vector3(x, 0, 0), Vector3.Zero, 4) == Vector3.Zero,
                "V3 blast radius is measured from the capsule surface and excludes its boundary");
        Check(Quake3MovementProfileV3.RocketImpulse(20, new Vector3(-4.5f + 1f / 1024, 0, 0), Vector3.Zero, 4).Length > 0,
            "a blast immediately inside the capsule-distance boundary retains positive strength");
        Vector3 maximum = Quake3MovementProfileV3.RocketImpulse(uint.MaxValue, Vector3.Zero, Vector3.Zero, 4);
        Check(maximum == new Vector3(0, 1.2f, 0) && maximum.Length < 1.5f,
            "V3 maximum physical impulse stays at 1.2 below the 1.5 wire candidate bound");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (Vector3 bad in new[] { new Vector3(invalid, 0, 0), new Vector3(0, invalid, 0), new Vector3(0, 0, invalid) })
            { RejectCapsuleV3(bad, Vector3.Zero); RejectCapsuleV3(Vector3.Zero, bad); }
            RejectCapsuleV3(Vector3.Zero, Vector3.Zero, bottom: invalid);
            RejectCapsuleV3(Vector3.Zero, Vector3.Zero, top: invalid);
            RejectCapsuleV3(Vector3.Zero, Vector3.Zero, bodyRadius: invalid);
        }
        foreach (var shape in new[] { (Bottom: -.5f, Top: 1.1f, Radius: 0f),
            (Bottom: -.5f, Top: 1.1f, Radius: -1f), (Bottom: 1f, Top: 0f, Radius: .5f),
            (Bottom: -.5f, Top: .4f, Radius: .5f) })
            RejectCapsuleV3(Vector3.Zero, Vector3.Zero, shape.Bottom, shape.Top, shape.Radius);
        foreach (float radius in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            bool refused = false;
            try { Quake3MovementProfileV3.RocketImpulse(20, Vector3.Zero, Vector3.Zero, radius); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check(refused, "V3 rejects nonfinite and nonpositive blast radii");
        }
        foreach (uint damage in new uint[] { 0, 1, 20, 100, uint.MaxValue })
        foreach (float radius in new[] { float.Epsilon, .25f, 4f, float.MaxValue })
        foreach (Vector3 body in new[] { Vector3.Zero, new Vector3(1, -2, 3), new Vector3(float.MaxValue), new Vector3(-float.MaxValue) })
        {
            Vector3 impulse = Quake3MovementProfileV3.RocketImpulse(damage, -body, body, radius);
            Check(Finite(impulse) && impulse.Length <= 1.200001f,
                "V3 finite extreme inputs produce finite physically bounded impulses");
        }
    }

    private static void SolverContractV3()
    {
        foreach (bool grounded in new[] { false, true })
        foreach (Vector2 axes in new[] { Vector2.Zero, Vector2.UnitX, new Vector2(-1, 1), new Vector2(.25f, -.5f) })
        foreach (float dt in new[] { 0f, 1f / 120, 1f / 60, .125f })
        foreach (float y in new[] { -.25f, 0f, .375f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) })
        foreach (Vector3 horizontal in new[] { Vector3.Zero, new Vector3(4, 0, 7), new Vector3(-30, 0, 50) })
        {
            var input = new MomentumInput(axes, Vector3.UnitZ, -Vector3.UnitX,
                Quake3MovementProfileV3.GroundSpeed, grounded, dt);
            Vector3 incoming = horizontal.WithY(y);
            var current = Quake3MovementProfileV3.Step(incoming, input);
            Check(current == Quake3MovementProfileV2.Step(incoming, input),
                "V3 preserves exact V2 solver velocity and diagnostics across valid inputs");
            Check(BitConverter.SingleToInt32Bits(current.Velocity.Y) == BitConverter.SingleToInt32Bits(y),
                "V3 horizontal solver preserves native vertical bits including signed zero");
        }
        var neutralAir = new MomentumInput(Vector2.Zero, Vector3.UnitZ, -Vector3.UnitX,
            Quake3MovementProfileV3.GroundSpeed, false, 1f / 60);
        Vector3 oldNative = new(.7f, -.125f, -.9f);
        Vector3 blast = Quake3MovementProfileV3.RocketImpulse(20, new Vector3(-1, -1, 0), Vector3.Zero, 4);
        Vector3 added = oldNative + blast;
        Vector3 solverInput = new(added.X * 30, added.Y, added.Z * 30);
        Vector3 retained = Quake3MovementProfileV3.Step(solverInput, neutralAir).Velocity;
        Check(retained == solverInput && retained.Xz.Length > Quake3MovementProfileV3.GroundSpeed,
            "V3 neutral airborne step retains the entire additive blast and incoming planar overspeed");
        Check(BitConverter.SingleToInt32Bits(retained.Y) == BitConverter.SingleToInt32Bits(oldNative.Y + blast.Y),
            "additive blast Y survives horizontal movement without a replacement jump impulse");
    }

    private static void AllocationContractV3()
    {
        Vector3 origin = new(-1, -.75f, 1), body = Vector3.Zero;
        var input = new MomentumInput(new Vector2(-1, 1), Vector3.UnitZ, -Vector3.UnitX,
            Quake3MovementProfileV3.GroundSpeed, false, 1f / 60);
        Vector3 sum = Vector3.Zero; float distances = 0;
        for (int i = 0; i < 10000; i++)
        {
            distances += Quake3MovementProfileV3.BlastDistance(origin, body);
            sum += Quake3MovementProfileV3.Step(
                Quake3MovementProfileV3.RocketImpulse(20, origin, body, 4), input).Velocity;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++)
        {
            distances += Quake3MovementProfileV3.BlastDistance(origin, body);
            sum += Quake3MovementProfileV3.Step(
                Quake3MovementProfileV3.RocketImpulse(20, origin, body, 4), input).Velocity;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && Finite(sum) && float.IsFinite(distances) && distances > 0,
            "warmed V3 capsule distance, blast and horizontal step allocate zero bytes");
    }
}
