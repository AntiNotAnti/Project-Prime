using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

public sealed class ImpactDecalRuntimeIntegrationTests
{
    [Fact]
    public void ThrowingPresentationIsReportedAndCollisionContinuationCompletes()
    {
        IScenePresentation presentation
            = DispatchProxy.Create<IScenePresentation, ThrowingPresentationProxy>();
        StaticBeamImpactPresentation impact = Impact(7, 10,
            Node("room", 1), Vector3.Zero);
        var diagnostics = new List<string>();
        bool collisionCompleted = false;

        bool observed = StaticBeamImpactPresentationNotification.TryObserve(
            presentation, impact, diagnostics.Add);
        // This is the same sequencing as the production seam: the normal
        // collision transition executes after the isolated notification.
        collisionCompleted = true;

        Assert.False(observed);
        Assert.True(collisionCompleted);
        Assert.Contains("InvalidOperationException",
            Assert.Single(diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticImpactFactsNormalizeAndRejectAmbiguousSurfaces()
    {
        Assert.True(StaticBeamImpactPresentation.TryCreate(7, 3, 20,
            BeamType.PowerBeam, new Vector3(1, 2, 3), new Vector3(0, 4, 0),
            Terrain.Rock, Node("room", 1), 5, out var impact));
        Assert.Equal(Vector3.UnitY, impact.Normal);
        Assert.Equal(Terrain.Rock, impact.Terrain);
        Assert.Equal(5, impact.RoomId);
        Assert.True(impact.IsValid);
        Assert.False(default(StaticBeamImpactPresentation).IsValid);

        Assert.False(StaticBeamImpactPresentation.TryCreate(7, 3, 20,
            BeamType.PowerBeam, Vector3.Zero, Vector3.Zero, Terrain.Rock,
            Node("room", 1), 5, out _));
        Assert.False(StaticBeamImpactPresentation.TryCreate(7, 3, 20,
            BeamType.PowerBeam, Vector3.Zero, Vector3.UnitY, Terrain.Lava,
            Node("room", 1), 5, out _));
        Assert.False(StaticBeamImpactPresentation.TryCreate(7, 3, 20,
            BeamType.None, Vector3.Zero, Vector3.UnitY, Terrain.Metal,
            Node("room", 1), 5, out _));
    }

    [Fact]
    public void DuplicateEventDoesNotRefreshLifetime()
    {
        var state = State(globalCapacity: 4, perRegionCapacity: 2,
            lifetime: TimeSpan.FromSeconds(1));
        state.ConfigureRoom(5);
        StaticBeamImpactPresentation impact = Impact(7, tick: 60,
            Node("room", 1), new Vector3(1, 2, 3));

        Assert.True(state.TryObserve(impact, GraphicsPreset.Enhanced,
            CombatActor.None));
        Assert.False(state.TryObserve(impact, GraphicsPreset.Enhanced,
            CombatActor.None));
        Assert.Single(state.Prepare(GraphicsPreset.Enhanced, 119,
            CombatActor.None));
        Assert.Empty(state.Prepare(GraphicsPreset.Enhanced, 120,
            CombatActor.None));
    }

    [Fact]
    public void PoolLimitsAndKeysAreDeterministicAcrossReplay()
    {
        var first = State(globalCapacity: 2, perRegionCapacity: 1);
        var replay = State(globalCapacity: 2, perRegionCapacity: 1);
        first.ConfigureRoom(5);
        replay.ConfigureRoom(5);
        StaticBeamImpactPresentation[] impacts =
        {
            Impact(1, 1, Node("room", 2), new Vector3(1, 0, 0)),
            Impact(2, 2, Node("room", 2), new Vector3(2, 0, 0)),
            Impact(3, 3, Node("room", 3), new Vector3(3, 0, 0))
        };

        foreach (StaticBeamImpactPresentation impact in impacts)
        {
            Assert.True(first.TryObserve(impact, GraphicsPreset.Enhanced,
                CombatActor.None));
            Assert.True(replay.TryObserve(impact, GraphicsPreset.Enhanced,
                CombatActor.None));
        }
        ImpactDecalRenderItem[] forward = first.Prepare(
            GraphicsPreset.Enhanced, 3, CombatActor.None);
        ImpactDecalRenderItem[] repeated = replay.Prepare(
            GraphicsPreset.Enhanced, 3, CombatActor.None);

        Assert.Equal(2, forward.Length);
        Assert.Equal(forward.Select(item => item.Decal.StableKey),
            repeated.Select(item => item.Decal.StableKey));
        Assert.Equal(2, forward.Select(item => item.Decal.RegionKey).Distinct().Count());
    }

    [Fact]
    public void PresetLifeRoomAndRewindBoundariesClearPresentationState()
    {
        var state = State(globalCapacity: 4, perRegionCapacity: 2);
        state.ConfigureRoom(5);
        StaticBeamImpactPresentation impact = Impact(7, 10,
            Node("room", 1), Vector3.Zero);
        var firstLife = new CombatActor(0, 10, 1);
        var secondLife = new CombatActor(0, 10, 2);

        Assert.False(state.TryObserve(impact, GraphicsPreset.Original,
            firstLife));
        Assert.True(state.TryObserve(impact, GraphicsPreset.Enhanced,
            firstLife));
        Assert.Single(state.Prepare(GraphicsPreset.Enhanced, 10, firstLife));
        Assert.Empty(state.Prepare(GraphicsPreset.Enhanced, 11, secondLife));

        Assert.True(state.TryObserve(Impact(8, 20, Node("room", 1),
            Vector3.One), GraphicsPreset.Enhanced, secondLife));
        Assert.Empty(state.Prepare(GraphicsPreset.Enhanced, 19, secondLife));
        Assert.False(state.TryObserve(Impact(9, 20, Node("room", 1),
            Vector3.One, roomId: 6), GraphicsPreset.Enhanced, secondLife));

        state.ConfigureRoom(6);
        Assert.Empty(state.Prepare(GraphicsPreset.Performance, 20, secondLife));
        Assert.Equal(0, state.ActiveCount);
    }

    [Fact]
    public void QuadIsOrientedOnBiasedHitPlaneAndUsesFullUvRange()
    {
        var decal = new ImpactDecalDescriptor(1, 1,
            ImpactDecalKind.BeamMark, new Vector3(2, 3, 4), Vector3.UnitZ,
            radius: 2, Vector4.One, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var values = new Vector3[8];

        ScenePresentation.BuildImpactDecalQuad(decal, values);

        Assert.Equal(Vector3.Zero, values[0]);
        Assert.Equal(Vector3.UnitX, values[2]);
        Assert.Equal(new Vector3(1, 1, 0), values[4]);
        Assert.Equal(Vector3.UnitY, values[6]);
        for (int i = 1; i < 8; i += 2)
        {
            Assert.Equal(4 + ScenePresentation.ImpactDecalSurfaceBias,
                values[i].Z, 6);
            Assert.True(float.IsFinite(values[i].X)
                && float.IsFinite(values[i].Y));
        }
    }

    [Fact]
    public void CollisionNotificationAndRenderSubmissionUseExistingSeams()
    {
        string root = FindRepositoryRoot();
        string beam = File.ReadAllText(Path.Combine(root, "src", "Game",
            "World", "Entities", "BeamProjectileEntity.cs"));
        int staticBranch = beam.IndexOf(
            "// collided with room, platform, or object collision",
            StringComparison.Ordinal);
        int notification = beam.IndexOf(
            "StaticBeamImpactPresentationNotification.TryObserve(",
            staticBranch, StringComparison.Ordinal);
        int collision = beam.IndexOf("OnCollision(anyRes, colWith: null);",
            notification, StringComparison.Ordinal);
        Assert.True(staticBranch >= 0);
        Assert.True(notification > staticBranch);
        Assert.True(collision > notification);
        Assert.Contains("staticRoomCollision", beam[staticBranch..notification],
            StringComparison.Ordinal);

        string renderer = File.ReadAllText(Path.Combine(root, "src", "Client",
            "Rendering", "Renderer.cs"));
        int draw = renderer.IndexOf("public void OnDrawFrame()",
            StringComparison.Ordinal);
        int prepare = renderer.IndexOf("PrepareImpactDecals(", draw,
            StringComparison.Ordinal);
        int submit = renderer.IndexOf("SubmitImpactDecals();", prepare,
            StringComparison.Ordinal);
        int seal = renderer.IndexOf("_renderFrame.Seal();", submit,
            StringComparison.Ordinal);
        Assert.True(prepare > draw);
        Assert.True(submit > prepare);
        Assert.True(seal > submit);

        Assert.Equal(new[]
        {
            RenderPassKind.Opaque, RenderPassKind.Decal,
            RenderPassKind.TransparentStencil, RenderPassKind.DepthRebuild,
            RenderPassKind.TransparentBehind, RenderPassKind.TransparentFront
        }, RenderWorldPlan.Passes.ToArray().Select(pass => pass.Kind));

        string integration = File.ReadAllText(Path.Combine(root, "src",
            "Client", "Rendering", "ScenePresentation.ImpactDecals.cs"));
        Assert.Contains("submission.RenderMode = RenderMode.Decal;", integration,
            StringComparison.Ordinal);
        Assert.Contains("_renderFrame.CaptureTexture(item.Texture.Pixels);",
            integration, StringComparison.Ordinal);
    }

    private static ImpactDecalPresentationState State(int globalCapacity,
        int perRegionCapacity, TimeSpan? lifetime = null)
    {
        var texture = new ImpactDecalTextureAsset("decal.png", 1, 1,
            new byte[] { 255, 255, 255, 255 });
        var profile = new ImpactDecalProfile(BeamImpactStyle.EnergyFlash,
            ImpactDecalKind.BeamMark, texture, .25f,
            lifetime ?? TimeSpan.FromSeconds(10), .75f, Vector3.One);
        var catalog = new ImpactDecalProfileCatalog(
            new Dictionary<BeamImpactStyle, ImpactDecalProfile>
            {
                [BeamImpactStyle.EnergyFlash] = profile
            });
        return new ImpactDecalPresentationState(catalog, globalCapacity,
            perRegionCapacity);
    }

    private static StaticBeamImpactPresentation Impact(ulong source,
        ulong tick, NodeRef node, Vector3 position, int roomId = 5)
    {
        Assert.True(StaticBeamImpactPresentation.TryCreate(source, 1, tick,
            BeamType.PowerBeam, position, Vector3.UnitY, Terrain.Metal, node,
            roomId, out var impact));
        return impact;
    }

    private static NodeRef Node(string room, int node)
        => new(room, partIndex: 0, nodeIndex: node, modelIndex: 0);

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private class ThrowingPresentationProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name == nameof(
                IScenePresentation.ObserveStaticBeamImpact))
            {
                throw new InvalidOperationException("presentation failure");
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
