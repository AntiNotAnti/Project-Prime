using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MphRead.Mods.Physics;

public readonly record struct ContactPlane(float X, float Y, float Z, float W);
public sealed record PhysicsContact(ContactPlane Plane, float PenetrationDepth, TraceVector Pushout);

/// <summary>Diagnostic scratch only; never part of the replay world or prediction state.</summary>
public sealed class PhysicsStages
{
    public int SampleHz { get; init; } = 60;
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public bool JumpApplied { get; set; }
    public bool JumpPressed { get; set; }
    public float AnalogScaleX { get; set; }
    public float AnalogScaleY { get; set; }
    public float TractionX { get; set; }
    public float TractionY { get; set; }
    public TraceVector? SpeedDelta { get; set; }
    public float SpeedCapBefore { get; set; }
    public float SpeedCapAfter { get; set; }
    public TraceVector? PreMovement { get; set; }
    public TraceVector? AfterAcceleration { get; set; }
    public TraceVector? AfterDamping { get; set; }
    public TraceVector? AfterGravity { get; set; }
    public TraceVector? PredictedPosition { get; set; }
    public TraceVector? AfterIntegration { get; set; }
    public TraceVector? AfterCollision { get; set; }
    public TraceVector? AfterTraction { get; set; }
    public List<PhysicsContact> Contacts { get; init; } = new();
    public int ContactCount => Contacts.Count;
    // These values are only used to finish the full biped update after traction.
    internal PhysicsSample? Pending;
}

public static class PhysicsStageCapture
{
    private static readonly ConditionalWeakTable<object, PhysicsStages> Active = new();
    public static PhysicsStages? Begin(object player, bool nativeMovement = false)
    {
        if (!PhysicsTrace.Enabled) return null;
        Active.Remove(player);
        var stages = new PhysicsStages { SampleHz = nativeMovement ? 30 : 60 };
        Active.Add(player, stages);
        return stages;
    }
    public static PhysicsStages? Get(object player)
        => PhysicsTrace.Enabled && Active.TryGetValue(player, out var stages) ? stages : null;
    public static void End(object player) => Active.Remove(player);
}
