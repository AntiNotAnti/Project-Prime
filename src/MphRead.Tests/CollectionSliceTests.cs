using System;
using Xunit;

namespace MphRead.Tests;

public sealed class CollectionSliceTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void UnsignedOffsetSelectsMutableSliceAndPreservesAliasing(uint start)
    {
        int[] values = { 10, 20, 30 };
        // Assignment to Span also guards against accidentally selecting a read-only overload.
        Span<int> slice = values.AsSpan().Slice(start);
        Assert.Equal(values.Length - (int)start, slice.Length);
        if (!slice.IsEmpty)
        {
            slice[0] = 99;
            Assert.Equal(99, values[(int)start]);
        }
        Assert.True(slice.SequenceEqual(values.AsSpan().Slice((long)start)));
    }

    [Fact]
    public void EmptyAndEndOffsetsWorkForMutableAndReadOnlySpans()
    {
        Span<int> empty = Span<int>.Empty.Slice(0u);
        Assert.True(empty.IsEmpty);
        Assert.True(Span<int>.Empty.Slice(0L).IsEmpty);
        ReadOnlySpan<int> readOnly = new[] { 10, 20, 30 };
        Assert.Equal(20, readOnly.Slice(1u)[0]);
        Assert.True(readOnly.Slice(3u).IsEmpty);
        Assert.True(ReadOnlySpan<int>.Empty.Slice(0u).IsEmpty);
    }

    [Theory]
    [InlineData(4u)]
    [InlineData(2147483647u)]
    [InlineData(2147483648u)]
    [InlineData(uint.MaxValue)]
    public void UnsignedOffsetsOutsideSpanRetainBoundsFailures(uint start)
    {
        int[] values = { 10, 20, 30 };
        Assert.Throws<ArgumentOutOfRangeException>(() => values.AsSpan().Slice(start));
        Assert.Throws<ArgumentOutOfRangeException>(() => values.AsSpan().Slice((long)start));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOnlySpan<int>(values).Slice(start));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(4L)]
    [InlineData(2147483648L)]
    [InlineData(long.MaxValue)]
    public void ExistingLongOffsetBoundsRemainUnchanged(long start)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new int[3].AsSpan().Slice(start));
    }

    [Fact]
    public void ExistingLongOffsetNarrowingIsNotChangedByCompilerCompatibilityFix()
    {
        int[] values = { 10, 20, 30 };
        // The pre-existing overload narrows to int; changing that policy is outside this migration.
        Span<int> slice = values.AsSpan().Slice(0x100000001L);
        Assert.Equal(2, slice.Length);
        slice[0] = 99;
        Assert.Equal(99, values[1]);
    }
}
