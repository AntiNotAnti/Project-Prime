using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class CameraIsolationTests
{
    [Fact]
    public void PlaybackAndTeardownAffectOnlyTheOwningScene()
    {
        using Scene first = new(headless: true);
        CameraSequence sequence = CreateSequence(first);
        var camera = new CameraInfo { Position = Vector3.UnitX };
        sequence.SetUp(camera, 12);
        first.CameraSequences.Intro = sequence;
        using Scene second = new(headless: true);
        CameraSequence other = CreateSequence(second);
        other.SetUp(new CameraInfo(), 3);
        other.End();
        second.CameraSequences.Clear();

        Assert.Same(sequence, first.CameraSequences.Current);
        Assert.Same(sequence, first.CameraSequences.Intro);
        Assert.Same(camera, sequence.CamInfoRef);
        Assert.Equal((ushort)12, sequence.TransitionTime);
        camera.Position = Vector3.UnitY;
        sequence.End();
        Assert.Null(first.CameraSequences.Current);
        Assert.Equal(Vector3.UnitX, camera.Position);
    }

    [Fact]
    public void CachedSequencesAndSpecialEntitiesBelongToOneScene()
    {
        using Scene first = new(headless: true);
        using Scene second = new(headless: true);
        CameraSequence sequence = CreateSequence(first);
        var cacheField = typeof(CameraSequenceManager).GetField("_sequences", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cache = (CameraSequence?[])cacheField.GetValue(first.CameraSequences)!;
        cache[0] = sequence;
        var entity = (CamSeqEntity)RuntimeHelpers.GetUninitializedObject(typeof(CamSeqEntity));
        first.SpecialEntities.CameraSequence = entity;
        var point = (PointModuleEntity)RuntimeHelpers.GetUninitializedObject(typeof(PointModuleEntity));
        typeof(SpecialEntityRegistry).GetProperty(nameof(SpecialEntityRegistry.PointModule))!.SetValue(first.SpecialEntities, point);

        second.CloseHeadless();
        Assert.Same(sequence, first.CameraSequences.GetOrLoad(0));
        Assert.Same(entity, first.SpecialEntities.CameraSequence);
        Assert.Same(point, first.SpecialEntities.PointModule);
        Assert.NotSame(cache, cacheField.GetValue(second.CameraSequences));
        first.CameraSequences.Clear();
        first.SpecialEntities.Clear();
        Assert.Null(cache[0]);
        Assert.Null(first.SpecialEntities.CameraSequence);
        Assert.Null(first.SpecialEntities.PointModule);
    }

    [Fact]
    public void CameraShakeConsumesOnlyTheProvidedSceneRandomStream()
    {
        using Scene first = new(headless: true);
        using Scene second = new(headless: true);
        first.Random.SetRng2(123);
        second.Random.SetRng2(456);
        var camera = new CameraInfo { Position = Vector3.UnitZ, Target = Vector3.Zero, UpVector = Vector3.UnitY, Shake = 1 };
        camera.Update(first);
        Assert.NotEqual(123u, first.Random.Rng2);
        Assert.Equal(456u, second.Random.Rng2);
    }

    private static CameraSequence CreateSequence(Scene scene) => (CameraSequence)Activator.CreateInstance(
        typeof(CameraSequence), BindingFlags.Instance | BindingFlags.NonPublic, null,
        new object[] { 0, "test.bin", scene, default(CameraSequenceHeader), Array.Empty<RawCameraSequenceKeyframe>() }, null)!;
}
