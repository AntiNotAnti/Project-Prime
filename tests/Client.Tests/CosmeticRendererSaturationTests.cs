using MphRead.Cosmetics;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace MphRead.Tests;

public sealed class CosmeticRendererSaturationTests
{
    [Fact]
    public void SaturatedRenderFrameSuppressesCosmeticsWithoutAcquiring()
    {
        var presentation = (ScenePresentation)RuntimeHelpers.GetUninitializedObject(
            typeof(ScenePresentation));
        var frame = new RenderFrame(capacity: 4, maximumCapacity: 4);
        typeof(ScenePresentation).GetField("_renderFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(
                presentation, frame);
        while (frame.Count < frame.MaximumCapacity) frame.Acquire();
        var buffer = new CosmeticPrimitiveSubmissionBuffer();
        Assert.True(buffer.TryAdd(new CosmeticPrimitiveSubmission(
            stableKey: 1, playerSlot: 0, kind: CosmeticPrimitiveKind.Particle,
            start: Vector3.Zero, end: Vector3.Zero, color: Vector3.One, intensity: 1,
            size: .1f)));

        Exception? error = Record.Exception(
            () => presentation.FlushCosmeticSubmissions(buffer.Seal()));

        Assert.Null(error);
        Assert.Equal(frame.MaximumCapacity, frame.Count);
    }
}
