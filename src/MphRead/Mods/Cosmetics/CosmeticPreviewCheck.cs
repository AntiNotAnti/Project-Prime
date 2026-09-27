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
        private static void CheckLocalPlayer(Scene scene)
        {
            var player = scene.Players.Main;
            player.Hunter = Hunter.Samus;
            typeof(Entities.PlayerEntity).GetProperty("Values")!.SetValue(player, Metadata.PlayerValues[(int)Hunter.Samus]);
            typeof(Entities.PlayerEntity).GetField("_timeSinceDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(player, (ushort)255);
            var priorMode = scene.GameState.Mode; bool bright = RenderOptions.BrightSkins;
            try
            {
                scene.GameState.Mode = GameMode.Battle;
                typeof(Scene).GetField("_cameraMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(scene, CameraMode.Player);
                Extract.LoadRuntimeData();
                player.ModPrepareHunterResources(Hunter.Samus);
                player.Initialize(); scene.InitEntity(player); player.Health = 100;
                CosmeticDebug.Apply(["-cosmetic", "skin", "skin.samus.obsidian", "-cosmetic", "armor", "armor.inferno", "-cosmetic", "death", "death.quantum"]);
                if (CosmeticRuntime.Get(scene, player.SlotIndex, Hunter.Samus, local: false).Skin.WireId == 0)
                    throw new InvalidOperationException("Camera mode lost local slot cosmetics");
                RenderOptions.BrightSkins = true;
                var surface = player.CosmeticMaterial(firstPerson: true);
                if (surface.Skin != 1 || surface.Effect == 0) throw new InvalidOperationException("Bright Skins hides local weapon cosmetics");
                typeof(Entities.PlayerEntity).GetProperty("Flags2")!.SetValue(player, player.Flags2 | Entities.PlayerFlags2.Cloaking);
                if (player.CosmeticMaterial(firstPerson: true) != default) throw new InvalidOperationException("Cloak leaks cosmetics");
                typeof(Entities.PlayerEntity).GetProperty("Flags2")!.SetValue(player, player.Flags2 & ~Entities.PlayerFlags2.Cloaking);
                player.ModCosmeticObserveAuthority(100, 1, 1);
                player.ModCosmeticObserveAuthority(0, 1, 1);
                if (!player.CosmeticDeathState.Active || player.CosmeticDeathState.Key != "death.quantum")
                    throw new InvalidOperationException("Local accepted death did not select equipped presentation");
                player.Health = 0;
                int health = player.Health; ushort respawn = player.RespawnTimer;
                var collecting = typeof(Scene).GetField("_collectingPreview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                collecting.SetValue(scene, true);
                try
                {
                    player.Draw();
                    var drawn = (System.Collections.Generic.List<RenderItem>)typeof(Scene)
                        .GetField("_previewItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                    if (!drawn.Any(item => item.Type == RenderItemType.Mesh && item.Cosmetics.Effect != 0))
                        throw new InvalidOperationException("Hidden first-person dead body skipped cosmetic presentation");
                    if (player.Health != health || player.RespawnTimer != respawn)
                        throw new InvalidOperationException("Death drawing changed gameplay lifecycle");
                }
                finally
                {
                    collecting.SetValue(scene, false);
                    typeof(Scene).GetMethod("ReleasePreviewItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(scene, null);
                    typeof(Scene).GetField("_singleParticleCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(scene, 0);
                }
                player.ModCosmeticObserveAuthority(100, 2, 1);
                if (player.CosmeticDeathState.Active) throw new InvalidOperationException("Death survived respawn");
                Console.WriteLine("[cosmeticpreviewcheck] real player: local weapon, Bright Skins, cloak, death and respawn passed");
            }
            finally
            {
                scene.GameState.Mode = priorMode; RenderOptions.BrightSkins = bright;
                CosmeticDebug.Apply(["-cosmetic", "clear"]);
            }
        }
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
                    CheckLocalPlayer(scene);
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
                            if (definition is Death.DeathPresentationDefinition death)
                            {
                                CosmeticPreview.DeathRequest = 1;
                                for (int step = 0; step < (int)(death.Duration * 60 * 0.45f); step++) scene.ModStepPreview();
                            }
                            GL.ClearColor(0.04f, 0.06f, 0.09f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                            if (!scene.ModDrawPreviewAlone(new Vector2i(640, 640))) throw new InvalidOperationException("Hunter preview did not render");
                            var previewItems = (System.Collections.Generic.List<RenderItem>)typeof(Scene)
                                .GetField("_previewItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                            bool expectParticles = definition is Armor.ArmorEffectDefinition { WireId: > 0 } || definition is Death.DeathPresentationDefinition;
                            if (expectParticles && !previewItems.Any(item => item.Type == RenderItemType.Particle
                                && item.Points.Length >= 8 && (item.Points[1] - item.Points[3]).LengthSquared > 0))
                                throw new InvalidOperationException("Preview particles missing or degenerate: " + definition.Key);
                            var used = (System.Collections.Generic.Queue<RenderItem>)typeof(Scene)
                                .GetField("_usedRenderItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                            if (used.Count != 0) throw new InvalidOperationException("Standalone preview leaks into the world render-item queue");
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
