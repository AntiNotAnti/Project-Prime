using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Render.Characters;

namespace MphRead.Mods.Launcher.Core
{
    internal sealed class LauncherHunterSelectionBackend : IHunterSelectionBackend
    {
        private readonly Task<(CharacterModelPack Pack, string? Issue)> _modelLoad = Task.Run(() =>
        {
            CharacterModelPack pack = CharacterModelPack.LoadDefault(out string? issue);
            return (pack, issue);
        });
        public Hunter PreferredHunter => Hunters.Resolve(LauncherPrefs.LastHunter);
        public byte PreferredColor => (byte)Network.PlayerColors.Clamp(LauncherPrefs.LastColor);
        public void SaveIdentity(Hunter hunter, byte color)
        { LauncherPrefs.LastHunter = hunter; LauncherPrefs.LastColor = color; LauncherPrefs.Save(); }

        public HunterSelectionProfile Capture(Hunter hunter)
        {
            CharacterModelPack models = CharacterModelPack.Empty;
            string? modelIssue = null;
            bool loading = !_modelLoad.IsCompleted;
            if (_modelLoad.IsCompletedSuccessfully) (models, modelIssue) = _modelLoad.Result;
            else if (_modelLoad.IsFaulted) modelIssue = _modelLoad.Exception?.GetBaseException().Message;
            var modes = ImmutableArray.CreateBuilder<SkinContext>();
            if (models.TryResolve(hunter, CharacterModelPart.Biped, out _)) modes.Add(SkinContext.Biped);
            if (models.TryResolve(hunter, CharacterModelPart.ViewModel, out _)) modes.Add(SkinContext.ViewModel);
            if (models.TryResolve(hunter, CharacterModelPart.AlternateForm, out _)) modes.Add(SkinContext.AltForm);
            if (models.TryResolve(hunter, CharacterModelPart.Halfturret, out _)) modes.Add(SkinContext.Halfturret);
            string modelMessage = loading ? "Loading installed custom character assets…"
                : modelIssue != null ? "Installed character pack could not load: " + modelIssue
                : modes.Count == 0 ? "Native character models. No replacement asset is installed for this Hunter."
                : RenderOptions.CharacterModelReplacements ? "Installed character pack enabled."
                : "Installed character pack available. Enable character model replacements in Graphics settings.";
            string visibility = !RenderOptions.ShowCustomCosmetics
                ? "Cosmetics are hidden by Show custom cosmetics in Graphics settings. Your selection still saves."
                : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off
                    ? "Cosmetic effect quality is Off: skins display, but armor and death effects are hidden."
                    : RenderOptions.BrightSkins
                        ? "Bright Skins overrides other players' cosmetics in matches. Your own weapon and this preview retain cosmetics."
                        : "Team colors remain visible. First-person effects are reduced to keep your aim clear.";
            return new(CosmeticPersistence.Get(hunter), CosmeticPersistence.IsPending(hunter), modes.ToImmutable(),
                RenderOptions.CharacterModelReplacements, modelMessage, visibility);
        }

        public bool IsUnlocked(Hunter hunter, CosmeticDefinition cosmetic, out string reason)
        {
            // The existing catalog and license customization have no per-item ownership gate.
            // Every currently shipped item is available; do not fabricate an entitlement service.
            bool available = cosmetic.Unlock is UnlockType.Default or UnlockType.Always;
            reason = available ? "" : cosmetic.UnlockText;
            return available;
        }

        public async Task<HunterEquipResult> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellationToken)
        {
#if !MPHREAD_SERVER
            var result = await HunterLicenseClient.UpdateCosmeticAsync(hunter, loadout, cancellationToken).ConfigureAwait(false);
            return new(result.Success, result.Message);
#else
            CosmeticPersistence.Equip(hunter, loadout);
            await Task.CompletedTask;
            return new(false, "EQUIPPED LOCALLY / NOT SYNCED — account synchronization is unavailable in this build.");
#endif
        }
    }
}
