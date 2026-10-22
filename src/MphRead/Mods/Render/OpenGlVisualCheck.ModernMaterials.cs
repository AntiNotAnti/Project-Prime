#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using NativeGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

internal static partial class OpenGlVisualCheck
{
    // Invented geometry/textures, production fragment math. This isolates
    // material invariants without retail assets or visual acceptance claims.
    private static void CheckModernMaterialCorrections(string output)
    {
        ResetPass();
        const string vertexSource = @"#version 120
uniform vec3 fixture_normal;
uniform bool fixture_degenerate_world;
varying vec2 texcoord;
varying vec4 color;
varying vec3 surface_normal;
varying vec3 surface_position;
void main() {
    gl_Position = gl_Vertex;
    surface_position = fixture_degenerate_world ? vec3(0.0) : gl_Vertex.xyz;
    surface_normal = fixture_normal;
    texcoord = gl_MultiTexCoord0.xy;
    color = vec4(0.2, 0.2, 0.2, 1.0);
}";
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
        int fragment = 0, program = 0, texture = GraphicsApi.GenTexture();
        try
        {
            GraphicsApi.ShaderSource(vertex, vertexSource); GraphicsApi.CompileShader(vertex);
            GraphicsApi.GetShader(vertex, ShaderParameter.CompileStatus, out int vertexReady);
            Require(vertexReady != 0, "material fixture vertex compiles");
            void Select(string source)
            {
                if (program != 0) { GraphicsApi.UseProgram(0); GraphicsApi.DeleteProgram(program); }
                if (fragment != 0) GraphicsApi.DeleteShader(fragment);
                fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
                GraphicsApi.ShaderSource(fragment, source); GraphicsApi.CompileShader(fragment);
                GraphicsApi.GetShader(fragment, ShaderParameter.CompileStatus, out int ready);
                Require(ready != 0, "production material shader compiles: " + GraphicsApi.GetShaderInfoLog(fragment));
                program = GraphicsApi.CreateProgram();
                GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment); GraphicsApi.LinkProgram(program);
                GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                Require(linked != 0, "production material shader links: " + GraphicsApi.GetProgramInfoLog(program));
                GraphicsApi.UseProgram(program);
            }
            int Loc(string name) => GraphicsApi.GetUniformLocation(program, name);
            void Int(string name, int value) => GraphicsApi.Uniform1(Loc(name), value);
            void Vec(string name, Vector3 value) => GraphicsApi.Uniform3(Loc(name), value);
            void Tex(byte red, byte green, byte blue)
            {
                GraphicsApi.ActiveTexture(TextureUnit.Texture0); GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                byte[] pixels = { red, green, blue, 255 };
                NativeGL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            }
            byte[] Picture(string name, bool mirrored = false, bool degenerateUv = false)
            {
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GraphicsApi.Begin(PrimitiveType.Quads);
                void Vertex(float x, float y, float u, float v)
                {
                    GraphicsApi.TexCoord2(degenerateUv ? 0 : u, degenerateUv ? 0 : mirrored ? 1 - v : v);
                    GraphicsApi.Vertex3(x, y, 0);
                }
                Vertex(-.8f, -.8f, 0, 0); Vertex(.8f, -.8f, 1, 0);
                Vertex(.8f, .8f, 1, 1); Vertex(-.8f, .8f, 0, 1);
                GraphicsApi.End();
                return Snapshot(output, name);
            }
            Select(Shaders.FragmentShader);
            Int("advanced_materials", 1); Int("modern_material_corrections", 1); Int("use_light", 1);
            GraphicsApi.Uniform1(Loc("mat_alpha"), 1f);
            Vec("fixture_normal", Vector3.UnitZ); Vec("light1vec", -Vector3.UnitZ);
            Vec("light1col", Vector3.One); Vec("light2col", Vector3.Zero); Vec("specular", Vector3.One);
            Vec("material_camera_position", new Vector3(0, 0, 2));
            byte[] front = Picture("material-camera-front");
            Vec("material_camera_position", new Vector3(2, 0, 0));
            byte[] side = Picture("material-camera-side");
            int center = (32 * Width + 32) * 4;
            Require(front[center] > side[center] + 20, "modern specular follows actual camera instead of fixed world Z");
            Int("modern_material_corrections", 0);
            byte[] legacySide = Picture("material-legacy-side");
            Vec("material_camera_position", new Vector3(0, 0, 2));
            byte[] legacyFront = Picture("material-legacy-front");
            Require(legacySide.SequenceEqual(legacyFront), "disabled material experiment retains exact legacy camera behavior");
            Int("advanced_materials", 0); Int("modern_material_corrections", 1);
            byte[] originalFront = Picture("material-original-front");
            Vec("material_camera_position", new Vector3(2, 0, 0));
            byte[] originalSide = Picture("material-original-side");
            Require(originalFront.SequenceEqual(originalSide), "Original appearance remains independent of modern camera correction");

            int main = Shaders.FragmentShader.IndexOf("void main()", StringComparison.Ordinal);
            Require(main >= 0, "material diagnostic uses production helper prefix");
            Select(Shaders.FragmentShader[..main] + "void main(){gl_FragColor=vec4(mapped_normal()*0.5+0.5,1.0);}");
            Int("modern_material_corrections", 1); Int("use_normal_map", 1); Int("normal_tex", 0);
            Vec("fixture_normal", new Vector3(.6f, 0, .8f));
            Tex(255, 128, 128);
            byte[] skewed = Picture("material-normal-skew");
            PixelIs(skewed, 32, 32, (r,g,b) => Math.Abs(r - 230) <= 3 && Math.Abs(g - 128) <= 3 && Math.Abs(b - 51) <= 3,
                "Gram-Schmidt tangent is perpendicular to smooth normal");
            Vec("fixture_normal", Vector3.UnitZ); Tex(128, 255, 128);
            byte[] regular = Picture("material-normal-regular-uv");
            byte[] mirror = Picture("material-normal-mirrored-uv", mirrored: true);
            PixelIs(regular, 32, 32, (r,g,b) => g > 250, "regular UV retains positive bitangent");
            PixelIs(mirror, 32, 32, (r,g,b) => g < 5, "mirrored UV preserves tangent handedness");
            Vec("fixture_normal", new Vector3(.6f, 0, .8f));
            byte[] flatUv = Picture("material-normal-degenerate-uv", degenerateUv: true);
            PixelIs(flatUv, 32, 32, (r,g,b) => Math.Abs(r - 204) <= 2 && Math.Abs(b - 230) <= 2,
                "degenerate UV uses finite geometric normal");
            Int("fixture_degenerate_world", 1);
            byte[] flatWorld = Picture("material-normal-degenerate-world");
            Require(flatUv.SequenceEqual(flatWorld), "degenerate world derivatives use the same finite fallback");
            Int("use_normal_map", 0); Vec("fixture_normal", Vector3.Zero);
            byte[] zero = Picture("material-normal-zero");
            PixelIs(zero, 32, 32, (r,g,b) => Math.Abs(r - 128) <= 1 && Math.Abs(g - 128) <= 1 && b == 255,
                "zero normal has deterministic finite fallback");
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "modern material fixtures produce no GL errors");
        }
        finally
        {
            GraphicsApi.UseProgram(0); GraphicsApi.ActiveTexture(TextureUnit.Texture0); GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteTexture(texture);
            if (program != 0) GraphicsApi.DeleteProgram(program);
            if (fragment != 0) GraphicsApi.DeleteShader(fragment);
            GraphicsApi.DeleteShader(vertex);
        }
    }
}
#endif
