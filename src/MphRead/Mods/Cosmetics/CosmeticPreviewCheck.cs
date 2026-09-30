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
        private static void CheckLocalPlayer(Scene scene, Hunter hunter)
        {
            var player = scene.Players.Main;
            player.Hunter = hunter;
            typeof(Entities.PlayerEntity).GetProperty("Values")!.SetValue(player, Metadata.PlayerValues[(int)hunter]);
            typeof(Entities.PlayerEntity).GetField("_timeSinceDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(player, (ushort)255);
            var priorMode = scene.GameState.Mode; bool bright = RenderOptions.BrightSkins;
            try
            {
                scene.GameState.Mode = GameMode.Battle;
                typeof(Scene).GetField("_cameraMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(scene, CameraMode.Player);
                Extract.LoadRuntimeData();
                player.ModPrepareHunterResources(hunter);
                player.Initialize(); scene.InitEntity(player); player.Health = 100;
                CosmeticDebug.Apply(["-cosmetic", "skin", $"skin.{hunter.ToString().ToLowerInvariant()}.obsidian", "-cosmetic", "armor", "armor.inferno", "-cosmetic", "death", "death.quantum"]);
                if (CosmeticRuntime.Get(scene, player.SlotIndex, hunter, local: false).Skin.WireId == 0)
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
                Console.WriteLine($"[cosmeticpreviewcheck] real player {hunter}: local weapon, Bright Skins, cloak, death and respawn passed");
            }
            finally
            {
                scene.GameState.Mode = priorMode; RenderOptions.BrightSkins = bright;
                CosmeticDebug.Apply(["-cosmetic", "clear"]);
            }
        }
        private static void CheckPreviewToggles(Scene scene)
        {
            var priorQuality = RenderOptions.CosmeticQuality;
            bool priorVisible = RenderOptions.ShowCustomCosmetics;
            try
            {
                Scene.LauncherHunter = Hunter.Samus;
                CosmeticPreview.Loadout = new("skin.samus.alimbic", "armor.inferno", "death.quantum");
                CosmeticPreview.DeathRequest = 0;
                foreach (var quality in Enum.GetValues<CosmeticEffectQuality>())
                foreach (bool visible in new[] { true, false })
                {
                    RenderOptions.CosmeticQuality = quality;
                    RenderOptions.ShowCustomCosmetics = visible;
                    scene.ModStepPreview();
                    scene.ModDrawPreviewAlone(new Vector2i(640, 640));
                    var items = (System.Collections.Generic.List<RenderItem>)typeof(Scene)
                        .GetField("_previewItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                    int particles = items.Count(i => i.Type == RenderItemType.Particle);
                    int limit = !visible || quality == CosmeticEffectQuality.Off ? 0
                        : quality == CosmeticEffectQuality.Low ? 2 : quality == CosmeticEffectQuality.Medium ? 8 : 16;
                    if (particles > limit || (limit > 0 && particles == 0))
                        throw new InvalidOperationException($"Particle budget/visibility failed: {quality}, visible={visible}, count={particles}");
                    if (!visible && items.Any(i => i.Cosmetics.Skin != 0 || i.Cosmetics.Effect != 0 || i.CosmeticMaterial.AlbedoBinding != 0))
                        throw new InvalidOperationException("Disabled preview retained a cosmetic material");
                    CosmeticPreview.DeathRequest = 1;
                    scene.ModStepPreview();
                    scene.ModDrawPreviewAlone(new Vector2i(640, 640));
                    int deathParticles = items.Count(i => i.Type == RenderItemType.Particle);
                    int deathLimit = !visible || quality == CosmeticEffectQuality.Off ? 0
                        : quality == CosmeticEffectQuality.Low ? 6 : quality == CosmeticEffectQuality.Medium ? 12 : 18;
                    if (deathParticles > deathLimit || (deathLimit > 0 && deathParticles == 0))
                        throw new InvalidOperationException($"Death particle budget/visibility failed: {quality}, visible={visible}, count={deathParticles}");
                    CosmeticPreview.DeathRequest = 0;
                    scene.ModStepPreview();
                }
                RenderOptions.ShowCustomCosmetics = true;
                RenderOptions.CosmeticQuality = CosmeticEffectQuality.High;
                CosmeticPreview.LoopDeath = true;
                CosmeticPreview.DeathRequest = 1;
                var preview = (HunterPreviewEntity)typeof(Scene).GetField("_preview",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                var death = (Death.DeathPresentationState)typeof(HunterPreviewEntity).GetField("_death",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(preview)!;
                scene.ModStepPreview();
                float initialDeathTime = death.StartTime;
                for (int frame = 0; frame < 600; frame++)
                {
                    scene.ModStepPreview();
                    scene.ModDrawPreviewAlone(new Vector2i(640, 640));
                    var used = (System.Collections.Generic.Queue<RenderItem>)typeof(Scene)
                        .GetField("_usedRenderItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                    if (used.Count != 0) throw new InvalidOperationException("Repeated death preview leaked world render items");
                }
                if (death.StartTime <= initialDeathTime + 4)
                    throw new InvalidOperationException("Looping death preview never restarted");
                CosmeticPreview.DeathRequest = 0;
                scene.ModStepPreview();
                if (death.Active) throw new InvalidOperationException("Reset preview did not stop the death loop");
                Console.WriteLine("[cosmeticpreviewcheck] quality/off budgets and 600 looping death frames passed");
            }
            finally
            {
                CosmeticPreview.LoopDeath = false; CosmeticPreview.DeathRequest = 0;
                RenderOptions.CosmeticQuality = priorQuality; RenderOptions.ShowCustomCosmetics = priorVisible;
            }
        }

        private static void CheckModelModes(Scene scene, string output)
        {
            CosmeticPreview.DeathRequest = 0; CosmeticPreview.LoopDeath = false;
            for (int h = 0; h < 7; h++)
            foreach (SkinContext mode in new[] { SkinContext.ViewModel, SkinContext.AltForm, SkinContext.Biped })
            foreach (string skin in new[] { "default", "obsidian", "alimbic" })
            {
                var hunter = (Hunter)h;
                Scene.LauncherHunter = hunter; CosmeticPreview.Mode = mode;
                CosmeticPreview.Loadout = new(skin == "default" ? "skin.default"
                    : $"skin.{hunter.ToString().ToLowerInvariant()}.{skin}", "armor.none", "death.classic");
                scene.ModStepPreview();
                if (!scene.ModDrawPreviewAlone(new Vector2i(640, 640)))
                    throw new InvalidOperationException($"{hunter} {mode} failed to render");
                var preview = (HunterPreviewEntity)typeof(Scene).GetField("_preview",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                var model = (ModelInstance)typeof(HunterPreviewEntity).GetField("_model",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(preview)!;
                int expected = mode == SkinContext.ViewModel ? 3 : mode == SkinContext.AltForm ? 2 : 0;
                if (model.Model.Name != Metadata.HunterModels[hunter][expected])
                    throw new InvalidOperationException($"{hunter} {mode} retained the wrong model");
                var items = (System.Collections.Generic.List<RenderItem>)typeof(Scene).GetField("_previewItems",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
                if (items.Any(item => item.Type == RenderItemType.Mesh && item.CosmeticMaterial.AlbedoBinding != 0) != (skin != "default"))
                    throw new InvalidOperationException($"{hunter} {mode} {skin} did not bind authored artwork");
                GL.Finish();
                if (!ScreenCapture.SaveWindow(640, 640, Path.Combine(output, hunter.ToString(), $"{mode}.{skin}.png")))
                    throw new InvalidOperationException("Mode capture failed");
            }
            CosmeticPreview.Mode = SkinContext.Biped;
            Console.WriteLine("[cosmeticpreviewcheck] 63 native/authored-skin/model-mode captures and bindings passed");
        }

        public static int Run(string output)
        {
            try
            {
                Paths.UpdatePaths(); Paths.ChooseMphPath();
                Directory.CreateDirectory(output);
                using var window = new GameWindow(new GameWindowSettings(), ThumbnailCapture.WindowSettings(640, 640));
                using var graphics = new Render.DesktopGraphicsSession(window);
                var scene = new Scene(new Vector2i(640, 640), window.KeyboardState, window.MouseState, _ => { }, () => { }) { SideScene = true };
                try
                {
                    scene.OnLoad(); scene.OnResize();
                    for (int hunter = 0; hunter < 7; hunter++) CheckLocalPlayer(scene, (Hunter)hunter);
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
                            var ribbons = previewItems.Where(item => item.Type == RenderItemType.TrailMulti).ToArray();
                            if (ribbons.Length > 32 || ribbons.Any(item => item.ItemCount > item.Points.Length
                                || item.ItemCount < 8 || item.ItemCount % 4 != 0
                                || (item.Points[1] - item.Points[3]).LengthSquared <= 0))
                                throw new InvalidOperationException("Invalid or over-budget cosmetic ribbons: " + definition.Key);
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
                    CheckModelModes(scene, output);
                    CheckPreviewToggles(scene);
                    Console.WriteLine("[cosmeticpreviewcheck] captured " + captures + " hunter/skin/armor/death previews: " + output);
                }
                finally { scene.UnloadGl(); Scene.LauncherPreview = false; CosmeticPreview.Loadout = null; CosmeticPreview.Mode = SkinContext.Biped; }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("[cosmeticpreviewcheck] " + ex); return 1; }
        }
    }
}
#endif
