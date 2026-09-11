using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal readonly record struct HeadshotValidationPlan(
    int Arm, bool Vertical, bool LongRange, InputButtons Buttons, InputButtons Pressed);

/// <summary>
/// Pure input choreography used only by the explicit rendered headshot test.
/// It never runs unless WorkerOptions.HeadshotValidationScenario is enabled.
/// </summary>
internal static class HeadshotValidationController
{
    internal static HeadshotValidationPlan Plan(uint tick, int seconds, float range)
    {
        int frames = checked(seconds * 60);
        uint frame = tick % (uint)frames;
        int arm = Math.Clamp((int)((long)frame * 4 / frames), 0, 3);
        bool vertical = arm < 2;
        bool longRange = arm is 1 or 3;
        InputButtons buttons = InputButtons.None;
        if (vertical)
        {
            if (longRange && range < 16f || !longRange && range < 5f)
                buttons |= InputButtons.Back;
            else if (longRange && range > 24f || !longRange && range > 8f)
                buttons |= InputButtons.Forward;
            if (frame % 90 == 0) buttons |= InputButtons.Jump;
        }
        else
        {
            buttons |= frame / 45 % 2 == 0 ? InputButtons.Right : InputButtons.Left;
            if (longRange && range < 16f || !longRange && range < 5f)
                buttons |= InputButtons.Back;
            else if (longRange && range > 24f || !longRange && range > 8f)
                buttons |= InputButtons.Forward;
        }
        InputButtons pressed = (buttons & InputButtons.Jump) != 0
            ? InputButtons.Jump : InputButtons.None;
        return new(arm, vertical, longRange, buttons, pressed);
    }

    internal static InputCommand CreateCommand(uint tick, int seconds, float range,
        Vector3 aim, uint inputEpoch)
    {
        HeadshotValidationPlan plan = Plan(tick, seconds, range);
        return CreateCommand(tick, plan, aim, inputEpoch);
    }

    internal static InputCommand CreateCommand(uint tick, in HeadshotValidationPlan plan,
        Vector3 aim, uint inputEpoch)
    {
        if (inputEpoch == 0) throw new ArgumentOutOfRangeException(nameof(inputEpoch));
        return new InputCommand(tick, tick, tick, plan.Buttons, plan.Pressed, aim,
            InputCommand.NoWeapon, inputEpoch: inputEpoch);
    }
}
