using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ReFuel.Stb;
using MphRead.Mods.Render.Materials;

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
        private static MaterialResolver? _resolver;
        private static bool _loaded;
        public static string Root => Path.Combine(OperatingSystem.IsAndroid()
            ? Launcher.LauncherPrefs.Directory : AppContext.BaseDirectory, "texture-packs", "default");
        public static int Revision { get; private set; }
        public static void Reload() { _loaded = false; _resolver = null; Revision++; }
        public static ResolvedMaterial? Resolve(string model, int texture, int palette, int recolor, MaterialAssetKey? key = null)
        {
            if (!_loaded)
            {
                _loaded = true;
                try { _resolver = new MaterialResolver(Root); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    DebugLog.Line("render", "Material manifest ignored; preserving legacy lookup: " + ex.Message);
                    _resolver = new MaterialResolver(Root, useManifest: false);
                }
            }
            return _resolver?.Resolve(model, texture, palette, recolor, key);
        }

        public static bool TryUpload(Model model, int textureId, int paletteId, int recolorId,
            out int width, out int height, out ResolvedMaterial? material)
        {
            width = height = 0;
            material = null;
            if (!RenderOptions.TextureReplacements) return false;
            material = Resolve(model.Name, textureId, paletteId, recolorId, MaterialAssetKey.ForModel(model, textureId, paletteId, recolorId));
            return material?.Albedo is { } albedo && TryUploadBound(albedo.Path, out width, out height);
        }

        public static MaterialMapBindings UploadCompanions(ResolvedMaterial material,
            Func<int> allocateTexture, Action<int> releaseTexture)
        {
            int normal = 0, specular = 0, emissive = 0;
            try
            {
                normal = UploadCompanion(material.Normal, allocateTexture, releaseTexture);
                specular = UploadCompanion(material.SpecularRoughness, allocateTexture, releaseTexture);
                emissive = UploadCompanion(material.Emissive, allocateTexture, releaseTexture);
                return new(normal, specular, emissive);
            }
            catch
            {
                // A later allocation may fail after earlier channels succeeded.
                if (normal != 0) releaseTexture(normal);
                if (specular != 0) releaseTexture(specular);
                if (emissive != 0) releaseTexture(emissive);
                throw;
            }
        }

        private static int UploadCompanion(MaterialImage? image,
            Func<int> allocateTexture, Action<int> releaseTexture)
        {
            if (image == null) return 0;
            string path = image.Path;
            int texture = allocateTexture();
            try
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                if (!TryUploadBound(path, out int width, out int height))
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    releaseTexture(texture);
                    return 0;
                }
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
                GL.BindTexture(TextureTarget.Texture2D, 0);
                releaseTexture(texture);
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
                MaterialPack.ContainedPath(Root, Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'));
                MaterialPack.ValidateImage(path);
                using FileStream stream = File.OpenRead(path);
#if ANDROID
                // ReFuel has no Android native decoder. BitmapFactory is supplied by the OS;
                // request straight, unscaled channels and explicitly convert ARGB ints to RGBA.
                using var options = new Android.Graphics.BitmapFactory.Options { InScaled = false, InPremultiplied = false };
                using var image = Android.Graphics.BitmapFactory.DecodeStream(stream, null, options);
                if (image == null) return false;
                width = image.Width; height = image.Height;
                if (width <= 0 || height <= 0 || width > MaterialPack.MaximumDimension || height > MaterialPack.MaximumDimension
                    || (long)width * height > MaterialPack.MaximumPixels) return false;
                byte[] rgba = new byte[checked(width * height * 4)];
                int[] row = new int[width];
                for (int y = 0; y < height; y++)
                {
                    image.GetPixels(row, 0, width, 0, y, width, 1);
                    for (int x = 0; x < width; x++)
                    {
                        int pixel = row[x], offset = (y * width + x) * 4;
                        rgba[offset] = (byte)(pixel >> 16); rgba[offset + 1] = (byte)(pixel >> 8);
                        rgba[offset + 2] = (byte)pixel; rgba[offset + 3] = (byte)(pixel >> 24);
                    }
                }
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
#else
                using StbImage image = StbImage.Load(stream, StbiImageFormat.Rgba);
                if (image.Width <= 0 || image.Height <= 0 || image.ImagePointer == IntPtr.Zero) return false;
                width = image.Width; height = image.Height;
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.ImagePointer);
#endif
                return GL.GetError() == ErrorCode.NoError;
            }
            catch (Exception ex)
            {
                DebugLog.Line("render", $"texture pack image ignored {path}: {ex.Message}");
                return false;
            }
        }

        public static string CompanionPath(string albedoPath, char kind)
            => Path.Combine(Path.GetDirectoryName(albedoPath) ?? "",
                Path.GetFileNameWithoutExtension(albedoPath) + "_" + kind + ".png");
    }
}
