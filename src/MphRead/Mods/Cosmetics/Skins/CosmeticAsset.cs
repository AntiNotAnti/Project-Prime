using System;
using System.IO;
namespace MphRead.Mods.Cosmetics.Skins
{
    public static class CosmeticAsset
    {
        public static Stream? Open(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
                || relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains('\\')) return null;
            string path = "Assets/Cosmetics/" + relativePath;
            // The same bundled assembly stream works in desktop and Android APKs.
            Stream? bundled = typeof(CosmeticAsset).Assembly.GetManifestResourceStream("Cosmetics/" + relativePath);
            if (bundled != null) return bundled;
            if (OperatingSystem.IsAndroid()) return null;
            try { return File.OpenRead(Path.Combine(AppContext.BaseDirectory, path)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
    public readonly record struct RenderMaterialOverride(int AlbedoBinding, int NormalBinding, int SpecularBinding, int EmissiveBinding);
}
