using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Declared constructed value/scope fixtures only. No native collision, launch
// admission, payment, or geometry-positive claim is established by these tests.
internal static class NativeImpactWitnessTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Impact witness: " + label);
        _checks++;
    }
    internal static int Run()
    {
        try
        {
            _checks = 0;
            var shot = new ShotKey(1, 2, 3, 4, 5, 6);
            var payment = new ShotPaymentKey(shot, 7, 8, 9, 1, 1);
            var point = new Vector3(1, 2, 3);
            var value = new NativeImpactWitness(payment, (byte)BeamType.PowerBeam, 10,
                point, 20, false, Vector3.Zero, Vector3.UnitX, Vector3.UnitX * 2, true, true);
            Check(value.Valid && value.Payment == payment, "complete constructed payment identity is retained");
            var badShots = new[] { shot with { AuthorityEpoch = 0 }, shot with { MatchId = 0 },
                shot with { ShooterSlot = -1 }, shot with { ShooterSlot = 8 }, shot with { Generation = 0 },
                shot with { LifeId = 0 }, shot with { ShotId = 0 } };
            foreach (var bad in badShots)
                Check(!(value with { Payment = payment with { Shot = bad } }).Valid, "incomplete shooter identity is invalid");
            var badPayments = new[] { payment with { Victim = 8 }, payment with { Generation = 0 },
                payment with { Life = 0 }, payment with { Component = 0 }, payment with { Kind = 0 },
                payment with { Kind = 3 } };
            foreach (var bad in badPayments)
                Check(!(value with { Payment = bad }).Valid, "incomplete victim/component/kind identity is invalid");
            foreach (byte kind in new byte[] { 1, 2, 5, 6 })
                Check((value with { Payment = payment with { Kind = kind } }).Valid, "native direct/splash/turret kind is representable");
            Check(!(value with { Point = new Vector3(float.NaN, 0, 0) }).Valid
                && !(value with { Origin = new Vector3(0, float.PositiveInfinity, 0) }).Valid
                && !(value with { SegmentStart = new Vector3(0, 0, float.NegativeInfinity) }).Valid
                && !(value with { SegmentEnd = new Vector3(float.NaN, 0, 0) }).Valid
                && !(value with { NativeChannel = 0 }).Valid
                && !(value with { Weapon = byte.MaxValue }).Valid,
                "invalid actual scalar geometry/channel/weapon refuses witness validity");
            point.X = 99;
            Check(value.Point == new Vector3(1, 2, 3), "stored vector remains a value copy after source mutation");
            using (new NetNativeImpactWitness.Scope(null))
            {
                Check(!NetNativeImpactWitness.TryCapture(out _), "empty scope exposes no witness");
                using (new NetNativeImpactWitness.Scope(value))
                {
                    Check(NetNativeImpactWitness.TryCapture(out var captured) && captured == value,
                        "constructed scope exposes exact readonly scalar value");
                    using (default(NetNativeImpactWitness.Scope))
                        Check(NetNativeImpactWitness.TryCapture(out captured) && captured == value,
                            "disabled default scope leaves active outer context untouched");
                    Check(NetNativeImpactWitness.TryCapture(out captured) && captured == value,
                        "disabled disposal performs no context mutation");
                    var inner = value with { Frame = 11, NativeChannel = 21 };
                    using (new NetNativeImpactWitness.Scope(inner))
                        Check(NetNativeImpactWitness.TryCapture(out captured) && captured == inner,
                            "nested scope exposes inner value");
                    Check(NetNativeImpactWitness.TryCapture(out captured) && captured == value,
                        "nested scope restores exact prior value");
                    try { using (new NetNativeImpactWitness.Scope(inner)) throw new FixtureException(); }
                    catch (FixtureException) { }
                    Check(NetNativeImpactWitness.TryCapture(out captured) && captured == value,
                        "exception cleanup restores outer scope");
                    using (new NetNativeImpactWitness.Scope(value with { NativeChannel = 0 }))
                        Check(!NetNativeImpactWitness.TryCapture(out _), "invalid constructed scope refuses capture");
                    using (new NetNativeImpactWitness.Scope(null))
                        Check(!NetNativeImpactWitness.TryCapture(out _), "null nested scope suppresses outer witness");
                    Check(NetNativeImpactWitness.TryCapture(out captured) && captured == value,
                        "suppression scopes restore outer value");
                }
                Check(!NetNativeImpactWitness.TryCapture(out _), "disposed outer scope leaves no leaked witness");
            }
            var outcome = new CombatAckEntry { ShotId = 6, DamageApplied = 20, HealthAfter = 80 };
            var completed = value.WithOutcome(outcome);
            outcome.DamageApplied = 99;
            var returned = completed.Outcome; returned.HealthAfter = 0;
            Check(!value.Completed && value.Outcome.DamageApplied == 0 && completed.Completed
                && completed.Outcome.DamageApplied == 20 && completed.Outcome.HealthAfter == 80
                && completed.Payment == value.Payment && completed.Point == value.Point,
                "WithOutcome returns independent immutable completed scalar value");
            Console.WriteLine($"PASS: {_checks} constructed impact-witness value/scope assertions; no native collision proof.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private sealed class FixtureException : Exception { }
}
