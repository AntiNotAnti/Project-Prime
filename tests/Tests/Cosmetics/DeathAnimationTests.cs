using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Cosmetics;
using MphRead.Cosmetics.Presentation;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Cosmetics;

public sealed class DeathAnimationTests
{
    private static readonly DeathSkeletonNode[] Skeleton =
    [
        new("Root", -1),
        new("Spine_1", 0),
        new("Head", 1)
    ];
    private static readonly DeathSkeletonNode[] ExactSamusSkeleton =
    [
        new("Dummy_Root", -1), new("Skeleton_Root", 0), new("Pelvis", 1),
        new("L_hip", 2), new("L_knee", 3), new("L_ankle", 4),
        new("R_hip", 2), new("R_knee", 6), new("R_ankle", 7),
        new("Spine_1", 1), new("Spine_2", 9), new("Head_1", 10),
        new("L_shoulder", 10), new("L_elbow", 12), new("L_wrist", 13),
        new("L_varias2_SDK", 10), new("R_shoulder", 10),
        new("R_elbow", 16), new("R_varias2_SDK", 10)
    ];

    [Fact]
    public void CookedClipRoundTripsWithExactSkeletonSignature()
    {
        byte[] signature = DeathAnimationSkeleton.ComputeSignature(Hunter.Samus, Skeleton);
        var track = new DeathAnimationTrack(0,
        [
            new(0, Vector3.Zero, Quaternion.Identity, Vector3.One),
            new(900, new Vector3(0, -.2f, -.4f),
                Quaternion.FromAxisAngle(Vector3.UnitX, -.9f), Vector3.One)
        ]);
        var source = new DeathAnimationClip("prime.death_animation.samus.backward_collapse",
            Hunter.Samus, signature, .9f, [track]);

        byte[] cooked = DeathAnimationCodec.Write(source);
        Assert.True(DeathAnimationCodec.TryRead(cooked, Skeleton.Length, out DeathAnimationClip? read));
        Assert.NotNull(read);
        Assert.True(read!.Matches(Hunter.Samus, signature));
        Assert.False(read.Matches(Hunter.Kanden, signature));
        Assert.Equal(source.Key, read.Key);
        Assert.Equal(2, read.Tracks[0].Frames.Count);
    }

    [Fact]
    public void SkeletonSignatureIncludesHierarchyAndMalformedCookedDataIsRejected()
    {
        byte[] signature = DeathAnimationSkeleton.ComputeSignature(Hunter.Samus, Skeleton);
        DeathSkeletonNode[] changed = (DeathSkeletonNode[])Skeleton.Clone();
        changed[2] = changed[2] with { ParentIndex = 0 };
        Assert.NotEqual(signature,
            DeathAnimationSkeleton.ComputeSignature(Hunter.Samus, changed));

        var clip = new DeathAnimationClip("prime.death_animation.test", Hunter.Samus,
            signature, .5f,
            [new DeathAnimationTrack(2,
                [new(0, Vector3.Zero, Quaternion.Identity, Vector3.One)])]);
        byte[] cooked = DeathAnimationCodec.Write(clip);
        Assert.False(DeathAnimationCodec.TryRead(cooked.AsSpan(0, cooked.Length - 1),
            Skeleton.Length, out _));
        cooked[4]++;
        Assert.False(DeathAnimationCodec.TryRead(cooked, Skeleton.Length, out _));
    }

    [Fact]
    public void DeathRuntimePreservesDefaultFallbackAndBoundsCustomLifetime()
    {
        CapturedDeathPose pose = CapturedPose();
        var actor = new CombatActor(1, 2, 3);
        var runtime = new DeathPresentationRuntime();

        Assert.False(runtime.Begin(actor, 100, requestedEffectId: 0, pose));
        Assert.False(runtime.Begin(actor, 100, UInt16.MaxValue, pose));
        Assert.True(runtime.Begin(actor, 100, BuiltInCosmeticIds.DeathQuantum,
            pose));
        Assert.True(runtime.TrySample(100, out DeathPresentationSample start));
        Assert.Equal(DeathParticleKind.Quantum, start.Particles);
        Assert.False(runtime.TrySample(100 + 3 * 60, out _));
        Assert.False(runtime.Active);
    }

    [Fact]
    public void DeathAccessibilityAndSharedBudgetAreAppliedDeterministically()
    {
        CapturedDeathPose pose = CapturedPose();
        var actor = new CombatActor(1, 2, 3);
        var first = new DeathPresentationRuntime();
        var second = new DeathPresentationRuntime();
        Assert.True(first.Begin(actor, 100, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 77));
        Assert.True(second.Begin(actor, 100, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 77));
        Assert.Equal(first.StableSeed, second.StableSeed);

        Assert.True(first.TryEvaluate(CosmeticPresentationSettings.DesktopDefault,
            localPlayer: true, visible: true, distanceSquared: 1, serverTick: 100,
            renderAlpha: .5f, out DeathEffectFrame full));
        Assert.Equal(16, full.BudgetRequest.Particles);
        Assert.Equal(1, full.BudgetRequest.DistortionSources);
        Assert.Equal(.6f, full.MaterialOverride!.Value.EmissionStrength!.Value, 3);

        ulong snapshotSeed = first.StableSeed;
        first.ReconcileAuthoritativeTick(actor, 99);
        Assert.NotEqual(snapshotSeed, first.StableSeed);
        var reconciled = new DeathPresentationRuntime();
        Assert.True(reconciled.Begin(actor, 99, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 77));
        Assert.Equal(reconciled.StableSeed, first.StableSeed);
        var otherMatch = new DeathPresentationRuntime();
        Assert.True(otherMatch.Begin(actor, 99, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 78));
        Assert.NotEqual(otherMatch.StableSeed, first.StableSeed);

        var reduced = new DeathPresentationRuntime();
        Assert.True(reduced.Begin(actor, 100, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 77));
        CosmeticPresentationSettings accessible = CosmeticPresentationSettings.ReducedDefault with
        { ReduceCosmeticFlashes = true };
        Assert.True(reduced.TryEvaluate(accessible, true, true, 1, 100, .5f,
            out DeathEffectFrame reducedFrame));
        Assert.Equal(8, reducedFrame.BudgetRequest.Particles);
        Assert.Equal(.12f, reducedFrame.Sample.EmissionStrength, 3);

        var hidden = new DeathPresentationRuntime();
        Assert.True(hidden.Begin(actor, 100, BuiltInCosmeticIds.DeathQuantum,
            pose, matchId: 77));
        Assert.False(hidden.TryEvaluate(CosmeticPresentationSettings.DesktopDefault with
            { ShowOtherPlayerCosmetics = false }, localPlayer: false, visible: true,
            distanceSquared: 1, serverTick: 100, renderAlpha: 0, out _));

        var bounded = new CosmeticPrimitiveSubmissionBuffer(8);
        var allowance = new CosmeticBudgetAllowance(full.BudgetRequest.StableKey,
            actor.Slot, 1, 4, 0, 0, 0, 1, 0);
        Assert.Equal(4, first.SubmitPrimitives(full, allowance, bounded));
        Assert.Equal(4, bounded.Count);
        Assert.All(bounded.Seal(), submission =>
            Assert.Equal("quantum.pixels", submission.AssetKey));
    }

    [Fact]
    public void DeathParticlesAndDistortionCanBeDisabledIndependently()
    {
        var runtime = new DeathPresentationRuntime();
        Assert.True(runtime.Begin(new CombatActor(1, 2, 3), 100,
            BuiltInCosmeticIds.DeathQuantum, CapturedPose(), matchId: 8));
        CosmeticPresentationSettings settings = CosmeticPresentationSettings.DesktopDefault with
        { DisableCosmeticParticles = true, DisableCosmeticDistortion = true };
        Assert.True(runtime.TryEvaluate(settings, true, true, 1, 100, 0,
            out DeathEffectFrame frame));
        Assert.Equal(0, frame.BudgetRequest.ParticleEmitters);
        Assert.Equal(0, frame.BudgetRequest.Particles);
        Assert.Equal(0, frame.BudgetRequest.DistortionSources);
    }

    [Fact]
    public void EmbeddedSamusClipMatchesExactAmhe1SkeletonAndWrongSkeletonFallsBack()
    {
        byte[] signature = DeathAnimationSkeleton.ComputeSignature(Hunter.Samus,
            ExactSamusSkeleton);
        Assert.Equal("7b7f82288843cd58ef03c59badcff3a5e289c1d40fa57e302f526a82c90eb349",
            Convert.ToHexString(signature).ToLowerInvariant());
        Assert.True(CosmeticCatalog.BuiltIn.TryGetDeathEffect(
            BuiltInCosmeticIds.DeathSamusBackwardCollapse,
            out DeathEffectDefinition effect));

        IReadOnlyList<Node> exactNodes = CreateNodes(ExactSamusSkeleton);
        Assert.True(DeathAnimationCatalog.TryResolve(effect, Hunter.Samus,
            exactNodes, out DeathAnimationClip? clip));
        Assert.NotNull(clip);
        Assert.True(clip!.Matches(Hunter.Samus, signature));
        Assert.Equal(0, Assert.Single(clip.Tracks).NodeIndex);

        CapturedDeathPose exactPose = CapturedPose(ExactSamusSkeleton,
            Hunter.Samus);
        var runtime = new DeathPresentationRuntime();
        Assert.True(runtime.Begin(new CombatActor(0, 1, 1), 10,
            BuiltInCosmeticIds.DeathSamusBackwardCollapse, exactPose));
        Assert.True(runtime.TrySample(10 + ModelPreviewCatalog.DeathStageSampleTick,
            out DeathPresentationSample collapse));
        Assert.NotEqual(Matrix4.Identity, collapse.Nodes[0]);
        Assert.InRange(collapse.BodyAlpha, 0, .999f);

        DeathSkeletonNode[] wrong = (DeathSkeletonNode[])ExactSamusSkeleton.Clone();
        wrong[11] = wrong[11] with { Name = "Head_changed" };
        Assert.False(DeathAnimationCatalog.TryResolve(effect, Hunter.Samus,
            CreateNodes(wrong), out _));
        Assert.False(new DeathPresentationRuntime().Begin(
            new CombatActor(0, 1, 1), 10,
            BuiltInCosmeticIds.DeathSamusBackwardCollapse,
            CapturedPose(wrong, Hunter.Samus)));
    }

    private static CapturedDeathPose CapturedPose()
    {
        Model model = (Model)RuntimeHelpers.GetUninitializedObject(typeof(Model));
        Node node = (Node)RuntimeHelpers.GetUninitializedObject(typeof(Node));
        Set(node, "Name", "Root");
        Set(node, "ParentIndex", -1);
        Set(node, "ChildIndex", -1);
        Set(node, "NextIndex", -1);
        node.Scale = Vector3.One;
        Set(model, "Nodes", new List<Node> { node });
        Set(model, "Scale", Vector3.One);
        Set(model, "NodeMatrixIds", new[] { 0 });
        typeof(Model).GetField("_matrixStackValues",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new float[16]);

        var pose = new CapturedDeathPose();
        pose.Capture(model, Matrix4.Identity, new[] { Matrix4.Identity },
            new float[16], new CapturedDeathAppearance(Hunter.Samus,
                new CosmeticLoadoutIds(0, 0, BuiltInCosmeticIds.DeathQuantum),
                Recolor: 0, Alpha: 1));
        return pose;
    }

    private static CapturedDeathPose CapturedPose(
        IReadOnlyList<DeathSkeletonNode> skeleton, Hunter hunter)
    {
        IReadOnlyList<Node> nodes = CreateNodes(skeleton);
        Model model = (Model)RuntimeHelpers.GetUninitializedObject(typeof(Model));
        Set(model, "Nodes", nodes.ToList());
        Set(model, "Scale", Vector3.One);
        Set(model, "NodeMatrixIds", Enumerable.Range(0, nodes.Count).ToArray());
        typeof(Model).GetField("_matrixStackValues",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new float[nodes.Count * 16]);
        var pose = new CapturedDeathPose();
        pose.Capture(model, Matrix4.Identity,
            Enumerable.Repeat(Matrix4.Identity, nodes.Count).ToArray(),
            new float[nodes.Count * 16], new CapturedDeathAppearance(hunter,
                new CosmeticLoadoutIds(0, 0,
                    BuiltInCosmeticIds.DeathSamusBackwardCollapse), 0, 1));
        return pose;
    }

    private static IReadOnlyList<Node> CreateNodes(
        IReadOnlyList<DeathSkeletonNode> skeleton)
    {
        var nodes = new List<Node>(skeleton.Count);
        for (int i = 0; i < skeleton.Count; i++)
        {
            Node node = (Node)RuntimeHelpers.GetUninitializedObject(typeof(Node));
            Set(node, "Name", skeleton[i].Name);
            Set(node, "ParentIndex", skeleton[i].ParentIndex);
            Set(node, "ChildIndex", -1);
            Set(node, "NextIndex", -1);
            node.Scale = Vector3.One;
            nodes.Add(node);
        }
        return nodes;
    }

    private static void Set(object value, string name, object field)
        => value.GetType().GetField($"<{name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
}
