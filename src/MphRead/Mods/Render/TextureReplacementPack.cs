using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ReFuel.Stb;

namespace MphRead.Mods.Render
{
    internal readonly record struct MaterialMapBindings(int Normal, int Specular, int Emissive)
    {
        public bool Any => Normal != 0 || Specular != 0 || Emissive != 0;
    }

    /// <summary>
    /// Optional user-owned high-resolution albedo and material maps.
    /// _n = tangent-space normal, _s = red specular / green roughness,
    /// _e = RGB emissive. Missing/bad files disable only that map.
    /// </summary>
    internal static class TextureReplacementPack
    {
        private static readonly char[] _invalid = Path.GetInvalidFileNameChars();

        public static bool TryUpload(Model model, int textureId, int paletteId, int recolorId,
            out int width, out int height, out string? sourcePath)
        {
            width = height = 0;
            sourcePath = null;
            if (!RenderOptions.TextureReplacements || OperatingSystem.IsAndroid()) return false;
            foreach (string path in Candidates(model, textureId, paletteId, recolorId))
            {
                if (!File.Exists(path)) continue;
                if (TryUploadBound(path, out width, out height))
                {
                    sourcePath = path;
                    DebugLog.Line("render", $"HD texture {model.Name}:{textureId}/{paletteId}/{recolorId} "
                        + $"<- {Path.GetFileName(path)} ({width}x{height})");
                    return true;
                }
            }
            return false;
        }

        public static MaterialMapBindings UploadCompanions(string albedoPath, Func<int> allocateTexture)
            => OperatingSystem.IsAndroid() ? default : new(
                UploadCompanion(albedoPath, 'n', allocateTexture),
                UploadCompanion(albedoPath, 's', allocateTexture),
                UploadCompanion(albedoPath, 'e', allocateTexture));

        private static int UploadCompanion(string albedoPath, char kind, Func<int> allocateTexture)
        {
            string path = CompanionPath(albedoPath, kind);
            if (!File.Exists(path)) return 0;
            int texture = allocateTexture();
            try
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                if (!TryUploadBound(path, out int width, out int height)) return 0;
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    (int)TextureWrapMode.Repeat);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    (int)TextureWrapMode.Repeat);
                DebugLog.Line("render", $"material map {Path.GetFileName(path)} ({width}x{height})");
                return texture;
            }
            catch (Exception ex)
            {
                DebugLog.Line("render", $"material map ignored {path}: {ex.Message}");
                return 0;
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, 0); }
        }

        private static bool TryUploadBound(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using FileStream stream = File.OpenRead(path);
                using StbImage image = StbImage.Load(stream, StbiImageFormat.Rgba);
                if (image.Width <= 0 || image.Height <= 0 || image.ImagePointer == IntPtr.Zero) return false;
                width = image.Width; height = image.Height;
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.ImagePointer);
                return GL.GetError() == ErrorCode.NoError;
            }
            catch (Exception ex)
            {
                DebugLog.Line("render", $"texture pack image ignored {path}: {ex.Message}");
                return false;
            }
        }

        private static string[] Candidates(Model model, int textureId, int paletteId, int recolorId)
        {
            string modelName = Safe(model.Name);
            string root = Path.Combine(AppContext.BaseDirectory, "texture-packs", "default");
            string folder = Path.Combine(root, modelName);
            return new[]
            {
                Path.Combine(folder, $"{textureId}_{paletteId}_{recolorId}.png"),
                Path.Combine(folder, $"{textureId}_{paletteId}.png"),
                Path.Combine(folder, $"{textureId}.png"),
                Path.Combine(root, $"{modelName}_{textureId}_{paletteId}_{recolorId}.png"),
                Path.Combine(root, $"{modelName}_{textureId}.png")
            };
        }

        public static string CompanionPath(string albedoPath, char kind)
            => Path.Combine(Path.GetDirectoryName(albedoPath) ?? "",
                Path.GetFileNameWithoutExtension(albedoPath) + "_" + kind + ".png");

        private static string Safe(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "unnamed";
            return new string(name.Select(c => _invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        }
    }
}
