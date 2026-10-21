using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Formats;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using NMatrix = System.Numerics.Matrix4x4;
#if !ANDROID && !MPHREAD_SERVER
using NativeGL = OpenTK.Graphics.OpenGL.GL;
#endif

namespace MphRead.Mods.Render.Characters
{
    // Immutable Scene-owned program+resident bindings. Authority remains with
    // the existing attachment/lease and preview instance which submits it.
    internal sealed record CharacterSourceGxBinding(Scene Owner, CharacterSourceGxProgram Program,
        int ProgramId, IReadOnlyList<int> Textures);
    internal sealed record CharacterSourceGxDraw(CharacterSourceGxBinding Binding, double Seconds,
        CharacterSourceBodyOwner? BodyOwner = null, bool ViewModel = false);
}

namespace MphRead
{
    public partial class Scene
    {
        private sealed class SourceGxGpuProgram
        {
            internal readonly int Id, Projection, View, Skin;
            internal readonly int[] Primary, Post, Samplers;
            internal SourceGxGpuProgram(int id)
            {
                Id = id;
                int U(string name) => GL.GetUniformLocation(id, name);
                Projection = U("gx_projection"); View = U("gx_view"); Skin = U("gx_skin[0]");
                Primary = Enumerable.Range(0, 9).Select(i => U($"gx_primary[{i}]")).ToArray();
                Post = Enumerable.Range(0, 9).Select(i => U($"gx_post[{i}]")).ToArray();
                Samplers = Enumerable.Range(0, 4).Select(i => U($"gx_tex{i}")).ToArray();
                if (Projection < 0 || View < 0 || Skin < 0) throw new InvalidOperationException("Incomplete source shader geometry contract.");
            }
        }
        private readonly Dictionary<string, SourceGxGpuProgram> _sourceGxPrograms = new(StringComparer.Ordinal);
        private static readonly TextureSamplerDescriptor SourceGxSampling = new(true, true, false, 1, 0);
        private Matrix4? _sourceGxPreviewView, _sourceGxPreviewProjection;
        internal CharacterSourceGxDraw? SourceGxSubmission { get; set; }
        private readonly List<RenderItem> _sourceBodyItems = new();
        private bool _drawingSourceBody, _sourceBodyDepthOnly;

        private void DrawSourceBodyPass(bool opaque, bool depthOnly)
        {
#if !MPHREAD_SERVER && !ANDROID
            _drawingSourceBody = true; _sourceBodyDepthOnly = depthOnly;
            try
            {
                foreach (var item in _sourceBodyItems)
                    if (item.SourceGx is { BodyOwner: { } owner } draw
                        && (draw.ViewModel ? owner.CannonCurrent : owner.Current)
                        && (draw.Binding.Program.DrawPass == CharacterSourceGxProgram.Pass.Opaque) == opaque)
                        DrawSourceGxRenderItem(item);
            }
            finally { _drawingSourceBody = false; _sourceBodyDepthOnly = false; }
#endif
        }

        internal IReadOnlyList<CharacterSourceGxBinding> PrepareSourceGxMaterials(CharacterSourceGxMaterials materials)
        {
#if MPHREAD_SERVER || ANDROID
            // Source-rig authority is currently desktop-private only. GLES
            // shader text is generated/testable, not an Android draw capability.
            throw new NotSupportedException("Source body rendering is desktop-private only.");
#else
            if (GL.GetInteger(GetPName.MaxTextureImageUnits) < 4
                || GL.GetInteger(GetPName.MaxVertexUniformComponents) < 32 * 16 + 18 * 4 + 32
                || GL.GetInteger(GetPName.MaxVaryingFloats) < 4 * 3 + 4)
                throw new NotSupportedException("Context cannot carry the complete source body shader profile.");
            int oldProgram = GL.GetInteger(GetPName.CurrentProgram), oldUnit = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            int oldBinding = GL.GetInteger(GetPName.TextureBinding2D);
            int oldUnpack = GL.GetInteger(GetPName.UnpackAlignment), oldRowLength = GL.GetInteger(GetPName.UnpackRowLength),
                oldSkipRows = GL.GetInteger(GetPName.UnpackSkipRows), oldSkipPixels = GL.GetInteger(GetPName.UnpackSkipPixels);
            int oldUnpackBuffer = NativeGL.GetInteger(GetPName.PixelUnpackBufferBinding);
            var created = new List<string>();
            try
            {
                NativeGL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
                GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
                // Admission is complete before publishing any body segment.
                var bindings = new Dictionary<uint, int>();
                foreach (var texture in materials.Archive.Textures)
                {
                    var image = materials.Images[texture.ResourceId];
                    string key = materials.Policy + "/" + texture.RgbaSha256 + "/" + texture.Width + "x" + texture.Height;
                    int binding = CharacterTextureAssets.UploadRgba(key, texture.Width, texture.Height, image.Pixels.ToArray(), SourceGxSampling);
                    if (binding == 0) throw new InvalidOperationException("Complete body source texture upload failed.");
                    bindings.Add(texture.ResourceId, binding); _characterPinnedBindings.Add(binding);
                }
                var result = new List<CharacterSourceGxBinding>();
                foreach (var material in materials.Programs)
                {
#if ANDROID
                    string vertexSource = material.Vertex300, fragmentSource = material.Fragment300;
#else
                    string vertexSource = material.Vertex120, fragmentSource = material.Fragment120;
#endif
                    string key = vertexSource + fragmentSource;
                    if (!_sourceGxPrograms.TryGetValue(key, out var gpu))
                    {
                        if (_sourceGxPrograms.Count >= 16) throw new InvalidOperationException("Source body program budget exceeded.");
                        int vertex = 0, fragment = 0, program = 0;
                        try
                        {
                            vertex = CompileSceneShader(ShaderType.VertexShader, vertexSource, "source-body-gx-vertex");
                            fragment = CompileSceneShader(ShaderType.FragmentShader, fragmentSource, "source-body-gx-fragment");
                            program = LinkSceneProgram(vertex, fragment, "source-body-gx-program");
                            gpu = new(program); _sourceGxPrograms.Add(key, gpu); created.Add(key); program = 0;
                        }
                        finally
                        {
                            if (program != 0) GL.DeleteProgram(program);
                            if (fragment != 0) GL.DeleteShader(fragment);
                            if (vertex != 0) GL.DeleteShader(vertex);
                        }
                    }
                    result.Add(new(this, material, gpu.Id, Array.AsReadOnly(material.Textures.Select(t => bindings[t]).ToArray())));
                }
                return result.AsReadOnly();
            }
            catch
            {
                foreach (string key in created) { GL.DeleteProgram(_sourceGxPrograms[key].Id); _sourceGxPrograms.Remove(key); }
                // Fixed, bounded image residents remain reusable until Scene
                // retirement. A failed admission never publishes draw authority.
                throw;
            }
            finally
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, oldUnpack);
                GL.PixelStore(PixelStoreParameter.UnpackRowLength, oldRowLength);
                GL.PixelStore(PixelStoreParameter.UnpackSkipRows, oldSkipRows);
                GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, oldSkipPixels);
                NativeGL.BindBuffer(BufferTarget.PixelUnpackBuffer, oldUnpackBuffer);
                GL.UseProgram(oldProgram); GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, oldBinding); GL.ActiveTexture((TextureUnit)oldUnit);
            }
#endif
        }

        private void StampSourceGxSubmission(RenderItem item)
        {
            if (SourceGxSubmission is not { } draw) return;
            bool body = !_collectingPreview && draw.ViewModel == _collectingViewModelItems
                && (draw.ViewModel ? draw.BodyOwner?.CannonCurrent == true : draw.BodyOwner?.Current == true);
            if ((!_collectingPreview && !body) || !item.WeightedSkinning || item.Type != RenderItemType.Mesh
                || !ReferenceEquals(draw.Binding.Owner, this)) throw new InvalidOperationException("Source GX requires its existing weighted preview owner.");
            _ = CharacterSourceGxProgram.Clock(draw.Seconds);
            item.SourceGx = draw;
            // A native draw anchor supplies neither alpha nor material authority.
            item.Alpha = 1; item.Lighting = false; item.Wireframe = false;
            item.PolygonMode = PolygonMode.Modulate;
            item.RenderMode = draw.Binding.Program.DrawPass == CharacterSourceGxProgram.Pass.Opaque ? RenderMode.Normal : RenderMode.Translucent;
            item.CullingMode = CullingMode.Neither; item.Cosmetics = default; item.CosmeticMaterial = default;
            item.OverrideColor = null; item.PaletteOverride = null; item.TexturedPlayerSkin = false; item.PlayerOutlineColor = null;
        }

        private void DrawSourceGxRenderItem(RenderItem item)
        {
#if MPHREAD_SERVER || ANDROID
            // Source-rig authority is currently desktop-private only. GLES
            // shader text is generated/testable, not an Android draw capability.
            throw new NotSupportedException("Source body rendering is desktop-private only.");
#else
            var draw = item.SourceGx!; var binding = draw.Binding;
            bool body = draw.BodyOwner != null;
            bool current = draw.ViewModel ? draw.BodyOwner?.CannonCurrent == true : draw.BodyOwner?.Current == true;
            Matrix4? selectedView = body ? _drawingSourceBody && current ? _viewMatrix : null : _sourceGxPreviewView;
            Matrix4? selectedProjection = body ? _drawingSourceBody && draw.ViewModel == item.ViewModel
                ? item.ViewModel ? _viewModelPerspectiveMatrix : _perspectiveMatrix : null : _sourceGxPreviewProjection;
            if (!ReferenceEquals(binding.Owner, this) || selectedView is not { } view
                || selectedProjection is not { } projection || !item.WeightedSkinning
                || item.MatrixStackCount is < 1 or > 32)
                throw new InvalidOperationException("Current source preview camera, Scene and palette required.");
            var gpu = _sourceGxPrograms.Values.SingleOrDefault(p => p.Id == binding.ProgramId)
                ?? throw new InvalidOperationException("Source program owner retired.");
            int oldProgram = GL.GetInteger(GetPName.CurrentProgram), oldUnit = GL.GetInteger(GetPName.ActiveTexture);
            var oldTextures = new int[4];
            for (int i = 0; i < 4; i++) { GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i)); oldTextures[i] = GL.GetInteger(GetPName.TextureBinding2D); }
            GL.PushAttrib(AttribMask.AllAttribBits);
            try
            {
                GL.UseProgram(gpu.Id);
                GL.UniformMatrix4(gpu.Projection, false, ref projection); GL.UniformMatrix4(gpu.View, false, ref view);
                GL.UniformMatrix4(gpu.Skin, item.MatrixStackCount, false, item.MatrixStack);
                // Environment texgen takes raw converted-object normals. The
                // PN-skinned normal is used only for the named preview light.
                // Presentation is already in gx_skin for positions, never twice.
                Matrix4 normal = Matrix4.Transpose(Matrix4.Invert(item.Transform * view));
                var matrices = binding.Program.Matrices(draw.Seconds, ToNumerics(normal));
                UploadRows(gpu.Primary, matrices.Primary); UploadRows(gpu.Post, matrices.Post);
                for (int i = 0; i < binding.Textures.Count; i++)
                {
                    GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i));
                    GL.BindTexture(TextureTarget.Texture2D, binding.Textures[i]);
                    GL.Uniform1(gpu.Samplers[i], i);
                }
                GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Lequal);
                bool opaque = binding.Program.DrawPass == CharacterSourceGxProgram.Pass.Opaque;
                GL.DepthMask(body ? opaque : binding.Program.SourceDepthWrite && !binding.Program.SourceDepthSorting);
                if (body)
                {
                    GL.ColorMask(!_sourceBodyDepthOnly, !_sourceBodyDepthOnly, !_sourceBodyDepthOnly, !_sourceBodyDepthOnly);
                    GL.Disable(EnableCap.StencilTest);
                }
                GL.Disable(EnableCap.AlphaTest); GL.Disable(EnableCap.CullFace);
                GL.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
                if (opaque) GL.Disable(EnableCap.Blend);
                else
                {
                    GL.Enable(EnableCap.Blend);
                    if (binding.Program.DrawPass == CharacterSourceGxProgram.Pass.Additive) GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                    else GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }
                DrawRenderItemGeometry(item, null);
            }
            finally
            {
                GL.PopAttrib();
                for (int i = 0; i < 4; i++) { GL.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i)); GL.BindTexture(TextureTarget.Texture2D, oldTextures[i]); }
                GL.ActiveTexture((TextureUnit)oldUnit); GL.UseProgram(oldProgram);
            }
#endif
        }
        private static NMatrix ToNumerics(Matrix4 m) => new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
        private static void UploadRows(int[] locations, NMatrix[] matrices)
        {
            for (int i = 0; i < 3; i++)
            {
                var m = matrices[i];
                GL.Uniform4(locations[i*3],m.M11,m.M21,m.M31,m.M41);
                GL.Uniform4(locations[i*3+1],m.M12,m.M22,m.M32,m.M42);
                GL.Uniform4(locations[i*3+2],m.M13,m.M23,m.M33,m.M43);
            }
        }
        private void ReleaseSourceGxPrograms()
        {
            int current = GL.GetInteger(GetPName.CurrentProgram);
            if (_sourceGxPrograms.Values.Any(p => p.Id == current)) GL.UseProgram(0);
            foreach (var p in _sourceGxPrograms.Values) GL.DeleteProgram(p.Id);
            _sourceGxPrograms.Clear(); SourceGxSubmission = null;
            _sourceBodyItems.Clear(); _drawingSourceBody = _sourceBodyDepthOnly = false;
            _sourceGxPreviewView = _sourceGxPreviewProjection = null;
        }
    }
}
