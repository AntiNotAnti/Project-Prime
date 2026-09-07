using MphRead.Mods;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class SpectatorTouchControlsTests
{
    [Theory]
    [InlineData(10, 8, 1)]
    [InlineData(100, 8, 2)]
    [InlineData(200, 8, 8)]
    [InlineData(10, 20, 16)]
    [InlineData(90, 20, 32)]
    [InlineData(150, 20, 64)]
    [InlineData(220, 20, 128)]
    [InlineData(100, 30, 0)]
    public void ExistingOverlayExposesCameraCommandsWithoutExtraHudArea(float x, float y, int command)
        => Assert.Equal(command, SpectatorCameraController.PointerCommand(x, y));

    [Fact]
    public void CameraPreferencesAreBoundedAndUnavailableDuringGameplay()
    {
        SpectatorMode.Reset();
        var camera = new SpectatorCameraController();
        camera.ApplyCommands(null!, 16 | 64);
        Assert.Equal(78, camera.FieldOfView); Assert.Equal(1, camera.SpeedScale);
        Assert.False(SpectatorCameraController.QueuePointerDown(10, 8));
        Assert.Equal(0, SpectatorCameraController.PointerCommand(float.NaN, 8));
        // Isolate presentation preference input from player/content initialization.
        typeof(SpectatorMode).GetProperty(nameof(SpectatorMode.IsSpectating))!
            .GetSetMethod(true)!.Invoke(null, new object[] { true });
        try
        {
            for (int i = 0; i < 30; i++) camera.ApplyCommands(null!, 16 | 64);
            Assert.Equal(40, camera.FieldOfView); Assert.Equal(.25f, camera.SpeedScale);
            for (int i = 0; i < 30; i++) camera.ApplyCommands(null!, 32 | 128);
            Assert.Equal(110, camera.FieldOfView); Assert.Equal(4, camera.SpeedScale);
        }
        finally { SpectatorMode.Reset(); }
    }
}
