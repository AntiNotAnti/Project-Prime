using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace MphRead.Tests;

public sealed class ReplayQuickCaptureTests
{
    [Theory]
    [InlineData(Keys.F10, false, false, false, true)]
    [InlineData(Keys.R, true, false, false, true)]
    [InlineData(Keys.R, false, true, false, true)]
    [InlineData(Keys.R, false, false, false, false)]
    [InlineData(Keys.F10, false, false, true, false)]
    [InlineData(Keys.F9, false, false, false, false)]
    public void HotkeysAreExplicitRisingEdges(Keys key, bool control,
        bool command, bool repeat, bool expected)
    {
        Assert.Equal(expected,
            ReplayQuickCapture.IsHotkey(key, control, command, repeat));
    }

    [Fact]
    public void KeyUpNeverTriggersQuickCapture()
    {
        var released = new WindowKeyEvent(Keys.F10, Down: false,
            Repeat: false, Modifiers: default);
        Assert.False(ReplayQuickCapture.IsHotkey(released));
    }

    [Fact]
    public void QuickCaptureWindowIsTenSecondsAtSixtyHertz()
    {
        Assert.Equal(10u, ReplayQuickCapture.RecentSeconds);
        Assert.Equal(600u, ReplayQuickCapture.RecentFrames);
    }
}
