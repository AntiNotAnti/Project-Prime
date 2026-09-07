using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ReFuel.Stb;

namespace MphRead.Export
{
    public static class Images
    {
        public static void ExportImages(Model model)
        {
            string exportPath = Paths.Combine(Paths.Export, model.Name);
            foreach (Recolor recolor in model.Recolors)
            {
                string colorPath = Paths.Combine(exportPath, recolor.Name);
                Directory.CreateDirectory(colorPath);
                var usedTextures = new HashSet<int>();
                int id = 0;
                var usedCombos = new HashSet<(int, int)>();

                void DoTexture(int textureId, int paletteId)
                {
                    if (textureId == -1 || usedCombos.Contains((textureId, paletteId)))
                    {
                        return;
                    }
                    Texture texture = recolor.Textures[textureId];
                    IReadOnlyList<ColorRgba> pixels = recolor.GetPixels(textureId, paletteId);
                    if (texture.Width == 0 || texture.Height == 0 || pixels.Count == 0)
                    {
                        return;
                    }
                    Debug.Assert(texture.Width * texture.Height == pixels.Count);
                    usedTextures.Add(textureId);
                    usedCombos.Add((textureId, paletteId));
                    string filename = $"{textureId}-{paletteId}";
                    if (id > 0)
                    {
                        filename = $"anim__{id.ToString().PadLeft(3, '0')}";
                    }
                    SaveTexture(colorPath, filename, texture.Width, texture.Height, pixels);
                }

                foreach (Material material in model.Materials.OrderBy(m => m.TextureId).ThenBy(m => m.PaletteId))
                {
                    DoTexture(material.TextureId, material.PaletteId);
                }
                id = 1;
                usedCombos.Clear();
                foreach (TextureAnimationGroup group in model.AnimationGroups.Texture)
                {
                    foreach (TextureAnimation animation in group.Animations.Values)
                    {
                        for (int i = animation.StartIndex; i < animation.StartIndex + animation.Count; i++)
                        {
                            DoTexture(group.TextureIds[i], group.PaletteIds[i]);
                            id++;
                        }
                    }
                }
                if (usedTextures.Count != recolor.Textures.Count)
                {
                    string unusedPath = Paths.Combine(colorPath, "unused");
                    Directory.CreateDirectory(unusedPath);
                    for (int t = 0; t < recolor.Textures.Count; t++)
                    {
                        if (usedTextures.Contains(t))
                        {
                            continue;
                        }
                        Texture texture = recolor.Textures[t];
                        for (int p = 0; p < recolor.Palettes.Count; p++)
                        {
                            IReadOnlyList<TextureData> textureData = recolor.TextureData[t];
                            IReadOnlyList<PaletteData> palette = recolor.PaletteData[p];
                            if (textureData.Any(t => t.Data >= palette.Count))
                            {
                                continue;
                            }
                            IReadOnlyList<ColorRgba> pixels = recolor.GetPixels(t, p);
                            string filename = $"{t}-{p}";
                            SaveTexture(unusedPath, filename, texture.Width, texture.Height, pixels);
                        }
                    }
                }
            }
        }

        public static void ExportPalettes(Model model)
        {
            string exportPath = Paths.Combine(Paths.Export, model.Name);
            foreach (Recolor recolor in model.Recolors)
            {
                string palettePath = Paths.Combine(exportPath, recolor.Name, "palettes");
                Directory.CreateDirectory(palettePath);
                for (int p = 0; p < recolor.Palettes.Count; p++)
                {
                    IReadOnlyList<ColorRgba> pixels = recolor.GetPalettePixels(p);
                    string filename = $"p{p}";
                    SaveTexture(palettePath, filename, 16, 16, pixels);
                }
            }
        }

        public static void SaveTexture(string directory, string filename, ushort width, ushort height, IReadOnlyList<ColorRgba> pixels)
        {
            string imagePath = Paths.Combine(directory, $"{filename}.png");
            Span<ColorRgba> pixelBuffer = pixels.ToArray();
            using FileStream fileStream = File.Create(imagePath);
            StbImage.FlipVerticallyOnSave = false;
            StbImage.WritePng<ColorRgba>(pixelBuffer, width, height, StbiImageFormat.Rgba, fileStream);
        }

        public static void ExportHudLayers()
        {
            HudExport.TestLayers(exportScreens: true);
        }

        public static void ExportHudObjects()
        {
            HudExport.TestObjects(null, 0, 0, 0, 0, export: true);
        }
    }
}
