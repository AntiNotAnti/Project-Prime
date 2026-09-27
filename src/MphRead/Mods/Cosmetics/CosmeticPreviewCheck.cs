#if MPHREAD_SHELL
using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
namespace MphRead.Mods.Cosmetics
{
    internal static class CosmeticPreviewCheck
    {
        public static int Run(string output)
        {
            try
            {
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                Directory.CreateDirectory(output);
                using var window = new GameWindow(new GameWindowSettings(), ThumbnailCapture.WindowSettings(640, 640));
                window.MakeCurrent();
                var scene = new Scene(new Vector2i(640, 640), window.KeyboardState, window.MouseState, _ => { }, () => { }) { SideScene = true };
                try
                {
                    scene.OnLoad(); scene.OnResize();
                    Scene.LauncherPreview = true; Scene.LauncherHunter = Hunter.Samus; Scene.LauncherSuit = 0;
                    Scene.PreviewWanted = true; Scene.PreviewLeft = Scene.PreviewTop = 0; Scene.PreviewRight = Scene.PreviewBottom = 1;
                    int captures = 0;
                    for (int hunter = 0; hunter < 7; hunter++)
                    {
                        Scene.LauncherHunter = (Hunter)hunter;
                        var definitions = CosmeticCatalog.Skins.Where(s => s.Hunter == null || (int)s.Hunter == hunter)
                            .Cast<CosmeticDefinition>().Concat(CosmeticCatalog.ArmorEffects).Concat(CosmeticCatalog.DeathPresentations);
                        foreach (var definition in definitions)
                        {
                            CosmeticPreview.DeathRequest = 0;
                            CosmeticPreview.Loadout = definition switch
                            {
                                Skins.SkinDefinition skin => new(skin.Key, "armor.none", "death.classic"),
                                Armor.ArmorEffectDefinition armor => new("skin.default", armor.Key, "death.classic"),
                                _ => new("skin.default", "armor.none", definition.Key)
                            };
                            scene.ModStepPreview();
                            if (definition is Death.DeathPresentationDefinition death && death.WireId != 0)
                            {
                                CosmeticPreview.DeathRequest = 1;
                                for (int step = 0; step < (int)(death.Duration * 60 * 0.45f); step++) scene.ModStepPreview();
                            }
                            GL.ClearColor(0.04f, 0.06f, 0.09f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                            if (!scene.ModDrawPreviewAlone(new Vector2i(640, 640))) throw new InvalidOperationException("Hunter preview did not render");
                            if (captures == 0) scene.CheckLiveTextureQuality();
                            GL.Finish();
                            string folder = Path.Combine(output, ((Hunter)hunter).ToString()); Directory.CreateDirectory(folder);
                            string file = Path.Combine(folder, definition.Key + ".png");
                            if (!ScreenCapture.SaveWindow(640, 640, file)) throw new InvalidOperationException("Preview capture failed");
                            string cached = CosmeticThumbnail.PathFor((Hunter)hunter, definition.Key);
                            Directory.CreateDirectory(Path.GetDirectoryName(cached)!); File.Copy(file, cached, overwrite: true);
                            captures++;
                        }
                    }
                    Console.WriteLine("[cosmeticpreviewcheck] captured " + captures + " hunter/skin/armor/death previews: " + output);
                }
                finally { scene.UnloadGl(); Scene.LauncherPreview = false; CosmeticPreview.Loadout = null; }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("[cosmeticpreviewcheck] " + ex); return 1; }
        }
    }
}
#endif
