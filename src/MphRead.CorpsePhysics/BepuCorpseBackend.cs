using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;

namespace MphRead.CorpsePhysics;

// Experimental, explicitly opted-in private world. This assembly has no engine
// reference; only copied static triangles enter it and copied poses leave it.
public sealed class BepuCorpseBackend : CorpseBackend
{
    public const long MaximumPoolBytes = 32 * 1024 * 1024;
    public const int MaximumContactConstraints = 512;
    private sealed class PhysicalSlot
    {
        internal readonly BodyHandle[] Bodies = new BodyHandle[CorpseRig.PartCount];
        internal readonly TypedIndex[] Shapes = new TypedIndex[CorpseRig.PartCount];
        internal int BodyCount, ShapeCount;
    }
    private readonly PhysicalSlot[] _physical = Enumerable.Range(0, 8).Select(_ => new PhysicalSlot()).ToArray();
    private readonly CorpsePhysicsArena _arena;
    private readonly bool _ownArena;
    private Simulation? _simulation;
    private TypedIndex _mesh;
    private StaticHandle _static;
    private bool _hasMesh, _hasStatic;
    public override string Name => "bepu-2.5.0-beta.33-experimental";
    protected override long PoolBytes => _arena.ReservedBytes;
    protected override int PhysicalBodies => _physical.Sum(p => p.BodyCount);
    protected override int Joints => Active * 42;
    public int SolverConstraints { get { OwnerThread(); return _simulation?.Solver.CountConstraints() ?? 0; } }
    public int SleepingBodies
    {
        get
        {
            OwnerThread();
            int sleeping = 0;
            if (_simulation != null) foreach (var p in _physical) for (int i = 0; i < p.BodyCount; i++)
                if (!_simulation.Bodies[p.Bodies[i]].Awake) sleeping++;
            return sleeping;
        }
    }
    public BepuCorpseBackend(CorpseStaticWorld world, CorpseQuality quality, bool experimentalOptIn,
        CorpsePhysicsArena? arena = null) : base(quality)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!experimentalOptIn) throw new InvalidOperationException("The non-shipping physics pilot requires explicit opt-in.");
        _arena = arena ?? new(); _ownArena = arena == null; _arena.Acquire();
        try
        {
            _simulation = Simulation.Create(_arena.Pool, new CorpseContacts(), new CorpseGravity(), new SolveDescription(6, 2),
                initialAllocationSizes: new SimulationAllocationSizes(128, 1, 16, 128, 1024, 128, 16));
            _arena.Pool.Take<Triangle>(world.Triangles.Length, out var triangles);
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = world.Triangles[i];
                // Boundary uses right-handed CCW outward faces. BEPU's
                // one-sided meshes see clockwise faces; convert exactly once.
                triangles[i] = new(t.A, t.C, t.B);
            }
            // Mesh takes ownership of this exact logical-length buffer. On a
            // constructor failure the wholly private arena is abandoned below.
            var mesh = new Mesh(triangles, Vector3.One, _arena.Pool);
            _mesh = _simulation.Shapes.Add(mesh); _hasMesh = true;
            _static = _simulation.Statics.Add(new StaticDescription(Vector3.Zero, _mesh)); _hasStatic = true;
        }
        catch
        {
            // A failed Mesh constructor may have partially transferred buffers;
            // an external arena cannot be reused after uncertain construction.
            try { Teardown(); } finally { _arena.Dispose(); }
            throw;
        }
    }
    protected override void OnSpawn(int index, CorpseSpawnDescriptor spawn)
    {
        var sim = _simulation!; var slot = _physical[index]; var parts = spawn.Rig.Parts;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i]; var pose = spawn.WorldPart(i); var box = new Box(p.Size.X, p.Size.Y, p.Size.Z);
            var shape = sim.Shapes.Add(box); slot.Shapes[slot.ShapeCount++] = shape;
            var description = BodyDescription.CreateDynamic(new(pose.Position, pose.Orientation),
                box.ComputeInertia(p.Mass), new CollidableDescription(shape, .02f), new BodyActivityDescription(.01f, 60));
            description.Velocity.Linear = spawn.InheritedVelocity + spawn.AcceptedDirection * 3;
            var body = sim.Bodies.Add(description);
            slot.Bodies[slot.BodyCount] = body; slot.BodyCount++;
        }
        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i]; var a = spawn.WorldPart(p.Parent); var b = spawn.WorldPart(i);
            var joint = spawn.Origin.Position + Vector3.Transform(p.Joint, spawn.Origin.Orientation);
            var inverseA = Quaternion.Conjugate(a.Orientation); var inverseB = Quaternion.Conjugate(b.Orientation);
            var spring = new SpringSettings(15, 1);
            sim.Solver.Add(slot.Bodies[p.Parent], slot.Bodies[i], new BallSocket
            { LocalOffsetA = Vector3.Transform(joint - a.Position, inverseA), LocalOffsetB = Vector3.Transform(joint - b.Position, inverseB), SpringSettings = spring });
            // Start both joint frames aligned in world space; anatomical part
            // rest orientations may differ. Local Z is the common twist axis.
            var worldBasis = spawn.Origin.Orientation;
            var localA = Quaternion.Normalize(inverseA * worldBasis); var localB = Quaternion.Normalize(inverseB * worldBasis);
            sim.Solver.Add(slot.Bodies[p.Parent], slot.Bodies[i], new SwingLimit
            { AxisLocalA = Vector3.Transform(Vector3.UnitZ, localA), AxisLocalB = Vector3.Transform(Vector3.UnitZ, localB), MaximumSwingAngle = p.Swing, SpringSettings = spring });
            sim.Solver.Add(slot.Bodies[p.Parent], slot.Bodies[i], new TwistLimit
            { LocalBasisA = localA, LocalBasisB = localB, MinimumAngle = -p.Twist, MaximumAngle = p.Twist, SpringSettings = spring });
        }
        var head = sim.Bodies[slot.Bodies[2]];
        var point = spawn.ImpactPoint ?? head.Pose.Position;
        var offset = point - head.Pose.Position;
        if (offset.LengthSquared() > .09f) offset = Vector3.Normalize(offset) * .3f;
        var impulse = spawn.AcceptedDirection * parts[2].Mass;
        head.ApplyImpulse(impulse, offset);
        // Small fixed seed variant breaks perfect standing balance without any
        // simulation RNG or client-selected force scalar.
        head.Velocity.Angular.X += (spawn.Seed & 1) == 0 ? .35f : -.35f;
        if (_arena.ReservedBytes > MaximumPoolBytes) throw new InvalidOperationException("Physics pool budget exceeded.");
    }
    protected override void OnStep()
    {
        var sim = _simulation!; sim.Timestep(1f / 60f); // no worker dispatcher
        if (sim.Solver.CountConstraints() > Joints + MaximumContactConstraints || _arena.ReservedBytes > MaximumPoolBytes)
            throw new InvalidOperationException("Physics contact/pool budget exceeded.");
        for (int n = 0; n < Slots.Length; n++)
            if (Slots[n].Active)
                for (int i = 0; i < _physical[n].BodyCount; i++)
                {
                    var body = sim.Bodies[_physical[n].Bodies[i]]; var pose = body.Pose;
                    if (!float.IsFinite(body.Velocity.Linear.LengthSquared()) || !float.IsFinite(body.Velocity.Angular.LengthSquared()))
                        throw new InvalidOperationException("Nonfinite corpse velocity.");
                    float speed = body.Velocity.Linear.Length(), spin = body.Velocity.Angular.Length();
                    if (speed > 12) body.Velocity.Linear *= 12 / speed;
                    if (spin > 16) body.Velocity.Angular *= 16 / spin;
                    Slots[n].Current[i] = new(pose.Position, pose.Orientation);
                }
    }
    protected override void OnRetire(int index)
    {
        var slot = _physical[index]; var sim = _simulation;
        if (sim == null) return;
        // Body removal owns its connected constraints; never separately remove
        // saved/recycled constraint handles. Shapes remain our responsibility.
        while (slot.BodyCount > 0)
        { sim.Bodies.Remove(slot.Bodies[slot.BodyCount - 1]); slot.BodyCount--; }
        while (slot.ShapeCount > 0)
        { sim.Shapes.RemoveAndDispose(slot.Shapes[slot.ShapeCount - 1], _arena.Pool); slot.ShapeCount--; }
    }
    private void Teardown()
    {
        try
        {
            if (_simulation is {} sim)
            {
                try { for (int i = 0; i < _physical.Length; i++) OnRetire(i); }
                finally
                {
                    try { if (_hasStatic) { sim.Statics.Remove(_static); _hasStatic = false; } }
                    finally
                    {
                        if (_hasMesh)
                        { _hasMesh = false; sim.Shapes.RemoveAndDispose(_mesh, _arena.Pool); }
                    }
                }
            }
        }
        catch { _arena.Poison(); throw; }
        finally
        {
            try { _simulation?.Dispose(); }
            catch { _arena.Poison(); throw; }
            finally { _simulation = null; _arena.Release(); }
        }
    }
    protected override void OnDispose()
    { try { Teardown(); } finally { if (_ownArena) _arena.Dispose(); } }
}
