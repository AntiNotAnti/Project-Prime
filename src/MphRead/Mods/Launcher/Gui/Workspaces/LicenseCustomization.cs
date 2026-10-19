#if MPHREAD_AVALONIA
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.Cosmetics;
namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class LicenseWorkspace
    {
        private Hunter _cosmeticHunter = Hunter.Samus;
        private CosmeticLoadout? _cosmeticDraft;
        private bool _cosmeticSaving;
        internal static Image CreateCosmeticThumbnail(string path)
        {
            var image = new Image { Height = 118, Stretch = Avalonia.Media.Stretch.Uniform };
            image.AttachedToVisualTree += (_, _) =>
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(path);
                    image.Source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 144);
                }
                catch (Exception ex) { Mods.DebugLog.Line("ui", $"Cosmetic thumbnail unavailable: {ex.Message}"); }
            };
            image.DetachedFromVisualTree += (_, _) =>
            {
                var bitmap = image.Source as Avalonia.Media.Imaging.Bitmap;
                image.Source = null;
                bitmap?.Dispose();
            };
            return image;
        }

        private static Control CreateBeamPreview(BeamEffectDefinition beam)
        {
            // A zero-asset preview, deliberately not a gameplay shot or GL
            // scene. Canvas geometry distinguishes beam palettes immediately.
            var canvas = new Canvas { Width = 144, Height = 74 };
            var color = Avalonia.Media.Color.FromRgb(
                (byte)(beam.PrimaryColor.X * 255), (byte)(beam.PrimaryColor.Y * 255),
                (byte)(beam.PrimaryColor.Z * 255));
            var tint = new Avalonia.Media.SolidColorBrush(color);
            foreach (var band in new[] { (Y: 37d, Thick: 13d, Alpha: 0.22d),
                (Y: 37d, Thick: 4d, Alpha: 0.82d) })
            {
                canvas.Children.Add(new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Avalonia.Point(8, band.Y), EndPoint = new Avalonia.Point(136, band.Y),
                    Stroke = tint, StrokeThickness = band.Thick, Opacity = band.Alpha
                });
            }
            void Stroke(double x1, double y1, double x2, double y2, double thick = 2,
                double alpha = 0.8)
            {
                canvas.Children.Add(new Avalonia.Controls.Shapes.Line
                {
                    StartPoint = new Avalonia.Point(x1, y1), EndPoint = new Avalonia.Point(x2, y2),
                    Stroke = tint, StrokeThickness = thick, Opacity = alpha
                });
            }
            // Nine intentionally distinct diagrams, not just nine different
            // colors. The preview is asset-free and does not fire a real shot.
            switch (beam.Style)
            {
                case BeamFxStyle.IonFracture:
                    Stroke(54, 27, 72, 47); Stroke(54, 47, 72, 27);
                    Stroke(90, 28, 110, 45, 1.5); break;
                case BeamFxStyle.VoidRift:
                    Stroke(9, 29, 135, 29); Stroke(9, 45, 135, 45);
                    Stroke(54, 29, 68, 18); Stroke(77, 45, 91, 55); break;
                case BeamFxStyle.QuantumSplit:
                    Stroke(9, 37, 136, 22); Stroke(9, 37, 136, 52);
                    Stroke(58, 21, 76, 36); break;
                case BeamFxStyle.AlimbicResonance:
                    foreach (double x in new[] { 32d, 72d, 112d })
                        for (int k = 0; k < 6; k++)
                        {
                            double a = k * Math.PI / 3;
                            double b = (k + 1) * Math.PI / 3;
                            Stroke(x + 11 * Math.Cos(a), 37 + 11 * Math.Sin(a),
                                x + 11 * Math.Cos(b), 37 + 11 * Math.Sin(b), 1.5);
                        }
                    break;
                case BeamFxStyle.OmegaPulse:
                    foreach (double x in new[] { 32d, 69d, 106d })
                    {
                        Stroke(x, 24, x, 50, 2.5);
                        Stroke(x - 6, 29, x + 6, 29); Stroke(x - 6, 45, x + 6, 45);
                    }
                    break;
                case BeamFxStyle.ArcDisruptor:
                    Stroke(24, 37, 39, 18); Stroke(39, 18, 54, 40);
                    Stroke(64, 36, 79, 54); Stroke(79, 54, 94, 27); break;
                case BeamFxStyle.SolarSpear:
                    Stroke(31, 37, 42, 15); Stroke(42, 15, 54, 37);
                    Stroke(77, 37, 91, 58); Stroke(91, 58, 105, 37); break;
                case BeamFxStyle.CryoShatter:
                    Stroke(25, 36, 37, 14); Stroke(37, 14, 52, 37);
                    Stroke(60, 37, 81, 51); Stroke(81, 51, 94, 37); break;
                case BeamFxStyle.PhantomTrace:
                    Stroke(12, 31, 132, 31, 1, 0.25);
                    Stroke(12, 43, 132, 43, 1, 0.3); break;
            }
            canvas.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Avalonia.Point(8, 37), EndPoint = new Avalonia.Point(136, 37),
                Stroke = Avalonia.Media.Brushes.White, StrokeThickness = 1.5
            });
            return new Border { Height = 74, Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.FromRgb(9, 17, 32)), Child = canvas };
        }

        private Control Customization()
        {
            _cosmeticDraft ??= CosmeticPersistence.Get(_cosmeticHunter);
            _stand.Name2 = _cosmeticHunter.ToString();
            _stand.Cosmetics = _cosmeticDraft;
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(SectionTitle("CUSTOMIZATION", "Choose a hunter, preview a combination, then equip it. Drag the model to rotate; scroll to zoom."));
            var hunter = new ChoiceRow("Hunter", HunterStand.Names, (int)_cosmeticHunter);
            hunter.Changed += (_, _) =>
            {
                _cosmeticHunter = (Hunter)hunter.Index;
                _cosmeticDraft = CosmeticPersistence.Get(_cosmeticHunter);
                _stand.ResetPreview();
                Show(Face.Customization);
            };
            panel.Children.Add(hunter);
            var view = new ChoiceRow("Preview", new[] { "Hunter", "Weapon", "Alternate form" }, (int)_stand.PreviewMode);
            ControllerNav.Identify(view, "cosmetics.preview-mode");
            view.Changed += (_, _) =>
            {
                _stand.ResetPreview();
                _stand.PreviewMode = (SkinContext)view.Index;
                Show(Face.Customization);
            };
            panel.Children.Add(view);
            void Cards(string title, CosmeticDefinition[] definitions, string selected, Action<string> select)
            {
                panel.Children.Add(SectionTitle(title, ""));
                var cards = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var definition in definitions)
                {
                    var card = new HubNavButton(definition.DisplayName, definition.Description, compact: true)
                    { Selected = definition.Key == selected, MinWidth = 145, Margin = new Avalonia.Thickness(0, 0, 6, 6) };
                    ControllerNav.Identify(card, "cosmetics." + definition.Key);
                    card.Click += (_, _) => { select(definition.Key); Show(Face.Customization); };
                    var tile = new StackPanel { Width = 150, Spacing = 3, Margin = new Avalonia.Thickness(0, 0, 6, 6) };
                    string thumbnail = CosmeticThumbnail.PathFor(_cosmeticHunter, definition.Key);
                    if (definition is BeamEffectDefinition beam)
                    {
                        tile.Children.Add(CreateBeamPreview(beam));
                    }
                    else if (System.IO.File.Exists(thumbnail))
                    {
                        tile.Children.Add(CreateCosmeticThumbnail(thumbnail));
                    }
                    else
                    {
                        OpenTK.Mathematics.Vector3 tint = definition is Cosmetics.Armor.ArmorEffectDefinition armor ? armor.PrimaryColor
                            : definition is Cosmetics.Death.DeathPresentationDefinition death ? death.LightEffect : new(0.4f, 0.5f, 0.6f);
                        tile.Children.Add(new Border { Height = 74, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(
                            (byte)(tint.X * 90), (byte)(tint.Y * 90), (byte)(tint.Z * 90))), Child = new TextBlock {
                            Text = "✦", FontSize = 32, Foreground = HubTheme.TextBrush,
                            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                    }
                    tile.Children.Add(card);
                    cards.Children.Add(tile);
                }
                panel.Children.Add(cards);
            }
            Cards("SKIN", CosmeticCatalog.Skins.Where(s => s.Hunter == null || s.Hunter == _cosmeticHunter).ToArray(),
                _cosmeticDraft.SkinKey, key => _cosmeticDraft = _cosmeticDraft with { SkinKey = key });
            Cards("ARMOR EFFECT", CosmeticCatalog.ArmorEffects.ToArray(), _cosmeticDraft.ArmorEffectKey,
                key => _cosmeticDraft = _cosmeticDraft with { ArmorEffectKey = key });
            Cards("INSTA-GIB BEAM FX", CosmeticCatalog.BeamEffects.ToArray(), _cosmeticDraft.BeamEffectKey,
                key => _cosmeticDraft = _cosmeticDraft with { BeamEffectKey = key });
            var beamColors = new ChoiceRow("Beam color", BeamPalette.Names.ToArray(), _cosmeticDraft.BeamColor);
            ControllerNav.Identify(beamColors, "cosmetics.beam-color");
            beamColors.Changed += (_, _) =>
            {
                _cosmeticDraft = _cosmeticDraft with { BeamColor = (byte)beamColors.Index };
                Show(Face.Customization);
            };
            panel.Children.Add(beamColors);
            panel.Children.Add(new TextBlock { Text = "Beam colors and styles are saved locally. Beam effects appear in Insta-Gib matches only.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = HubTheme.TextBrush });
            Cards("DEATH PRESENTATION", CosmeticCatalog.DeathPresentations.ToArray(), _cosmeticDraft.DeathEffectKey,
                key => { _cosmeticDraft = _cosmeticDraft with { DeathEffectKey = key }; _stand.PreviewDeath(); });
            var actions = new WrapPanel();
            actions.Children.Add(new PrimeButton("PREVIEW DEATH", () => { _stand.PreviewDeath(); Show(Face.Customization); }));
            var loopDeath = new CheckBox { Content = "LOOP DEATH", IsChecked = _stand.LoopDeath,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(8, 0) };
            loopDeath.IsCheckedChanged += (_, _) =>
            {
                _stand.LoopDeath = loopDeath.IsChecked == true;
                if (_stand.LoopDeath) { _stand.PreviewDeath(); Show(Face.Customization); }
            };
            actions.Children.Add(loopDeath);
            actions.Children.Add(new PrimeButton("RESET PREVIEW", () => _stand.ResetPreview()));
            actions.Children.Add(new PrimeButton("COMPARE NATIVE", () =>
            {
                _stand.ResetPreview();
                _stand.Cosmetics = _stand.Cosmetics == CosmeticLoadout.Default ? _cosmeticDraft : CosmeticLoadout.Default;
            }));
            actions.Children.Add(new PrimeButton("RESET LOADOUT", () =>
            {
                _cosmeticDraft = CosmeticLoadout.Default; _stand.ResetPreview(); Show(Face.Customization);
            }));
            var status = new TextBlock { Text = _cosmeticDraft != CosmeticPersistence.Get(_cosmeticHunter) ? "PREVIEW / NOT EQUIPPED"
                : CosmeticPersistence.IsPending(_cosmeticHunter) ? "EQUIPPED LOCALLY / NOT SYNCED" : "EQUIPPED", Foreground = HubTheme.TextBrush };
            var equip = new PrimeButton("EQUIP", async () =>
            {
                if (_cosmeticSaving) return;
                _cosmeticSaving = true;
                Hunter selectedHunter = _cosmeticHunter;
                CosmeticLoadout submitted = _cosmeticDraft;
                try
                {
                    status.Text = "SAVING…";
                    var result = await HunterLicenseClient.UpdateCosmeticAsync(selectedHunter, submitted);
                    status.Text = result.Message;
                    _snapshot.Cosmetics.RemoveAll(x => x.Hunter == (int)selectedHunter);
                    _snapshot.Cosmetics.Add(new HunterLicenseCosmetic { Hunter = (int)selectedHunter, SkinKey = submitted.SkinKey,
                        ArmorEffectKey = submitted.ArmorEffectKey, DeathEffectKey = submitted.DeathEffectKey });
                }
                catch (Exception ex) { status.Text = "SAVE FAILED: " + ex.Message; }
                finally { _cosmeticSaving = false; }
            });
            actions.Children.Add(equip);
            panel.Children.Insert(2, actions); panel.Children.Insert(3, status);
            string visibility = !RenderOptions.ShowCustomCosmetics
                ? "Cosmetics are hidden by Show custom cosmetics in Graphics settings. Your selection still saves."
                : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off
                    ? "Cosmetic effect quality is Off: skins display, but armor and death effects are hidden."
                    : RenderOptions.BrightSkins
                        ? "Bright Skins overrides other players' cosmetics in matches. Your own weapon and this preview retain cosmetics."
                        : "Team-colored panels stay visible in team matches. First-person effects are reduced to keep your aim clear.";
            panel.Children.Insert(4, new TextBlock { Text = visibility, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = HubTheme.TextBrush });
            return new ScrollViewer { Content = panel };
        }
    }
}
#endif
