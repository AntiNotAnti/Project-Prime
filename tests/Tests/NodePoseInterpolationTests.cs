using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;
namespace MphRead.Tests;
public class NodePoseInterpolationTests
{
    [Fact]
    public void AuthoredDoorLutMatchesGameEvaluatorWithoutWritingSharedCaches()
    {
        var (model, info) = Fixture();
        var scratch = new Matrix4[2];
        var poses = new Matrix4[2];
        Matrix4 sentinel = Matrix4.CreateTranslation(90, 80, 70);
        foreach (Node node in model.Nodes) { node.Animation = sentinel; node.Transform = sentinel; }
        float[] before = new float[32];
        for (int i = 0; i < before.Length; i++) before[i] = model.MatrixStackValues[i];
        NodePoseSampler.Sample(model, info, Matrix4.CreateTranslation(10, 0, 0), scratch, poses);
        foreach (Node node in model.Nodes) { Assert.Equal(sentinel, node.Animation); Assert.Equal(sentinel, node.Transform); }
        Assert.Equal(before, model.MatrixStackValues);
        model.ComputeNodeMatrices(0);
        model.AnimateNodes(0, true, Matrix4.CreateTranslation(10, 0, 0), model.Scale, info);
        for (int i = 0; i < poses.Length; i++) Assert.Equal(model.Nodes[i].Animation, poses[i]);
    }
    [Fact]
    public void DoorOpeningBlendsNodesAndSkinStackAndResetsAtAnimationChange()
    {
        var (model, info) = Fixture();
        var history = new ModelPoseHistory(model);
        info.Frame[0] = 0;
        history.Capture(info, Matrix4.Identity, 1, 0);
        info.Frame[0] = 1;
        history.Capture(info, Matrix4.Identity, 2, 0);
        history.Resolve(.5f, out Matrix4[] poses, out float[] stack);
        Assert.Equal(1f, poses[0].M41);
        Assert.Equal(1f, stack[12]);
        Matrix4 nodeBefore = model.Nodes[0].Animation;
        // Repeated render submissions neither advance nor alter authoritative animation.
        history.Resolve(.75f, out poses, out stack);
        Assert.Equal(1.5f, poses[0].M41);
        Assert.Equal(1, info.Frame[0]);
        Assert.Equal(nodeBefore, model.Nodes[0].Animation);
        info.Index[0] = 2;
        history.Capture(info, Matrix4.Identity, 3, 0);
        history.Resolve(0, out poses, out stack);
        Assert.Equal(2f, poses[0].M41);
    }
    private static (Model, AnimationInfo) Fixture()
    {
        Model model = (Model)RuntimeHelpers.GetUninitializedObject(typeof(Model));
        Node root = NewNode("door", -1, 1);
        Node child = NewNode("panel", 0, -1);
        child.Position = new Vector3(0, 1, 0);
        Set(model, "Nodes", new List<Node> { root, child });
        Set(model, "Scale", Vector3.One);
        Set(model, "NodeMatrixIds", new int[] { 0, 1 });
        typeof(Model).GetField("_matrixStackValues", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(model, new float[32]);
        object raw = default(RawNodeAnimationGroup);
        typeof(RawNodeAnimationGroup).GetField("FrameCount")!.SetValue(raw, 2u);
        object animation = default(NodeAnimation);
        foreach (FieldInfo field in typeof(NodeAnimation).GetFields())
        {
            if (field.Name.Contains("LutLength")) field.SetValue(animation, (ushort)1);
            if (field.Name.Contains("Blend")) field.SetValue(animation, (byte)1);
        }
        typeof(NodeAnimation).GetField("TranslateLutLengthX")!.SetValue(animation, (ushort)2);
        var group = new NodeAnimationGroup((RawNodeAnimationGroup)raw, new float[] { 1 }, new float[] { 0 },
            new float[] { 0, 2 }, new Dictionary<string, NodeAnimation> { ["door"] = (NodeAnimation)animation });
        var info = new AnimationInfo();
        info.Node.Group = group;
        info.Index[0] = 0;
        info.Frame[0] = 1;
        return (model, info);
    }
    private static Node NewNode(string name, int parent, int child)
    {
        Node node = (Node)RuntimeHelpers.GetUninitializedObject(typeof(Node));
        Set(node, "Name", name); Set(node, "ParentIndex", parent); Set(node, "ChildIndex", child); Set(node, "NextIndex", -1);
        node.Scale = Vector3.One;
        return node;
    }
    private static void Set(object value, string name, object field) => value.GetType()
        .GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
}
