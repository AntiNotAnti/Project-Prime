using MphRead.Entities;
using Xunit;

namespace MphRead.Tests;

public sealed class SpirePresentationTests
{
    [Theory]
    [InlineData(Hunter.Spire, true, true, 2, 2, true)]
    [InlineData(Hunter.Spire, true, false, 2, -1, true)]
    [InlineData(Hunter.Spire, true, false, 2, 3, true)]
    [InlineData(Hunter.Spire, true, false, 2, 2, false)]
    [InlineData(Hunter.Samus, true, true, 2, 3, false)]
    [InlineData(Hunter.Spire, false, true, 2, 3, false)]
    public void PortalCullingFailsOpenOnlyForUncertainSpireAltCamera(
        Hunter hunter, bool altForm, bool climbing, int trackedPart,
        int resolvedPart, bool expected)
    {
        Assert.Equal(expected, RoomEntityPresentation.ShouldBypassSpirePortalCulling(
            hunter, altForm, climbing, trackedPart, resolvedPart));
    }
}
