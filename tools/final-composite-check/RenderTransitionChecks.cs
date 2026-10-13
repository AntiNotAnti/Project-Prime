using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Hud;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using G = MphRead.Mods.Render.GraphicsApi;

// Exercises the production private entry points with a real GL context, without
// loading game assets or starting a simulation. The synthetic scene only owns
// shader locations and room metadata; it must never be processed or disposed.
internal static class RenderTransitionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(int width, int height)
    {
        var scene = (Scene)RuntimeHelpers.GetUninitializedObject(typeof(Scene));
        scene.Size = new(width, height);
        var locations = new ShaderLocations();
        foreach (var property in typeof(ShaderLocations).GetProperties())
            if (property.PropertyType == typeof(int)) property.SetValue(locations, -1);
        Set(scene, "_shaderLocations", locations);
        Set(scene, "_exiting", true); // UpdateUniforms must not advance a game fade.
        int world = Program(Shaders.RttVertexShader, """
            #version 120
            uniform vec4 fog_color;
            uniform float fog_min;
            uniform float fog_max;
            void main() { gl_FragColor = vec4(fog_color.rgb * (fog_min + fog_max), fog_color.a); }
            """);
        int hud = Program(Shaders.RttVertexShader, Shaders.RttFragmentShader);
        int texture = G.GenTexture();
        try
        {
            Set(scene, "_shaderProgramId", world);
            Set(scene, "_rttShaderProgramId", hud);
            locations.FogColor = G.GetUniformLocation(world, "fog_color");
            locations.FogMinDistance = G.GetUniformLocation(world, "fog_min");
            locations.FogMaxDistance = G.GetUniformLocation(world, "fog_max");
            locations.LayerAlpha = G.GetUniformLocation(hud, "alpha");
            locations.UseMask = G.GetUniformLocation(hud, "use_mask");
            locations.ViewWidth = G.GetUniformLocation(hud, "view_width");
            locations.ViewHeight = G.GetUniformLocation(hud, "view_height");
            locations.FadeColor = G.GetUniformLocation(hud, "fade_color");

            // Reproduce the removed setter ordering on the real driver: upload
            // world locations while RTT owns the context. Even if locations
            // overlap legally, the intended scene program cannot be updated.
            var reproductionRoom = Metadata.RoomList.First(r => r.FogEnabled
                && (r.FogColor.Red != 0 || r.FogColor.Green != 0 || r.FogColor.Blue != 0));
            G.UseProgram(hud);
            scene.SetRoomValues(reproductionRoom);
            Invoke(scene, "SetShaderFog");
            float[] stale = new float[4];
            GL.GetUniform(world, locations.FogColor, stale);
            Check(stale.All(v => v == 0), "baseline reproduction: wrong-program setter leaves scene fog stale");
            while (G.GetError() != ErrorCode.NoError) { }

            // A room transition can occur after the previous frame's RTT pass.
            // It must not write scene locations into that unrelated program.
            var rooms = Metadata.RoomList.Where(r => r.FogEnabled)
                .GroupBy(r => (r.FogColor.Red, r.FogColor.Green, r.FogColor.Blue, r.FogOffset, r.FogSlope))
                .Select(group => group.First()).Take(4).ToArray();
            Check(rooms.Length >= 2, "transition fixtures have distinct authored fog parameters");
            foreach (var room in rooms)
            {
                G.UseProgram(hud);
                G.Uniform1(locations.LayerAlpha, 0.375f);
                scene.SetRoomValues(room);
                Check(G.GetError() == ErrorCode.NoError, "room metadata update does not write to the HUD program");
                GL.GetUniform(hud, locations.LayerAlpha, out float alpha);
                Check(alpha == 0.375f, "room transition preserves HUD uniforms");
                G.UseProgram(world);
                Invoke(scene, "UpdateUniforms");
                VerifyFog(world, locations, room);
                // Same-value uploads after a relink must not be suppressed by
                // the program-scoped uniform cache.
                G.LinkProgram(world);
                G.GetProgram(world, GetProgramParameterName.LinkStatus, out int linked);
                Check(linked != 0, "scene shader relink succeeds");
                locations.FogColor = G.GetUniformLocation(world, "fog_color");
                locations.FogMinDistance = G.GetUniformLocation(world, "fog_min");
                locations.FogMaxDistance = G.GetUniformLocation(world, "fog_max");
                G.UseProgram(world);
                Invoke(scene, "UpdateUniforms");
                VerifyFog(world, locations, room);
            }

            G.UseProgram(hud);
            G.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            G.Viewport(0, 0, width, height);
            G.Disable(EnableCap.DepthTest);
            G.Disable(EnableCap.CullFace);
            G.Disable(EnableCap.Blend);
            G.ActiveTexture(TextureUnit.Texture0);
            G.BindTexture(TextureTarget.Texture2D, texture);
            byte[] white = { 255, 255, 255, 255 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, white);
            G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            G.ActiveTexture(TextureUnit.Texture1);
            G.BindTexture(TextureTarget.Texture2D, texture);
            G.ActiveTexture(TextureUnit.Texture0);
            G.Uniform1(G.GetUniformLocation(hud, "tex"), 0);
            G.Uniform1(G.GetUniformLocation(hud, "mask"), 1);
            G.Uniform4(locations.FadeColor, Vector4.Zero);
            G.Uniform1(locations.UseMask, 0);
            var layer = new LayerInfo { BindingId = texture, ScaleX = 1, ScaleY = 1 };
            foreach (float alpha in new[] { 0f, -0.5f, 0.5f, 1f })
            {
                G.ClearColor(0, 0, 0, 1);
                G.Clear(ClearBufferMask.ColorBufferBit);
                layer.Alpha = alpha;
                Check(OpenGlSubmissionProfiler.BeginWorldGraph(), "HUD draw counter enabled");
                Invoke(scene, "DrawHudLayer", layer);
                var sample = OpenGlSubmissionProfiler.EndWorldGraph(0, 0, 0);
                Check(sample.ImmediateBegins == (alpha > 0 ? 1 : 0), $"alpha {alpha}: expected texture draws");
                Check(alpha > 0 || sample.TextureBindRequests == 0, "hidden layer performs no texture work");
                var pixels = FinalCompositeCapture.Read(width, height);
                Check(pixels[(height / 2 * width + width / 2) * 3] == (alpha > 0 ? 255 : 0),
                    "visible HUD pixels retained and hidden pixels untouched");
                string? captures = Environment.GetEnvironmentVariable("PRIME_RENDER_CHECK_CAPTURES");
                if (captures != null)
                {
                    System.IO.Directory.CreateDirectory(captures);
                    using var capture = System.IO.File.Create(System.IO.Path.Combine(captures,
                        alpha <= 0 ? "hidden.png" : "visible.png"));
                    ReFuel.Stb.StbImage.FlipVerticallyOnSave = true;
                    MphRead.Mods.PngEncoder.Write<byte>(pixels, width, height, ReFuel.Stb.StbiImageFormat.Rgb, capture);
                }
            }
            layer.Alpha = 0;
            Invoke(scene, "DrawHudLayer", layer);
            Invoke(scene, "BeginHudMask", texture);
            GL.GetUniform(hud, locations.UseMask, out int mask);
            Check(mask == 1, "hidden visor does not suppress downstream mask");
            Invoke(scene, "EndHudMask");
            Check(G.GetError() == ErrorCode.NoError, "HUD/fog checks leave no GL errors");
        }
        finally
        {
            G.UseProgram(0);
            G.DeleteTexture(texture);
            G.DeleteProgram(world);
            G.DeleteProgram(hud);
        }
    }

    private static void VerifyFog(int program, ShaderLocations locations, RoomMetadata room)
    {
        float[] color = new float[4];
        GL.GetUniform(program, locations.FogColor, color);
        GL.GetUniform(program, locations.FogMinDistance, out float min);
        GL.GetUniform(program, locations.FogMaxDistance, out float max);
        Check(Math.Abs(color[0] - room.FogColor.Red / 31f) < 1e-6f
            && Math.Abs(color[1] - room.FogColor.Green / 31f) < 1e-6f
            && Math.Abs(color[2] - room.FogColor.Blue / 31f) < 1e-6f
            && Math.Abs(min - room.FogOffset / (float)0x7FFF) < 1e-6f
            && Math.Abs(max - (room.FogOffset + 32 * (0x400 >> room.FogSlope)) / (float)0x7FFF) < 1e-6f,
            "authored room fog refreshed after transition/relink: " + room.Name);
    }
    private static void Set(object obj, string name, object value) => typeof(Scene).GetField(name, Private)!.SetValue(obj, value);
    private static void Invoke(object obj, string name, params object[] values) => typeof(Scene).GetMethod(name, Private)!.Invoke(obj, values);
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static int Program(string vertexSource, string fragmentSource)
    {
        int program = G.CreateProgram();
        foreach (var (kind, source) in new[] { (ShaderType.VertexShader, vertexSource), (ShaderType.FragmentShader, fragmentSource) })
        {
            int shader = G.CreateShader(kind);
            G.ShaderSource(shader, source); G.CompileShader(shader);
            G.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled == 0) throw new Exception(G.GetShaderInfoLog(shader));
            G.AttachShader(program, shader); G.DeleteShader(shader);
        }
        G.LinkProgram(program);
        G.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0) throw new Exception("Test shader failed to link");
        return program;
    }
}
