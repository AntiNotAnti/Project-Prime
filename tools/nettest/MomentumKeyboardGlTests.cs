using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// OS automation: Windows SendInput or own-PID macOS CGEvent -> real GLFW KeyboardState -> local
// Scene controls -> Native/Momentum body -> visible GL. Not physical human input.
internal static class MomentumKeyboardGlTests
{
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion
    { [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct OsInput
    { internal uint Type; internal InputUnion Data; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, OsInput[] input, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightPostEventAccess();
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr value, ulong flags);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPostToPid(int pid, IntPtr value);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static int Run(MovementModel model, string outputPath)
    {
        Scene? scene = null; NativeWindow? window = null; DesktopGraphicsSession? graphics = null; IntPtr handle = IntPtr.Zero;
        var held = new HashSet<ushort>();
        bool mac = OperatingSystem.IsMacOS();
        int? sessionId = null; int postedEvents = 0;
        string output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            Check(OperatingSystem.IsWindows() || mac, "requires Windows or macOS interactive OS input");
            if (mac)
            {
                // Read-only current-process TCC preflight. Never request access
                // or open a window when this process cannot post events.
                bool access = CGPreflightPostEventAccess();
                if (!access)
                {
                    File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, model = model.ToString(),
                        platform = "macOS", status = "BLOCKED_EVENT_POST_ACCESS", CGPreflightPostEventAccess = access,
                        postedEvents, windowCreated = false, sessionId,
                        scope = "Actual movement fixture current-process preflight; no permission request, prompt, window or event post." }));
                    throw new InvalidOperationException("macOS event-post access denied; zero events posted, no window created, no permission prompt requested.");
                }
            }
            else
            {
                sessionId = Process.GetCurrentProcess().SessionId;
                Check(sessionId != 0, "requires actual interactive Windows session, not SSH session0");
            }
            Check(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive, "ordinary offline visible process");
            Check(ServerSim.Available(out string reason), reason);
            string title = "Project Prime Movement Keyboard Acceptance " + Environment.ProcessId;
            var settings = DesktopGlContext.Settings(); settings.ClientSize = new(800, 600);
            settings.StartVisible = true; settings.Title = title;
            window = new NativeWindow(settings);
            graphics = new DesktopGraphicsSession(window);
            GLFW.SwapInterval(0); window.Focus(); window.ProcessEvents(0);
            if (mac)
                Check(window.IsFocused && window.Title == title, "automation owns its uniquely titled focused native window and targets only its process ID");
            else
            {
                handle = FindWindow(null, title); GetWindowThreadProcessId(handle, out uint windowProcess);
                Check(handle != IntPtr.Zero && windowProcess == Environment.ProcessId && GetForegroundWindow() == handle,
                    "automation owns only its uniquely titled foreground window");
            }
            GamepadManager.ClearAll(); PointerDevice.Reset(); Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            // Real GLFW state objects only: no SyntheticInput or SetKeyState seam.
            scene = new Scene(window.FramebufferSize, window.KeyboardState, window.MouseState, _ => { }, () => { });
            scene.AddPlayer(Hunter.Samus); scene.AddRoom("MP3 PROVING GROUND", GameMode.Battle, playerCount: 1);
            new MatchDefinition { Mode = GameMode.Battle, MovementModel = model, TimeLimitSeconds = 0, PointGoal = 10000 }.ApplyInitialConfiguration(scene.GameState);
            typeof(Scene).GetField("_developerMomentumDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, true);
            scene.OnLoad(); scene.OnResize(); var player = scene.Players.Main;
            foreach (var control in player.Controls.All) { control.Type = ButtonType.Key; control.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter; player.Controls.MoveUp.Key = Keys.W;
            player.Controls.MoveRight.Key = Keys.D; player.Controls.Jump.Key = Keys.Space;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = false;
            int pictures = 0; var wall = Stopwatch.StartNew(); double next = 0;
            void Step(bool capture = false)
            {
                while (wall.Elapsed.TotalSeconds < next) Thread.Sleep(1);
                next += 1.0 / 60;
                Check(mac ? window.IsFocused : GetForegroundWindow() == handle, "foreground ownership retained; abort rather than injecting into another app");
                window.ProcessEvents(0); scene.OnSimulationFrame(); scene.OnDrawFrame();
                Check(scene.OnRenderFrame(), "visible real HUD/world GL renders");
                if (capture) Check(ScreenCapture.SaveWindow(scene, Path.ChangeExtension(output, ".png")), "actual visible GL framebuffer saved");
                DesktopGraphicsSession.Present(window); scene.AfterRenderFrame(); pictures++;
            }
            Key(0x0D, true);
            for (int tick = 0; tick < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); tick++) Step();
            bool enterObserved = window.KeyboardState.IsKeyDown(Keys.Enter);
            Key(0x0D, false);
            Check(enterObserved && player.LoadFlags.TestFlag(LoadFlags.Spawned), "OS Enter traverses real GLFW keyboard state and native spawn");
            scene.CameraSequences.Current?.End(); for (int tick = 0; tick < 120; tick++) Step();
            Vector3 origin = player.Position; long start = scene.DeveloperMomentumSteps;
            int wFrames = 0, dFrames = 0, jumpFrames = 0, consumedFrames = 0; bool airborne = false;
            var samples = new List<object>();
            for (int tick = 0; tick < 180; tick++)
            {
                Key(0x57, tick < 48 || tick is >= 90 and < 120);
                Key(0x44, tick is >= 48 and < 90); Key(0x20, tick == 90);
                Step(capture: tick == 110);
                if (window.KeyboardState.IsKeyDown(Keys.W)) wFrames++;
                if (window.KeyboardState.IsKeyDown(Keys.D)) dFrames++;
                if (window.KeyboardState.IsKeyDown(Keys.Space)) jumpFrames++;
                if (player.Controls.MoveUp.IsDown || player.Controls.MoveRight.IsDown || player.Controls.Jump.IsDown) consumedFrames++;
                airborne |= !player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (tick % 30 == 0) samples.Add(new { tick, position = new[] { player.Position.X, player.Position.Y, player.Position.Z },
                    nativeVelocity = new[] { player.Speed.X, player.Speed.Y, player.Speed.Z }, w = window.KeyboardState.IsKeyDown(Keys.W),
                    d = window.KeyboardState.IsKeyDown(Keys.D), space = window.KeyboardState.IsKeyDown(Keys.Space) });
            }
            Check(wFrames > 0 && dFrames > 0 && jumpFrames > 0 && consumedFrames > 0 && airborne && (player.Position - origin).Length > .1f,
                "OS injected movement/jump reaches actual keyboard, controls and collision body");
            long solverSteps = scene.DeveloperMomentumSteps - start;
            Check(model == MovementModel.Native ? solverSteps == 0 : solverSteps > 0, "actual visible local model governs solver");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, model = model.ToString(), sessionId, postedEvents,
                processId = Environment.ProcessId, renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version),
                pictures, wallSeconds = wall.Elapsed.TotalSeconds, fixedSimulationHz = 60, wFrames, dFrames, jumpFrames, consumedFrames, solverSteps,
                scope = mac
                    ? "Automated macOS CGEventPostToPid targeting only this process through focused native window/actual GLFW KeyboardState/local Samus controls and rendered GL. No physical keyboard/human playtest or achieved display-Hz claim."
                    : "Automated Windows SendInput through actual interactive foreground window/GLFW KeyboardState/local Samus controls and rendered GL. No physical keyboard/human playtest or achieved display-Hz claim.", samples }));
            Console.WriteLine($"MOMENTUM KEYBOARD GL PASS {model}, session={sessionId}, OS input frames={consumedFrames}, solver={solverSteps}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("MOMENTUM KEYBOARD GL FAIL " + error); return 1; }
        finally
        {
            // Release exactly the keys this automation pressed, even on failure.
            foreach (ushort key in new List<ushort>(held)) Send(key, false);
            try { if (scene != null) MomentumSceneTests.Release(scene); }
            finally { graphics?.Dispose(); window?.Dispose(); }
        }
        void Key(ushort key, bool down)
        {
            if (down == held.Contains(key)) return;
            Check(mac ? window?.IsFocused == true : handle != IntPtr.Zero && GetForegroundWindow() == handle, "OS key injection targets only own foreground test window");
            Send(key, down); if (down) held.Add(key); else held.Remove(key);
        }
        void Send(ushort key, bool down)
        {
            if (mac)
            {
                // HIToolbox Events.h: kVK_Return=0x24, kVK_ANSI_W=0x0D,
                // kVK_ANSI_D=0x02, kVK_Space=0x31. Map fixture Win32 keys only.
                ushort nativeKey = key switch { 0x0D => 0x24, 0x57 => 0x0D, 0x44 => 0x02, 0x20 => 0x31,
                    _ => throw new ArgumentOutOfRangeException(nameof(key)) };
                IntPtr value = CGEventCreateKeyboardEvent(IntPtr.Zero, nativeKey, down);
                Check(value != IntPtr.Zero, "macOS created own-process keyboard event");
                try { CGEventSetFlags(value, 0); CGEventPostToPid(Environment.ProcessId, value); postedEvents++; }
                finally { CFRelease(value); }
                return;
            }
            var input = new[] { new OsInput { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = down ? 0u : 2u } } } };
            Check(SendInput(1, input, Marshal.SizeOf<OsInput>()) == 1, "Windows accepted exactly one automated OS key event");
            postedEvents++;
        }
    }
}
