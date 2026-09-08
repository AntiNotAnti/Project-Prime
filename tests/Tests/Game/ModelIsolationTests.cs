using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class ModelIsolationTests
{
    [Fact]
    public void InstancesOwnNestedRuntimeStateAndShareRendererAssetIdentity()
    {
        Model asset = CreateModel();
        ModelInstance first = new(asset);
        ModelInstance second = new(asset);
        Assert.NotSame(first.Model, second.Model);
        Assert.Equal(asset.Id, first.Model.Id);
        Assert.Equal(asset.Id, second.Model.Id);
        Assert.Same(first.Model.RenderInstructionLists, second.Model.RenderInstructionLists);
        Assert.Same(first.Model.AnimationGroups.Node[0].Scales, second.Model.AnimationGroups.Node[0].Scales);
        first.Model.Nodes[0].Position = new Vector3(3, 4, 5);
        first.Model.Nodes[0].Bounds[0] = 99;
        first.Model.Nodes[0].Enabled = false;
        first.Model.Materials[0].Alpha = 10;
        first.Model.Materials[0].CurrentTextureId = 42;
        first.Model.Meshes[0].Visible = false;
        first.Model.AnimationGroups.Node[0].CurrentFrame = 8;
        first.Model.AnimationGroups.Material[0].CurrentFrame = 8;
        first.Model.AnimationGroups.Texture[0].CurrentFrame = 8;
        first.Model.AnimationGroups.Texcoord[0].CurrentFrame = 8;
        first.Model.ComputeNodeMatrices(0);
        first.Model.AnimateNodes(0, true, Matrix4.Identity, Vector3.One, first.AnimInfo);
        first.Model.UpdateMatrixStack();
        first.AnimInfo.FrameCount[0] = 10;
        first.UpdateAnimFrames();

        foreach (Model untouched in new[] { asset, second.Model })
        {
            Assert.Equal(Vector3.Zero, untouched.Nodes[0].Position);
            Assert.Equal(0, untouched.Nodes[0].Bounds[0]);
            Assert.True(untouched.Nodes[0].Enabled);
            Assert.Equal(0, untouched.Materials[0].Alpha);
            Assert.Equal(0, untouched.Materials[0].CurrentTextureId);
            Assert.True(untouched.Meshes[0].Visible);
            Assert.Equal(0, untouched.AnimationGroups.Node[0].CurrentFrame);
            Assert.Equal(0, untouched.AnimationGroups.Material[0].CurrentFrame);
            Assert.Equal(0, untouched.AnimationGroups.Texture[0].CurrentFrame);
            Assert.Equal(0, untouched.AnimationGroups.Texcoord[0].CurrentFrame);
            Assert.Equal(Matrix4.Identity, untouched.Nodes[0].Animation);
            Assert.Equal(0, untouched.MatrixStackValues[12]);
        }
        Assert.Equal(3, first.Model.MatrixStackValues[12]);
        Assert.Equal(1, first.AnimInfo.Frame[0]);
        Assert.Equal(0, second.AnimInfo.Frame[0]);
    }

    [Fact]
    public void SetModelCopiesCurrentConfigurationWithoutSharingIt()
    {
        Model source = CreateModel();
        source.Nodes[0].AfterTransform = Matrix4.CreateTranslation(7, 0, 0);
        source.Materials[0].Alpha = 17;
        ModelInstance instance = new(CreateModel());
        instance.SetModel(source);
        Assert.Equal(source.Id, instance.Model.Id);
        Assert.Equal(17, instance.Model.Materials[0].Alpha);
        Assert.Equal(source.Nodes[0].AfterTransform, instance.Model.Nodes[0].AfterTransform);
        instance.Model.Nodes[0].AfterTransform = null;
        instance.Model.Materials[0].Alpha = 2;
        Assert.NotNull(source.Nodes[0].AfterTransform);
        Assert.Equal(17, source.Materials[0].Alpha);
        Model runtime = instance.Model;
        instance.SetModel(source);
        Assert.Same(runtime, instance.Model);
        Assert.Equal(2, instance.Model.Materials[0].Alpha);
        instance.SetModel(CreateModel());
        instance.SetModel(source);
        Assert.Same(runtime, instance.Model);
    }

    [Fact]
    public void InterleavedKeyframeSamplingPreservesEachInstancePose()
    {
        Model asset = CreateModel(animated: true);
        ModelInstance first = new(asset);
        ModelInstance second = new(asset);
        first.SetAnimation(0);
        second.SetAnimation(0);
        first.UpdateAnimFrames();
        first.Model.AnimateNodes(0, true, Matrix4.Identity, Vector3.One, first.AnimInfo);
        first.Model.UpdateMatrixStack();
        second.AnimInfo.Frame[0] = 2;
        second.Model.AnimateNodes(0, true, Matrix4.Identity, Vector3.One, second.AnimInfo);
        second.Model.UpdateMatrixStack();
        Assert.Equal(10, first.Model.Nodes[0].Animation.M41);
        Assert.Equal(10, first.Model.MatrixStackValues[12]);
        Assert.Equal(20, second.Model.Nodes[0].Animation.M41);
        Assert.Equal(20, second.Model.MatrixStackValues[12]);
        Assert.Equal(Matrix4.Identity, asset.Nodes[0].Animation);
        Assert.Equal(0, asset.MatrixStackValues[12]);
    }

    [Fact]
    public void RendererPreparationNormalizesEveryRuntimeCopyAndRetainsGeometryIdentity()
    {
        Model asset = CreateModel();
        asset.Materials[0].RenderMode = RenderMode.Unknown3;
        ModelInstance first = new(asset);
        ModelInstance second = new(asset);
        ScenePresentation.NormalizeModelMaterials(first.Model);
        Assert.Equal(RenderMode.Unknown3, second.Model.Materials[0].RenderMode);
        ScenePresentation.NormalizeModelMaterials(second.Model);
        Assert.Equal(RenderMode.Normal, first.Model.Materials[0].RenderMode);
        Assert.Equal(RenderMode.Normal, second.Model.Materials[0].RenderMode);
        Assert.NotSame(first.Model.Meshes[0], second.Model.Meshes[0]);
        Assert.Same(first.Model.Meshes[0].GeometryIdentity, second.Model.Meshes[0].GeometryIdentity);
    }

    [Fact]
    public void LateLodCopyNeedsItsOwnMaterialNormalizationWithoutChangingSource()
    {
        Model lodSource = CreateModel(); lodSource.Materials[0].RenderMode = RenderMode.Unknown4;
        ModelInstance rendered = new(CreateModel());
        ScenePresentation.NormalizeModelMaterials(rendered.Model);
        rendered.SetModel(lodSource);
        ScenePresentation.NormalizeModelMaterials(rendered.Model);
        Assert.Equal(RenderMode.Normal, rendered.Model.Materials[0].RenderMode);
        Assert.Equal(RenderMode.Unknown4, lodSource.Materials[0].RenderMode);
        Assert.Same(lodSource.Meshes[0].GeometryIdentity, rendered.Model.Meshes[0].GeometryIdentity);
    }

    [Fact]
    public void RawMetadataNameArraysAreIsolatedAcrossRuntimeCopies()
    {
        Model asset = CreateModel(metadata: true);
        Model first = new ModelInstance(asset).Model;
        Model second = new ModelInstance(asset).Model;
        first.RawNodes[0].Name[0] = 42;
        first.AnimationGroups.Material[0].Animations[string.Empty].Name[0] = 42;
        first.AnimationGroups.Texture[0].Animations[string.Empty].Name[0] = 42;
        first.AnimationGroups.Texcoord[0].Animations[string.Empty].Name[0] = 42;
        foreach (Model untouched in new[] { asset, second })
        {
            Assert.Equal(0, untouched.RawNodes[0].Name[0]);
            Assert.Equal(0, untouched.AnimationGroups.Material[0].Animations[string.Empty].Name[0]);
            Assert.Equal(0, untouched.AnimationGroups.Texture[0].Animations[string.Empty].Name[0]);
            Assert.Equal(0, untouched.AnimationGroups.Texcoord[0].Animations[string.Empty].Name[0]);
        }
    }

    private static T SingleAnimationGroup<T>() where T : struct
    {
        byte[] bytes = new byte[Marshal.SizeOf<T>()];
        bytes[Marshal.OffsetOf<T>("AnimationCount").ToInt32()] = 1;
        return Read.ReadStruct<T>(bytes);
    }

    private static Model CreateModel(bool animated = false, bool metadata = false)
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
                    [string.Empty] = Read.ReadStruct<NodeAnimation>(animationBytes)
                }));
        }
        else
        {
            animations.NodeAnimationGroups.Add(NodeAnimationGroup.Empty());
        }
        animations.MaterialAnimationGroups.Add(metadata
            ? new MaterialAnimationGroup(SingleAnimationGroup<RawMaterialAnimationGroup>(), new[] { 0f },
                new Dictionary<string, MaterialAnimation> { [string.Empty] = Read.ReadStruct<MaterialAnimation>(new byte[Marshal.SizeOf<MaterialAnimation>()]) })
            : MaterialAnimationGroup.Empty());
        animations.TextureAnimationGroups.Add(metadata
            ? new TextureAnimationGroup(SingleAnimationGroup<RawTextureAnimationGroup>(), Array.Empty<ushort>(), Array.Empty<ushort>(), Array.Empty<ushort>(),
                new Dictionary<string, TextureAnimation> { [string.Empty] = Read.ReadStruct<TextureAnimation>(new byte[Marshal.SizeOf<TextureAnimation>()]) })
            : TextureAnimationGroup.Empty());
        animations.TexcoordAnimationGroups.Add(metadata
            ? new TexcoordAnimationGroup(SingleAnimationGroup<RawTexcoordAnimationGroup>(), new[] { 1f }, new[] { 0f }, new[] { 0f },
                new Dictionary<string, TexcoordAnimation> { [string.Empty] = Read.ReadStruct<TexcoordAnimation>(new byte[Marshal.SizeOf<TexcoordAnimation>()]) })
            : TexcoordAnimationGroup.Empty());
        animations.NodeGroupOffsets.Add(0);
        animations.MaterialGroupOffsets.Add(0);
        animations.TextureGroupOffsets.Add(0);
        animations.TexcoordGroupOffsets.Add(0);
        return new Model("isolation", false, Read.ReadStruct<Header>(header),
            new[] { Read.ReadStruct<RawNode>(node) }, new[] { default(RawMesh) },
            new[] { Read.ReadStruct<RawMaterial>(new byte[Marshal.SizeOf<RawMaterial>()]) },
            Array.Empty<DisplayList>(), Array.Empty<IReadOnlyList<RenderInstruction>>(), animations,
            Array.Empty<Matrix4>(), Array.Empty<Recolor>(), new[] { 0 }, Array.Empty<Vector3Fx>(),
            Array.Empty<Vector3Fx>(), Array.Empty<int>(), Array.Empty<Fixed>());
    }
}
