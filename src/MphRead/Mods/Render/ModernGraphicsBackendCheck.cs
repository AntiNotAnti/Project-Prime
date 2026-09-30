using System;

namespace MphRead.Mods.Render
{
    public static class ModernGraphicsBackendCheck
    {
        public static int Run()
        {
            int failures = 0;
            CheckDefault(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, ref failures);
            CheckDefault(GraphicsPlatform.MacOS, GraphicsBackend.Metal, ref failures);
            CheckDefault(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, ref failures);
            CheckDefault(GraphicsPlatform.Android, GraphicsBackend.Vulkan, ref failures);

            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Metal, false, ref failures);

            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Metal, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.DirectX12, false, ref failures);

            CheckAlias("d3d12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("directx12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("vk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("moltenvk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("metal", GraphicsBackend.Metal, ref failures);
            CheckAlias("opengl", GraphicsBackend.OpenGL, ref failures);
            CheckGeometry(ref failures);
#if !MPHREAD_SERVER
            CheckUniformCompatibility(ref failures);
#endif

            Console.WriteLine(failures == 0
                ? "RENDERBACKENDS all policy cases pass"
                : $"RENDERBACKENDS {failures} case(s) FAILED");
            return failures;
        }

        private static void CheckDefault(GraphicsPlatform platform, GraphicsBackend expected, ref int failures)
        {
            GraphicsBackend actual = GraphicsBackendPolicy.DefaultFor(platform);
            Check(actual == expected, $"{platform} default = {expected} (actual {actual})", ref failures);
        }

        private static void CheckSupported(GraphicsPlatform platform, GraphicsBackend backend,
            bool expected, ref int failures)
        {
            bool actual = GraphicsBackendPolicy.IsSupported(platform, backend);
            Check(actual == expected, $"{platform} {(expected ? "supports" : "rejects")} {backend}", ref failures);
        }

        private static void CheckAlias(string value, GraphicsBackend expected, ref int failures)
        {
            bool parsed = GraphicsBackendPolicy.TryParse(value, out GraphicsBackend actual);
            Check(parsed && actual == expected, $"'{value}' parses as {expected}", ref failures);
        }

        private static void CheckGeometry(ref int failures)
        {
            var batch = new LegacyGeometryBatch();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.Quads);
            for (int i = 0; i < 4; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: true);
            }
            batch.End();
            Check(batch.VertexCount == 4
                && batch.TriIndices.Count == 6
                && batch.TriIndices[0] == 0 && batch.TriIndices[1] == 1 && batch.TriIndices[2] == 2
                && batch.TriIndices[3] == 0 && batch.TriIndices[4] == 2 && batch.TriIndices[5] == 3,
                "legacy quad becomes two indexed triangles", ref failures);
            Check(batch.Vertices.Count == 4 * LegacyGeometryBatch.FloatsPerVertex,
                "legacy vertex layout is stable", ref failures);

            batch.Clear();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.TriangleStrip);
            for (int i = 0; i < 4; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: false);
            }
            batch.End();
            Check(batch.TriIndices.Count == 6
                && batch.TriIndices[0] == 0 && batch.TriIndices[1] == 1 && batch.TriIndices[2] == 2
                && batch.TriIndices[3] == 2 && batch.TriIndices[4] == 1 && batch.TriIndices[5] == 3,
                "triangle strip preserves alternating winding", ref failures);

            batch.Clear();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.LineLoop);
            for (int i = 0; i < 3; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: false);
            }
            batch.End();
            Check(batch.LineIndices.Count == 6
                && batch.LineIndices[4] == 2 && batch.LineIndices[5] == 0,
                "line loop closes explicitly", ref failures);
        }

#if !MPHREAD_SERVER
        private static void CheckUniformCompatibility(ref int failures)
        {
            var state = new ModernGraphicsCompatState();
            int vertex = state.CreateShader(OpenTK.Graphics.OpenGL.ShaderType.VertexShader);
            state.ShaderSource(vertex, "uniform mat4 proj_mtx; void main() { }");
            state.CompileShader(vertex);
            int fragment = state.CreateShader(OpenTK.Graphics.OpenGL.ShaderType.FragmentShader);
            state.ShaderSource(fragment,
                "uniform int mat_mode; uniform float shift_table[64]; void main() { }");
            state.CompileShader(fragment);
            int program = state.CreateProgram();
            state.AttachShader(program, vertex);
            state.AttachShader(program, fragment);
            state.LinkProgram(program);
            state.GetProgram(program, OpenTK.Graphics.OpenGL.GetProgramParameterName.LinkStatus,
                out int linked);
            Check(linked == 1, "compat shader/program IDs link without GL objects", ref failures);

            int mode = state.GetUniformLocation(program, "mat_mode");
            int shift = state.GetUniformLocation(program, "shift_table");
            int missing = state.GetUniformLocation(program, "definitely_missing");
            Check(mode > 0 && shift > 0 && missing == -1,
                "compat uniform locations preserve declared/missing behavior", ref failures);

            state.UseProgram(program);
            state.Uniform1(mode, 7);
            state.GetUniform(program, mode, out int stored);
            Check(stored == 7, "compat uniform writes remain program-local and readable", ref failures);
        }
#endif

                private static void Check(bool success, string name, ref int failures)
        {
            Console.WriteLine($"RENDERBACKENDS {(success ? "PASS" : "FAIL")} {name}");
            if (!success) failures++;
        }
    }
}
