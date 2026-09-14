using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class RoomPortalCullingTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void PortalCullingFailsOpenForDetachedOrImpossibleCamera(
        bool detachedView, bool trackedPartCouldContainCamera, bool expected)
    {
        Assert.Equal(expected, RoomEntityPresentation.ShouldBypassPortalCulling(
            detachedView, trackedPartCouldContainCamera));
    }
}
