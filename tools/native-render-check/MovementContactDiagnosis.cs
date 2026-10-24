using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

sealed partial class CheckWindow
{
    private int? _movementInputUntil;
    private bool _movementContactDiagnosis;

    private void ConfigureContactDiagnosis(string[] args)
    {
        _movementContactDiagnosis = args.Contains("-movement-contact-diagnosis");
        int at = Array.IndexOf(args, "-movement-input-until");
        if (at < 0) return;
        if (at + 1 >= args.Length || !int.TryParse(args[at + 1], out int frame) || frame is <60 or >300)
            throw new ArgumentException("Pressure release requires an input-until frame from 60 through 300.");
        _movementInputUntil = frame;
    }

    private object? ObserveContactDiagnosis(PlayerEntity player, int frame)
    {
        if (!_movementContactDiagnosis || frame is not (40 or 45 or 90 or 149 or 151 or 180 or 240 or 300 or 360)) return null;
        float Property(string name) => (float)typeof(PlayerEntity).GetProperty(name, ReplayPrivate)!.GetValue(player)!;
        float Constant(string name) => (float)typeof(PlayerEntity).GetField(name, ReplayPrivate | BindingFlags.Static)!.GetRawConstantValue()!;
        float bottom = Property("ModBipedBottom"), top = Property("ModBipedTop");
        float radius = Fixed.ToFloat(player.Values.BipedColRadius);
        float skin = Constant("CollisionRecoverySkin"), limit = Constant("CollisionRecoveryLimit");
        float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
        var scratch = (CollisionResult[])typeof(PlayerEntity).GetField("_penetrationScratch", ReplayPrivate)!.GetValue(player)!;
        var saved = scratch.ToArray();
        try
        {
            var points = new[] { bottom + radius, (bottom + top) / 2, top - radius }.Select(offset =>
            {
                var center = player.Position + Vector3.UnitY * offset;
                var hits = new CollisionResult[scratch.Length];
                int count = CollisionDetection.CheckSpherePenetration(center, radius, hits.Length, TestFlags.Players, Scene, hits);
                return new { offset, center = V(center), radius, count, contacts = hits.Take(count).Select(h => new
                {
                    h.Field0, normal = V(h.Plane.Xyz), planeW = h.Plane.W, depth = h.Field14,
                    side = Vector3.Dot(center, h.Plane.Xyz) - h.Plane.W, entity = h.EntityCollision != null
                }).ToArray() };
            }).ToArray();
            var query = typeof(PlayerEntity).GetMethod("FindBodyPenetration", ReplayPrivate)!;
            var position = player.Position;
            float moved = 0;
            var trace = new List<object>();
            for (int i = 0; i < 16; i++)
            {
                object?[] arguments = { position, player.IsAltForm, true, default(CollisionResult), false };
                bool penetrating = (bool)query.Invoke(player, arguments)!;
                var contact = (CollisionResult)arguments[3]!; bool saturated = (bool)arguments[4]!;
                float push = contact.Field14 + 1f / 4096;
                bool apply = penetrating && !saturated && float.IsFinite(contact.Field14) && push > 0 && moved + push <= limit;
                trace.Add(new { iteration = i, position = V(position), penetrating, saturated, contact.Field0,
                    normal = V(contact.Plane.Xyz), planeW = contact.Plane.W, depth = contact.Field14, moved, push, apply });
                if (!apply) break;
                position += contact.Plane.Xyz.Normalized() * push;
                moved += push;
            }
            return new
            {
                phase = "Post-native simulation/render. Query-only virtual continuation of static refinement; no live actor writes.",
                minPickup = Fixed.ToFloat(player.Values.MinPickupHeight), maxPickup = Fixed.ToFloat(player.Values.MaxPickupHeight),
                bottom, top, radius, canonicalGatherRadius = (top - bottom) / 2, skin, limit,
                nativeIterationLimit = (int)typeof(PlayerEntity).GetField("CollisionResolveIterations", ReplayPrivate | BindingFlags.Static)!.GetRawConstantValue()!,
                points, virtualContinuation = trace
            };
        }
        finally { Array.Copy(saved, scratch, scratch.Length); }
    }
}
