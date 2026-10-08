#if MPHREAD_SHELL && MPHREAD_AVALONIA
using System;
using MphRead.Mods.Launcher.Gui;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>Exercise the real desktop launcher without cartridge data.</summary>
    internal static class LauncherWindowCheck
    {
        private static bool _active;
        private static bool _passed;
        private static int _frames;
        private static bool _audio;
        private static bool _match;
        private static bool _training;
        private static Training.AimTrainerDefinition _trainingDefinition;
        private static string? _trainingCapture;
        private static Scene? _closingScene;

        public static int Run()
        {
            _active = true;
            _passed = false;
            _frames = 0;
            _closingScene = null;
            _audio = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-audiocheck");
            _training = Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-trainingwindowcheck");
            var args = Environment.GetCommandLineArgs();
            int drillIndex = Array.IndexOf(args, "-trainingdrill");
            var drill = drillIndex >= 0 && drillIndex + 1 < args.Length
                && Enum.TryParse<Training.AimTrainerDrill>(args[drillIndex + 1], out var selected)
                ? selected : Training.AimTrainerDrill.StaticPrecision;
            _trainingDefinition = (Training.AimTrainerDefinition.Default with { Drill = drill, TargetCount = 5,
                Weapon = BeamType.Imperialist }).Sanitize();
            int captureIndex = Array.IndexOf(args, "-trainingcapture");
            _trainingCapture = captureIndex >= 0 && captureIndex + 1 < args.Length ? args[captureIndex + 1] : null;
            _match = _training || Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-matchclosecheck");
            if (_match && !Launcher.GameFiles.Ready)
            {
                Console.Error.WriteLine("Match close check requires extracted game files.");
                _active = false;
                return 1;
            }
            bool geometry = WindowGeometry.Enabled;
            WindowGeometry.Enabled = false;
            try
            {
                bool ran = GuiLauncher.TryRun();
                if (_match && (_closingScene?.Exiting != true
                    || !MphRead.Sound.Sfx.ShutdownCompletion.IsCompletedSuccessfully))
                {
                    Console.Error.WriteLine("Match or OpenAL cleanup did not finish before window disposal.");
                    _passed = false;
                }
                if ((_match || _audio) && MusicPlayer.Available)
                {
                    Console.Error.WriteLine("Music engine remained open after the window closed.");
                    _passed = false;
                }
                Console.WriteLine(ran && _passed ? "Launcher window check passed." : "Launcher window check failed.");
                return ran && _passed ? 0 : 1;
            }
            finally
            {
                _active = false;
                WindowGeometry.Enabled = geometry;
            }
        }

        // Called before buffer swap, after the same UI path used on first launch.
        internal static void AfterDraw(RenderWindow window)
        {
            if (!_active || (++_frames != 20 && _frames != (_training ? 180 : 40))) return;
            try
            {
                if (_frames == 20)
                {
                    // No cartridge data required: exercise the process-wide
                    // device also used by settings previews and match music.
                    if (_audio)
                    {
                        if (!MphRead.MusicPlayer.Available)
                            throw new InvalidOperationException("Audio device unavailable; audio close check cannot run.");
                        MphRead.MusicPlayer.PlaybackDevice!.Start();
                        Console.WriteLine("[windowcheck] audio device started");
                    }
                    Console.WriteLine($"[windowcheck] {GL.GetString(StringName.Renderer)}; GL {GL.GetString(StringName.Version)}");
                    Link(Shaders.VertexShader, Shaders.FragmentShader);
                    Link(Shaders.RttVertexShader, Shaders.RttFragmentShader);
                    Link(Shaders.RttVertexShader, Shaders.CelFragmentShader);
                    Link(Shaders.RttVertexShader, Shaders.ShiftFragmentShader);
                    Link(Render.GraphicsPipelineShader.VertexSource,
                        Render.GraphicsPipelineShader.FragmentSource);
                }
                int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
                if (!Render.UiOverlay.HasFrame || width <= 0 || height <= 0)
                    throw new InvalidOperationException("Launcher did not upload a frame.");
                byte[] pixels = new byte[checked(width * height * 4)];
                GL.ReadBuffer(ReadBufferMode.Back);
                GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                int lit = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                    if (pixels[i] > 16 || pixels[i + 1] > 16 || pixels[i + 2] > 16) lit++;
                if (lit < width * height / 100)
                    throw new InvalidOperationException("Launcher frame is black.");
                var error = GL.GetError();
                if (error != ErrorCode.NoError) throw new InvalidOperationException($"OpenGL error: {error}");
                Console.WriteLine($"[windowcheck] rendered {width}x{height}, {lit} lit pixels");
                if (_frames == 20)
                {
                    window.ClientSize = new Vector2i(1100, 740);
                    if (_match)
                    {
                        if (!Launcher.MatchStart.Begin(window, GameState.LoadSettings(), new Launcher.LaunchPlan
                        {
                            Kind = _training ? Launcher.LaunchKind.AimTrainer : Launcher.LaunchKind.Offline,
                            RoomKey = _training ? Launcher.AimTrainerLaunch.Room : "MP3 PROVING GROUND",
                            Training = _training ? _trainingDefinition : null,
                            Bots = _training ? _trainingDefinition.TargetCount : 0,
                            Mode = GameMode.Battle, Hunter = Hunter.Samus, PlayerName = "Close check"
                        })) throw new InvalidOperationException("Match close check could not load its room.");
                        UiSurface.Current?.Hide();
                        Console.WriteLine("[windowcheck] match loaded");
                    }
                }
                else
                {
                    if (_match && !window.HasScene) throw new InvalidOperationException("Match ended before close check.");
                    if (_training && window.Scene.AimTrainer?.Stats.ElapsedFrames is not > 0)
                        throw new InvalidOperationException("Trainer did not advance after launch.");
                    if (_training && window.Scene.Players.Main.CurrentWeapon != _trainingDefinition.Weapon)
                        throw new InvalidOperationException("Trainer did not equip the selected weapon.");
                    if (_training && _trainingCapture != null
                        && !ScreenCapture.SaveWindow(width, height, _trainingCapture))
                        throw new InvalidOperationException("Trainer capture failed.");
                    if (_match) _closingScene = window.Scene;
                    _passed = true;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                // Never throw through GLFW's native callbacks.
                Console.Error.WriteLine($"[windowcheck] {ex}");
                _active = false;
                window.Close();
            }
        }

        private static void Link(string vertex, string fragment)
        {
            int program = GL.CreateProgram();
            try
            {
                foreach (var (type, source) in new[] {
                    (ShaderType.VertexShader, vertex), (ShaderType.FragmentShader, fragment) })
                {
                    int shader = GL.CreateShader(type);
                    try
                    {
                        GL.ShaderSource(shader, source);
                        GL.CompileShader(shader);
                        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                        if (compiled == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
                        GL.AttachShader(program, shader);
                    }
                    finally { GL.DeleteShader(shader); }
                }
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            }
            finally { GL.DeleteProgram(program); }
        }
    }
}
#endif
