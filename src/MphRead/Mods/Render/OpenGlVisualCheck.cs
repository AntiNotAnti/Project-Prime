#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using NativeGL = OpenTK.Graphics.OpenGL.GL;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render;

/// <summary>
/// Real software/desktop OpenGL pixel regression, no proprietary game assets.
/// This is a depth/stencil/alpha baseline, not a claim that full Ice Hive or
/// shadow-map rendering has been verified on end-user GPUs.
/// </summary>
internal static partial class OpenGlVisualCheck
{
    private const int Width = 64;
    private const int Height = 64;
    private static readonly Dictionary<string, string> _images = new();
    private static int _assertions;

    internal static int Run(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new Vector2i(Width, Height);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            DesktopGraphicsSession.Resize(window);
            Render(output);
            File.WriteAllText(Path.Combine(output, "evidence.json"),
                JsonSerializer.Serialize(new
                {
                    schema = 1,
                    scope = "synthetic-desktop-OpenGL-depth-stencil-alpha",
                    width = Width,
                    height = Height,
                    format = "RGBA8-bottom-up",
                    driver = GraphicsApi.GetString(StringName.Version),
                    checkedPixels = _assertions,
                    captures = _images
                }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[glvisualcheck] PASS {_assertions} pixel checks, {_images.Count} captures");
            return 0;
        }
        catch (InvalidOperationException ex)
            when (DesktopGlContext.HostedMacLacksNsgl(ex))
        {
            Console.Error.WriteLine("[glvisualcheck] SKIP hosted macOS NSGL unavailable");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[glvisualcheck] FAIL " + ex);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PixelIs(byte[] frame, int x, int y,
        Func<byte, byte, byte, bool> predicate, string label)
    {
        int offset = (y * Width + x) * 4;
        byte red = frame[offset], green = frame[offset + 1], blue = frame[offset + 2];
        Require(predicate(red, green, blue),
            $"{label}: got ({red},{green},{blue}) at ({x},{y})");
    }

    private static void DrawQuad(float left, float bottom,
        float right, float top, float depth, float red, float green,
        float blue, float alpha = 1f)
    {
        GraphicsApi.Color4(red, green, blue, alpha);
        GraphicsApi.Begin(PrimitiveType.Quads);
        GraphicsApi.Vertex3(left, bottom, depth);
        GraphicsApi.Vertex3(right, bottom, depth);
        GraphicsApi.Vertex3(right, top, depth);
        GraphicsApi.Vertex3(left, top, depth);
        GraphicsApi.End();
    }

    private static void ResetPass()
    {
        GraphicsApi.UseProgram(0);
        GraphicsApi.MatrixMode(MatrixMode.Projection);
        GraphicsApi.LoadIdentity();
        GraphicsApi.MatrixMode(MatrixMode.Modelview);
        GraphicsApi.LoadIdentity();
        GraphicsApi.Viewport(0, 0, Width, Height);
        GraphicsApi.ColorMask(true, true, true, true);
        GraphicsApi.DepthMask(true);
        GraphicsApi.Enable(EnableCap.DepthTest);
        GraphicsApi.DepthFunc(DepthFunction.Less);
        GraphicsApi.Disable(EnableCap.AlphaTest);
        GraphicsApi.Disable(EnableCap.Texture2D);
        GraphicsApi.Disable(EnableCap.StencilTest);
        GraphicsApi.Disable(EnableCap.Blend);
        GraphicsApi.Disable(EnableCap.ScissorTest);
        GraphicsApi.StencilMask(0xFF);
        GraphicsApi.ClearStencil(0);
        GraphicsApi.ClearColor(0, 0, 0, 1);
        GraphicsApi.Clear(ClearBufferMask.ColorBufferBit
            | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    private static void CheckMatrixArrayRestore()
    {
        ResetPass();
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
        int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        int program = GraphicsApi.CreateProgram();
        try
        {
            GraphicsApi.ShaderSource(vertex, "#version 120\nuniform mat4 bones[2]; uniform int bone; void main(){gl_Position=bones[bone]*gl_Vertex;}");
            GraphicsApi.ShaderSource(fragment, "#version 120\nvoid main(){gl_FragColor=vec4(0,1,0,1);}");
            GraphicsApi.CompileShader(vertex);
            GraphicsApi.CompileShader(fragment);
            GraphicsApi.AttachShader(program, vertex);
            GraphicsApi.AttachShader(program, fragment);
            GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Require(linked != 0, "matrix palette regression shader must link");
            GraphicsApi.UseProgram(program);
            int location = GraphicsApi.GetUniformLocation(program, "bones[0]");
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "bone"), 0);
            Matrix4 identity = Matrix4.Identity;
            GraphicsApi.UniformMatrix4(location, false, ref identity);
            // A skinned model/HUD uploads a palette after a static room has
            // cached identity. Restoring that same identity must reach GL.
            float[] palette = {
                1,0,0,0, 0,1,0,0, 0,0,1,0, 3,0,0,1,
                1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1
            };
            GraphicsApi.UniformMatrix4(location, 2, false, palette);
            GraphicsApi.UniformMatrix4(location, false, ref identity);
            DrawQuad(-.5f, -.5f, .5f, .5f, 0, 0, 1, 0);
            byte[] pixels = new byte[Width * Height * 4];
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            PixelIs(pixels, 32, 32, (r,g,b) => g > 245 && r < 10,
                "room transform must be restored after an animated matrix palette");

            // The restored room must also write depth at its real position.
            // Translucent jump-pad geometry behind it must remain occluded.
            GraphicsApi.UseProgram(0);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.DepthMask(false);
            DrawQuad(-.8f, -.8f, .8f, .8f, .5f, 1, 0, 0, .5f);
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            PixelIs(pixels, 32, 32, (r,g,b) => g > 245 && r < 10,
                "restored room depth must occlude translucent effects");
            PixelIs(pixels, 8, 32, (r,g,b) => r > 100 && g < 10,
                "translucent effect must remain visible outside the wall");
        }
        finally
        {
            GraphicsApi.UseProgram(0);
            GraphicsApi.DeleteProgram(program);
            GraphicsApi.DeleteShader(vertex);
            GraphicsApi.DeleteShader(fragment);
        }
    }

    private static void CheckRetainedVertexArrayState(string output)
    {
        ResetPass();
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
        int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        int program = GraphicsApi.CreateProgram();
        int first = GraphicsApi.GenLists(4);
        int callerVbo = NativeGL.GenBuffer(), callerIbo = NativeGL.GenBuffer();
        OpenGlVertexArrayApi api = OpenGlContextPolicy.Current.DesktopVertexArrayApi;
        int callerVao = api == OpenGlVertexArrayApi.None ? 0 : DesktopVertexArrays.Create(api);
        object? Compiled(int id)
            => (typeof(DesktopRetainedGeometry).GetField("_lists", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as IDictionary)?[id];
        int Vao(int id) => Compiled(id)?.GetType().GetField("Vao", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(Compiled(id)) is int value ? value : 0;
        void CallerState()
        {
            if (callerVao != 0) DesktopVertexArrays.Bind(api, callerVao);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, callerVbo);
            NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, callerIbo);
            NativeGL.ClientActiveTexture(TextureUnit.Texture1);
        }
        void StateIsPreserved(string label)
        {
            Require(NativeGL.GetInteger(GetPName.ArrayBufferBinding) == callerVbo, label + " changed caller VBO");
            Require(NativeGL.GetInteger(GetPName.ElementArrayBufferBinding) == callerIbo, label + " changed caller IBO");
            Require(NativeGL.GetInteger(GetPName.ClientActiveTexture) == (int)TextureUnit.Texture1,
                label + " changed caller client texture unit");
            Require(NativeGL.GetInteger(GetPName.TextureCoordArray) != 0
                && NativeGL.GetInteger(GetPName.TextureCoordArrayBufferBinding) == callerVbo
                && NativeGL.GetInteger(GetPName.TextureCoordArrayStride) == 16,
                label + " changed caller texture1 array state");
            if (callerVao != 0)
                Require(NativeGL.GetInteger(GetPName.VertexArrayBinding) == callerVao, label + " changed caller VAO");
        }
        void Compile(int id, int attributes, float offset = 0)
        {
            CallerState();
            GraphicsApi.RegisterRoomGeometryList(id);
            GraphicsApi.NewList(id, ListMode.Compile);
            GraphicsApi.Begin(PrimitiveType.Triangles);
            float[] xs = { -.7f, .7f, 0f };
            float[] ys = { -.6f, -.6f, .6f };
            for (int i = 0; i < 3; i++)
            {
                if (attributes == 1 || attributes == 2 && i > 0)
                {
                    GraphicsApi.Color4(.1f + .15f * i, .6f, .2f + offset, .75f);
                    GraphicsApi.Normal3(.2f, .3f + .1f * i, .4f);
                    GraphicsApi.TexCoord3(.1f * i, .2f, .25f + .1f * i);
                }
                GraphicsApi.Vertex3(xs[i], ys[i], 0);
            }
            GraphicsApi.End();
            GraphicsApi.EndList();
            StateIsPreserved("list compilation");
        }
        float[] lastAttributes = Array.Empty<float>();
        byte[] Picture(int id, bool retained, int second = 0)
        {
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GraphicsApi.Color4(.7f, .2f, .4f, .85f);
            GraphicsApi.Normal3(.6f, .2f, .3f);
            GraphicsApi.TexCoord3(.4f, .2f, .8f);
            CallerState();
            if (retained)
            {
                GraphicsApi.BeginScopedWorldStateElision();
                try
                {
                    if (second != 0 && DesktopRetainedGeometry.Enabled)
                        Require(!DesktopRetainedGeometry.TryDrawBatch(new[] { id, second }, 2),
                            "combined room lists must reject nonfinal trailing inherited attribute writes");
                    GraphicsApi.CallList(id);
                    if (second != 0) GraphicsApi.CallList(second);
                }
                finally { GraphicsApi.EndScopedWorldStateElision(); }
            }
            else
            {
                NativeGL.CallList(id);
                if (second != 0) NativeGL.CallList(second);
            }
            StateIsPreserved(retained ? "retained draw" : "native list draw");
            float[] currentColor = new float[4], currentNormal = new float[3], currentUv = new float[4];
            NativeGL.GetFloat(GetPName.CurrentColor, currentColor);
            NativeGL.GetFloat(GetPName.CurrentNormal, currentNormal);
            NativeGL.GetFloat(GetPName.CurrentTextureCoords, currentUv);
            lastAttributes = currentColor.Concat(currentNormal).Concat(currentUv).ToArray();
            byte[] pixels = new byte[Width * Height * 4];
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "retained attribute fixture generated GL error");
            return pixels;
        }
        try
        {
            if (callerVao != 0) DesktopVertexArrays.Bind(api, callerVao);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, callerVbo);
            NativeGL.BufferData(BufferTarget.ArrayBuffer, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, callerIbo);
            NativeGL.BufferData(BufferTarget.ElementArrayBuffer, 16, IntPtr.Zero, BufferUsageHint.StaticDraw);
            NativeGL.ClientActiveTexture(TextureUnit.Texture1);
            NativeGL.EnableClientState(ArrayCap.TextureCoordArray);
            NativeGL.TexCoordPointer(3, TexCoordPointerType.Float, 16, (IntPtr)4);
            GraphicsApi.ShaderSource(vertex, "#version 120\nvarying vec4 c; varying vec3 n; varying vec3 uv; void main(){gl_Position=gl_Vertex;c=gl_Color;n=gl_Normal;uv=gl_MultiTexCoord0.xyz;}");
            GraphicsApi.ShaderSource(fragment, "#version 120\nvarying vec4 c; varying vec3 n; varying vec3 uv; void main(){gl_FragColor=vec4(c.rgb*.5+n*.2+vec3(uv.z*.2),c.a);}");
            GraphicsApi.CompileShader(vertex); GraphicsApi.CompileShader(fragment);
            GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment);
            GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Require(linked != 0, "retained attribute shader must link");
            GraphicsApi.UseProgram(program);
            Compile(first, 0); Compile(first + 1, 1); Compile(first + 2, 2);
            for (int i = 0; i < 3; i++)
            {
                int id = first + i;
                if (DesktopRetainedGeometry.Enabled && i != 2)
                {
                    Require(Compiled(id) != null, "homogeneous attribute list failed VBO promotion");
                    if (OpenGlContextPolicy.Vao)
                        Require(Vao(id) != 0, "enabled retained VAO path did not create an accelerated object");
                }
                if (i == 2) Require(Compiled(id) == null, "mixed attribute list must keep ordered native fallback");
                byte[] native = Picture(id, false);
                float[] nativeAttributes = lastAttributes;
                byte[] retained = Picture(id, true);
                Require(nativeAttributes.SequenceEqual(lastAttributes),
                    "retained draw did not preserve native final color/normal/UV current attributes " + i);
                Require(native.SequenceEqual(retained), "retained inherited/color/normal/UV.z pixels differ from native list " + i);
                PixelIs(retained, 32, 32, (r,g,b) => r > 20 && g > 20 && b > 20,
                    "retained attribute fixture must actually draw");
            }
            // Replacing the same native list ID must replace promoted buffers/VAO,
            // while an unsupported replacement must remove the promotion entirely.
            byte[] previous = Picture(first + 1, false);
            Compile(first + 1, 1, .4f);
            byte[] replacement = Picture(first + 1, true);
            Require(!previous.SequenceEqual(replacement), "same-ID recompile reused stale retained pixels");
            Require(Picture(first + 1, false).SequenceEqual(replacement), "recompiled retained pixels differ from native replacement");
            Compile(first + 1, 2);
            Require(Compiled(first + 1) == null, "mixed same-ID replacement left stale retained geometry");
            Require(Picture(first + 1, false).SequenceEqual(Picture(first + 1, true)), "same-ID mixed fallback changed pixels");
            GraphicsApi.DeleteLists(first + 1, 1);
            Require(Compiled(first + 1) == null, "list deletion retained an owned VAO/mesh");
            // Both lists have vertex-only layouts. The first changes current
            // attributes after its final vertex; the second must inherit them.
            for (int i = 0; i < 2; i++)
            {
                int id = first + 2 + i;
                GraphicsApi.RegisterRoomGeometryList(id);
                GraphicsApi.NewList(id, ListMode.Compile);
                GraphicsApi.Begin(PrimitiveType.Triangles);
                float x = i == 0 ? -.4f : .4f;
                GraphicsApi.Vertex3(x - .3f, -.5f, 0);
                GraphicsApi.Vertex3(x + .3f, -.5f, 0);
                GraphicsApi.Vertex3(x, .5f, 0);
                GraphicsApi.End();
                if (i == 0)
                {
                    GraphicsApi.Normal3(.1f, .8f, .2f);
                    GraphicsApi.TexCoord3(.2f, .3f, .6f);
                    GraphicsApi.Color4(.2f, .8f, .3f, .9f);
                }
                GraphicsApi.EndList();
            }
            byte[] orderedNative = Picture(first + 2, false, first + 3);
            float[] orderedAttributes = lastAttributes;
            byte[] orderedRetained = Picture(first + 2, true, first + 3);
            Require(orderedNative.SequenceEqual(orderedRetained),
                "ordered room fallback preserves trailing inherited normal/UV/color pixels");
            Require(orderedAttributes.SequenceEqual(lastAttributes),
                "ordered room fallback preserves trailing final current attributes");
            PixelIs(orderedRetained, 44, 28, (r,g,b) => g > r && g > b,
                "second room list visibly consumes first list trailing current attributes");
            // Existing comparators require their fixed capture set; this fixture
            // supplies assertions without changing the public manifest inventory.
        }
        finally
        {
            GraphicsApi.UseProgram(0);
            if (callerVao != 0) DesktopVertexArrays.Bind(api, 0);
            NativeGL.ClientActiveTexture(TextureUnit.Texture1);
            NativeGL.DisableClientState(ArrayCap.TextureCoordArray);
            NativeGL.ClientActiveTexture(TextureUnit.Texture0);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, 0); NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GraphicsApi.DeleteLists(first, 4);
            if (callerVao != 0) DesktopVertexArrays.Delete(api, callerVao);
            NativeGL.DeleteBuffer(callerVbo); NativeGL.DeleteBuffer(callerIbo);
            GraphicsApi.DeleteProgram(program); GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
        }
    }

    private static byte[] Snapshot(string output, string name)
    {
        byte[] pixels = new byte[Width * Height * 4];
        GraphicsApi.ReadPixels(0, 0, Width, Height,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Require(GraphicsApi.GetError() == ErrorCode.NoError,
            name + " generated a GL error");
        File.WriteAllBytes(Path.Combine(output, name + ".rgba"), pixels);
        _images[name + ".rgba"] = Convert.ToHexString(SHA256.HashData(pixels));
        return pixels;
    }

    private static void CompileColorQuad(int id, float left, float right,
        float red, float green)
    {
        GraphicsApi.RegisterRoomGeometryList(id);
        GraphicsApi.NewList(id, ListMode.Compile);
        try
        {
            GraphicsApi.Color3(red, green, 0f);
            GraphicsApi.TexCoord3(0f, 0f, 0f);
            DrawQuad(left, -.6f, right, .6f, 0f, red, green, 0f);
        }
        finally { GraphicsApi.EndList(); }
    }

    private static void CheckDisplayListAttributeMetadata()
    {
        int first = GraphicsApi.GenLists(4);
        try
        {
            void Triangle(bool explicitAttributes)
            {
                if (explicitAttributes)
                {
                    GraphicsApi.Color4(.2f, .4f, .6f, 1f);
                    GraphicsApi.Normal3(0f, 0f, 1f);
                    GraphicsApi.TexCoord3(.25f, .75f, 2f);
                }
                GraphicsApi.Begin(PrimitiveType.Triangles);
                GraphicsApi.Vertex3(-.5f, -.5f, 0f);
                GraphicsApi.Vertex3(.5f, -.5f, 0f);
                GraphicsApi.Vertex3(0f, .5f, 0f);
                GraphicsApi.End();
            }
            GraphicsApi.NewList(first, ListMode.Compile); Triangle(true); GraphicsApi.EndList();
            Require(GraphicsApi.TryGetDisplayListAttributeEffects(first, out var known)
                && known.WritesColor && known.Color == new Vector4(.2f, .4f, .6f, 1f)
                && known.WritesNormal && known.Normal == Vector3.UnitZ
                && known.WritesTexcoord && known.Texcoord == new Vector3(.25f, .75f, 2f)
                && !known.ReadsInheritedNormal && !known.ReadsInheritedTexcoord,
                "facade records trusted explicit display-list color/normal/UV");
            GraphicsApi.NewList(first + 1, ListMode.Compile); Triangle(false); GraphicsApi.EndList();
            Require(GraphicsApi.TryGetDisplayListAttributeEffects(first + 1, out var inherited)
                && inherited.ReadsInheritedNormal && inherited.ReadsInheritedTexcoord,
                "facade records attributes read before any explicit write");
            GraphicsApi.NewList(first + 2, ListMode.Compile);
            GraphicsApi.Enable(EnableCap.CullFace); Triangle(true); GraphicsApi.EndList();
            Require(!GraphicsApi.TryGetDisplayListAttributeEffects(first + 2, out _),
                "unsupported display-list raster command invalidates metadata");
            GraphicsApi.NewList(first + 3, ListMode.Compile); GraphicsApi.CallList(first); GraphicsApi.EndList();
            Require(GraphicsApi.TryGetDisplayListAttributeEffects(first + 3, out _), "nested trusted list records dependency");
            GraphicsApi.NewList(first, ListMode.Compile); Triangle(true); GraphicsApi.EndList();
            Require(!GraphicsApi.TryGetDisplayListAttributeEffects(first + 3, out _), "recompilation invalidates dependent metadata");
            GraphicsApi.NewList(first + 3, ListMode.Compile); GraphicsApi.CallList(first); GraphicsApi.EndList();
            GraphicsApi.DeleteLists(first, 1);
            Require(!GraphicsApi.TryGetDisplayListAttributeEffects(first, out _)
                && !GraphicsApi.TryGetDisplayListAttributeEffects(first + 3, out _), "deletion invalidates source and dependent metadata");
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "display-list metadata fixture generates no GL errors");
        }
        finally { GraphicsApi.DeleteLists(first, 4); }
    }

    private static void CheckDepthVertexSpecialization()
    {
        ResetPass();
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
        int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        int program = GraphicsApi.CreateProgram();
        int texture = GraphicsApi.GenTexture();
        try
        {
            GraphicsApi.ShaderSource(vertex, Shaders.VertexShader);
            GraphicsApi.ShaderSource(fragment, Shaders.FragmentShader);
            GraphicsApi.CompileShader(vertex); GraphicsApi.CompileShader(fragment);
            GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment);
            GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Require(linked != 0, "depth vertex/full fragment fixture must link: " + GraphicsApi.GetProgramInfoLog(program));
            GraphicsApi.UseProgram(program);
            int Location(string name) => GraphicsApi.GetUniformLocation(program, name);
            void Int(string name, int value) => GraphicsApi.Uniform1(Location(name), value);
            void Float(string name, float value) => GraphicsApi.Uniform1(Location(name), value);
            void Vector(string name, Vector3 value) => GraphicsApi.Uniform3(Location(name), value);
            void Matrix(string name, Matrix4 value) => GraphicsApi.UniformMatrix4(Location(name), false, ref value);
            Require(Location("depth_vertex_fast") >= 0, "vertex fast-path fixture exercises linked uniform");
            Matrix("proj_mtx", Matrix4.Identity); Matrix("view_mtx", Matrix4.Identity);
            Matrix("tex_mtx", Matrix4.Identity);
            float[] palette = new float[32 * 16];
            for (int i = 0; i < 32; i++)
                palette[i * 16] = palette[i * 16 + 5] = palette[i * 16 + 10] = palette[i * 16 + 15] = 1;
            // Nonidentity second joint verifies weighted position/UV inputs remain intact.
            palette[16 + 12] = .2f;
            GraphicsApi.UniformMatrix4(Location("mtx_stack[0]"), 32, false, palette);
            Int("show_colors", 1); Int("use_light", 1); Int("tex", 0);
            Vector("light1vec", -Vector3.UnitZ); Vector("light2vec", -Vector3.UnitZ);
            Vector("light1col", new Vector3(.5f)); Vector("light2col", new Vector3(.25f));
            Vector("diffuse", new Vector3(.6f)); Vector("ambient", new Vector3(.1f));
            Vector("specular", new Vector3(.2f)); Vector("emission", new Vector3(.05f));
            Float("mat_alpha", .75f);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            byte[] texels = { 255,32,64,255, 32,255,64,0, 64,32,255,128, 255,255,32,255 };
            NativeGL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 2, 2, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, texels);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            (byte[] Color, float[] Depth) Picture(bool fast, bool weighted, bool billboard)
            {
                Int("depth_vertex_fast", fast ? 1 : 0);
                Int("weighted_skinning", weighted ? 1 : 0);
                Matrix("view_inv_mtx", billboard ? Matrix4.CreateRotationZ(.2f) : Matrix4.Identity);
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GraphicsApi.Normal3(.2f, .3f, .9f);
                // Joints 0/1 with equal weights; otherwise ordinary vertex color.
                GraphicsApi.Color4(weighted ? .5f : .2f, .5f, weighted ? 0 : .8f, weighted ? 0 : 1);
                GraphicsApi.Begin(PrimitiveType.Quads);
                GraphicsApi.TexCoord3(.1f, .1f, weighted ? 32 : 0); GraphicsApi.Vertex3(-.7f, -.7f, -.2f);
                GraphicsApi.TexCoord3(.9f, .1f, weighted ? 32 : 0); GraphicsApi.Vertex3(.7f, -.7f, -.2f);
                GraphicsApi.TexCoord3(.9f, .9f, weighted ? 32 : 0); GraphicsApi.Vertex3(.7f, .7f, -.2f);
                GraphicsApi.TexCoord3(.1f, .9f, weighted ? 32 : 0); GraphicsApi.Vertex3(-.7f, .7f, -.2f);
                GraphicsApi.End();
                byte[] color = new byte[Width * Height * 4];
                float[] depth = new float[Width * Height];
                GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, color);
                GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.DepthComponent, PixelType.Float, depth);
                return (color, depth);
            }
            void Pair(string name, bool weighted = false, bool billboard = false, bool fullColor = false)
            {
                var ordinary = Picture(false, weighted, billboard);
                var specialized = Picture(true, weighted, billboard);
                Require(ordinary.Color.SequenceEqual(specialized.Color), name + " vertex branch preserves exact RGBA/alpha");
                Require(ordinary.Depth.SequenceEqual(specialized.Depth), name + " vertex branch preserves exact depth/coverage");
                Require(ordinary.Depth.Any(d => d < 1), name + " fixture actually draws covered pixels");
                if (fullColor) Require(ordinary.Color.Where((_, i) => i % 4 != 3).Any(v => v != 0),
                    name + " exercises full-color fallback rather than black depth output");
            }
            Int("depth_only", 1);
            GraphicsApi.Enable(EnableCap.AlphaTest);
            GraphicsApi.AlphaFunc(AlphaFunction.Greater, .3f);
            for (int mode = 0; mode <= 3; mode++)
            {
                Int("texgen_mode", mode); Int("use_texture", 1);
                for (int material = 0; material <= 2; material++)
                {
                    Int("mat_mode", material);
                    Pair($"depth UV mode {mode}, material {material}");
                }
            }
            Int("texgen_mode", 2); Int("mat_mode", 0);
            Pair("weighted depth geometry", weighted: true);
            Pair("billboard depth geometry", billboard: true);
            Int("use_texture", 0); Pair("untextured depth alpha");
            GraphicsApi.Disable(EnableCap.AlphaTest);
            GraphicsApi.Uniform4(Location("override_color"), new Vector4(.3f, .7f, .2f, .8f));
            Int("use_override", 1); Pair("override fallback", fullColor: true); Int("use_override", 0);
            Vector("player_outline_color", new Vector3(.2f, .6f, .8f));
            Int("player_outline_mask", 1); Pair("outline fallback", fullColor: true); Int("player_outline_mask", 0);
            Vector("cosmetic_primary", new Vector3(.6f, .2f, .4f));
            Vector("cosmetic_secondary", new Vector3(.2f, .7f, .3f));
            Float("cosmetic_intensity", .5f);
            Int("cosmetic_skin", 1); Pair("cosmetic skin fallback", fullColor: true); Int("cosmetic_skin", 0);
            Int("cosmetic_effect", 1); Pair("cosmetic effect fallback", fullColor: true); Int("cosmetic_effect", 0);
            // Dissolve noise is derived from UVs; an untextured draw has UV=0
            // and intentionally discards every pixel for any positive dissolve.
            Int("use_texture", 1); Int("texgen_mode", 0);
            Float("cosmetic_dissolve", .01f); Pair("cosmetic dissolve fallback"); Float("cosmetic_dissolve", 0);
            Int("depth_only", 0); Pair("ordinary lit color pass", fullColor: true);
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "depth vertex fixture generated GL error");
        }
        finally
        {
            GraphicsApi.UseProgram(0);
            GraphicsApi.DeleteProgram(program); GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0); GraphicsApi.DeleteTexture(texture);
        }
    }

    private static void CheckProceduralStream()
    {
        if (!OpenGlContextPolicy.Vao) return;
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader), fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        int program = GraphicsApi.CreateProgram();
        var api = OpenGlContextPolicy.VaoApi;
        int callerVao = DesktopVertexArrays.Create(api), buffer = NativeGL.GenBuffer(), element = NativeGL.GenBuffer();
        long initialBytes = DesktopProceduralStream.ResidentBytes;
        var stream = new DesktopProceduralStream();
        void Caller()
        {
            DesktopVertexArrays.Bind(api, callerVao);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, element);
            NativeGL.ClientActiveTexture(TextureUnit.Texture1);
        }
        void Preserved(string name)
        {
            Require(NativeGL.GetInteger(GetPName.VertexArrayBinding) == callerVao
                && NativeGL.GetInteger(GetPName.ArrayBufferBinding) == buffer
                && NativeGL.GetInteger(GetPName.ElementArrayBufferBinding) == element
                && NativeGL.GetInteger(GetPName.ClientActiveTexture) == (int)TextureUnit.Texture1,
                name + " preserves procedural caller VAO/buffers/client texture");
        }
        RetainedDrawPacket Packet(RenderItem item) => new(item, 0, new RetainedMeshDescriptor(RetainedMeshDescriptor.KeyOf(item)));
        (byte[] Color, float[] Depth, float[] Attributes) Picture(RenderItem item, bool accelerated, bool direct = false)
        {
            ResetPass(); GraphicsApi.UseProgram(program);
            GraphicsApi.Color4(.6f, .3f, .2f, .8f); GraphicsApi.Normal3(.2f, .4f, .6f);
            Caller();
            if (direct) Require(GraphicsApi.TryDrawProceduralImmediate(item), "procedural fixture uses direct native submission");
            else if (accelerated)
            {
                GraphicsApi.BeginScopedWorldStateElision();
                try { Require(stream.TryDraw(item), "procedural fixture uses accelerated submission"); }
                finally { GraphicsApi.EndScopedWorldStateElision(); }
            }
            else
            {
                PrimitiveType mode = item.Type is RenderItemType.TrailSingle or RenderItemType.TrailMulti ? PrimitiveType.QuadStrip : PrimitiveType.Quads;
                int count = item.Type == RenderItemType.TrailStack ? item.ItemCount * 4 : item.Type == RenderItemType.TrailMulti ? item.ItemCount / 2 : 4;
                NativeGL.Begin(mode);
                for (int i = 0; i < count; i++)
                {
                    Vector3 uv = item.Points[i * 2];
                    if (item.Type == RenderItemType.Particle) uv = new(uv.X * item.ScaleS, uv.Y * item.ScaleT, 0);
                    NativeGL.TexCoord3(uv); NativeGL.Vertex3(item.Points[i * 2 + 1]);
                }
                NativeGL.End();
            }
            Preserved("draw");
            var pixels = new byte[Width * Height * 4]; var depth = new float[Width * Height];
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.DepthComponent, PixelType.Float, depth);
            var c = new float[4]; var n = new float[3]; var uvState = new float[4];
            NativeGL.GetFloat(GetPName.CurrentColor, c); NativeGL.GetFloat(GetPName.CurrentNormal, n);
            NativeGL.GetFloat(GetPName.CurrentTextureCoords, uvState);
            return (pixels, depth, c.Concat(n).Concat(uvState).ToArray());
        }
        try
        {
            GraphicsApi.ShaderSource(vertex, "#version 120\nvarying vec4 c; void main(){gl_Position=vec4(gl_Vertex.xy+vec2(gl_MultiTexCoord0.z*.01),gl_Vertex.z,1);c=vec4(gl_Color.rgb*.5+gl_Normal*.2+gl_MultiTexCoord0.xyz*.1,gl_Color.a);}");
            GraphicsApi.ShaderSource(fragment, "#version 120\nvarying vec4 c; void main(){gl_FragColor=c;}");
            GraphicsApi.CompileShader(vertex); GraphicsApi.CompileShader(fragment);
            GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment); GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Require(linked != 0, "procedural fixture shader links");
            foreach (var type in new[] { RenderItemType.Particle, RenderItemType.TrailSingle, RenderItemType.TrailMulti, RenderItemType.TrailStack })
            {
                var item = new RenderItem { Type = type, ScaleS = .7f, ScaleT = .8f, ItemCount = type == RenderItemType.TrailStack ? 2 : 8,
                    Points = new[] { new Vector3(.1f,.2f,0), new Vector3(-.6f,-.5f,-.2f), new Vector3(.5f,.2f,1), new Vector3(.6f,-.5f,.1f),
                        new Vector3(.6f,.8f,2), new Vector3(.5f,.6f,-.1f), new Vector3(.1f,.7f,3), new Vector3(-.5f,.5f,.2f),
                        new Vector3(.2f,.3f,4), new Vector3(-.3f,-.2f,-.3f), new Vector3(.6f,.3f,5), new Vector3(.3f,-.2f,-.3f),
                        new Vector3(.6f,.6f,6), new Vector3(.3f,.2f,-.3f), new Vector3(.2f,.6f,7), new Vector3(-.3f,.2f,-.3f) } };
                var packets = new[] { Packet(item) };
                for (int frame = 0; frame < 2; frame++)
                {
                    if (frame != 0) item.Points[1] += new Vector3(.13f, .07f, -.04f);
                    Caller(); stream.Prepare(packets, Array.Empty<RetainedDrawPacket>(), Array.Empty<RetainedDrawPacket>()); Preserved("prepare");
                    var immediate = Picture(item, false); var streamed = Picture(item, true);
                    var direct = Picture(item, false, direct: true);
                    Require(immediate.Color.SequenceEqual(direct.Color), type + " direct emission preserves exact RGBA");
                    Require(immediate.Depth.SequenceEqual(direct.Depth), type + " direct emission preserves exact float depth");
                    Require(immediate.Attributes.SequenceEqual(direct.Attributes), type + " direct emission preserves current color/normal/final UV");
                    Require(immediate.Color.SequenceEqual(streamed.Color), type + " current-frame stream preserves exact RGBA");
                    Require(immediate.Depth.SequenceEqual(streamed.Depth) && streamed.Depth.Any(d => d < 1), type + " preserves nonplanar primitive depth");
                    Require(immediate.Attributes.SequenceEqual(streamed.Attributes), type + " preserves inherited color/normal and final 3D UV");
                    stream.End(); Require(!stream.TryDraw(item), "ended procedural frame cannot replay stale geometry");
                }
            }
            var overflow = new RenderItem { Type = RenderItemType.TrailStack, ItemCount = ProceduralFrameGeometry.MaxVertices / 4 + 1 };
            stream.Prepare(new[] { Packet(overflow) }, Array.Empty<RetainedDrawPacket>(), Array.Empty<RetainedDrawPacket>());
            GraphicsApi.BeginScopedWorldStateElision();
            try { Require(!stream.TryDraw(overflow), "oversized procedural item falls back to immediate submission"); }
            finally { GraphicsApi.EndScopedWorldStateElision(); }
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "procedural fixture generates no GL errors");
        }
        finally
        {
            stream.Dispose(); Require(DesktopProceduralStream.ResidentBytes == initialBytes, "procedural dispose releases every resident buffer byte");
            GraphicsApi.UseProgram(0); DesktopVertexArrays.Bind(api, 0);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, 0); NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            NativeGL.ClientActiveTexture(TextureUnit.Texture0); DesktopVertexArrays.Delete(api, callerVao);
            NativeGL.DeleteBuffer(buffer); NativeGL.DeleteBuffer(element);
            GraphicsApi.DeleteProgram(program); GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
        }
    }

    private static void CheckUnlitLightingOmission()
    {
        ResetPass();
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader), fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        int program = GraphicsApi.CreateProgram(), texture = GraphicsApi.GenTexture();
        int fullProgram = program, nativeProgram = 0, nativeFragment = 0, plainProgram = 0, plainFragment = 0;
        try
        {
            GraphicsApi.ShaderSource(vertex, Shaders.SelectedVertexShader);
            GraphicsApi.ShaderSource(fragment, Shaders.FragmentShader);
            GraphicsApi.CompileShader(vertex); GraphicsApi.CompileShader(fragment);
            GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment); GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Require(linked != 0, "unlit omission production shader links");
            GraphicsApi.UseProgram(program);
            int Loc(string name) => GraphicsApi.GetUniformLocation(program, name);
            void Int(string name, int value) => GraphicsApi.Uniform1(Loc(name), value);
            void Vec(string name, Vector3 value) => GraphicsApi.Uniform3(Loc(name), value);
            void Matrix(string name) { Matrix4 identity = Matrix4.Identity; GraphicsApi.UniformMatrix4(Loc(name), false, ref identity); }
            foreach (string name in new[] { "proj_mtx", "view_mtx", "view_inv_mtx", "tex_mtx", "mtx_stack[0]" }) Matrix(name);
            Int("show_colors", 1); Int("mat_mode", 0); Int("advanced_materials", 1);
            Int("use_emissive_map", 1); Int("emissive_tex", 0);
            GraphicsApi.Uniform1(Loc("mat_alpha"), .7f); GraphicsApi.Uniform1(Loc("emissive_intensity"), .4f);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0); GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            byte[] texel = { 32, 64, 96, 255 };
            NativeGL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texel);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            void Lighting(bool stale)
            {
                Vec("diffuse", new Vector3(stale ? .95f : .3f)); Vec("ambient", new Vector3(stale ? .8f : .05f));
                Vec("specular", new Vector3(stale ? .9f : .1f)); Vec("emission", new Vector3(stale ? .7f : .02f));
                Vec("light1vec", stale ? Vector3.UnitX : -Vector3.UnitZ); Vec("light2vec", stale ? Vector3.UnitY : -Vector3.UnitZ);
                Vec("light1col", new Vector3(stale ? .9f : .3f)); Vec("light2col", new Vector3(stale ? .8f : .1f));
            }
            (byte[] Color, float[] Depth) Picture(bool lit)
            {
                Int("use_light", lit ? 1 : 0);
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GraphicsApi.Color4(.2f, .5f, .8f, 1); GraphicsApi.Normal3(0f, 0f, 1f);
                GraphicsApi.Begin(PrimitiveType.Triangles);
                GraphicsApi.TexCoord3(0f, 0f, 0f); GraphicsApi.Vertex3(-.6f, -.6f, -.2f);
                GraphicsApi.Vertex3(.6f, -.6f, -.2f); GraphicsApi.Vertex3(0f, .6f, -.2f); GraphicsApi.End();
                var color = new byte[Width * Height * 4]; var depth = new float[Width * Height];
                GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, color);
                GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.DepthComponent, PixelType.Float, depth);
                return (color, depth);
            }
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "unlit setup generates no GL errors");
            Lighting(false); var unlit = Picture(false); var litReference = Picture(true);
            Int("use_emissive_map", 0); var withoutEmission = Picture(false); Int("use_emissive_map", 1);
            Require(!unlit.Color.SequenceEqual(withoutEmission.Color), "unlit omission fixture exercises emissive-map contribution");
            Lighting(true); var omitted = Picture(false);
            Require(unlit.Color.SequenceEqual(omitted.Color), "unlit stale lighting/material RGB uniforms preserve exact RGBA including emissive map");
            Require(unlit.Depth.SequenceEqual(omitted.Depth) && unlit.Depth.Any(d => d < 1), "unlit omission preserves exact covered depth");
            var staleLit = Picture(true);
            Require(!staleLit.Color.SequenceEqual(litReference.Color), "lit fixture actually consumes omitted lighting state");
            Lighting(false); var restoredLit = Picture(true);
            Require(restoredLit.Color.SequenceEqual(litReference.Color) && restoredLit.Depth.SequenceEqual(litReference.Depth),
                "unlit to lit transition with restored uniforms preserves exact RGBA/depth");
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "unlit baseline generates no GL errors");
            nativeFragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
            GraphicsApi.ShaderSource(nativeFragment, Shaders.NativeFragmentShader);
            GraphicsApi.CompileShader(nativeFragment);
            nativeProgram = GraphicsApi.CreateProgram();
            GraphicsApi.AttachShader(nativeProgram, vertex); GraphicsApi.AttachShader(nativeProgram, nativeFragment);
            GraphicsApi.LinkProgram(nativeProgram);
            GraphicsApi.GetProgram(nativeProgram, GetProgramParameterName.LinkStatus, out int nativeLinked);
            Require(nativeLinked != 0, "native material variant links: " + GraphicsApi.GetProgramInfoLog(nativeProgram));
            plainFragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
            GraphicsApi.ShaderSource(plainFragment, Shaders.NativeNoCosmeticsFragmentShader);
            GraphicsApi.CompileShader(plainFragment);
            plainProgram = GraphicsApi.CreateProgram();
            GraphicsApi.AttachShader(plainProgram, vertex); GraphicsApi.AttachShader(plainProgram, plainFragment);
            GraphicsApi.LinkProgram(plainProgram);
            GraphicsApi.GetProgram(plainProgram, GetProgramParameterName.LinkStatus, out int plainLinked);
            Require(plainLinked != 0, "plain native material variant links: " + GraphicsApi.GetProgramInfoLog(plainProgram));
            texel[3] = 128;
            NativeGL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texel);
            // Reapply all fixture state by each program's own locations. Three
            // cases cover alpha/textures, lit fog, and cosmetic processing.
            (byte[] Color, float[] Depth) Variant(int selected, int variantCase)
            {
                program = selected; GraphicsApi.UseProgram(program);
                foreach (string name in new[] { "proj_mtx", "view_mtx", "view_inv_mtx", "tex_mtx", "mtx_stack[0]" }) Matrix(name);
                Int("show_colors", 1); Int("mat_mode", variantCase == 1 ? 1 : 0);
                Int("advanced_materials", 0); Int("use_texture", 1); Int("tex", 0);
                GraphicsApi.Uniform1(Loc("mat_alpha"), .7f);
                Lighting(false);
                Int("fog_enable", variantCase == 1 ? 1 : 0);
                GraphicsApi.Uniform4(Loc("fog_color"), .1f, .2f, .3f, 1f);
                GraphicsApi.Uniform1(Loc("fog_min"), 0f); GraphicsApi.Uniform1(Loc("fog_max"), 1f);
                Int("cosmetic_skin", variantCase == 2 ? 1 : 0);
                Int("cosmetic_effect", variantCase == 2 ? 1 : 0);
                Vec("cosmetic_primary", new Vector3(.6f, .2f, .4f));
                Vec("cosmetic_secondary", new Vector3(.2f, .7f, .3f));
                GraphicsApi.Uniform1(Loc("cosmetic_intensity"), .5f);
                GraphicsApi.Uniform1(Loc("cosmetic_time"), .25f);
                return Picture(variantCase == 1);
            }
            for (int variantCase = 0; variantCase < 3; variantCase++)
            {
                var full = Variant(fullProgram, variantCase);
                var native = Variant(nativeProgram, variantCase);
                Require(full.Color.SequenceEqual(native.Color) && full.Depth.SequenceEqual(native.Depth)
                    && full.Depth.Any(d => d < 1), $"native/full exact color and depth parity case {variantCase}");
                if (variantCase != 2)
                {
                    var plain = Variant(plainProgram, variantCase);
                    Require(full.Color.SequenceEqual(plain.Color) && full.Depth.SequenceEqual(plain.Depth),
                        $"plain/native/full exact color and depth parity without cosmetics case {variantCase}");
                }
            }
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "unlit omission fixture generates no GL errors");
        }
        finally
        {
            GraphicsApi.UseProgram(0); GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0); GraphicsApi.DeleteTexture(texture);
            GraphicsApi.DeleteProgram(fullProgram);
            if (nativeProgram != 0) GraphicsApi.DeleteProgram(nativeProgram);
            if (nativeFragment != 0) GraphicsApi.DeleteShader(nativeFragment);
            if (plainProgram != 0) GraphicsApi.DeleteProgram(plainProgram);
            if (plainFragment != 0) GraphicsApi.DeleteShader(plainFragment);
            GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
        }
    }

    private static void CheckDepthReplaySnapshot()
    {
        if (!OpenGlContextPolicy.Current.FramebufferBlit) return;
        int framebuffer = GraphicsApi.GetInteger(GetPName.FramebufferBinding);
        long before = DepthReplaySnapshot.ResidentBytes;
        var snapshot = new DepthReplaySnapshot();
        float[] Depth()
        {
            var data = new float[Width * Height];
            GraphicsApi.ReadPixels(0, 0, Width, Height, PixelFormat.DepthComponent, PixelType.Float, data);
            return data;
        }
        byte[] Read(PixelFormat format, int channels)
        {
            var data = new byte[Width * Height * channels];
            GraphicsApi.ReadPixels(0, 0, Width, Height, format, PixelType.UnsignedByte, data);
            return data;
        }
        try
        {
            ResetPass(); GraphicsApi.UseProgram(0);
            GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.StencilMask(0xff);
            DrawQuad(-.7f, -.6f, .7f, .6f, -.3f, .3f, .6f, .2f);
            var referenceDepth = Depth();
            Require(referenceDepth.Any(d => d < 1), "depth snapshot fixture contains covered samples");
            Require(snapshot.Capture(framebuffer, Width, Height), "depth snapshot captures native D24S8 depth");
            long allocated = DepthReplaySnapshot.ResidentBytes;
            Require(allocated == before + Width * Height * 4L, "depth snapshot accounts bounded resident bytes");
            Require(!snapshot.Restore(framebuffer, Width + 1, Height), "wrong-size snapshot cannot restore");
            NativeGL.ClearDepth(.9); GraphicsApi.ClearStencil(73);
            GraphicsApi.ClearColor(.2f, .4f, .7f, .8f);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            GraphicsApi.Normal3(.17f, .31f, .53f); GraphicsApi.TexCoord3(.23f, .47f, .71f);
            var normalBefore = new float[3]; var uvBefore = new float[4];
            NativeGL.GetFloat(GetPName.CurrentNormal, normalBefore); NativeGL.GetFloat(GetPName.CurrentTextureCoords, uvBefore);
            var changedColor = Read(PixelFormat.Rgba, 4); var changedStencil = Read(PixelFormat.StencilIndex, 1);
            Require(!referenceDepth.SequenceEqual(Depth()) && changedStencil.All(s => s == 73), "snapshot fixture changes depth and stencil independently");
            Require(snapshot.Restore(framebuffer, Width, Height), "depth-only snapshot restores successfully");
            Require(referenceDepth.SequenceEqual(Depth()), "snapshot restoration preserves exact float depth");
            var normalAfter = new float[3]; var uvAfter = new float[4];
            NativeGL.GetFloat(GetPName.CurrentNormal, normalAfter); NativeGL.GetFloat(GetPName.CurrentTextureCoords, uvAfter);
            Require(normalBefore.SequenceEqual(normalAfter) && uvBefore.SequenceEqual(uvAfter),
                "depth snapshot mechanics preserve current normal and 3D UV rather than emulate replay writes");
            Require(changedColor.SequenceEqual(Read(PixelFormat.Rgba, 4))
                && changedStencil.SequenceEqual(Read(PixelFormat.StencilIndex, 1)), "depth restoration preserves current color and stencil winners");
            Require(GraphicsApi.GetInteger(GetPName.ReadFramebufferBinding) == framebuffer
                && GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding) == framebuffer, "snapshot restores source read/draw framebuffer bindings");
            Require(snapshot.Capture(framebuffer, Width, Height) && DepthReplaySnapshot.ResidentBytes == allocated,
                "same-size capture reuses snapshot storage");
            snapshot.End(); Require(!snapshot.Restore(framebuffer, Width, Height), "ended snapshot cannot replay stale depth");
            Require(!snapshot.Capture(framebuffer, 8192, 8192), "oversized snapshot falls back without allocation");
            Require(snapshot.Capture(framebuffer, Width / 2, Height / 2)
                && DepthReplaySnapshot.ResidentBytes == before + Width / 2 * (Height / 2) * 4L,
                "resized capture replaces allocation with exact smaller storage");
            Require(snapshot.Capture(framebuffer, Width, Height) && DepthReplaySnapshot.ResidentBytes == allocated,
                "resize back restores bounded full-size storage");
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "depth snapshot fixture generates no GL errors");
        }
        finally
        {
            snapshot.Dispose(); Require(DepthReplaySnapshot.ResidentBytes == before, "snapshot disposal releases resident bytes");
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            NativeGL.ClearDepth(1.0); GraphicsApi.ClearStencil(0); GraphicsApi.ClearColor(0, 0, 0, 0);
            GraphicsApi.StencilMask(0xff); ResetPass();
        }
    }

    private static void Render(string output)
    {
        int framebuffer = GraphicsApi.GenFramebuffer();
        int color = GraphicsApi.GenTexture();
        int depthStencil = GraphicsApi.GenRenderbuffer();
        try
        {
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                PixelInternalFormat.Rgba8, Width, Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthStencil);
            GraphicsApi.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                RenderbufferStorage.Depth24Stencil8, Width, Height);
            GraphicsApi.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, depthStencil);
            Require(GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                == FramebufferErrorCode.FramebufferComplete,
                "depth/stencil framebuffer incomplete");

            CheckDepthReplaySnapshot();
            Require(GraphicsApi.GetError() == ErrorCode.NoError, "snapshot cleanup generates no GL errors");
            CheckUnlitLightingOmission();
            CheckModernMaterialCorrections(output);
            CheckProceduralStream();
            CheckDepthVertexSpecialization();
            CheckDisplayListAttributeMetadata();
            CheckMatrixArrayRestore();
            CheckRetainedVertexArrayState(output);
            CheckCharacterSourceGx();

            // A red wall in front of a green pickup. Only portions of the
            // green geometry peeking around wall edges may reach the image.
            ResetPass();
            DrawQuad(-.25f, -1f, .25f, 1f, -.6f, 1, 0, 0);
            DrawQuad(-.5f, -.4f, .5f, .4f, .4f, 0, 1, 0);
            byte[] wall = Snapshot(output, "opaque-wall");
            PixelIs(wall, 32, 32, (r,g,b) => r > 245 && g < 10 && b < 10,
                "pickup must not bleed through opaque wall");
            PixelIs(wall, 20, 32, (r,g,b) => g > 245 && r < 10 && b < 10,
                "pickup must remain visible beside edge");

            // Check the same draw twice, catching retained stale depth/color
            // state and frame-to-frame ghosting in the synthetic path.
            ResetPass();
            DrawQuad(-.25f, -1f, .25f, 1f, -.6f, 1, 0, 0);
            DrawQuad(-.5f, -.4f, .5f, .4f, .4f, 0, 1, 0);
            byte[] repeat = Snapshot(output, "opaque-wall-repeat");
            Require(wall.SequenceEqual(repeat),
                "repeated world frame changed without any scene changes");

            // Glass is not an opaque occluder: the behind-surface pickup
            // must contribute to the blended output at the same pixel.
            ResetPass();
            DrawQuad(-.5f, -.5f, .5f, .5f, .4f, 0, 1, 0);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha,
                BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.DepthMask(false);
            DrawQuad(-.25f, -.7f, .25f, .7f, -.6f, 0, 0, 1, .5f);
            byte[] glass = Snapshot(output, "translucent-window");
            PixelIs(glass, 32, 32,
                (r,g,b) => r < 15 && g > 90 && b > 90,
                "see-through glass lost the behind-surface pickup");

            // Stencil policy: block the center of a front item while
            // allowing identical depth elsewhere on the same render surface.
            ResetPass();
            DrawQuad(-1, -1, 1, 1, .7f, 1, 0, 0);
            GraphicsApi.Enable(EnableCap.StencilTest);
            GraphicsApi.ColorMask(false, false, false, false);
            GraphicsApi.DepthMask(false);
            GraphicsApi.StencilFunc(StencilFunction.Always, 1, 0xFF);
            GraphicsApi.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            DrawQuad(-.25f, -.25f, .25f, .25f, -.7f, 1, 1, 1);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.DepthMask(true);
            GraphicsApi.StencilMask(0);
            GraphicsApi.StencilFunc(StencilFunction.Equal, 0, 0xFF);
            GraphicsApi.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            DrawQuad(-.5f, -.5f, .5f, .5f, .25f, 0, 1, 0);
            byte[] stencil = Snapshot(output, "stencil-mask");
            PixelIs(stencil, 32, 32, (r,g,b) => r > 245 && g < 10,
                "stencil-masked pickup leaked through center");
            PixelIs(stencil, 20, 32, (r,g,b) => g > 245 && r < 10,
                "stencil geometry outside mask must stay visible");

            // The same geometry is drawn through native display lists in
            // -gllegacylist mode, and a single merged indexed draw by
            // default. The A/B fixture comparator is pixel-exact.
            ResetPass();
            int firstList = GraphicsApi.GenLists(2);
            try
            {
                CompileColorQuad(firstList, -.8f, 0f, 1f, 0f);
                CompileColorQuad(firstList + 1, 0f, .8f, 0f, 1f);
                if (DesktopRetainedGeometry.BatchEnabled)
                {
                    GraphicsApi.BeginScopedWorldStateElision();
                    try
                    {
                        Require(GraphicsApi.TryDrawRetainedBatch(
                            new[] { firstList, firstList + 1 }, 2),
                            "static opaque geometry failed indexed batch promotion");
                    }
                    finally { GraphicsApi.EndScopedWorldStateElision(); }
                }
                else
                {
                    GraphicsApi.CallList(firstList);
                    GraphicsApi.CallList(firstList + 1);
                }
                byte[] combined = Snapshot(output, "combined-opaque");
                PixelIs(combined, 16, 32, (r,g,b) => r > 240 && g < 10,
                    "left merged draw lost vertex color");
                PixelIs(combined, 48, 32, (r,g,b) => g > 240 && r < 10,
                    "right merged draw lost vertex color");
            }
            finally
            {
                GraphicsApi.DeleteLists(firstList, 2);
            }
        }
        finally
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteRenderbuffer(depthStencil);
            GraphicsApi.DeleteTexture(color);
            GraphicsApi.DeleteFramebuffer(framebuffer);
        }
    }
}
#endif
