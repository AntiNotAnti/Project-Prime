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
                Show(Face.Customization);
            };
            panel.Children.Add(hunter);
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
                    if (System.IO.File.Exists(thumbnail))
                    {
                        try
                        {
                            using var stream = System.IO.File.OpenRead(thumbnail);
                            var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 144);
                            tile.Children.Add(new Image { Source = bitmap, Height = 118, Stretch = Avalonia.Media.Stretch.Uniform });
                            tile.DetachedFromVisualTree += (_, _) => bitmap.Dispose();
                        }
                        catch (Exception) { }
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
            Cards("DEATH PRESENTATION", CosmeticCatalog.DeathPresentations.ToArray(), _cosmeticDraft.DeathEffectKey,
                key => _cosmeticDraft = _cosmeticDraft with { DeathEffectKey = key });
            var actions = new WrapPanel();
            actions.Children.Add(new PrimeButton("PREVIEW DEATH", () => _stand.PreviewDeath()));
            actions.Children.Add(new PrimeButton("RESET PREVIEW", () => _stand.ResetPreview()));
            var status = new TextBlock { Text = CosmeticPersistence.IsPending(_cosmeticHunter) ? "LOCAL / NOT SYNCED" : "READY", Foreground = HubTheme.TextBrush };
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
            actions.Children.Add(equip); panel.Children.Add(actions); panel.Children.Add(status);
            return new ScrollViewer { Content = panel };
        }
    }
}
#endif
