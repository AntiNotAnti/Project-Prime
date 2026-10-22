using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.Input;
using MphRead.Mods.MapEditor;
using System.Security.Cryptography;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

internal static class ShaderOwnershipCheck
{
    internal static int Run(int cycles)
    {
        FieldInfo callback = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!
            .GetField("ShaderObjectCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        string[] stages = { "main-vertex", "main-fragment", "rtt-vertex", "rtt-fragment",
            "shift-fragment", "cel-fragment", "main-program", "rtt-program", "shift-program", "cel-program",
            "variant-vertex", "variant-fragment", "variant-program", "variant-initialized" };
        int failures = 0;
        string[] ownedFields = { "_shaderProgramId", "_rttShaderProgramId", "_shiftShaderProgramId", "_celShaderProgramId",
            "_frameBuffer", "_celFrameBuffer", "_renderBuffer", "_screenTexture", "_celTexture", "_depthTexture" };
        int Field(Scene owner, string name) => (int)typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        MethodInfo initialize = typeof(Scene).GetMethod("InitShaders", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var definition = MapTemplates.Create("Shader ownership", true).Definition;
        var cache = new MapViewportCache();
        cache.Invalidate(definition, new(MapChangeDomain.Geometry | MapChangeDomain.Material));
        var frame = new MapRenderFrame(new(320, 180),
            new(new System.Numerics.Vector3(12, 10, 12), System.Numerics.Vector3.Zero, true),
            cache.Meshes, new HashSet<Guid>(), new Dictionary<Guid, System.Numerics.Matrix4x4>(), false, false);
        (byte[] Color, float[] Depth) Capture(Scene owner)
        {
            owner.DrawEditorFrame(frame, new Vector2i(320, 180), capture: true);
            byte[] color = owner.ReadSceneTarget(out int width, out int height)!;
            float[] depth = new float[width * height];
            int previous = GL.GetInteger(GetPName.ReadFramebufferBinding);
            try
            {
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Field(owner, "_frameBuffer"));
                GL.ReadPixels(0, 0, width, height, PixelFormat.DepthComponent, PixelType.Float, depth);
            }
            finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); }
            if (!Enumerable.Range(1, color.Length / 3 - 1).Any(i => color[3 * i] != color[0]
                || color[3 * i + 1] != color[1] || color[3 * i + 2] != color[2])
                || !depth.Any(d => d < 1) || depth.Any(d => !float.IsFinite(d)))
                throw new InvalidOperationException("Shader recovery fixture must contain colored geometry and depth.");
            return (color, depth);
        }
        Scene reference = Scene.CreateEditorRenderer(new Vector2i(32, 32));
        var expected = Capture(reference);
        reference.UnloadGl();

        foreach (int cycle in Enumerable.Range(0, cycles))
        foreach (string failureStage in stages)
        {
            var shaders = new HashSet<int>(); var programs = new HashSet<int>();
            bool hit = false, rejected = false; int initializedVariants = 0;
            Scene? scene = null;
            // The second initialized variant fails after the first variant has
            // transferred ownership to the scene.
            callback.SetValue(null, (Action<string, int>)((stage, handle) =>
            {
                if (stage.EndsWith("-program") || stage == "variant-initialized") programs.Add(handle);
                else shaders.Add(handle);
                if (stage != failureStage || hit) return;
                if (stage == "variant-initialized" && ++initializedVariants < 2) return;
                hit = true;
                if (stage == "variant-initialized") throw new InvalidOperationException("Injected late variant initialization failure.");
                if (stage.EndsWith("-program"))
                {
                    int[] attached = new int[2];
                    GL.GetAttachedShaders(handle, attached.Length, out int count, attached);
                    if (count != 2) throw new InvalidOperationException("Link fixture requires two shaders.");
                    foreach (int shader in attached)
                    {
                        GL.GetShader(shader, ShaderParameter.ShaderType, out int type);
                        bool vertex = type == (int)ShaderType.VertexShader;
                        string source = vertex
                            ? "#version 120\nuniform float ownership_conflict; void main(){ gl_Position=vec4(ownership_conflict); }"
                            : "#version 120\nuniform vec4 ownership_conflict; void main(){ gl_FragColor=ownership_conflict; }";
                        GL.ShaderSource(shader, source); GL.CompileShader(shader);
                        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
                        if (compiled == 0) throw new InvalidOperationException("Link fixture shader failed to compile: " + GL.GetShaderInfoLog(shader));
                    }
                }
                else GL.ShaderSource(handle, "#version 120\nthis is deliberately invalid GLSL");
            }));
            try
            {
                scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(),
                    SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
                try { initialize.Invoke(scene, null); }
                catch (TargetInvocationException error) when (error.InnerException is ProgramException or InvalidOperationException) { rejected = true; }
                // Check the initializer itself, before caller teardown can mask a leak.
                if (rejected && (!ownedFields.All(name => Field(scene, name) == 0)
                    || shaders.Any(GL.IsShader) || programs.Any(GL.IsProgram)))
                {
                    failures++;
                    Console.WriteLine($"SHADEROWNERSHIP FAIL rollback cycle={cycle} stage={failureStage}");
                }
            }
            finally
            {
                callback.SetValue(null, null);
                scene?.UnloadGl();
            }
            bool optional = failureStage.StartsWith("variant-") && failureStage != "variant-initialized";
            bool released = shaders.All(shader => !GL.IsShader(shader)) && programs.All(program => !GL.IsProgram(program));
            bool clean = GL.GetError() == ErrorCode.NoError;
            if (!hit || rejected == optional || !released || !clean)
            {
                failures++;
                Console.WriteLine($"SHADEROWNERSHIP FAIL cycle={cycle} stage={failureStage} hit={hit} rejected={rejected} released={released} glClean={clean}");
            }
            // Bound negative-control runs before the fix too.
            GL.UseProgram(0);
            foreach (int program in programs) if (GL.IsProgram(program)) GL.DeleteProgram(program);
            foreach (int shader in shaders) if (GL.IsShader(shader)) GL.DeleteShader(shader);
            while (GL.GetError() != ErrorCode.NoError) { }
        }
        // Recover repeatedly after the failure matrix and prove exact color/depth,
        // immediate deletion of a bound program and another scene's binding lifetime.
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            Scene owner = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            int ownerProgram = Field(owner, "_shaderProgramId");
            var actual = Capture(owner);
            if (!expected.Color.SequenceEqual(actual.Color) || !expected.Depth.SequenceEqual(actual.Depth))
            { failures++; Console.WriteLine($"SHADEROWNERSHIP FAIL recovery pixels/depth cycle={cycle}"); }
            GL.UseProgram(ownerProgram);
            Scene other = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            other.UnloadGl();
            if (GL.GetInteger(GetPName.CurrentProgram) != ownerProgram || !GL.IsProgram(ownerProgram))
            { failures++; Console.WriteLine($"SHADEROWNERSHIP FAIL independent binding cycle={cycle}"); }
            owner.UnloadGl(); owner.UnloadGl();
            if (GL.IsProgram(ownerProgram) || GL.GetInteger(GetPName.CurrentProgram) != 0 || GL.GetError() != ErrorCode.NoError)
            { failures++; Console.WriteLine($"SHADEROWNERSHIP FAIL bound program release cycle={cycle}"); }
        }
        // Auxiliary programs have independent release paths and may be current
        // when public teardown is called. Exercise the real lazy allocators.
        foreach (string field in new[] { "_graphicsProgram", "_playerOutlineProgram" })
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            Scene owner = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            MethodInfo allocate = typeof(Scene).GetMethod(field == "_graphicsProgram"
                ? "EnsureGraphicsPipeline" : "EnsurePlayerOutlineTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
            allocate.Invoke(owner, field == "_graphicsProgram" ? new object[] { new Vector2i(32, 32) } : null);
            int program = Field(owner, field);
            Scene other = Scene.CreateEditorRenderer(new Vector2i(32, 32));
            GL.UseProgram(program);
            bool recognized = program != 0 && GL.IsProgram(program);
            other.UnloadGl();
            bool independent = GL.GetInteger(GetPName.CurrentProgram) == program && GL.IsProgram(program);
            owner.UnloadGl(); owner.UnloadGl();
            int fieldAfter = Field(owner, field), currentAfter = GL.GetInteger(GetPName.CurrentProgram);
            bool recognizedAfter = GL.IsProgram(program);
            int deletePending = 0;
            if (recognizedAfter) GL.GetProgram(program, GetProgramParameterName.DeleteStatus, out deletePending);
            bool released = fieldAfter == 0 && !recognizedAfter && currentAfter == 0;
            bool clean = GL.GetError() == ErrorCode.NoError;
            Console.WriteLine($"SHADEROWNERSHIP AUX field={field} cycle={cycle} recognized={recognized} independentBinding={independent} released={released} fieldAfter={fieldAfter} recognizedAfter={recognizedAfter} currentAfter={currentAfter} deletePending={deletePending} glClean={clean}");
            if (!recognized || !independent || !released || !clean) failures++;
            // Bound the negative control without masking the failed witness.
            GL.UseProgram(0);
            if (GL.IsProgram(program)) GL.DeleteProgram(program);
            while (GL.GetError() != ErrorCode.NoError) { }
        }
        Console.WriteLine($"SHADEROWNERSHIP recoveryFrames={cycles} colorSha256={Convert.ToHexString(SHA256.HashData(expected.Color))} depthSha256={Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.Depth.AsSpan())))}");
        Console.WriteLine($"SHADEROWNERSHIP cycles={cycles} scenarios={stages.Length * cycles} failures={failures}");
        return failures;
    }
}
