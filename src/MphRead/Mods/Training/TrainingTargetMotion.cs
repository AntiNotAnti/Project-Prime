using System;
using MphRead.Entities;
using OpenTK.Mathematics;
namespace MphRead.Mods.Training;
public static class TrainingTargetGeometry
{
    public static float HeadTop(PlayerEntity player) => Fixed.ToFloat(player.Values.MaxPickupHeight);
    public static float HeadBottom(PlayerEntity player) => HeadTop(player) - .3f;
}
// A private integer PRNG makes authored anchor sequences stable across platforms.
public sealed class TrainingTargetMotion
{
    private uint _state;
    public TrainingTargetMotion(uint seed) => _state = seed == 0 ? 1u : seed;
    public int Next(int count) { _state ^= _state << 13; _state ^= _state >> 17; _state ^= _state << 5; return (int)(_state % (uint)count); }
    public static readonly Vector3[] Anchors = { new(-14, .1f, -8), new(-7, .1f, -18), new(7, .1f, -18), new(14, .1f, -8), new(-12, .1f, -30), new(12, .1f, -30), new(0, .1f, -38), new(-18, 4.1f, -20), new(18, 4.1f, -20), new(-9, 8.1f, -29), new(9, 12.1f, -34), new(0, 16.1f, -43) };
}
internal sealed class AimTrainerTargetController
{
    internal readonly PlayerEntity Player;
    internal int Appeared;
    internal int Attempts;
    internal int WakeFrame;
    internal int Anchor = -1;
    private readonly bool[] _previous;
    internal AimTrainerTargetController(PlayerEntity player) { Player = player; _previous = new bool[player.Controls.All.Length]; }
    internal void Drive(int frame, AimTrainerDefinition definition, bool running)
    {
        var c = Player.Controls;
        for (int i = 0; i < c.All.Length; i++) { _previous[i] = c.All[i].IsDown; c.All[i].IsDown = c.All[i].IsPressed = c.All[i].IsReleased = false; }
        c.ClearAnalogMovement();
        Player.IgnoreItemPickups = true;
        if (running && WakeFrame == 0 && definition.Movement != AimTrainerMovement.Static)
        {
            int period = 100 - (int)definition.Difficulty * 20;
            if (definition.Movement == AimTrainerMovement.WideStrafe) period *= 2;
            if (definition.Movement == AimTrainerMovement.DirectionChange) period = 20 + (frame / 120 * 17 % 50);
            int phase = (frame + Player.SlotIndex * 17 + (int)(definition.Seed % 97)) / period;
            bool right = (phase & 1) == 0;
            var anchor = TrainingTargetMotion.Anchors[Anchor];
            float width = anchor.Y > 1 ? 1.5f : definition.Movement == AimTrainerMovement.WideStrafe ? 9 : 5;
            if (Player.Position.X > anchor.X + width) right = false;
            if (Player.Position.X < anchor.X - width) right = true;
            // Targets face +Z, so their local right points toward world -X.
            c.MoveRight.IsDown = !right; c.MoveLeft.IsDown = right;
            if (definition.Movement == AimTrainerMovement.Jump) c.MoveRight.IsDown = c.MoveLeft.IsDown = false;
            if (definition.Difficulty == TrainingDifficulty.Beginner && frame % 4 == 0) c.MoveRight.IsDown = c.MoveLeft.IsDown = false;
            if (definition.Movement is AimTrainerMovement.Jump or AimTrainerMovement.JumpStrafe or AimTrainerMovement.AirborneCrossing) c.Jump.IsDown = frame % (definition.Movement == AimTrainerMovement.AirborneCrossing ? 45 : 75) < 3;
            if (definition.Movement == AimTrainerMovement.Circular)
            {
                c.MoveUp.IsDown = phase % 4 == 0;
                c.MoveDown.IsDown = phase % 4 == 2;
                if (Player.Position.Z > anchor.Z + width) { c.MoveUp.IsDown = false; c.MoveDown.IsDown = true; }
                if (Player.Position.Z < anchor.Z - width) { c.MoveUp.IsDown = true; c.MoveDown.IsDown = false; }
            }
            if (definition.Movement == AimTrainerMovement.RandomBurst && frame % 90 > 60) c.MoveLeft.IsDown = c.MoveRight.IsDown = false;
        }
        if (running && WakeFrame == 0 && definition.Movement != AimTrainerMovement.Static)
        {
            float speed = .4f + (int)definition.Difficulty * .2f;
            c.SetAnalogMovement((c.MoveRight.IsDown ? speed : c.MoveLeft.IsDown ? -speed : 0),
                c.MoveUp.IsDown ? speed : c.MoveDown.IsDown ? -speed : 0);
        }
        for (int i = 0; i < c.All.Length; i++) { c.All[i].IsPressed = c.All[i].IsDown && !_previous[i]; c.All[i].IsReleased = !c.All[i].IsDown && _previous[i]; }
        Player.ModNoteInput();
    }
}
