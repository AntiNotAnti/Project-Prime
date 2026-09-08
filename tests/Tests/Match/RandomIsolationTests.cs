using System.Threading.Tasks;
using Xunit;

namespace MphRead.Tests;

public sealed class RandomIsolationTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(0x3DE9179Bu)]
    [InlineData(uint.MaxValue)]
    [InlineData(0x12345678u)]
    public void AlgorithmMatchesOriginalArithmetic(uint seed)
    {
        uint actual = seed, expected = seed;
        uint[] bounds = [0, 1, 4096, 0x168000, uint.MaxValue];
        for (int i = 0; i < 1000; i++)
        {
            uint bound = bounds[i % bounds.Length];
            unchecked { expected *= 0x7FF8A3ED; expected += 0x2AA01D31; }
            uint result = (uint)((expected >> 16) * (long)bound / 0x10000L);
            Assert.Equal(result, RngAlgorithm.Next(ref actual, bound));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task SceneStreamsRemainIndependentAcrossParallelWorkAndReset()
    {
        using var a = new Scene(headless: true);
        using var b = new Scene(headless: true);
        var expected = new MatchRandom();
        uint initial1 = a.Random.Rng1, initial2 = a.Random.Rng2;
        await Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                b.Random.GetRandomInt1(100);
                b.Random.GetRandomInt2(100);
            }
        });
        Assert.Equal(initial1, a.Random.Rng1);
        Assert.Equal(initial2, a.Random.Rng2);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(expected.GetRandomInt1(1000), a.Random.GetRandomInt1(1000));
            Assert.Equal(expected.GetRandomInt2(1000), a.Random.GetRandomInt2(1000));
        }
        a.Random.SetRng1(initial1);
        a.Random.SetRng2(initial2);
        expected = new MatchRandom();
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(expected.GetRandomInt1(1000), a.Random.GetRandomInt1(1000));
            Assert.Equal(expected.GetRandomInt2(1000), a.Random.GetRandomInt2(1000));
        }
    }
}
