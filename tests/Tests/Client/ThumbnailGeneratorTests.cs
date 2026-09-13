using MphRead;
using MphRead.Mods;
using MphRead.Mods.MapGen;
using Xunit;

public sealed class ThumbnailGeneratorTests
{
    [Fact]
    public void SpawnProbeReadsOnlyBuiltInEntityFiles()
    {
        Assert.True(ThumbnailGenerator.ShouldProbeBaseGameEntityFile(
            RuntimeRoomRegistry.FirstCustomRoomId - 1));
        Assert.False(ThumbnailGenerator.ShouldProbeBaseGameEntityFile(
            RuntimeRoomRegistry.FirstCustomRoomId));
        Assert.False(ThumbnailGenerator.ShouldProbeBaseGameEntityFile(int.MaxValue));
    }
}
