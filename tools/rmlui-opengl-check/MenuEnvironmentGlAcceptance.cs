using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using GLFW = OpenTK.Windowing.GraphicsLibraryFramework.GLFW;

/// <summary>Real procedural chamber pixels and timing, without cartridge assets or live service state.</summary>
internal static class MenuEnvironmentGlAcceptance
{
    private static int _checks;
    private static double _now;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("MENU ENVIRONMENT: " + name);
        _checks++; Console.WriteLine("MENU ENVIRONMENT PASS " + name);
    }

    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_VFX_EVIDENCE")
            ?? Path.Combine("artifacts", "vfx-3-validation", "current"));
        Directory.CreateDirectory(output);
        CosmeticEffectQuality originalQuality = RenderOptions.CosmeticQuality;
        bool originalReduced = LauncherPrefs.ReduceMotion;
        var rows = new List<object>();
        var hardware = new { gpu = GL.GetString(StringName.Renderer), vendor = GL.GetString(StringName.Vendor),
            version = GL.GetString(StringName.Version), width, height,
            timerQueriesSupported = TimerSupported(),
            gpuTiming = TimerSupported() ? "GL_TIME_ELAPSED query around the same procedural draw; 30 warmed samples per scene/quality." : "Required ARB/core timer-query support/entry points unavailable; GPU-only duration remains null." };
        try
        {
            Check(host.Active && width > 0 && height > 0, "existing native context and host are active");
            Check(LauncherLobbyFormation.Validate(), "all eight shared pad/preview anchors are valid");
            TestPreviewLighting();
            foreach (bool lobby in new[] { false, true })
            foreach (CosmeticEffectQuality quality in Enum.GetValues<CosmeticEffectQuality>())
            {
                SetFixture(lobby, 0xff, 0x55);
                RenderOptions.CosmeticQuality = quality;
                LauncherPrefs.ReduceMotion = false;
                Settle(Hunter.Samus);
                string name = (lobby ? "lobby" : "home") + "-" + quality.ToString().ToLowerInvariant();
                for (int i = 0; i < 40; i++) Draw(width, height);
                GL.Finish();
                var submit = new double[180]; var completed = new double[180];
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < submit.Length; i++)
                {
                    long start = Stopwatch.GetTimestamp(); Draw(width, height);
                    submit[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    GL.Finish(); completed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Array.Sort(submit); Array.Sort(completed);
                byte[] pixels = Read(width, height);
                Check(pixels.Any(value => value > 25), name + " paints actual framebuffer pixels");
                string path = Path.Combine(output, name + ".png");
                Check(ScreenCapture.SaveWindow(width, height, path), name + " PNG capture");
                Check(GL.GetError() == ErrorCode.NoError, name + " GL error-free");
                rows.Add(new { name, path, quality = quality.ToString(), samples = submit.Length,
                    submitP50Ms = submit[90], submitP95Ms = submit[171], submitP99Ms = submit[178],
                    completionP50Ms = completed[90], completionP95Ms = completed[171], completionP99Ms = completed[178],
                    gpu = GpuSamples(width, height), allocatedBytes = allocated, measurement = "Unpaced CPU submission and draw+glFinish GPU completion; excludes host/native UI, swap, input and model rendering." });
            }

            RenderOptions.CosmeticQuality = CosmeticEffectQuality.Off;
            SetFixture(false, 0, 0); LauncherPrefs.ReduceMotion = true; Settle(Hunter.Samus);
            LauncherPrefs.ReduceMotion = false;
            Draw(width, height); byte[] minimal = Read(width, height);
            Advance(2, Hunter.Samus, true); Draw(width, height);
            Check(minimal.AsSpan().SequenceEqual(Read(width, height)), "Off frame stays static while timeline advances");
            RenderOptions.CosmeticQuality = CosmeticEffectQuality.High;
            Settle(Hunter.Samus); Draw(width, height); byte[] focused = Read(width, height);
            Advance(2, Hunter.Samus, false); Draw(width, height);
            Check(focused.AsSpan().SequenceEqual(Read(width, height)), "unfocused frame freezes purposeful ambient animation");
            LauncherBackdropComposer.Enabled = false;
            GL.ClearColor(.11f, .17f, .23f, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
            byte[] hidden = Read(width, height); LauncherBackdropComposer.Draw(width, height);
            Check(hidden.AsSpan().SequenceEqual(Read(width, height)), "hidden disabled backdrop paints no pixels");
            LauncherBackdropComposer.Enabled = true;
            SetFixture(true, 0, 0); LauncherPrefs.ReduceMotion = true; Settle(Hunter.Samus);
            Draw(width, height); byte[] empty = Read(width, height);
            for (int slot = 0; slot < 8; slot++)
            {
                SetFixture(true, (byte)(1 << slot), 0); Settle(Hunter.Samus); Draw(width, height);
                byte[] occupied = Read(width, height);
                var pad = LauncherLobbyFormation.At(slot);
                int changed = RegionChanges(empty, occupied, width, height, pad.PadX, pad.PadY, pad.RadiusX, pad.RadiusY);
                Check(changed > 8, $"slot {slot} activation changes its shared authored pad region ({changed} pixels)");
            }
            SetFixture(true, 0xff, 0x55); Settle(Hunter.Sylux);
            Draw(width, height); byte[] reduced = Read(width, height);
            Advance(2, Hunter.Sylux, true); Draw(width, height);
            Check(reduced.AsSpan().SequenceEqual(Read(width, height)), "reduced-motion procedural frame is stable as presentation clock advances");
            Check(ScreenCapture.SaveWindow(width, height, Path.Combine(output, "lobby-reduced-motion.png")), "reduced-motion capture");

            LauncherPrefs.ReduceMotion = false;
            foreach (Hunter hunter in Enum.GetValues<Hunter>().Where(h => (int)h is >= 0 and < 7))
            {
                SetFixture(false, 0, 0); Settle(hunter); Draw(width, height);
                Check(ScreenCapture.SaveWindow(width, height, Path.Combine(output, "theme-" + hunter.ToString().ToLowerInvariant() + ".png")), hunter + " theme capture");
            }
            TestStateFence(host, width, height);
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new {
                scope = "Actual procedural OpenGL chamber only. Explicit synthetic lobby state; no game meshes, cartridge imagery, network, account, saves, or gameplay. No claim of device-wide FPS, input latency, Android or Windows qualification.",
                hardware, checks = _checks, rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            LauncherBackdropComposer.Release(); LauncherStageFx.Release();
            LauncherLobbyVisuals.Reset(); LauncherPresentation.Active = false;
            RenderOptions.CosmeticQuality = originalQuality; LauncherPrefs.ReduceMotion = originalReduced;
        }
    }

    private static void TestPreviewLighting()
    {
        bool preview = Scene.LauncherPreview, cinematic = Scene.LauncherPreviewCinematicLighting;
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var entity = (HunterPreviewEntity)RuntimeHelpers.GetUninitializedObject(typeof(HunterPreviewEntity));
        var property = typeof(HunterPreviewEntity).GetProperty("CurrentLight", hidden)!;
        var hunter = typeof(HunterPreviewEntity).GetField("_hunter", hidden)!;
        LightInfo ReadLight() => (LightInfo)property.GetValue(entity)!;
        static bool Same(LightInfo a, LightInfo b) => a.Light1Vector == b.Light1Vector && a.Light1Color == b.Light1Color
            && a.Light2Vector == b.Light2Vector && a.Light2Color == b.Light2Color;
        try
        {
            hunter.SetValue(entity, Hunter.Samus);
            Scene.LauncherPreview = false; Scene.LauncherPreviewCinematicLighting = false;
            LightInfo original = ReadLight();
            Scene.LauncherPreviewCinematicLighting = true;
            Check(Same(original, ReadLight()), "cinematic permission alone cannot relight results/live scene previews");
            Scene.LauncherPreview = true; Scene.LauncherPreviewCinematicLighting = false;
            Check(Same(original, ReadLight()), "launcher preview without cinematic permission preserves original light");
            Scene.LauncherPreviewCinematicLighting = true;
            LightInfo samus = ReadLight();
            Check(!Same(original, samus), "both launcher cinematic permissions enable new presentation light");
            hunter.SetValue(entity, Hunter.Weavel); LightInfo weavel = ReadLight();
            Check(samus.Light1Vector == weavel.Light1Vector && samus.Light1Color == weavel.Light1Color
                && samus.Light2Vector == weavel.Light2Vector && samus.Light2Color != weavel.Light2Color,
                "hunter identity adjusts only secondary fill while retaining neutral key and shared directions");
        }
        finally { Scene.LauncherPreview = preview; Scene.LauncherPreviewCinematicLighting = cinematic; }
    }

    private static bool TimerSupported()
    {
        string version = (GL.GetString(StringName.Version) ?? "").Split(' ')[0];
        bool core = Version.TryParse(version, out Version? parsed) && parsed >= new Version(3, 3);
        bool arb = (GL.GetString(StringName.Extensions) ?? "").Split(' ').Contains("GL_ARB_timer_query");
        return (core || arb) && new[] { "glBeginQuery", "glEndQuery", "glGetQueryObjectui64v" }
            .All(name => GLFW.GetProcAddress(name) != IntPtr.Zero);
    }
    private static object? GpuSamples(int width, int height)
    {
        if (!TimerSupported()) return null;
        int query = GL.GenQuery(); var samples = new double[30];
        try
        {
            for (int i = 0; i < samples.Length; i++)
            {
                GL.BeginQuery(QueryTarget.TimeElapsed, query); Draw(width, height); GL.EndQuery(QueryTarget.TimeElapsed);
                GL.Finish(); GL.GetQueryObject((uint)query, GetQueryObjectParam.QueryResult, out ulong ns);
                samples[i] = ns / 1_000_000.0;
            }
            Check(GL.GetError() == ErrorCode.NoError, "elapsed GPU queries are valid");
            Array.Sort(samples);
            return new { samples = samples.Length, p50Ms = samples[15], p95Ms = samples[28], p99Ms = samples[29] };
        }
        finally { GL.DeleteQuery(query); }
    }

    private static void SetFixture(bool lobby, byte occupied, byte ready)
    {
        LauncherBackdrop.Set(lobby ? LauncherBackdropScene.Lobby : LauncherBackdropScene.Home);
        LauncherLobbyVisuals.Active = lobby; LauncherLobbyVisuals.OccupiedMask = occupied;
        LauncherLobbyVisuals.ReadyMask = ready; LauncherLobbyVisuals.Starting = false;
        for (int i = 0; i < 8; i++)
        {
            LauncherLobbyVisuals.SetIdentity(i, 100 + i);
            LauncherLobbyVisuals.SetHunter(i, (Hunter)(i % 7));
        }
    }
    private static void Advance(double seconds, Hunter hunter, bool focused)
    {
        int frames = (int)Math.Ceiling(seconds * 144);
        for (int frame = 0; frame < frames; frame++)
        {
            _now += 1.0 / 144;
            LauncherPresentation.Motion.Advance(_now, focused, LauncherPrefs.ReduceMotion, LauncherMenuVisuals.Activity, hunter);
        }
    }
    private static void Settle(Hunter hunter) => Advance(2, hunter, true);
    private static void Draw(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        LauncherBackdropComposer.Enabled = true; LauncherBackdropComposer.Draw(width, height);
    }
    private static byte[] Read(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        byte[] pixels = new byte[width * height * 3];
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, pixels);
        return pixels;
    }
    private static int RegionChanges(byte[] a, byte[] b, int width, int height, float cx, float cy, float rx, float ry)
    {
        int changes = 0;
        int left = Math.Clamp((int)((cx - rx) * width), 0, width - 1), right = Math.Clamp((int)((cx + rx) * width), 0, width);
        int top = Math.Clamp((int)((cy - ry) * height), 0, height - 1), bottom = Math.Clamp((int)((cy + ry) * height), 0, height);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int p = ((height - y - 1) * width + x) * 3;
            if (Math.Abs(a[p] - b[p]) + Math.Abs(a[p + 1] - b[p + 1]) + Math.Abs(a[p + 2] - b[p + 2]) > 8) changes++;
        }
        return changes;
    }

    private static void TestStateFence(RmlUiHost host, int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.Viewport(0, 0, width, height);
        GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ClearDepth(.371);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        int texture = GL.GenTexture(), texture0 = GL.GenTexture(), readFbo = GL.GenFramebuffer(), drawFbo = GL.GenFramebuffer();
        int program = SentinelProgram(); GL.UseProgram(program);
        GL.ActiveTexture(TextureUnit.Texture0); GL.Disable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, texture0);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 16, 16, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFbo);
        GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture0, 0);
        Check(GL.CheckFramebufferStatus(FramebufferTarget.DrawFramebuffer) == FramebufferErrorCode.FramebufferComplete,
            "nondefault caller framebuffer is complete for restoration test");
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFbo);
        GL.Enable(EnableCap.DepthTest); GL.DepthMask(false); GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.AlphaTest); GL.Enable(EnableCap.StencilTest); GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(11, 13, width - 22, height - 26);
        GL.Viewport(3, 5, width - 6, height - 10);
        GL.ActiveTexture(TextureUnit.Texture1); GL.Enable(EnableCap.Texture2D); GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.MatrixMode(MatrixMode.Texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        string before = State();
        LauncherBackdropComposer.Draw(width, height);
        Check(before == State(), "procedural composer preserves caller GL capabilities/bindings/viewport/scissor/matrix mode");
        var hunter = (LauncherHunter.Wanted, LauncherHunter.Left, LauncherHunter.Top, LauncherHunter.Right, LauncherHunter.Bottom);
        bool lobby = LauncherLobbyVisuals.Active;
        try
        {
            LauncherStageFx.Enabled = true; LauncherHunter.Wanted = true;
            LauncherHunter.Left = .32f; LauncherHunter.Top = .10f; LauncherHunter.Right = .68f; LauncherHunter.Bottom = .94f;
            LauncherLobbyVisuals.Active = false;
            LauncherStageFx.DrawUnderHunter(width, height);
            Check(before == State(), "hero underlay preserves caller GL state including first radial texture upload");
            LauncherStageFx.DrawOverHunter(width, height);
            Check(before == State(), "hero foreground haze preserves caller GL state");
            LauncherLobbyVisuals.Active = true;
            LauncherStageFx.DrawUnderHunter(width, height);
            Check(before == State(), "lobby atmosphere preserves caller GL state");
        }
        finally
        {
            (LauncherHunter.Wanted, LauncherHunter.Left, LauncherHunter.Top, LauncherHunter.Right, LauncherHunter.Bottom) = hunter;
            LauncherLobbyVisuals.Active = lobby; LauncherStageFx.Enabled = false;
        }
        host.Update(); RmlUiOpenGlFrame.Draw(host, width, height);
        Check(before == State(), "native terminal RmlUi pass preserves caller GL state after chamber");
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        float[] depth = new float[1];
        GL.ReadPixels(width / 2, height / 2, 1, 1, PixelFormat.DepthComponent, PixelType.Float, depth);
        Check(Math.Abs(depth[0] - .371f) < .0001f, "chamber and native compositor do not overwrite or clear caller depth buffer");
        GL.Disable(EnableCap.ScissorTest); GL.DepthMask(true); GL.ActiveTexture(TextureUnit.Texture0);
        GL.Disable(EnableCap.StencilTest); GL.Disable(EnableCap.AlphaTest); GL.ClearDepth(1);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.DeleteTexture(texture); GL.DeleteTexture(texture0); GL.DeleteFramebuffer(readFbo); GL.DeleteFramebuffer(drawFbo);
        GL.UseProgram(0); GL.DeleteProgram(program);
    }
    private static int SentinelProgram()
    {
        int vertex = GL.CreateShader(ShaderType.VertexShader), fragment = GL.CreateShader(ShaderType.FragmentShader);
        int program = GL.CreateProgram();
        try
        {
            GL.ShaderSource(vertex, "#version 120\nvoid main(){gl_Position=gl_Vertex;}"); GL.CompileShader(vertex);
            GL.ShaderSource(fragment, "#version 120\nvoid main(){gl_FragColor=vec4(0.1,0.3,0.5,1.0);}"); GL.CompileShader(fragment);
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Check(linked != 0, "nondefault caller shader links for state restoration test");
            return program;
        }
        finally { GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }

    private static string State()
    {
        int[] viewport = new int[4], scissor = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport); GL.GetInteger(GetPName.ScissorBox, scissor);
        int active = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        string unit0 = GL.GetInteger(GetPName.TextureBinding2D) + ":" + GL.IsEnabled(EnableCap.Texture2D);
        GL.ActiveTexture((TextureUnit)active);
        return unit0 + ":" + string.Join(',', new[] { GetPName.CurrentProgram, GetPName.ActiveTexture, GetPName.TextureBinding2D,
            GetPName.MatrixMode, GetPName.DepthWritemask, GetPName.BlendSrc, GetPName.BlendDst,
            GetPName.DrawFramebufferBinding, GetPName.ReadFramebufferBinding, GetPName.UnpackAlignment, GetPName.PackAlignment }.Select(p => GL.GetInteger(p)))
            + ":" + string.Join(',', viewport) + ":" + string.Join(',', scissor)
            + ":" + string.Join(',', new[] { EnableCap.DepthTest, EnableCap.CullFace, EnableCap.AlphaTest,
                EnableCap.StencilTest, EnableCap.Blend, EnableCap.ScissorTest, EnableCap.Texture2D }.Select(GL.IsEnabled));
    }
}
