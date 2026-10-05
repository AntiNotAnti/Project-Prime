#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    internal readonly record struct ModernUniformLayout(string Name, int Offset, int Components,
        int Count, int Stride, bool Integer);
    internal sealed record ModernShaderLayout(int Size, int FlipOffset,
        ModernUniformLayout[] Uniforms, string[] Samplers);

    internal sealed unsafe partial class ModernGraphicsCompat
    {
        private sealed class GeneratedBindGroupCacheEntry
        {
            internal BindGroup* Group;
            internal nint[] Resources = Array.Empty<nint>();
            internal long TextureRevision;
        }

        private sealed class GeneratedProgram
        {
            internal ShaderModule* Vertex;
            internal ShaderModule* Fragment;
            internal WgpuBuffer* UniformBuffer;
            internal ulong UniformOffset;
            internal ModernShaderLayout Layout = null!;
            internal uint[] Words = Array.Empty<uint>();
            internal ModernUniformLayout[] Members = Array.Empty<ModernUniformLayout>();
            internal int[] Textures = Array.Empty<int>();
            internal readonly List<GeneratedBindGroupCacheEntry> BindGroups = new();
            internal int BindGroupCursor;
        }

        private readonly Dictionary<ModernProgramKind, GeneratedProgram> _generatedPrograms = new();
        private NativeTexture? _fallbackDepth;

        private static bool UsesGeneratedShader(ModernProgramKind kind) =>
            kind is ModernProgramKind.World or ModernProgramKind.DeferredPbr
                or ModernProgramKind.DeferredPbrMrt or ModernProgramKind.PostProcess;

        internal static void ValidateGeneratedShaders()
        {
            Current.GeneratedShader(ModernProgramKind.World);
            Current.GeneratedShader(ModernProgramKind.DeferredPbr);
            Current.GeneratedShader(ModernProgramKind.DeferredPbrMrt);
            Current.GeneratedShader(ModernProgramKind.PostProcess);
            Current._device.ThrowIfFailed();
        }

        private GeneratedProgram GeneratedShader(ModernProgramKind kind)
        {
            if (_generatedPrograms.TryGetValue(kind, out var cached)) return cached;
            var layout = GeneratedShaderLayouts.Get(kind);
            var program = new GeneratedProgram { Layout = layout, Words = new uint[layout.Size / 4],
                Textures = new int[layout.Samplers.Length] };
            program.Members = layout.Uniforms.Concat(layout.Uniforms.Where(u => u.Count > 1)
                .SelectMany(u => Enumerable.Range(0, u.Count).Select(i =>
                    u with { Name = $"{u.Name}[{i}]", Offset = u.Offset + i * u.Stride, Count = 1 }))).ToArray();
            _generatedPrograms.Add(kind, program); // Partial initialization is owned by Dispose.
            program.Vertex = CreateWgslModule(ReadGeneratedShader(kind, "vertex"));
            program.Fragment = CreateWgslModule(ReadGeneratedShader(kind, "fragment"));
            _device.ThrowIfFailed();
            return program;
        }

        private static string ReadGeneratedShader(ModernProgramKind kind, string stage)
        {
            using Stream stream = typeof(ModernGraphicsCompat).Assembly.GetManifestResourceStream(
                $"MphRead.Mods.Render.Generated.{kind}.{stage}.wgsl")
                ?? throw new InvalidOperationException($"Missing generated shader {kind}/{stage}.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void PrepareGeneratedResources(ModernProgramKind kind, CoreTarget target)
        {
            GeneratedProgram generated = GeneratedShader(kind);
            ModernGraphicsCompatState.ProgramRecord program =
                _programs.Program(_programs.CurrentProgram);
            PopulateGeneratedUniformWords(kind, target, generated, program);
            PopulateGeneratedTextureBindings(kind, target, generated, program);
            UploadGeneratedUniformWords(generated);
        }

        private void PopulateGeneratedUniformWords(ModernProgramKind kind,
            CoreTarget target, GeneratedProgram generated,
            ModernGraphicsCompatState.ProgramRecord program)
        {
            Array.Clear(generated.Words);
            foreach (ModernUniformLayout uniform in generated.Members)
            {
                int at = uniform.Offset / 4;
                if (uniform.Name == "prime_viewport")
                {
                    var viewport = ViewportTransform(target.Width, target.Height);
                    generated.Words[at] = BitConverter.SingleToUInt32Bits(viewport.X);
                    generated.Words[at + 1] = BitConverter.SingleToUInt32Bits(viewport.Y);
                    generated.Words[at + 2] = BitConverter.SingleToUInt32Bits(viewport.Z);
                    generated.Words[at + 3] = BitConverter.SingleToUInt32Bits(viewport.W);
                    continue;
                }
                if (uniform.Name == "prime_imm_color")
                {
                    generated.Words[at] = BitConverter.SingleToUInt32Bits(_currentColor.X);
                    generated.Words[at + 1] = BitConverter.SingleToUInt32Bits(_currentColor.Y);
                    generated.Words[at + 2] = BitConverter.SingleToUInt32Bits(_currentColor.Z);
                    generated.Words[at + 3] = BitConverter.SingleToUInt32Bits(_currentColor.W);
                    continue;
                }
                if (uniform.Name == "prime_imm_normal")
                {
                    generated.Words[at] = BitConverter.SingleToUInt32Bits(_currentNormal.X);
                    generated.Words[at + 1] = BitConverter.SingleToUInt32Bits(_currentNormal.Y);
                    generated.Words[at + 2] = BitConverter.SingleToUInt32Bits(_currentNormal.Z);
                    continue;
                }
                if (uniform.Name == "prime_alpha_func")
                {
                    generated.Words[at] = (uint)(_enabled.Contains(
                        OpenTK.Graphics.OpenGL.EnableCap.AlphaTest)
                        ? _alphaFunction : OpenTK.Graphics.OpenGL.AlphaFunction.Always);
                    continue;
                }
                if (uniform.Name == "prime_alpha_ref")
                {
                    generated.Words[at] =
                        BitConverter.SingleToUInt32Bits(_alphaReference);
                    continue;
                }
                if (!program.Uniforms.TryGetValue(uniform.Name, out var value))
                    continue;
                if (uniform.Integer)
                {
                    generated.Words[at] = unchecked((uint)value.IntValue);
                }
                else if (value.Data != null)
                {
                    for (int element = 0; element < uniform.Count; element++)
                    for (int component = 0; component < uniform.Components; component++)
                    {
                        int source = element * uniform.Components + component;
                        if (source >= value.Data.Length) break;
                        generated.Words[
                            at + element * uniform.Stride / 4 + component] =
                            unchecked((uint)BitConverter.SingleToInt32Bits(
                                value.Data[source]));
                    }
                }
            }
        }

        private void PopulateGeneratedTextureBindings(ModernProgramKind kind,
            CoreTarget target, GeneratedProgram generated,
            ModernGraphicsCompatState.ProgramRecord program)
        {
            for (int i = 0; i < generated.Layout.Samplers.Length; i++)
            {
                string name = generated.Layout.Samplers[i];
                string? flag = name switch
                {
                    "tex" when kind is ModernProgramKind.DeferredPbr
                        or ModernProgramKind.DeferredPbrMrt
                        or ModernProgramKind.World => "use_texture",
                    "normal_tex" => "use_normal_map",
                    "specular_tex" => "use_specular_map",
                    "emissive_tex" => "use_emissive_map",
                    "depth_tex" => "depth_available",
                    "shadow_tex" => "shadow_enabled",
                    "history_tex" => "history_valid",
                    "pbr_albedo" or "pbr_normal" or "pbr_material" => "pbr_enabled",
                    _ => null
                };
                int unit = Int(program, name);
                bool required = flag == null || Int(program, flag) != 0;
                if (kind == ModernProgramKind.World
                    && name is "normal_tex" or "specular_tex" or "emissive_tex")
                {
                    required &= Int(program, "advanced_materials") != 0;
                }
                int id = ValidateRenderPassResources(unit, required, target);
                generated.Textures[i] = id;
                generated.Words[generated.Layout.FlipOffset / 4 + i * 4] =
                    unchecked((uint)BitConverter.SingleToInt32Bits(
                        _resources.IsFramebufferTexture(id) ? 1f : 0f));
            }
        }

        private void UploadGeneratedUniformWords(GeneratedProgram generated)
        {
            UniformAllocation allocation =
                RentUniformBuffer((ulong)generated.Layout.Size);
            generated.UniformBuffer = (WgpuBuffer*)allocation.Buffer;
            generated.UniformOffset = allocation.Offset;
            fixed (uint* words = generated.Words)
                WriteUniformBuffer(allocation, words,
                    (nuint)generated.Layout.Size);
        }

        private BindGroup* GeneratedBindGroup(ModernProgramKind kind, BindGroupLayout* layout)
        {
            var generated = GeneratedShader(kind);
            var entries = stackalloc BindGroupEntry[1 + generated.Textures.Length * 2];
            Span<nint> resources = stackalloc nint[3 + generated.Textures.Length * 2];
            resources[0] = (nint)layout;
            resources[1] = (nint)generated.UniformBuffer;
            resources[2] = (nint)generated.UniformOffset;

            entries[0] = new BindGroupEntry
            {
                Binding = 0, Buffer = generated.UniformBuffer,
                Offset = generated.UniformOffset, Size = (ulong)generated.Layout.Size
            };
            uint count = 1;
            for (int i = 0; i < generated.Textures.Length; i++)
            {
                bool depth = generated.Layout.Samplers[i] is "depth_tex" or "shadow_tex";
                NativeTexture? texture = generated.Textures[i] == 0 ? null : EnsureTexture(generated.Textures[i]);
                TextureView* view = texture != null ? texture.SampleView
                    : depth ? FallbackDepthView() : _whiteView;
                Silk.NET.WebGPU.Sampler* sampler = !depth
                    ? texture != null ? texture.Sampler : _whiteSampler : null;

                entries[count++] = new BindGroupEntry { Binding = (uint)(1 + i * 2), TextureView = view };
                if (!depth)
                    entries[count++] = new BindGroupEntry { Binding = (uint)(2 + i * 2), Sampler = sampler };

                int fingerprint = 3 + i * 2;
                resources[fingerprint] = (nint)view;
                resources[fingerprint + 1] = (nint)sampler;
            }

            int slot = generated.BindGroupCursor++;
            GeneratedBindGroupCacheEntry? cached = slot < generated.BindGroups.Count
                ? generated.BindGroups[slot] : null;
            if (cached != null && cached.Group != null
                && cached.TextureRevision == _textureBindingRevision
                && cached.Resources.AsSpan().SequenceEqual(resources))
            {
                return cached.Group;
            }

            if (cached != null && cached.Group != null)
                ReleaseTrackedBindGroup(cached.Group);

            BindGroup* group = CreateTrackedBindGroup(new BindGroupDescriptor
            {
                Layout = layout, Entries = entries, EntryCount = count
            });
            if (cached == null)
            {
                cached = new GeneratedBindGroupCacheEntry();
                generated.BindGroups.Add(cached);
            }
            cached.Group = group;
            cached.Resources = resources.ToArray();
            cached.TextureRevision = _textureBindingRevision;
            return group;
        }

        private TextureView* FallbackDepthView()
        {
            if (_fallbackDepth != null) return _fallbackDepth.SampleView;
            var texture = new NativeTexture { Format = WgpuTextureFormat.Depth24Plus, Width = 1, Height = 1, MipCount = 1 };
            texture.Texture = _api.DeviceCreateTexture(_device.Device, new TextureDescriptor
            {
                Size = new Extent3D(1, 1, 1), Format = texture.Format,
                Usage = TextureUsage.TextureBinding | TextureUsage.RenderAttachment,
                MipLevelCount = 1, SampleCount = 1, Dimension = TextureDimension.Dimension2D
            });
            texture.View = _api.TextureCreateView(texture.Texture, null);
            texture.SampleView = texture.View;
            _fallbackDepth = texture;
            return texture.View;
        }

        private void DisposeGeneratedShaders()
        {
            DisposeRetainedWorldBindGroups();
            DisposeRetainedPbrBindGroups();
            foreach (var program in _generatedPrograms.Values)
            {
                foreach (var cached in program.BindGroups)
                    if (cached.Group != null) ReleaseTrackedBindGroup(cached.Group);
                program.BindGroups.Clear();
                if (program.Vertex != null) _api.ShaderModuleRelease(program.Vertex);
                if (program.Fragment != null) _api.ShaderModuleRelease(program.Fragment);
            }
            _generatedPrograms.Clear();
            if (_fallbackDepth != null) ReleaseNativeTexture(_fallbackDepth);
            _fallbackDepth = null;
        }
    }
}
#endif
