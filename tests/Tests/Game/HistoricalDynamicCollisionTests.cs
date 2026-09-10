using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Network;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class HistoricalDynamicCollisionTests
{
    [Fact]
    public void DynamicGeometryIsDisabledUntilExplicitlyOptedIn()
    {
        Assert.False(new ServerCombat().HistoricalDynamicCollisionEnabled);
        Assert.True(new ServerCombat(historicalDynamicCollisionEnabled: true).HistoricalDynamicCollisionEnabled);
    }

    [Fact]
    public void RegistryUsesGenerationForReusedEntityIdsAndHonorsHardCap()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene, hardColliderCap: 2);
        var first = new TestEntity(scene, 41);
        var replacement = new TestEntity(scene, 41);
        var other = new TestEntity(scene, 42);

        Assert.True(registry.TryRegister(first, 0, out HistoricalColliderId firstId));
        Assert.True(registry.TryRegister(replacement, 0, out HistoricalColliderId replacementId));
        Assert.Equal(1u, firstId.Generation);
        Assert.Equal(2u, replacementId.Generation);
        Assert.False(registry.TryRegister(other, 0, out _));
        Assert.Equal(1, registry.RegistrationRejected);
    }

    [Fact]
    public void RemovedRegistrationHasNoCurrentFallbackAndMustFailClosed()
    {
        using Scene scene = Scene.CreateHeadless();
        var entity = new TestEntity(scene, 51);
        scene.InsertEntity(entity);
        var registry = new HistoricalCollisionRegistry(scene);
        Assert.True(registry.TryRegister(entity, 0, out _));
        registry.Seal();

        Assert.True(registry.TryCaptureCurrentState(0, out _));
        scene.RemoveEntity(entity);
        Assert.False(registry.TryCaptureCurrentState(0, out _));
        Assert.False(registry.TryResolveCurrent(new HistoricalColliderId(51, 1), out _));
    }

    [Fact]
    public void HistoryWrapsAtThirtyTwoTicksAndClearsMetrics()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene);
        var entity = new TestEntity(scene, 7);
        Assert.True(registry.TryRegister(entity, 0, out HistoricalColliderId identity));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);

        for (uint tick = 0; tick <= DynamicCollisionHistory.Capacity; tick++) history.Record(tick);
        Assert.False(history.TryGet(identity, 0, out _));
        Assert.True(history.TryGet(identity, DynamicCollisionHistory.Capacity, out HistoricalCollisionState state));
        Assert.Equal(identity, state.Identity);
        Assert.Equal(1, history.Missing);
        history.Clear();
        Assert.Equal(0, history.Records);
        Assert.Equal(0, history.Queries);
        Assert.Equal(0, history.Missing);
    }

    [Fact]
    public void RecordingUsesFixedStorageAfterWarmup()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene);
        var entity = new TestEntity(scene, 9);
        Assert.True(registry.TryRegister(entity, 0, out _));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        history.Record(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (uint tick = 1; tick < 10_000; tick++) history.Record(tick);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void BoundedDiagnosticSnapshotSamplesEveryActiveRegisteredKindBeforeFilling()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene);
        for (short id = 100; id < 106; id++)
        {
            DoorEntity door = MakeDoor(scene, id, new(id, 0, 0), Vector3.UnitZ);
            scene.InsertEntity(door);
            Assert.True(registry.TryRegister(door, 0, out _));
        }
        ForceFieldEntity field = MakeForceField(scene, 106, Vector3.Zero,
            Vector3.UnitY, Vector3.UnitZ, 2, 2, active: true);
        scene.InsertEntity(field);
        Assert.True(registry.TryRegister(field, 0, out _));
        var obj = new TestEntity(scene, 107, EntityType.Object);
        AttachCollision(obj, 0);
        scene.InsertEntity(obj);
        Assert.True(registry.TryRegister(obj, 0, out _));
        var platform = new TestEntity(scene, 108, EntityType.Platform);
        AttachCollision(platform, 0);
        scene.InsertEntity(platform);
        Assert.True(registry.TryRegister(platform, 0, out _));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        var destination = new HistoricalCollisionDiagnostic[8];

        int count = history.CopyDiagnosticSnapshot(0, destination, out bool truncated);

        Assert.Equal(8, count);
        Assert.True(truncated);
        Assert.Equal(new[]
        {
            HistoricalColliderKind.Door,
            HistoricalColliderKind.ForceField,
            HistoricalColliderKind.Object,
            HistoricalColliderKind.Platform
        }, destination.Take(4).Select(item => item.ColliderKind));
        Assert.All(destination.Take(4), item => Assert.True(item.State.Active));
    }

    [Fact]
    public void HistoricalStaticQueriesReuseBoundedWorkspaceWithoutAllocating()
    {
        using Scene scene = MakeStaticScene(cells: 4, planeZ: 0);
        var registry = new HistoricalCollisionRegistry(scene);
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BeginTick(10);
        var engine = new HistoricalCollisionQueryEngine(scene, registry, history, combat);
        var query = new HistoricalCollisionQuery(new(1, 1, 1), new(1, 1, -1), TestFlags.Beams);

        for (int i = 0; i < 100; i++)
            Assert.True(engine.TryQueryCurrent(query, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allQueriesHit = true;
        for (int i = 0; i < 10_000; i++)
            allQueriesHit &= engine.TryQueryCurrent(query, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allQueriesHit);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void HistoricalPlatformQueriesUseRecordedTransformAndDeterministicSurfaceOrder()
    {
        using Scene scene = MakeStaticScene(cells: 1, planeZ: 0);
        var platform = new TestEntity(scene, 81, EntityType.Platform);
        EntityCollision collision = AttachCollision(platform, z: 0);
        scene.InsertEntity(platform);
        var registry = new HistoricalCollisionRegistry(scene);
        Assert.True(registry.TryRegister(platform, 0, out HistoricalColliderId identity));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BeginTick(12);
        var engine = new HistoricalCollisionQueryEngine(scene, registry, history, combat);
        var query = new HistoricalCollisionQuery(new(1, 1, 1), new(1, 1, -1), TestFlags.Beams);

        history.Record(10); // platform and static room are exactly tied
        Assert.True(engine.TryQuery(query, 10, out HistoricalCollisionResult tied));
        Assert.Equal(HistoricalColliderKind.StaticRoom, tied.ColliderKind);

        SetTransform(collision, Matrix4.CreateTranslation(0, 0, 0.5f));
        history.Record(11); // platform moved nearer than the static room
        Assert.True(engine.TryQuery(query, 11, out HistoricalCollisionResult nearer));
        Assert.Equal(HistoricalColliderKind.Platform, nearer.ColliderKind);
        Assert.Equal(identity, nearer.ColliderId);
        Assert.Equal(0.25f, nearer.Distance, 5);

        SetTransform(collision, Matrix4.CreateTranslation(10, 0, 0));
        history.Record(12); // platform moved out of the path
        Assert.True(engine.TryQuery(query, 12, out HistoricalCollisionResult historicalOut));
        Assert.Equal(HistoricalColliderKind.StaticRoom, historicalOut.ColliderKind);
        Assert.True(engine.TryQueryCurrent(query, out HistoricalCollisionResult current));
        Assert.Equal(HistoricalColliderKind.StaticRoom, current.ColliderKind);

        // The earlier transform remains immutable after the live collider moves.
        Assert.True(engine.TryQuery(query, 11, out HistoricalCollisionResult retained));
        Assert.Equal(HistoricalColliderKind.Platform, retained.ColliderKind);
    }

    [Fact]
    public void TransformableHistoricalBoundsRejectOutsideShapesBeforeFaceTraversal()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene);
        const int outsideCount = 63;
        for (int index = 0; index < outsideCount; index++)
        {
            var outside = new TestEntity(scene, 100 + index, EntityType.Platform);
            EntityCollision collision = AttachCollision(outside, z: 0);
            SetTransform(collision, Matrix4.CreateTranslation(10 + index * 4, 0, 0));
            scene.InsertEntity(outside);
            Assert.True(registry.TryRegister(outside, 0, out _));
        }
        var inside = new TestEntity(scene, 200, EntityType.Platform);
        EntityCollision insideCollision = AttachCollision(inside, z: 0);
        SetTransform(insideCollision, Matrix4.CreateTranslation(0, 0, 0.5f));
        scene.InsertEntity(inside);
        Assert.True(registry.TryRegister(inside, 0, out HistoricalColliderId insideId));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        history.Record(30);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BeginTick(31);
        var engine = new HistoricalCollisionQueryEngine(scene, registry, history, combat);
        var hitQuery = new HistoricalCollisionQuery(new(1, 1, 1), new(1, 1, -1), TestFlags.Beams);
        var missQuery = new HistoricalCollisionQuery(new(-10, 1, 1), new(-10, 1, -1), TestFlags.Beams);

        Assert.True(engine.TryQuery(hitQuery, 30, out HistoricalCollisionResult hit));
        Assert.Equal(insideId, hit.ColliderId);
        Assert.Equal(1, engine.TransformableShapeQueries);
        Assert.False(engine.TryQuery(missQuery, 30, out _));
        Assert.Equal(1, engine.TransformableShapeQueries);

        // Count-based proof is deterministic; also retain the QZ1 warmed
        // allocation invariant while walking the bounded registry.
        for (int i = 0; i < 100; i++) Assert.True(engine.TryQuery(hitQuery, 30, out _));
        long traversals = engine.TransformableShapeQueries;
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool allQueriesHit = true;
        for (int i = 0; i < 1_000; i++)
        {
            allQueriesHit &= engine.TryQuery(hitQuery, 30, out _);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allQueriesHit);
        Assert.Equal(0, allocated);
        Assert.Equal(traversals + 1_000, engine.TransformableShapeQueries);
    }

    [Fact]
    public void FullHistoricalEngineUsesRecordedDoorTransitionsAndPosition()
    {
        using Scene scene = Scene.CreateHeadless();
        DoorEntity door = MakeDoor(scene, 83, Vector3.Zero, Vector3.UnitZ);
        scene.InsertEntity(door);
        var registry = new HistoricalCollisionRegistry(scene);
        Assert.True(registry.TryRegister(door, 0, out HistoricalColliderId identity));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BeginTick(13);
        var engine = new HistoricalCollisionQueryEngine(scene, registry, history, combat);
        Vector3 center = door.LockPosition;
        var query = new HistoricalCollisionQuery(center + Vector3.UnitZ * 2,
            center - Vector3.UnitZ * 2, TestFlags.Beams);

        door.Flags &= ~DoorFlags.Open;
        history.Record(10);
        door.Flags |= DoorFlags.Open;
        history.Record(11);
        door.Flags &= ~DoorFlags.Open;
        door.Position += Vector3.UnitX * 8;
        history.Record(12);

        Assert.True(engine.TryQuery(query, 10, out HistoricalCollisionResult closed));
        Assert.Equal(HistoricalColliderKind.Door, closed.ColliderKind);
        Assert.Equal(identity, closed.ColliderId);
        Assert.False(engine.TryQuery(query, 11, out _));
        Assert.False(engine.TryQuery(query, 12, out _));

        // Current state is closed at the original position, but none of the
        // recorded tick results may consult that live transform/state.
        door.Position -= Vector3.UnitX * 8;
        Assert.True(engine.TryQueryCurrent(query, out HistoricalCollisionResult current));
        Assert.Equal(HistoricalColliderKind.Door, current.ColliderKind);
        Assert.True(engine.TryQuery(query, 10, out _));
        Assert.False(engine.TryQuery(query, 11, out _));
        Assert.False(engine.TryQuery(query, 12, out _));
    }

    [Fact]
    public void FullHistoricalEngineUsesRecordedRotatedForceFieldStateAndEdges()
    {
        using Scene scene = Scene.CreateHeadless();
        ForceFieldEntity field = MakeForceField(scene, 84, new(4, 0, 0),
            Vector3.UnitY, Vector3.UnitX, width: 2, height: 3, active: true);
        scene.InsertEntity(field);
        var registry = new HistoricalCollisionRegistry(scene);
        Assert.True(registry.TryRegister(field, 0, out HistoricalColliderId identity));
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BeginTick(22);
        var engine = new HistoricalCollisionQueryEngine(scene, registry, history, combat);

        history.Record(20);
        SetForceFieldActive(field, active: false);
        history.Record(21);
        SetForceFieldActive(field, active: true);
        SetForceFieldPosition(field, new(8, 0, 0));
        history.Record(22);
        Vector3 exactEdge = field.Position + field.FieldUpVector * field.Height
            + field.FieldRightVector * field.Width;
        Vector3 originalEdge = exactEdge - Vector3.UnitX * 4;
        var edgeQuery = new HistoricalCollisionQuery(originalEdge + field.FieldFacingVector,
            originalEdge - field.FieldFacingVector, TestFlags.Beams);
        var outsideQuery = new HistoricalCollisionQuery(
            originalEdge + field.FieldRightVector * 0.001f + field.FieldFacingVector,
            originalEdge + field.FieldRightVector * 0.001f - field.FieldFacingVector,
            TestFlags.Beams);
        var movedQuery = new HistoricalCollisionQuery(exactEdge + field.FieldFacingVector,
            exactEdge - field.FieldFacingVector, TestFlags.Beams);

        Assert.True(engine.TryQuery(edgeQuery, 20, out HistoricalCollisionResult active));
        Assert.Equal(HistoricalColliderKind.ForceField, active.ColliderKind);
        Assert.Equal(identity, active.ColliderId);
        Assert.False(engine.TryQuery(outsideQuery, 20, out _));
        Assert.False(engine.TryQuery(edgeQuery, 21, out _));
        Assert.False(engine.TryQuery(edgeQuery, 22, out _));
        Assert.True(engine.TryQuery(movedQuery, 22, out HistoricalCollisionResult moved));
        Assert.Equal(identity, moved.ColliderId);

        // The historical inactive sample remains inactive after the current
        // field becomes active again.
        SetForceFieldPosition(field, new(4, 0, 0));
        Assert.True(engine.TryQueryCurrent(edgeQuery, out HistoricalCollisionResult current));
        Assert.Equal(HistoricalColliderKind.ForceField, current.ColliderKind);
        Assert.False(engine.TryQuery(edgeQuery, 21, out _));
    }

    [Fact]
    public void FullHistoricalDoorAndPlayerHistoriesPreserveNearestOrdering()
    {
        using Scene scene = Scene.CreateHeadless();
        DoorEntity door = MakeDoor(scene, 85, Vector3.Zero, Vector3.UnitZ);
        scene.InsertEntity(door);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BindScene(scene);
        Assert.True(combat.HistoricalCollisionRegistry.TryRegister(door, 0, out _));
        combat.HistoricalCollisionRegistry.Seal();
        combat.InitializeHistoricalCollision(scene);
        combat.BeginTick(12);
        PlayerEntity target = scene.Players[1];
        PreparePlayer(target, slot: 1, connectionId: 101, life: 3);
        Vector3 center = door.LockPosition;
        Vector3 start = center + Vector3.UnitZ * 4;
        Vector3 end = center - Vector3.UnitZ * 4;
        var front = new LagCompensationState
        {
            Slot = 1, ConnectionId = 101, LifeId = 3, Alive = true,
            AltForm = true, Hunter = Hunter.Samus,
            Position = center + Vector3.UnitZ * 1.5f,
            SpherePosition = center + Vector3.UnitZ * 1.5f,
            SphereRadius = 0.4f
        };
        var behind = front with
        {
            Position = center - Vector3.UnitZ * 1.5f,
            SpherePosition = center - Vector3.UnitZ * 1.5f
        };
        door.Flags &= ~DoorFlags.Open;
        combat.DynamicCollisionHistory.Record(10);
        combat.DynamicCollisionHistory.Record(11);
        combat.History.Record(10, front);
        combat.History.Record(11, behind);

        AssertOrdering(10, expectedPlayerFirst: true);
        AssertOrdering(11, expectedPlayerFirst: false);

        void AssertOrdering(uint tick, bool expectedPlayerFirst)
        {
            var shot = new CombatShot(new CombatActor(0, 99, 1), 1, 12, tick,
                tick, 12 - tick, LagCompensationMode.HistoricalTrace);
            Assert.True(combat.TryGetHistoricalBeamCollision(start, end, shot,
                out HistoricalCollisionResult doorResult));
            Assert.Equal(HistoricalColliderKind.Door, doorResult.ColliderKind);
            Assert.True(combat.TryGetPlayerCollider(target, shot, out LagCompensationState playerState));
            CollisionResult playerResult = default;
            Assert.True(playerState.CheckPlayer(start, end, 0, ref playerResult));
            Assert.Equal(expectedPlayerFirst, playerResult.Distance < doorResult.Distance);
        }
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public void ProjectileCatchUpDrivesBeamThroughItsHistoricalStepTick()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ContentEnvironment.PreserveContext("AMHE1");
        ContentEnvironment.Open(data, "AMHE1");
        RuntimeData.Load();
        using Scene scene = MakeStaticScene(cells: 1, planeZ: 0);
        var platform = new TestEntity(scene, 82, EntityType.Platform);
        EntityCollision collision = AttachCollision(platform, z: 0);
        scene.InsertEntity(platform);
        var combat = new ServerCombat(historicalDynamicCollisionEnabled: true);
        combat.BindScene(scene);
        Assert.True(combat.HistoricalCollisionRegistry.TryRegister(platform, 0,
            out HistoricalColliderId identity));
        combat.HistoricalCollisionRegistry.Seal();
        combat.InitializeHistoricalCollision(scene);
        scene.Services = new ServerSceneServices(combat);
        combat.BeginTick(11);

        SetTransform(collision, Matrix4.CreateTranslation(0, 0, 0.5f));
        combat.DynamicCollisionHistory.Record(10);
        SetTransform(collision, Matrix4.CreateTranslation(10, 0, 0));
        combat.DynamicCollisionHistory.Record(11);
        PlayerEntity owner = scene.Players[0];
        PreparePlayer(owner, slot: 0, connectionId: 99, life: 1);
        combat.SetCommand(0, new InputCommand(1, 11, 9, InputButtons.Shoot,
            InputButtons.Shoot, -Vector3.UnitZ, InputCommand.NoWeapon));
        var equip = new EquipInfo(Weapons.Ricochets[0], [new BeamProjectileEntity(scene)])
        {
            InfiniteAmmo = true
        };
        equip.DrawFuncIds[0] = 0;
        BeamProjectileEntity.Spawn(owner, equip, new(1, 1, 1), -Vector3.UnitZ,
            BeamSpawnFlags.NoMuzzle, owner.NodeRef, scene);
        BeamProjectileEntity beam = equip.Beams[0];
        beam.Flags = (beam.Flags | BeamFlags.SurfaceCollision) & ~BeamFlags.Ricochet;
        beam.CollisionEffect = 255;
        beam.SplashDamage = 0;
        beam.RicochetWeapon = null;
        beam.Velocity = new(0, 0, -2);
        beam.Acceleration = Vector3.Zero;
        beam.Lifespan = 1;

        Assert.True(beam.CatchUpPending);
        Assert.Equal(LagCompensationMode.ProjectileCatchUp, beam.TimingMode);
        Assert.Equal(2u, beam.CombatShot.RewindTicks);
        combat.CatchUp.Drain();

        Assert.False(beam.CatchUpPending);
        Assert.True(beam.Flags.TestFlag(BeamFlags.Collided));
        Assert.Equal(1, combat.CatchUp.ProjectilesCaughtUp);
        Assert.Equal(1, combat.CatchUp.Steps);
        Assert.Equal(1, combat.CatchUp.Collisions);
        Assert.Null(combat.CollisionTick);
        Assert.Equal(identity, combat.HistoricalCollisionRegistry.GetIdentity(0));
    }

    [Fact]
    public void MatchOwnedRingsCannotObserveOneAnother()
    {
        using Scene firstScene = Scene.CreateHeadless();
        using Scene secondScene = Scene.CreateHeadless();
        var firstRegistry = new HistoricalCollisionRegistry(firstScene);
        var secondRegistry = new HistoricalCollisionRegistry(secondScene);
        var firstEntity = new TestEntity(firstScene, 11);
        var secondEntity = new TestEntity(secondScene, 11);
        Assert.True(firstRegistry.TryRegister(firstEntity, 0, out HistoricalColliderId firstId));
        Assert.True(secondRegistry.TryRegister(secondEntity, 0, out HistoricalColliderId secondId));
        firstRegistry.Seal();
        secondRegistry.Seal();
        var firstHistory = new DynamicCollisionHistory(firstRegistry);
        var secondHistory = new DynamicCollisionHistory(secondRegistry);
        firstHistory.Record(12);
        Assert.True(firstHistory.TryGet(firstId, 12, out _));
        Assert.False(secondHistory.TryGet(secondId, 12, out _));
    }

    [Fact]
    public void ContractFactoriesCaptureDoorAndForceFieldBlockingFacts()
    {
        var id = new HistoricalColliderId(3, 4);
        var door = HistoricalCollisionState.ForDoor(id, true, OpenTK.Mathematics.Vector3.UnitZ,
            OpenTK.Mathematics.Vector3.UnitX, 25);
        var field = HistoricalCollisionState.ForForceField(id, true,
            new OpenTK.Mathematics.Vector4(OpenTK.Mathematics.Vector3.UnitZ, 2),
            OpenTK.Mathematics.Vector3.UnitX, OpenTK.Mathematics.Vector3.UnitY,
            OpenTK.Mathematics.Vector3.UnitX, 4, 5);
        Assert.True(door.Blocking);
        Assert.Equal(25, door.RadiusSquared);
        Assert.True(field.Active);
        Assert.Equal(5, field.Height);
    }

    [Fact]
    public void NetDebugCommandsExposeBoundedServerFactsOnly()
    {
        var combat = new ServerCombat();
        combat.BeginTick(42);
        string history = combat.NetDebug("netdebug lagcomp-history", default,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitX);
        string dynamic = combat.NetDebug("lagcomp-dynamic", default,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitX);

        Assert.Contains("query_tick=42", history);
        Assert.Contains("rewind_ticks=0", history);
        Assert.Contains("path_end=(1,0,0)", history);
        Assert.Contains("dynamic=0", dynamic);
        Assert.DoesNotContain("packet", dynamic, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => combat.NetDebug("netdebug gameplay", default,
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitX));
    }

    [Fact]
    public void HistoricalDoorStateControlsBlockingAndNearestOrdering()
    {
        var id = new HistoricalColliderId(61, 1);
        var closed = HistoricalCollisionState.ForDoor(id, blocking: true,
            OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.Zero, 4);
        var open = HistoricalCollisionState.ForDoor(id, blocking: false,
            OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.Zero, 4);
        var moved = HistoricalCollisionState.ForDoor(id, blocking: true,
            OpenTK.Mathematics.Vector3.UnitZ, new(10, 0, 0), 4);
        Assert.True(HistoricalCollisionQueryEngine.TryIntersectDoor(closed,
            new(0, 0, 1), new(0, 0, -1), out CollisionResult doorHit));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectDoor(open,
            new(0, 0, 1), new(0, 0, -1), out _));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectDoor(moved,
            new(0, 0, 1), new(0, 0, -1), out _));

        var field = HistoricalCollisionState.ForForceField(new(62, 1), active: true,
            new OpenTK.Mathematics.Vector4(OpenTK.Mathematics.Vector3.UnitZ, 0),
            OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Vector3.UnitY,
            OpenTK.Mathematics.Vector3.UnitX, 2, 2);
        Assert.True(HistoricalCollisionQueryEngine.TryIntersectForceField(field,
            new(0, 0, 1), new(0, 0, -1), out CollisionResult fieldHit));
        Assert.True(doorHit.Distance < fieldHit.Distance);
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectForceField(field with { Active = false },
            new(0, 0, 1), new(0, 0, -1), out _));
    }

    [Fact]
    public void HistoricalDoorTransitionAndPlayerOrderingUseTheSameSegmentDistance()
    {
        var id = new HistoricalColliderId(64, 1);
        var closed = HistoricalCollisionState.ForDoor(id, blocking: true,
            Vector3.UnitZ, Vector3.Zero, radiusSquared: 16);
        var open = HistoricalCollisionState.ForDoor(id, blocking: false,
            Vector3.UnitZ, Vector3.Zero, radiusSquared: 16);
        Vector3 start = new(0, 0, 4);
        Vector3 end = new(0, 0, -4);

        // These are the two sides of an exact transition boundary: neither
        // consults the opposite/current state.
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectDoor(open, start, end, out _));
        Assert.True(HistoricalCollisionQueryEngine.TryIntersectDoor(closed, start, end,
            out CollisionResult door));

        var playerInFront = new LagCompensationState
        {
            Slot = 1, ConnectionId = 1, LifeId = 1, Alive = true, AltForm = true,
            Hunter = Hunter.Samus, SpherePosition = new(0, 0, 1.5f), SphereRadius = 0.4f
        };
        var playerBehind = playerInFront with { SpherePosition = new(0, 0, -1.5f) };
        CollisionResult front = default;
        CollisionResult behind = default;
        Assert.True(playerInFront.CheckPlayer(start, end, 0, ref front));
        Assert.True(playerBehind.CheckPlayer(start, end, 0, ref behind));
        Assert.True(front.Distance < door.Distance);
        Assert.True(door.Distance < behind.Distance);
    }

    [Fact]
    public void HistoricalForceFieldIncludesExactRectangleEdgesAndRecordedMovement()
    {
        var id = new HistoricalColliderId(63, 1);
        var field = HistoricalCollisionState.ForForceField(id, active: true,
            new Vector4(Vector3.UnitZ, 0), Vector3.Zero, Vector3.UnitY, Vector3.UnitX,
            width: 2, height: 3);

        Assert.True(HistoricalCollisionQueryEngine.TryIntersectForceField(field,
            new(2, 3, 1), new(2, 3, -1), out _));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectForceField(field,
            new(2.001f, 3, 1), new(2.001f, 3, -1), out _));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectForceField(field,
            new(2, 3.001f, 1), new(2, 3.001f, -1), out _));

        var moved = field with { Position = new(4, 0, 0) };
        Assert.True(HistoricalCollisionQueryEngine.TryIntersectForceField(moved,
            new(6, 3, 1), new(6, 3, -1), out _));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectForceField(moved,
            new(1.999f, 3, 1), new(1.999f, 3, -1), out _));

        var rotated = field with
        {
            Plane = new Vector4(Vector3.UnitX, 4),
            Position = new(4, 0, 0),
            Right = -Vector3.UnitZ
        };
        Assert.True(HistoricalCollisionQueryEngine.TryIntersectForceField(rotated,
            new(5, 3, 2), new(3, 3, 2), out _));
        Assert.False(HistoricalCollisionQueryEngine.TryIntersectForceField(rotated,
            new(5, 3, 2.001f), new(3, 3, 2.001f), out _));
    }

    [Fact]
    public void HostDiagnosticPacketRoundTripsBoundedHistoryAndDynamicFacts()
    {
        var player = new HistoricalPlayerVolumeDiagnostic(2, true, false,
            new(1, 2, 3), new(1, 2.5f, 3), 0.75f, -1, 2);
        var id = new HistoricalColliderId(77, 4);
        var state = HistoricalCollisionState.ForForceField(id, true,
            new OpenTK.Mathematics.Vector4(OpenTK.Mathematics.Vector3.UnitZ, 0),
            new(4, 5, 6), OpenTK.Mathematics.Vector3.UnitY,
            OpenTK.Mathematics.Vector3.UnitX, 2, 3);
        var dynamic = new HistoricalCollisionDiagnostic(id, HistoricalColliderKind.ForceField, state);
        var metrics = new HistoricalCollisionDebugMetrics(10, 9, 1, 4, 3, 2, 1);
        var frame = new HistoricalCollisionDebugFrame(100, 94, 6, new(0, 1, 2),
            new(3, 4, 5), true, false, 1, 1, false);
        byte[] bytes = new byte[HistoricalCollisionDebugPacket.MaxSize];
        int length = HistoricalCollisionDebugPacket.Write(bytes, 9,
            HistoricalCollisionDebugMode.History, frame, new[] { player }, default, true, metrics);
        Assert.True(HistoricalCollisionDebugPacket.TryRead(bytes[..length], 9, out var history));
        Assert.Equal(100u, history.CurrentTick);
        Assert.Equal(94u, history.QueryTick);
        Assert.True(history.HasProjectilePath);
        Assert.Single(history.Players);
        Assert.Equal(player.SpherePosition, history.Players[0].SpherePosition);
        Assert.Empty(history.Colliders);
        Assert.Equal(metrics, history.Metrics);

        length = HistoricalCollisionDebugPacket.Write(bytes, 9,
            HistoricalCollisionDebugMode.Dynamic, frame, default, new[] { dynamic }, false, metrics);
        Assert.True(HistoricalCollisionDebugPacket.TryRead(bytes[..length], 9, out var dynamicPacket));
        Assert.False(dynamicPacket.HasProjectilePath);
        Assert.Single(dynamicPacket.Colliders);
        Assert.Equal(HistoricalColliderKind.ForceField, dynamicPacket.Colliders[0].Kind);
        Assert.Equal(3, dynamicPacket.Colliders[0].Height);
        Assert.Equal(metrics, dynamicPacket.Metrics);
        Assert.True(HistoricalCollisionDebugPacket.MaxSize + NetHeader.Size <= NetConfig.MaxPacketSize);

        length = HistoricalCollisionDebugPacket.WriteClear(bytes, 9);
        Assert.True(HistoricalCollisionDebugPacket.TryRead(bytes[..length], 9, out var clear));
        Assert.True(clear.IsClear);
        Assert.Empty(clear.Players);
        Assert.Empty(clear.Colliders);
    }

    [Fact]
    public void DebugPacketRejectsClientSelectedOrUnboundedShapes()
    {
        byte[] bytes = new byte[HistoricalCollisionDebugPacket.MaxSize];
        var frame = new HistoricalCollisionDebugFrame(1, 1, 0, default, default,
            false, false, 0, 0, false);
        int length = HistoricalCollisionDebugPacket.Write(bytes, 1,
            HistoricalCollisionDebugMode.History, frame, default, default, false);
        bytes[17] = 0x80;
        Assert.False(HistoricalCollisionDebugPacket.TryRead(bytes[..length], 1, out _));
        Assert.False(HistoricalCollisionDebugPacket.TryRead(bytes[..length], 2, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HistoricalCollisionDebugPacket.Write(bytes, 1, HistoricalCollisionDebugMode.History,
                frame, new HistoricalPlayerVolumeDiagnostic[HistoricalCollisionDebugPacket.MaxPlayers + 1], default, false));
    }

    private static Scene MakeStaticScene(int cells, float planeZ)
    {
        var scene = new Scene(headless: true);
        var info = MakePlaneInfo(cells, planeZ);
        var room = new RoomEntity(scene);
        room._roomCollision.Add(new CollisionInstance("historical-static-test", info, false));
        typeof(Scene).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, room);
        return scene;
    }

    private static DoorEntity MakeDoor(Scene scene, short id, Vector3 position, Vector3 facing)
    {
        var door = (DoorEntity)RuntimeHelpers.GetUninitializedObject(typeof(DoorEntity));
        InitializeSyntheticEntity(door, scene, EntityType.Door, id, position,
            Vector3.UnitY, facing);
        SetField(typeof(DoorEntity), door, "_lockTransform", Matrix4.Identity);
        SetField(typeof(DoorEntity), door, "<Radius>k__BackingField", 2f);
        SetField(typeof(DoorEntity), door, "<RadiusSquared>k__BackingField", 4f);
        door.Flags = DoorFlags.Closed;
        return door;
    }

    private static ForceFieldEntity MakeForceField(Scene scene, short id, Vector3 position,
        Vector3 up, Vector3 facing, float width, float height, bool active)
    {
        var field = (ForceFieldEntity)RuntimeHelpers.GetUninitializedObject(typeof(ForceFieldEntity));
        InitializeSyntheticEntity(field, scene, EntityType.ForceField, id, position, up, facing);
        Vector3 right = Vector3.Cross(up, facing).Normalized();
        SetField(typeof(ForceFieldEntity), field, "_upVector", up);
        SetField(typeof(ForceFieldEntity), field, "_facingVector", facing);
        SetField(typeof(ForceFieldEntity), field, "_rightVector", right);
        SetField(typeof(ForceFieldEntity), field, "_plane",
            new Vector4(facing, Vector3.Dot(facing, position)));
        SetField(typeof(ForceFieldEntity), field, "_width", width);
        SetField(typeof(ForceFieldEntity), field, "_height", height);
        SetForceFieldActive(field, active);
        return field;
    }

    private static void InitializeSyntheticEntity(EntityBase entity, Scene scene, EntityType type,
        int id, Vector3 position, Vector3 up, Vector3 facing)
    {
        Matrix4 transform = EntityBase.GetTransformMatrix(facing, up, position);
        SetField(typeof(EntityBase), entity, "<Type>k__BackingField", type);
        SetField(typeof(EntityBase), entity, "<Id>k__BackingField", id);
        SetField(typeof(EntityBase), entity, "_scene", scene);
        SetField(typeof(EntityBase), entity, "_transform", transform);
        SetField(typeof(EntityBase), entity, "_position", position);
        SetField(typeof(EntityBase), entity, "_scale", Vector3.One);
        SetField(typeof(EntityBase), entity, "_soundSource", new MphRead.Sound.SoundSource(scene));
        SetField(typeof(EntityBase), entity, "_models", new System.Collections.Generic.List<ModelInstance>());
    }

    private static void SetForceFieldActive(ForceFieldEntity field, bool active)
        => SetField(typeof(ForceFieldEntity), field, "_active", active);

    private static void SetForceFieldPosition(ForceFieldEntity field, Vector3 position)
    {
        field.Position = position;
        SetField(typeof(ForceFieldEntity), field, "_plane",
            new Vector4(field.FieldFacingVector,
                Vector3.Dot(field.FieldFacingVector, position)));
    }

    private static void SetField(Type type, object target, string name, object value)
        => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void PreparePlayer(PlayerEntity player, int slot, ulong connectionId, uint life)
    {
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(player, slot);
        typeof(PlayerEntity).GetField("_networkInputActive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, true);
        typeof(PlayerEntity).GetField("_serverConnectionId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, connectionId);
        typeof(PlayerEntity).GetField("_serverLife", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(player, life);
        player.Health = 100;
    }

    private static EntityCollision AttachCollision(TestEntity entity, float z)
    {
        var instance = new CollisionInstance("historical-platform-test", MakePlaneInfo(1, z), true);
        var collision = new EntityCollision(instance, entity)
        {
            CurrentCenter = new(1, 1, z),
            MaxDistance = 3,
            Transform = Matrix4.Identity,
            Inverse1 = Matrix4.Identity
        };
        entity.EntityCollision[0] = collision;
        return collision;
    }

    private static void SetTransform(EntityCollision collision, Matrix4 transform)
    {
        collision.Transform = transform;
        collision.Inverse1 = transform.Inverted();
        collision.CurrentCenter = transform.ExtractTranslation() + new Vector3(1, 1, 0);
    }

    private static MphCollisionInfo MakePlaneInfo(int cells, float planeZ)
    {
        int fixedZ = Fixed.ToInt(planeZ);
        var header = Struct<CollisionHeader>((nameof(CollisionHeader.PartsX), cells),
            (nameof(CollisionHeader.PartsY), 1), (nameof(CollisionHeader.PartsZ), 1));
        var data = Struct<CollisionData>((nameof(CollisionData.LayerMask), (ushort)2),
            (nameof(CollisionData.PointIndexCount), (ushort)4));
        return new MphCollisionInfo(header,
            new[]
            {
                new Vector3Fx(0, 0, fixedZ), new Vector3Fx(4 * 4096, 0, fixedZ),
                new Vector3Fx(4 * 4096, 4 * 4096, fixedZ), new Vector3Fx(0, 4 * 4096, fixedZ)
            },
            new[] { Struct<Vector4Fx>((nameof(Vector4Fx.Z), new Fixed(4096)),
                (nameof(Vector4Fx.W), new Fixed(fixedZ))) },
            new ushort[] { 0, 1, 2, 3, 0 }, new[] { data }, new ushort[] { 0 },
            Enumerable.Repeat(new CollisionEntry(1, 0), cells).ToArray(), Array.Empty<Portal>());
    }

    private static T Struct<T>(params (string Name, object Value)[] fields) where T : struct
    {
        object value = default(T);
        foreach ((string name, object fieldValue) in fields)
            typeof(T).GetField(name)!.SetValue(value, fieldValue);
        return (T)value;
    }

    private sealed class TestEntity : EntityBase
    {
        public TestEntity(Scene scene, int id, EntityType type = EntityType.Object) : base(type, scene) => Id = id;
    }
}
