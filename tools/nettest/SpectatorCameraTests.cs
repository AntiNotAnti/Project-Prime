using System;
using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
namespace MphRead.NetTest;
internal static class SpectatorCameraTests
{
    public static int Run()
    {
        float oldSensitivity = InputSettings.MouseSensitivity;
        bool oldX = InputSettings.InvertMouseX, oldY = InputSettings.InvertMouseY;
        try
        {
            var focus = new HardwareFocusLatch();
            NetArchitectureTests.Check(focus.Poll(true, true, false), "ordinary held keys work");
            focus.LoseFocus();
            NetArchitectureTests.Check(!focus.Poll(false, false, false), "no camera movement while unfocused");
            NetArchitectureTests.Check(!focus.Poll(true, true, false), "Alt/Shift/movement remain suppressed on focus regain");
            NetArchitectureTests.Check(!focus.Poll(true, false, true), "held fire remains suppressed on focus regain");
            NetArchitectureTests.Check(focus.Poll(true, false, false), "neutral release restores hardware ownership");
            NetArchitectureTests.Check(focus.Poll(true, true, true), "fresh press after release works");
            foreach (var initial in new[] { Vector3.UnitY, -Vector3.UnitY, new Vector3(.00001f, 1, .00001f), -Vector3.UnitZ })
            {
                var facing = initial;
                for (int i = 0; i < 100; i++)
                {
                    SpectatorCameraMath.Rotate(ref facing, out var right, out var up, .03f, .2f, true);
                    NetArchitectureTests.Check(MathF.Abs(right.Length - 1) < .00001f && MathF.Abs(up.Length - 1) < .00001f
                        && MathF.Abs(facing.Length - 1) < .00001f, "unit camera axes even near vertical");
                    NetArchitectureTests.Check(MathF.Abs(Vector3.Dot(right, facing)) < .00001f
                        && MathF.Abs(Vector3.Dot(up, facing)) < .00001f && MathF.Abs(facing.Y) <= .98501f,
                        "orthogonal camera axes and hunter pitch limit");
                }
            }
            foreach (var vertical in new[] { Vector3.UnitY, -Vector3.UnitY, new Vector3(.00001f, 1, -.00001f) })
            {
                var facing = vertical;
                SpectatorCameraMath.Rotate(ref facing, out var right, out var up, 0, 0, true, Vector3.UnitZ);
                NetArchitectureTests.Check((right - Vector3.UnitZ).Length < .00001f,
                    "vertical entry preserves the previous +Z right axis instead of arbitrary yaw");
                NetArchitectureTests.Check(MathF.Abs(Vector3.Dot(up, right)) < .00001f,
                    "vertical fallback remains orthogonal");
                facing = vertical;
                SpectatorCameraMath.Rotate(ref facing, out right, out up, .2f, 0, true, Vector3.UnitZ);
                NetArchitectureTests.Check((right - new Vector3(-MathF.Sin(.2f), 0, MathF.Cos(.2f))).Length < .00001f,
                    "vertical fallback applies subsequent yaw input to the preserved basis");
            }
            InputSettings.MouseSensitivity = 2; InputSettings.InvertMouseX = false; InputSettings.InvertMouseY = false;
            var look = SpectatorCameraMath.MouseLook(4, 4);
            NetArchitectureTests.Check(MathF.Abs(look.Yaw - MathHelper.DegreesToRadians(2)) < .00001f
                && MathF.Abs(look.Pitch + MathHelper.DegreesToRadians(2)) < .00001f, "normal hunter sensitivity");
            InputSettings.InvertMouseX = true; InputSettings.InvertMouseY = true;
            var inverted = SpectatorCameraMath.MouseLook(4, 4);
            NetArchitectureTests.Check(inverted.Yaw == -look.Yaw && inverted.Pitch == -look.Pitch, "invert each axis once");
            NetArchitectureTests.Check(SpectatorCameraMath.AscendKey(true, false) == Keys.E
                && SpectatorCameraMath.AscendKey(true, true) == Keys.E
                && SpectatorCameraMath.AscendKey(false, false) == Keys.Space, "preserve spectator toggle and viewer ascent, unify replay ascent");
            Console.WriteLine("PASS: spectator focus release, camera basis, pitch, mouse scale, inversion and key compatibility"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { InputSettings.MouseSensitivity = oldSensitivity; InputSettings.InvertMouseX = oldX; InputSettings.InvertMouseY = oldY; }
    }
}
