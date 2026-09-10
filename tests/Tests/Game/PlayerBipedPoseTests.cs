using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MphRead.Entities;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class PlayerBipedPoseTests
{
    [Fact]
    public void SplitBodyTracksAndMuzzleUseRenderedPrivatePose()
    {
        Model source = CreateModel();
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        ModelInstance otherPlayer = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        legs.AnimInfo.Frame[0] = 1;
        torso.AnimInfo.Frame[0] = 2;
        PlayerEntity.AnimateBipedPose(torso.Model, legs.AnimInfo, torso.AnimInfo, 0);
        Assert.Equal(10, torso.Model.Nodes[0].Animation.M41);
        Assert.Equal(20, torso.Model.GetNodeByName("Spine_1")!.Animation.M41);
        Assert.Equal(40, torso.Model.GetNodeByName("R_elbow")!.Animation.M41);
        Assert.Equal(new Vector3(41, 0, 0), Matrix.Vec3MultMtx4(Vector3.UnitX, torso.Model.GetNodeByName("R_elbow")!.Animation));
        Assert.Equal(Matrix4.Identity, legs.Model.Nodes[0].Animation);
        Assert.Equal(Matrix4.Identity, otherPlayer.Model.Nodes[0].Animation);
        Assert.Equal(Matrix4.Identity, source.Nodes[0].Animation);
        Assert.False(torso.Model.GetNodeByName("Spine_1")!.AnimIgnoreChild);
        Assert.Null(torso.Model.GetNodeByName("Spine_1")!.AfterTransform);
    }

    [Fact]
    public void ReadOnlyBipedSamplerMatchesAuthoredSplitAndRestoresRuntimeState()
    {
        Model source = CreateModel();
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        legs.AnimInfo.Frame[0] = 1;
        torso.AnimInfo.Frame[0] = 2;
        Matrix4 sentinel = Matrix4.CreateTranslation(90, 80, 70);
        foreach (Node node in torso.Model.Nodes) node.Animation = sentinel;

        Matrix4[] sampled = new Matrix4[torso.Model.Nodes.Count];
        NodePoseSampler.SampleBiped(torso.Model, legs.AnimInfo, torso.AnimInfo,
            torso.Model.GetNodeIndexByName("Spine_1"), .25f, sampled);
        PlayerEntity.AnimateBipedPose(torso.Model, legs.AnimInfo, torso.AnimInfo, .25f);

        for (int i = 0; i < sampled.Length; i++)
            Assert.Equal(torso.Model.Nodes[i].Animation, sampled[i]);
        foreach (Node node in torso.Model.Nodes)
            Assert.NotEqual(sentinel, node.Animation);
        Assert.Equal(Matrix4.Identity, legs.Model.Nodes[0].Animation);
        Assert.Equal(Matrix4.Identity, source.Nodes[0].Animation);
        Node spine = torso.Model.GetNodeByName("Spine_1")!;
        Assert.False(spine.AnimIgnoreChild);
        Assert.Null(spine.AfterTransform);
    }

    [Fact]
    public void BipedHistoryInterpolatesLocalNodesAndAppliesWorldRootOnce()
    {
        Model source = CreateModel(animationStep: 2);
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        PlayerBipedPoseHistory history = new(torso.Model);
        legs.AnimInfo.Frame[0] = 1;
        torso.AnimInfo.Frame[0] = 1;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 1, 0);
        legs.AnimInfo.Frame[0] = 2;
        torso.AnimInfo.Frame[0] = 2;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 2, 0);
        Matrix4 worldRoot = Matrix4.CreateTranslation(100, 3, -4);
        history.Resolve(.5f, worldRoot, out Matrix4[] nodes, out float[] stack);
        Assert.Equal(103, nodes[0].M41);
        Assert.Equal(3, nodes[0].M42);
        Assert.Equal(-4, nodes[0].M43);
        Assert.Equal(nodes[0].M41, stack[12]);
        Assert.Equal(nodes[0].M42, stack[13]);
        Assert.Equal(nodes[0].M43, stack[14]);
        Assert.Equal(16, stack.Length);
    }

    [Fact]
    public void BipedHistoryUsesOneSampleAndResetsOnTrackDiscontinuity()
    {
        Model source = CreateModel(animationStep: 2);
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        PlayerBipedPoseHistory history = new(torso.Model);
        legs.AnimInfo.Frame[0] = 2;
        torso.AnimInfo.Frame[0] = 2;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 1, 0);
        history.Resolve(.5f, Matrix4.CreateTranslation(10, 0, 0), out Matrix4[] nodes, out _);
        Assert.Equal(14, nodes[0].M41);

        // Replacing one animation group is a hard fence, not a blend from the
        // previous action.  This also exercises the one-sample fallback after
        // the reset.
        legs.AnimInfo.Node.Group = NodeAnimationGroup.Empty();
        legs.AnimInfo.Index[0] = 1;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 2, 0);
        history.Resolve(.5f, Matrix4.Identity, out nodes, out _);
        Assert.Equal(0, nodes[0].M41);
        Assert.Equal(Matrix4.Identity, nodes[0]);
    }

    [Fact]
    public void BipedHistoryDoesNotBlendAcrossTeleportDiscontinuity()
    {
        Model source = CreateModel(animationStep: 2);
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        PlayerBipedPoseHistory history = new(torso.Model);
        legs.AnimInfo.Frame[0] = 1;
        torso.AnimInfo.Frame[0] = 1;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 1, 0);
        legs.AnimInfo.Frame[0] = 2;
        torso.AnimInfo.Frame[0] = 2;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 2, 0);

        // Teleport() advances the presentation epoch; this is the equivalent
        // history fence when testing the render-only component in isolation.
        legs.AnimInfo.Frame[0] = 1;
        torso.AnimInfo.Frame[0] = 1;
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 3, 0, discontinuity: true);
        history.Resolve(.5f, Matrix4.Identity, out Matrix4[] nodes, out _);
        Assert.Equal(2, nodes[0].M41);
    }

    [Fact]
    public void BipedHistoryResolveReusesBuffersWithoutAllocating()
    {
        Model source = CreateModel();
        ModelInstance legs = new(source);
        ModelInstance torso = new(source);
        legs.SetAnimation(0);
        torso.SetAnimation(0);
        PlayerBipedPoseHistory history = new(torso.Model);
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 1, 0);
        history.Capture(legs.AnimInfo, torso.AnimInfo, 0, 2, 0);
        history.Resolve(1, Matrix4.Identity, out Matrix4[] nodes, out float[] stack);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
        {
            history.Resolve((i & 15) / 15f, Matrix4.Identity, out nodes, out stack);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(nodes);
        GC.KeepAlive(stack);
        Assert.Equal(0, allocated);
    }

    private static Model CreateModel(bool animated = true, float animationStep = 10)
    {
        byte[] header = new byte[Marshal.SizeOf<Header>()];
        BitConverter.GetBytes(4096).CopyTo(header, 4);
        BitConverter.GetBytes((ushort)1).CopyTo(header, 28);
        byte[] node = new byte[Marshal.SizeOf<RawNode>()];
        for (int offset = 64; offset < 70; offset += 2)
            BitConverter.GetBytes((short)-1).CopyTo(node, offset);
        BitConverter.GetBytes(1).CopyTo(node, 72);
        for (int offset = 80; offset < 92; offset += 4)
            BitConverter.GetBytes(4096).CopyTo(node, offset);
        string[] names = { "root", "Spine_1", "R_elbow" };
        RawNode[] nodes = new RawNode[3];
        for (int i = 0; i < nodes.Length; i++)
        {
            byte[] raw = (byte[])node.Clone();
            System.Text.Encoding.ASCII.GetBytes(names[i]).CopyTo(raw, 0);
            BitConverter.GetBytes((short)(i - 1)).CopyTo(raw, 64);
            BitConverter.GetBytes((short)(i == 2 ? -1 : i + 1)).CopyTo(raw, 66);
            nodes[i] = Read.ReadStruct<RawNode>(raw);
        }
        AnimationResults animations = new();
        if (animated)
        {
            byte[] groupBytes = new byte[Marshal.SizeOf<RawNodeAnimationGroup>()];
            BitConverter.GetBytes(3u).CopyTo(groupBytes, 0);
            byte[] animationBytes = new byte[Marshal.SizeOf<NodeAnimation>()];
            foreach (int offset in new[] { 4, 6, 8, 20, 22, 24, 36, 38, 40 })
                BitConverter.GetBytes((ushort)1).CopyTo(animationBytes, offset);
            animationBytes[32] = 1; // translation X uses one sample per frame
            BitConverter.GetBytes((ushort)3).CopyTo(animationBytes, 36);
            animations.NodeAnimationGroups.Add(new NodeAnimationGroup(
                Read.ReadStruct<RawNodeAnimationGroup>(groupBytes), new[] { 1f }, new[] { 0f },
                new[] { 0f, animationStep, animationStep * 2 }, new Dictionary<string, NodeAnimation>
                {
                    ["root"] = Read.ReadStruct<NodeAnimation>(animationBytes),
                    ["Spine_1"] = Read.ReadStruct<NodeAnimation>(animationBytes),
                    ["R_elbow"] = Read.ReadStruct<NodeAnimation>(animationBytes)
                }));
        }
        else
        {
            animations.NodeAnimationGroups.Add(NodeAnimationGroup.Empty());
        }
        animations.MaterialAnimationGroups.Add(MaterialAnimationGroup.Empty());
        animations.TextureAnimationGroups.Add(TextureAnimationGroup.Empty());
        animations.TexcoordAnimationGroups.Add(TexcoordAnimationGroup.Empty());
        animations.NodeGroupOffsets.Add(0);
        animations.MaterialGroupOffsets.Add(0);
        animations.TextureGroupOffsets.Add(0);
        animations.TexcoordGroupOffsets.Add(0);
        return new Model("isolation", false, Read.ReadStruct<Header>(header),
            nodes, new[] { default(RawMesh) },
            new[] { Read.ReadStruct<RawMaterial>(new byte[Marshal.SizeOf<RawMaterial>()]) },
            Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(), animations,
            Array.Empty<Matrix4>(), Array.Empty<Recolor>(), new[] { 0 }, Array.Empty<Vector3Fx>(),
            Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
    }
}
