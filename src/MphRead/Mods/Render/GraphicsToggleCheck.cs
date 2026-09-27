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
            try
            {
                if (_textureSources.Count == 0) throw new InvalidOperationException("No texture sources to test");
                GL.UseProgram(_shaderProgramId);
                var active = new Mods.Cosmetics.CosmeticSurface(1, 6, 1, OpenTK.Mathematics.Vector3.One,
                    OpenTK.Mathematics.Vector3.One, 0.3f, 2, 1);
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    ApplyCosmeticUniforms(active);
                    GL.GetUniform(_shaderProgramId, GL.GetUniformLocation(_shaderProgramId, "cosmetic_effect"), out int effect);
                    if (effect != 6) throw new InvalidOperationException("Cosmetic uniforms did not enable");
                    ApplyCosmeticUniforms(default); ApplyCosmeticUniforms(default);
                    GL.GetUniform(_shaderProgramId, GL.GetUniformLocation(_shaderProgramId, "cosmetic_effect"), out effect);
                    if (effect != 0) throw new InvalidOperationException("Cosmetic uniforms remained active after Off");
                }
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
                RefreshTextureQuality();
                int binding = sources[0].Key;
                // Stand in for a replacement companion even on machines without
                // an installed HD pack, then exercise the real Off refresh path.
                if (_materialMaps.Remove(binding, out var previous))
                {
                    if (previous.Normal != 0) ReleaseTexture(previous.Normal);
                    if (previous.Specular != 0) ReleaseTexture(previous.Specular);
                    if (previous.Emissive != 0) ReleaseTexture(previous.Emissive);
                }
                int companion = AllocateTexture();
                GL.BindTexture(TextureTarget.Texture2D, companion);
                _materialMaps[binding] = new(companion, 0, 0);
                RenderOptions.TextureReplacements = false;
                RefreshTextureQuality();
                if (_materialMaps.ContainsKey(binding) || GL.IsTexture(companion))
                    throw new InvalidOperationException("HD replacements Off retained a companion texture");
                Console.WriteLine("[graphicstogglecheck] upscale Off restores native GPU dimensions; HD Off releases companions; binding handles remain stable");
            }
            finally
            {
                RenderOptions.TextureUpscale = originalScale;
                RenderOptions.TextureReplacements = originalReplacements;
                RefreshTextureQuality();
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }
    }
}
#endif
