#if MPHREAD_SHELL
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using MphRead.Mods;
namespace MphRead
{
    public partial class Scene
    {
        // Called with the real preview's context/materials loaded. Validate the
        // actual GPU texture dimensions, not just the saved setting values.
        internal void CheckLiveTextureQuality()
        {
            var originalScale = RenderOptions.TextureUpscale;
            bool originalReplacements = RenderOptions.TextureReplacements;
            bool originalAdvancedMaterials = RenderOptions.AdvancedMaterials;
            try
            {
                if (_textureSources.Count == 0) throw new InvalidOperationException("No texture sources to test");
                GL.UseProgram(_shaderProgramId);
                var active = new Mods.Cosmetics.CosmeticSurface(1, 6, 1, OpenTK.Mathematics.Vector3.One,
                    OpenTK.Mathematics.Vector3.One, 0.3f, 2, 1);
                int cosmeticEffect = GL.GetUniformLocation(_shaderProgramId, "cosmetic_effect");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    ApplyCosmeticUniforms(active);
                    GL.GetUniform(_shaderProgramId, cosmeticEffect, out int effect);
                    if (effect != 6) throw new InvalidOperationException("Cosmetic uniforms did not enable");
                    ApplyCosmeticUniforms(default); ApplyCosmeticUniforms(default);
                    GL.GetUniform(_shaderProgramId, cosmeticEffect, out effect);
                    if (effect != 0) throw new InvalidOperationException("Cosmetic uniforms remained active after Off");
                }

                // Reproduce the failure mode the world-pass boundary protects:
                // the cache says neutral while the actual program contains a
                // remote player's cosmetic value. A forced reset must repair it.
                ApplyCosmeticUniforms(default);
                GL.Uniform1(cosmeticEffect, 6);
                ResetCosmeticUniforms();
                GL.GetUniform(_shaderProgramId, cosmeticEffect, out int resetEffect);
                if (resetEffect != 0)
                    throw new InvalidOperationException("Cosmetic boundary reset retained a remote surface effect");

                CosmeticSubmission = active;
                CosmeticMaterialSubmission = new Mods.Cosmetics.Skins.RenderMaterialOverride(11, 12, 13, 14);
                ClearCosmeticSubmissionState();
                if (CosmeticSubmission != default || CosmeticMaterialSubmission != default)
                    throw new InvalidOperationException("Cosmetic submission state leaked across an entity boundary");

                GL.UseProgram(0);
                var sources = _textureSources.ToArray();
                int owned = _ownedTextures.Count;
                foreach (var scale in new[] { TextureUpscaleMode.Scale2x, TextureUpscaleMode.Off,
                    TextureUpscaleMode.Scale4x, TextureUpscaleMode.Off })
                {
                    RenderOptions.TextureReplacements = false;
                    RenderOptions.TextureUpscale = scale;
                    RefreshTextureQuality();
                    foreach (var source in sources)
                    {
                        var texture = source.Value.Model.Recolors[source.Value.Recolor].Textures[source.Value.Texture];
                        GL.BindTexture(TextureTarget.Texture2D, source.Key);
                        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
                        GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureHeight, out int height);
                        if (width != texture.Width * RenderOptions.TextureUpscaleFactor
                            || height != texture.Height * RenderOptions.TextureUpscaleFactor)
                            throw new InvalidOperationException("Texture quality toggle left a stale GPU image");
                    }
                    if (_textureSources.Count != sources.Length || _ownedTextures.Count > owned)
                        throw new InvalidOperationException("Texture quality toggle leaked texture handles");
                }
                RenderOptions.TextureReplacements = true;
                RenderOptions.AdvancedMaterials = true;
                RefreshTextureQuality();
                int binding = sources[0].Key;

                // Stand in for a replacement companion even on machines without
                // an installed HD pack. Turning advanced maps off must free it
                // immediately instead of leaving unused normal/material/emissive
                // images resident until the room is destroyed.
                if (_materialMaps.Remove(binding, out var previous))
                {
                    if (previous.Normal != 0) ReleaseTexture(previous.Normal);
                    if (previous.Specular != 0) ReleaseTexture(previous.Specular);
                    if (previous.Emissive != 0) ReleaseTexture(previous.Emissive);
                }
                int companion = AllocateTexture();
                GL.BindTexture(TextureTarget.Texture2D, companion);
                _materialMaps[binding] = new(companion, 0, 0);
                _flatColors[companion] = OpenTK.Mathematics.Vector3.One;
                _mipmappedTextures.Add(companion);
                RenderOptions.AdvancedMaterials = false;
                RefreshTextureQuality();
                if (_materialMaps.ContainsKey(binding) || GL.IsTexture(companion)
                    || _flatColors.ContainsKey(companion) || _mipmappedTextures.Contains(companion))
                    throw new InvalidOperationException("Advanced material maps Off retained an unused companion texture");

                // Repeat the ownership check for the master HD replacement
                // switch so the two live settings cannot regress independently.
                RenderOptions.AdvancedMaterials = true;
                RefreshTextureQuality();
                if (_materialMaps.Remove(binding, out previous))
                {
                    if (previous.Normal != 0) ReleaseTexture(previous.Normal);
                    if (previous.Specular != 0) ReleaseTexture(previous.Specular);
                    if (previous.Emissive != 0) ReleaseTexture(previous.Emissive);
                }
                companion = AllocateTexture();
                GL.BindTexture(TextureTarget.Texture2D, companion);
                _materialMaps[binding] = new(companion, 0, 0);
                _flatColors[companion] = OpenTK.Mathematics.Vector3.One;
                _mipmappedTextures.Add(companion);
                RenderOptions.TextureReplacements = false;
                RefreshTextureQuality();
                if (_materialMaps.ContainsKey(binding) || GL.IsTexture(companion)
                    || _flatColors.ContainsKey(companion) || _mipmappedTextures.Contains(companion))
                    throw new InvalidOperationException("HD replacements Off retained a companion texture or side-cache entry");
                Console.WriteLine("[graphicstogglecheck] upscale Off restores native GPU dimensions; advanced maps/HD Off release companions; binding handles remain stable");
            }
            finally
            {
                RenderOptions.TextureUpscale = originalScale;
                RenderOptions.TextureReplacements = originalReplacements;
                RenderOptions.AdvancedMaterials = originalAdvancedMaterials;
                RefreshTextureQuality();
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }
    }
}
#endif
