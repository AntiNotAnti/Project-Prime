using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class AltFormPoseInterpolationTests
{
    [Fact]
    public void SpireAttackSamplingMatchesAuthoredNoNodeTransformPath()
    {
        Model model = Fixture();
        model.Nodes[0].Position = new Vector3(4, 0, 0);
        var info = new AnimationInfo();
        Matrix4 parent = Matrix4.CreateRotationY(.5f);
        var transforms = new Matrix4[1];
        var poses = new Matrix4[1];

        NodePoseSampler.Sample(model, info, parent, transforms, poses,
            useNodeTransform: false);
        model.ComputeNodeMatrices(0);
        model.AnimateNodes(0, useNodeTransform: false, parent,
            Vector3.One, info);

        Assert.Equal(model.Nodes[0].Animation, poses[0]);
    }

    [Fact]
    public void ResolvedAltPoseBlendsSimulationAndUsesCurrentPresentationRoot()
    {
        Model model = Fixture();
        var info = new AnimationInfo();
        var history = new ModelPoseHistory(model);
        Matrix4[] first = [Matrix4.CreateTranslation(0, 0, 0)];
        Matrix4[] second = [Matrix4.CreateTranslation(2, 0, 0)];
        Matrix4[] presented = [Matrix4.CreateTranslation(10, 0, 0)];

        history.CaptureResolved(info, first, 1, 0);
        history.CaptureResolved(info, second, 2, 0);
        history.Resolve(.5f, presented, out Matrix4[] poses,
            out float[] stack);

        // The simulation history contributes its halfway pose (1), while the
        // temporary remote/local presentation root moves the current sample
        // from 2 to 10. The result is 1 + 8, without mutating gameplay state.
        Assert.Equal(9, poses[0].Row3.X, 5);
        Assert.Equal(9, stack[12], 5);
    }

    [Fact]
    public void AltPoseModeChangeIsAPresentationDiscontinuity()
    {
        Model model = Fixture();
        var info = new AnimationInfo();
        var history = new ModelPoseHistory(model);
        history.CaptureResolved(info,
            [Matrix4.CreateTranslation(1, 0, 0)], 1, 0, mode: 0);
        history.CaptureResolved(info,
            [Matrix4.CreateTranslation(8, 0, 0)], 2, 0, mode: 1);

        history.Resolve(0, out Matrix4[] poses, out _);

        Assert.Equal(8, poses[0].Row3.X, 5);
    }

    private static Model Fixture()
    {
        Model model = (Model)RuntimeHelpers.GetUninitializedObject(typeof(Model));
        Node node = (Node)RuntimeHelpers.GetUninitializedObject(typeof(Node));
        Set(node, "Name", "alt");
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
        return model;
    }

    private static void Set(object value, string name, object field)
        => value.GetType().GetField($"<{name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
}
