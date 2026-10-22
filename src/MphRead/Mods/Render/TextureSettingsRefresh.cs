using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MphRead;

public partial class Scene
{
    [Flags]
    private enum TextureRefreshWork { None = 0, Rebind = 1, Sampling = 2, Companions = 4 }
    private readonly record struct TextureRefreshState(Mods.TextureUpscaleMode Upscale,
        Mods.TextureAssetQuality Quality, string SamplingKey, bool Replacements, bool Advanced,
        int Revision, Mods.Render.TextureSamplerDescriptor NativeSampling)
    {
        internal static TextureRefreshState Capture() => new(Mods.RenderOptions.TextureUpscale,
            Mods.RenderOptions.TextureQuality, Mods.Render.TextureSamplingPolicy.RuntimeKey,
            Mods.RenderOptions.TextureReplacements, Mods.RenderOptions.AdvancedMaterials,
            Mods.Render.TextureReplacementPack.Revision, Mods.Render.TextureSamplingPolicy.ResolveNativeWorld());
        internal bool SameImages(TextureRefreshState other) => Upscale == other.Upscale && Quality == other.Quality
            && Replacements == other.Replacements && Revision == other.Revision
            && (!Replacements || SamplingKey == other.SamplingKey);
    }
    private readonly record struct TextureRefreshJob(int Binding, AuthoredTextureRefresh? Authored);
    private readonly Queue<TextureRefreshJob> _textureSettingsJobs = new();
    private readonly Dictionary<int, Mods.Render.TextureSamplerDescriptor> _textureUploadSampling = new();
    private readonly Dictionary<int, Mods.Render.TextureSamplerDescriptor> _deferredTextureSampling = new();
    private TextureRefreshState _desiredTextureRefresh = TextureRefreshState.Capture();
    private TextureRefreshWork _textureRefreshWork;
    private int _textureSettingsGeneration, _appliedTextureSettingsGeneration;
    private long _textureSettingsJobsCompleted, _textureSettingsUploadBytes;
    private double _worstTextureSettingsJobMs;
    internal Mods.TextureUpscaleMode AppliedTextureUpscale => _uploadedTextureUpscale;
    internal Mods.TextureAssetQuality AppliedTextureAssetQuality => _uploadedTextureAssetQuality;
    internal string AppliedTextureSamplingKey => _uploadedTextureSamplingKey;
    internal bool AppliedTextureReplacements => _uploadedTextureReplacements;
    internal bool AppliedAdvancedMaterials => _uploadedAdvancedMaterials;
    internal int AppliedMaterialRevision => _uploadedMaterialRevision;
    internal int PendingTextureSettingsJobs => _textureSettingsJobs.Count;
    internal int DesiredTextureSettingsGeneration => _textureSettingsGeneration;
    internal int AppliedTextureSettingsGeneration => _appliedTextureSettingsGeneration;
    internal long TextureSettingsJobsCompleted => _textureSettingsJobsCompleted;
    internal long TextureSettingsUploadBytes => _textureSettingsUploadBytes;
    internal double WorstTextureSettingsJobMs => _worstTextureSettingsJobMs;

    private void RefreshTextureQuality()
    {
        if (Mods.Headless.Active) return;
        TextureRefreshState desired = TextureRefreshState.Capture();
        if (desired != _desiredTextureRefresh)
        {
            TextureRefreshWork work = _textureSettingsJobs.Count == 0 ? TextureRefreshWork.None : _textureRefreshWork;
            if (!desired.SameImages(_desiredTextureRefresh)) work |= TextureRefreshWork.Rebind;
            if (desired.NativeSampling != _desiredTextureRefresh.NativeSampling) work |= TextureRefreshWork.Sampling;
            if (desired.Replacements && desired.Advanced != _desiredTextureRefresh.Advanced)
                work |= TextureRefreshWork.Companions;
            _desiredTextureRefresh = desired;
            _textureRefreshWork = work;
            _textureSettingsGeneration++;
            _textureSettingsJobs.Clear();
            if (work != TextureRefreshWork.None)
            {
                // Snapshot identity only. No decode/upload occurs while gathering
                // a new desired generation, and a newer request coalesces this one.
                foreach (int binding in _textureSources.Keys)
                {
                    FreezeTextureSampling(binding);
                    if (_materialMaps.TryGetValue(binding, out var maps))
                    {
                        FreezeTextureSampling(maps.Normal); FreezeTextureSampling(maps.Specular); FreezeTextureSampling(maps.Emissive);
                    }
                    _textureSettingsJobs.Enqueue(new(binding, null));
                }
                if ((work & TextureRefreshWork.Rebind) != 0)
                    foreach (Model model in _textureSources.Values.Select(source => source.Model).Distinct().ToArray())
                        if (_texPalMap.TryGetValue(model.Id, out var map))
                            foreach (var authored in MissingAuthoredTextures(model, map))
                                _textureSettingsJobs.Enqueue(new(0, authored));
            }
        }
        bool uploaded = PumpTextureSettingsJob();
        // Share the existing promotion slot: a settings reupload never stacks
        // another completed HD image upload onto the same picture.
        PumpModernTextureStreamingCore(allowPromotion: !uploaded);
        if (_textureSettingsJobs.Count == 0)
        {
            _appliedTextureSettingsGeneration = _textureSettingsGeneration;
            _textureRefreshWork = TextureRefreshWork.None;
            _uploadedTextureUpscale = desired.Upscale; _uploadedTextureAssetQuality = desired.Quality;
            _uploadedTextureSamplingKey = desired.SamplingKey; _uploadedTextureReplacements = desired.Replacements;
            _uploadedAdvancedMaterials = desired.Advanced; _uploadedMaterialRevision = desired.Revision;
        }
    }

    private void FreezeTextureSampling(int binding)
    {
        if (binding == 0 || _deferredTextureSampling.ContainsKey(binding)) return;
        if (_appliedTextureSampling.TryGetValue(binding, out var applied))
            _deferredTextureSampling[binding] = applied.Sampling;
        else if (_textureUploadSampling.TryGetValue(binding, out var uploaded))
            _deferredTextureSampling[binding] = uploaded with {
                // A never-drawn native upload can still owe its old lazy mip
                // work. Freezing its requested recipe must not generate that
                // work while this binding is waiting for its scheduled job.
                Mipmaps = uploaded.Mipmaps && (_modernTextureSampling.ContainsKey(binding)
                    || _mipmappedTextures.Contains(binding)) };
        else _deferredTextureSampling[binding] = ResolveResidentTextureSampling(binding);
    }

    private Mods.Render.TextureSamplerDescriptor ResolveResidentTextureSampling(int binding)
    {
        if (_deferredTextureSampling.TryGetValue(binding, out var frozen)) return frozen;
        if (_modernTextureSampling.TryGetValue(binding, out var modern))
        {
            var sampling = Mods.Render.TextureSamplingPolicy.ResolveModern(modern.AssetClass, modern.Channel);
            return sampling with { Mipmaps = sampling.Mipmaps && modern.MipmapsAvailable };
        }
        return Mods.Render.TextureSamplingPolicy.ResolveNativeWorld();
    }

    private void ThawTextureSampling(int binding)
    {
        _deferredTextureSampling.Remove(binding);
        if (_materialMaps.TryGetValue(binding, out var maps))
        {
            _deferredTextureSampling.Remove(maps.Normal); _deferredTextureSampling.Remove(maps.Specular);
            _deferredTextureSampling.Remove(maps.Emissive);
        }
    }

    private bool PumpTextureSettingsJob()
    {
        // Old map IDs can become stale together. Bound metadata retirement too,
        // rather than scanning an arbitrarily long cancelled queue in one frame.
        for (int examined = 0; examined < 16 && _textureSettingsJobs.TryDequeue(out var job); examined++)
        {
            if (job.Authored is { } authored)
            {
                if (!_texPalMap.TryGetValue(authored.Model.Id, out var map)
                    || map.Authored.ContainsKey((authored.Material, authored.Texture, authored.Palette, authored.Recolor))) continue;
            }
            else if (!_textureSources.ContainsKey(job.Binding)) continue;
            long start = Stopwatch.GetTimestamp();
            long bytes = 0;
            try
            {
                using var timing = Mods.Render.OpenGlPassProfiler.Measure(Mods.Render.OpenGlCpuPass.TextureUpload);
                GL.ActiveTexture(TextureUnit.Texture0);
                int binding = job.Binding;
                bool hadCompanions = _materialMaps.ContainsKey(binding);
                if (job.Authored is { } create)
                {
                    ApplyAuthoredTextureRefresh(create, _texPalMap[create.Model.Id]);
                    binding = _lastTextureId;
                }
                else
                {
                    ThawTextureSampling(binding);
                    if ((_textureRefreshWork & TextureRefreshWork.Rebind) != 0)
                    {
                        var source = _textureSources[binding];
                        BindTexture(source.Model, source.Texture, source.Palette, source.Recolor, binding, source.Authored);
                    }
                    else if ((_textureRefreshWork & TextureRefreshWork.Companions) != 0) RefreshAdvancedMaterialMap(binding);
                }
                // Pay first-use native mip work inside this one bounded job,
                // never on every still-pending texture's draw after a toggle.
                var sampling = ResolveResidentTextureSampling(binding);
                if (sampling.Mipmaps && !_mipmappedTextures.Contains(binding))
                {
                    GL.BindTexture(TextureTarget.Texture2D, binding);
                    GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                    _mipmappedTextures.Add(binding);
                }
                GL.BindTexture(TextureTarget.Texture2D, 0);
                if ((_textureRefreshWork & TextureRefreshWork.Rebind) != 0 || job.Authored.HasValue)
                {
                    if (_worldMaterialTextureBytes.TryGetValue(binding, out long resident)) bytes += resident;
                    else if (_textureSources.TryGetValue(binding, out var source))
                    {
                        Texture native = source.Model.Recolors[source.Recolor].Textures[source.Texture];
                        int factor = Mods.RenderOptions.TextureUpscaleFactor;
                        bytes += (long)native.Width * native.Height * factor * factor * 4;
                    }
                }
                if (((_textureRefreshWork & TextureRefreshWork.Rebind) != 0 || job.Authored.HasValue || !hadCompanions)
                    && _materialMaps.TryGetValue(binding, out var maps))
                    bytes += TextureResidencyBytes(maps.Normal) + TextureResidencyBytes(maps.Specular) + TextureResidencyBytes(maps.Emissive);
                _textureSettingsJobsCompleted++;
                _textureSettingsUploadBytes += bytes;
                Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.TextureSettingsJobs);
                Mods.Render.OpenGlPassProfiler.Count(Mods.Render.OpenGlFrameCounter.TextureSettingsUploadBytes, bytes);
            }
            finally { _worstTextureSettingsJobMs = Math.Max(_worstTextureSettingsJobMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
            return true;
        }
        return false;
    }

    private long TextureResidencyBytes(int binding) => _worldMaterialTextureBytes.GetValueOrDefault(binding);

    private void ClearTextureSettingsRefresh()
    {
        _textureSettingsJobs.Clear(); _deferredTextureSampling.Clear(); _textureUploadSampling.Clear();
        _textureRefreshWork = TextureRefreshWork.None;
    }
}
