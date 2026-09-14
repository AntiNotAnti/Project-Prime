using System;
using System.Linq;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class CosmeticRenderBudgetTests
{
    [Fact]
    public void DefaultLimitsMatchCosmeticSubBudget()
    {
        CosmeticBudgetLimits limits = CosmeticBudgetLimits.Default;
        Assert.Equal(32, limits.ParticleEmitters);
        Assert.Equal(512, limits.Particles);
        Assert.Equal(24, limits.RibbonSystems);
        Assert.Equal(384, limits.RibbonSegments);
        Assert.Equal(32, limits.AttachmentMeshes);
        Assert.Equal(8, limits.DistortionSources);
        Assert.Equal(8, limits.LocalLights);
    }

    [Fact]
    public void AdmissionIsStableAcrossTraversalOrderAndBounded()
    {
        CosmeticBudgetRequest[] forward = Enumerable.Range(0, 16)
            .Select(index => Request((ulong)(100 + index), (byte)(15 - index),
                distanceSquared: index % 4, particles: 48, segments: 32))
            .ToArray();
        CosmeticBudgetRequest[] reverse = forward.Reverse().ToArray();

        CosmeticBudgetAllowance[] first = CosmeticBudgetArbiter.Admit(forward).ToArray();
        CosmeticBudgetAllowance[] second = CosmeticBudgetArbiter.Admit(reverse).ToArray();

        Assert.Equal(first, second);
        Assert.True(first.Sum(item => item.Particles) <= 512);
        Assert.True(first.Sum(item => item.RibbonSegments) <= 384);
        Assert.True(first.Sum(item => item.DistortionSources) <= 8);
        Assert.True(first.Sum(item => item.LocalLights) <= 8);
    }

    [Fact]
    public void LocalThenFocusThenDistanceThenSlotDefinesPriority()
    {
        CosmeticBudgetRequest[] requests =
        {
            Request(10, 7, 0, local: false, focus: false),
            Request(20, 6, 100, local: false, focus: true),
            Request(30, 5, 1000, local: true, focus: false),
            Request(40, 2, 25, local: false, focus: false),
            Request(50, 1, 25, local: false, focus: false)
        };

        ulong[] order = CosmeticBudgetArbiter.Admit(requests)
            .Select(item => item.StableKey).ToArray();

        Assert.Equal(new ulong[] { 30, 20, 10, 50, 40 }, order);
    }

    [Fact]
    public void DuplicateStableKeysAreRejected()
    {
        CosmeticBudgetRequest[] requests = { Request(1, 0, 0), Request(1, 1, 1) };
        Assert.Throws<ArgumentException>(() => CosmeticBudgetArbiter.Admit(requests));
    }

    [Fact]
    public void FrameLoopAdmissionWritesIntoCallerOwnedBuffers()
    {
        CosmeticBudgetRequest[] requests =
        {
            Request(10, 7, 0),
            Request(20, 6, 100, focus: true),
            Request(30, 5, 1000, local: true)
        };
        var allowances = new CosmeticBudgetAllowance[requests.Length];

        int count = CosmeticBudgetArbiter.Admit(requests.AsSpan(), allowances);

        Assert.Equal(3, count);
        Assert.Equal(new ulong[] { 30, 20, 10 },
            allowances.Select(value => value.StableKey));
    }

    [Fact]
    public void PrimitiveSubmissionRejectsUnboundedRibbon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CosmeticPrimitiveSubmission(1, 0, CosmeticPrimitiveKind.Ribbon,
                Vector3.Zero, Vector3.One, Vector3.One, 1,
                CosmeticPrimitiveSubmission.MaximumRibbonSegments + 1));
        Assert.Throws<ArgumentException>(() =>
            new CosmeticPrimitiveSubmission(1, 0, CosmeticPrimitiveKind.Particle,
                Vector3.Zero, Vector3.One, Vector3.One, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CosmeticPrimitiveSubmission(1, 0, CosmeticPrimitiveKind.Particle,
                Vector3.Zero, Vector3.One, Vector3.One, 1, size: 0));
    }

    [Fact]
    public void PrimitiveBufferAppendsThenSortsAndCanBeReused()
    {
        var buffer = new CosmeticPrimitiveSubmissionBuffer(2);
        Assert.True(buffer.TryAdd(Primitive(30)));
        Assert.True(buffer.TryAdd(Primitive(10)));
        Assert.False(buffer.TryAdd(Primitive(20)));
        Assert.Equal(new ulong[] { 10, 30 },
            buffer.Seal().Select(item => item.StableKey));
        Assert.Throws<InvalidOperationException>(() => buffer.TryAdd(Primitive(20)));

        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.TryAdd(Primitive(40)));
    }

    [Fact]
    public void PrimitiveCapacityCoversEveryGlobalPrimitiveAllowance()
        => Assert.Equal(576, CosmeticPrimitiveSubmissionBuffer.MaximumCapacity);

    [Fact]
    public void SealCollapsesExactDuplicatesAndRejectsConflictingKeys()
    {
        var exact = new CosmeticPrimitiveSubmissionBuffer();
        Assert.True(exact.TryAdd(Primitive(2)));
        Assert.True(exact.TryAdd(Primitive(2)));
        Assert.Single(exact.Seal());

        var conflict = new CosmeticPrimitiveSubmissionBuffer();
        Assert.True(conflict.TryAdd(Primitive(3)));
        Assert.True(conflict.TryAdd(new CosmeticPrimitiveSubmission(3, 0,
            CosmeticPrimitiveKind.Particle, Vector3.One, Vector3.One,
            Vector3.One, 1)));
        Assert.Throws<InvalidOperationException>(() => conflict.Seal());
    }

    private static CosmeticBudgetRequest Request(ulong key, byte slot,
        float distanceSquared, bool local = false, bool focus = false,
        int particles = 8, int segments = 6)
        => new(key, slot, local, focus, distanceSquared,
            particleEmitters: 1, particles, ribbonSystems: 1, segments,
            attachmentMeshes: 1, distortionSources: 1, localLights: 1);

    private static CosmeticPrimitiveSubmission Primitive(ulong key)
        => new(key, 0, CosmeticPrimitiveKind.Particle, Vector3.Zero,
            Vector3.One, Vector3.One, 1);
}
