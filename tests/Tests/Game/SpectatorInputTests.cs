using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class SpectatorInputTests
{
    [Fact]
    public void InputStarvationKeepsSpectatingUntilExplicitRejoin()
    {
        var stream = new ServerInputStream();
        var spectate = new InputCommand(0, 0, 0,
            InputButtons.Spectate | InputButtons.Shoot | InputButtons.Forward,
            InputButtons.Jump, -Vector3.UnitZ, 3);
        stream.Receive(new[] { spectate }, 0);
        stream.Take(0);
        stream.Take(1);
        Assert.Equal(spectate, stream.Take(2));
        for (uint tick = 10; tick < 600; tick++)
        {
            InputCommand stale = stream.Take(tick);
            Assert.Equal(InputButtons.Spectate, stale.Buttons);
            Assert.Equal(InputButtons.None, stale.Buttons & InputButtons.Shoot);
            Assert.Equal(InputButtons.None, stale.Buttons & InputButtons.Forward);
            Assert.Equal(InputButtons.None, stale.Pressed);
            Assert.Equal(InputCommand.NoWeapon, stale.DesiredWeapon);
        }
        var rejoin = spectate with { Sequence = 1, ClientTick = 1, Buttons = 0, Pressed = 0 };
        stream.Receive(new[] { rejoin }, 600);
        Assert.Equal(InputButtons.None, stream.Take(600).Buttons);
    }
}
