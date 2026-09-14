using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;
using PrimeGamepadState = MphRead.Mods.Input.GamepadState;

namespace MphRead.Tests;

public sealed class KillcamInputTests
{
    [Fact]
    public void CompatibilityInputKeepsHeldKeyboardInputQuarantinedAfterExit()
    {
        var input = new SdlCompatibilityInput();
        WindowInputSnapshot held = Snapshot(Keys.W);

        input.Apply(held);
        Assert.True(input.Keyboard.IsKeyDown(Keys.W));

        input.Apply(held, suppressGameplayInput: true);
        Assert.False(input.Keyboard.IsKeyDown(Keys.W));

        // The physical key is still down when the replay exits. It must not
        // become a live movement bind until the user releases and presses it.
        input.Apply(held);
        Assert.False(input.Keyboard.IsKeyDown(Keys.W));
        input.Apply(default);
        input.Apply(held);
        Assert.True(input.Keyboard.IsKeyDown(Keys.W));
    }

    [Fact]
    public void GamepadInputQuarantineSuppressesHeldButtonsUntilRelease()
    {
        PrimeGamepadState before = GamepadInput.State;
        try
        {
            GamepadInput.State = new PrimeGamepadState
            {
                Connected = true,
                Buttons = GamepadButtons.B
            };
            GamepadInput.BeginGameplayInputQuarantine();
            GamepadInput.BeginFrame();
            Assert.Equal(GamepadButtons.None, GamepadInput.EffectiveButtons);
            Assert.Equal(GamepadButtons.None, GamepadInput.PressedButtons);

            GamepadInput.EndGameplayInputQuarantine();
            GamepadInput.BeginFrame();
            Assert.Equal(GamepadButtons.None, GamepadInput.EffectiveButtons);

            GamepadInput.State = default;
            GamepadInput.BeginFrame();
            GamepadInput.State = new PrimeGamepadState
            {
                Connected = true,
                Buttons = GamepadButtons.B
            };
            GamepadInput.BeginFrame();
            Assert.NotEqual(GamepadButtons.None, GamepadInput.EffectiveButtons);
        }
        finally
        {
            GamepadInput.State = before;
            GamepadInput.Reset();
        }
    }

    [Theory]
    [InlineData(0.75f, 0f, 0f, 0f, 0f, 0f)]
    [InlineData(0f, 0.75f, 0f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0.75f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0.75f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f, 0.75f, 0f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0.75f)]
    public void GamepadInputQuarantineWaitsForAllAnalogRelease(
        float leftX, float leftY, float rightX, float rightY,
        float leftTrigger, float rightTrigger)
    {
        PrimeGamepadState before = GamepadInput.State;
        try
        {
            GamepadInput.State = new PrimeGamepadState
            {
                Connected = true,
                LeftX = leftX,
                LeftY = leftY,
                RightX = rightX,
                RightY = rightY,
                LeftTrigger = leftTrigger,
                RightTrigger = rightTrigger
            };
            GamepadInput.BeginGameplayInputQuarantine();
            GamepadInput.BeginFrame();
            GamepadInput.EndGameplayInputQuarantine();
            GamepadInput.BeginFrame();
            Assert.Equal(GamepadButtons.None, GamepadInput.EffectiveButtons);
            Assert.False(GamepadInput.Movement.Active);
            Assert.True(GamepadInput.InputQuarantined);

            GamepadInput.State = new PrimeGamepadState { Connected = true };
            GamepadInput.BeginFrame();
            Assert.False(GamepadInput.InputQuarantined);
            GamepadInput.State = new PrimeGamepadState { Connected = true,
                LeftX = leftX, LeftY = leftY, RightX = rightX, RightY = rightY,
                LeftTrigger = leftTrigger, RightTrigger = rightTrigger };
            GamepadInput.BeginFrame();
            Assert.False(GamepadInput.InputQuarantined);
        }
        finally
        {
            GamepadInput.State = before;
            GamepadInput.Reset();
        }
    }

    [Fact]
    public void ConfiguredDesktopFirePressIsAKillcamSkipSurface()
    {
        var mouseFire = new Keybind(PrimeMouseButton.Left);
        var mouse = new WindowInputSnapshot(null, null, default, default,
            default, string.Empty, focused: true,
            mouseButtonEvents: new[]
            {
                new WindowMouseButtonEvent(MouseButton.Left, Down: true)
            });
        Assert.True(KillcamController.IsBindingPressed(mouseFire, mouse));

        var keyFire = new Keybind(PrimeKey.F);
        var keyboard = new WindowInputSnapshot(null, null, default, default,
            default, string.Empty, focused: true,
            keyEvents: new[]
            {
                new WindowKeyEvent(Keys.F, Down: true, Repeat: false, default)
            });
        Assert.True(KillcamController.IsBindingPressed(keyFire, keyboard));
        Assert.Equal(KillcamCommand.Skip,
            KillcamController.TranslateCommand(
                KillcamController.IsBindingPressed(mouseFire, mouse),
                gamepadPressed: false, touchPressed: false));
    }

    private static WindowInputSnapshot Snapshot(Keys key)
        => new(new HashSet<int> { (int)key }, new HashSet<int>(),
            Vector2.Zero, Vector2.Zero, Vector2.Zero, string.Empty, focused: true);
}
