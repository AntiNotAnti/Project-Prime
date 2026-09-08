using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MphRead.Entities;
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

    private static Model CreateModel(bool animated = true)
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
                new[] { 0f, 10f, 20f }, new Dictionary<string, NodeAnimation>
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
