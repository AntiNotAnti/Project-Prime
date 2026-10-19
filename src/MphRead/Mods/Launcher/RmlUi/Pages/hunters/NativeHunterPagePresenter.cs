#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Hunters;

/// <summary>
/// Independent native Home Hunter picker. The active lobby still uses its
/// own authoritative choice/roster/low-tier rules, never this local draft.
/// </summary>
internal sealed class NativeHunterPagePresenter : IDisposable
{
    private readonly RmlUiPageManager _pages;
    private readonly LauncherHunterChoiceController _controller;
    private readonly LauncherCosmeticDraftController _cosmetics;
    private readonly LauncherHunterPreviewController _preview = new();
    private RmlUiDocumentToken _page;
    private long _revision;
    private bool _disposed;
    private string _navigationMessage = "";
    private string _cosmeticVisibility = "";

    public bool Closed { get; private set; }
    public bool OpenFullLicenseRequested { get; private set; }
    public RmlUiDocumentToken PreviewDocument => _disposed ? default : _page;
    public LauncherHunterPreviewSnapshot PreviewState => _preview.Current();
    public (Hunter Hunter, int Suit, CosmeticLoadout Cosmetics) PreviewSelection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _controller.Current().Choice;
            return (selected.Hunter, selected.Suit, _preview.Current().CompareNative
                ? CosmeticLoadout.Default : _cosmetics.Current().Draft);
        }
    }
    public bool CanNavigateAway => !_controller.Current().Dirty
        && !_cosmetics.Current().Dirty && !_cosmetics.Current().Busy;

    public NativeHunterPagePresenter(RmlUiPageManager pages, ILauncherHunterChoiceStore store,
        ILauncherCosmeticEquipService? cosmeticService = null)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new LauncherHunterChoiceController(store);
        _cosmetics = new LauncherCosmeticDraftController(cosmeticService ?? new CurrentCosmeticEquipService(),
            _controller.Current().Choice.Hunter);
    }

    public void Open(bool focusBeamGallery = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("native-hunter",
            "pages/hunters/native-picker.rml", focusBeamGallery ? "hunter_beam0" : "hunter_choice0"));
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Document != _page || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.HunterSelect => SelectHunter(intent.Argument),
            RmlUiIntentKind.HunterSuit => _controller.SelectSuit(intent.Argument),
            RmlUiIntentKind.HunterApply => _controller.Apply(),
            RmlUiIntentKind.HunterResetPreview => Discard(),
            RmlUiIntentKind.HunterCancel => CloseIfClean(),
            RmlUiIntentKind.HunterOpen => FullIfClean(),
            RmlUiIntentKind.HunterSkinNext => ChangeCosmetics(_cosmetics.NextSkin),
            RmlUiIntentKind.HunterArmorNext => ChangeCosmetics(_cosmetics.NextArmor),
            RmlUiIntentKind.HunterDeathNext => NextDeath(),
            RmlUiIntentKind.HunterBeamNext => ChangeCosmetics(_cosmetics.NextBeam),
            RmlUiIntentKind.HunterBeamColorNext => ChangeCosmetics(_cosmetics.NextBeamColor),
            RmlUiIntentKind.HunterBeamSelect => ChangeCosmetics(() => _cosmetics.SelectBeam(intent.Argument)),
            RmlUiIntentKind.HunterBeamColorSelect => ChangeCosmetics(() => _cosmetics.SelectBeamColor(intent.Argument)),
            RmlUiIntentKind.HunterCosmeticsReset => ChangeCosmetics(_cosmetics.Reset),
            RmlUiIntentKind.HunterLoadoutDefault => DefaultLoadout(),
            RmlUiIntentKind.HunterCosmeticsSave => _cosmetics.Equip(),
            RmlUiIntentKind.HunterPreviewMode => _preview.SelectMode(intent.Argument),
            RmlUiIntentKind.HunterPreviewDeath => _preview.PreviewDeath(),
            RmlUiIntentKind.HunterRotate => _preview.Rotate(intent.Argument),
            RmlUiIntentKind.HunterZoom => _preview.Zoom(intent.Argument),
            RmlUiIntentKind.HunterLoopDeath => _preview.ToggleLoopDeath(),
            RmlUiIntentKind.HunterCompareNative => _preview.ToggleCompareNative(),
            RmlUiIntentKind.HunterPreviewReset => _preview.Reset(),
            _ => false
        };
        if (accepted)
        {
            _navigationMessage = "";
            Refresh();
        }
        return accepted;
    }

    private bool SelectHunter(int index)
    {
        if (_cosmetics.Current().Dirty || _cosmetics.Current().Busy) return false;
        if (!_controller.SelectHunter(index)) return false;
        if (!_cosmetics.SwitchHunter(_controller.Current().Choice.Hunter)) return false;
        _preview.ClearComparison();
        return true;
    }

    private bool ChangeCosmetics(Func<bool> change)
    {
        if (!change()) return false;
        _preview.ClearComparison();
        return true;
    }

    private bool DefaultLoadout()
    {
        if (!ChangeCosmetics(_cosmetics.SetDefaultLoadout)) return false;
        return _preview.Reset();
    }

    private bool NextDeath()
    {
        if (!ChangeCosmetics(_cosmetics.NextDeath)) return false;
        _preview.PreviewDeath();
        return true;
    }

    private bool Discard()
    {
        if (_cosmetics.Current().Busy) return false;
        _cosmetics.Reset();
        _controller.Discard();
        _cosmetics.SwitchHunter(_controller.Current().Choice.Hunter);
        _preview.ClearComparison();
        return true;
    }

    public void Pump()
    {
        if (_disposed) return;
        var previous = _cosmetics.Current();
        _cosmetics.Pump();
        if (previous != _cosmetics.Current())
        {
            _preview.ClearComparison();
            Refresh();
        }
        else if (_cosmeticVisibility != CosmeticVisibility()) Refresh();
    }

    private static string CosmeticVisibility()
        => !RenderOptions.ShowCustomCosmetics
            ? "Custom cosmetics are hidden in Graphics settings. Your selection still saves."
            : RenderOptions.CosmeticQuality == CosmeticEffectQuality.Off
                ? "Cosmetic quality is Off: skins display; armor and death effects are hidden."
                : RenderOptions.BrightSkins
                    ? "Bright Skins overrides other players' cosmetics in matches. Your weapon and this preview retain cosmetics."
                    : "Team-colored panels stay visible in team matches. First-person effects are reduced to keep your aim clear.";

    private bool CloseIfClean()
    {
        if (!PrepareNavigation()) return false;
        Closed = true;
        return true;
    }

    public void Back() => CloseIfClean();

    public bool PrepareNavigation()
    {
        if (CanNavigateAway) return true;
        _navigationMessage = _cosmetics.Current().Busy
            ? "SAVING COSMETICS // WAIT BEFORE LEAVING"
            : "UNSAVED CHANGES // EQUIP OR DISCARD BEFORE LEAVING";
        Refresh();
        return false;
    }

    private bool FullIfClean()
    {
        if (!PrepareNavigation()) return false;
        OpenFullLicenseRequested = true;
        return true;
    }

    private void Refresh()
    {
        var state = _controller.Current();
        var cosmetic = _cosmetics.Current();
        var preview = _preview.Current();
        if (CanNavigateAway) _navigationMessage = "";
        _cosmeticVisibility = CosmeticVisibility();
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_hunter_name"] = RmlUiBindingValue.FromText(
                state.Choice.Hunter.ToString().ToUpperInvariant()),
            ["native_hunter_status"] = RmlUiBindingValue.FromText(
                _navigationMessage.Length == 0 ? state.Status : _navigationMessage),
            ["native_hunter_hint"] = RmlUiBindingValue.FromText(
                (GameFiles.Ready ? "LIVE ENGINE 3D PREVIEW" :
                    "3D PREVIEW REQUIRES CONFIGURED GAME FILES") +
                " // SUIT " + (state.Choice.Suit + 1) + " // YOUR NEXT DEPLOYMENT"),
            ["hunter_switch_hint"] = RmlUiBindingValue.FromText(cosmetic.Busy
                ? "SAVING COSMETICS // WAIT TO SWITCH HUNTERS"
                : cosmetic.Dirty ? "EQUIP OR DISCARD COSMETICS TO SWITCH HUNTERS"
                : "Choose a Hunter for your next deployment."),
            ["hunter_preview_state"] = RmlUiBindingValue.FromText(preview.CompareNative
                ? "NATIVE LOADOUT // COMPARISON" : cosmetic.Dirty
                    ? "COSMETIC DRAFT // NOT EQUIPPED" : "EQUIPPED COSMETICS"),
            ["hunter_skin_description"] = RmlUiBindingValue.FromText(
                CosmeticCatalog.ResolveSkin(cosmetic.Draft.SkinKey, cosmetic.Hunter).Description),
            ["hunter_armor_description"] = RmlUiBindingValue.FromText(
                CosmeticCatalog.ResolveArmor(cosmetic.Draft.ArmorEffectKey).Description),
            ["hunter_death_description"] = RmlUiBindingValue.FromText(
                CosmeticCatalog.ResolveDeath(cosmetic.Draft.DeathEffectKey).Description),
            ["hunter_beam_description"] = RmlUiBindingValue.FromText(
                CosmeticCatalog.ResolveBeam(cosmetic.Draft.BeamEffectKey).Description),
            ["hunter_beam_preview_text"] = RmlUiBindingValue.FromText(
                cosmetic.Draft.BeamEffectKey switch
                {
                    "beam.ion_fracture" => "==== X ====",
                    "beam.void_rift" => "|=====|====",
                    "beam.quantum_split" => "==<=== >==",
                    "beam.alimbic" => "==[ ]==[ ]",
                    "beam.omega" => "==O==O==O=",
                    "beam.arc" => "==/\\==/\\==",
                    "beam.solar" => "==^==^==^=",
                    "beam.cryo" => "==<>==<>==",
                    "beam.phantom" => "============",
                    _ => "============"
                }),
            ["disabled:hunter_discard"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || (!state.Dirty && !cosmetic.Dirty)),
            ["disabled:hunter_apply"] = RmlUiBindingValue.FromBoolean(!state.Dirty),
            ["hunter_skin"] = RmlUiBindingValue.FromText("SKIN // " + cosmetic.SkinName),
            ["hunter_armor"] = RmlUiBindingValue.FromText("ARMOR EFFECT // " + cosmetic.ArmorName),
            ["hunter_death"] = RmlUiBindingValue.FromText("DEATH PRESENTATION // " + cosmetic.DeathName),
            ["hunter_beam"] = RmlUiBindingValue.FromText("INSTA-GIB BEAM // " + cosmetic.BeamName),
            ["hunter_beam_color"] = RmlUiBindingValue.FromText("BEAM COLOR // " + cosmetic.BeamToneName),
            ["hunter_cosmetics_state"] = RmlUiBindingValue.FromText(
                cosmetic.Busy ? cosmetic.Status
                : cosmetic.Dirty ? "LOCAL DRAFT // NOT EQUIPPED"
                : cosmetic.PendingSync ? "EQUIPPED LOCALLY // ACCOUNT SYNC PENDING"
                : cosmetic.Status),
            ["hunter_cosmetics_visibility"] = RmlUiBindingValue.FromText(_cosmeticVisibility),
            ["disabled:hunter_skin"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_armor"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_death"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_beam"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_beam_color"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_loadout_default"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_cosmetics_save"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || !cosmetic.Dirty),
            ["disabled:hunter_cosmetics_reset"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || !cosmetic.Dirty),
            ["class:hunter_preview_biped:selected"] = RmlUiBindingValue.FromBoolean(preview.Mode == SkinContext.Biped),
            ["class:hunter_preview_weapon:selected"] = RmlUiBindingValue.FromBoolean(preview.Mode == SkinContext.ViewModel),
            ["class:hunter_preview_alt:selected"] = RmlUiBindingValue.FromBoolean(preview.Mode == SkinContext.AltForm),
            ["class:hunter_loop_death:selected"] = RmlUiBindingValue.FromBoolean(preview.LoopDeath),
            ["class:hunter_compare_native:selected"] = RmlUiBindingValue.FromBoolean(preview.CompareNative),
            ["hunter_compare_native"] = RmlUiBindingValue.FromText(preview.CompareNative
                ? "SHOW DRAFT" : "COMPARE NATIVE"),
            ["hunter_loop_death"] = RmlUiBindingValue.FromText(preview.LoopDeath ? "LOOP DEATH // ON" : "LOOP DEATH // OFF"),
            ["disabled:hunter_zoom_out"] = RmlUiBindingValue.FromBoolean(preview.Zoom <= 0.8f),
            ["disabled:hunter_zoom_in"] = RmlUiBindingValue.FromBoolean(preview.Zoom >= 1.2f)
        };
        for (int i = 0; i < LauncherHunterChoiceController.Playable.Length; i++)
        {
            data["class:hunter_choice" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                LauncherHunterChoiceController.Playable[i] == state.Choice.Hunter);
            data["disabled:hunter_choice" + i] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || cosmetic.Dirty);
        }
        for (int i = 0; i < 4; i++)
            data["class:hunter_suit" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                i == state.Choice.Suit);
        for (int i = 0; i < CosmeticCatalog.BeamEffects.Count; i++)
        {
            data["class:hunter_beam" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Draft.BeamEffectKey == CosmeticCatalog.BeamEffects[i].Key);
            data["disabled:hunter_beam" + i] = RmlUiBindingValue.FromBoolean(cosmetic.Busy);
        }
        for (int i = 0; i < BeamPalette.Names.Count; i++)
        {
            data["class:hunter_beam_color" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Draft.BeamColor == i);
            data["disabled:hunter_beam_color" + i] = RmlUiBindingValue.FromBoolean(cosmetic.Busy);
        }
        _pages.Present(_page, ++_revision, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
        _controller.Dispose();
        _cosmetics.Dispose();
        _preview.Dispose();
    }
}

internal sealed class CurrentCosmeticEquipService : ILauncherCosmeticEquipService
{
    public CosmeticLoadout Capture(Hunter hunter) => CosmeticPersistence.Get(hunter);
    public bool PendingSync(Hunter hunter) => CosmeticPersistence.IsPending(hunter);
    public async Task<string> EquipAsync(Hunter hunter, CosmeticLoadout draft, CancellationToken cancellation)
    {
        HunterLicenseActionResult result = await HunterLicenseClient.UpdateCosmeticAsync(
            hunter, draft, cancellation).ConfigureAwait(false);
        return result.Message;
    }
}

internal sealed class LauncherHunterChoiceStore : ILauncherHunterChoiceStore
{
    public LauncherHunterChoice Capture()
        => new(LauncherPrefs.LastHunter, LauncherPrefs.LastColor);

    public void Save(LauncherHunterChoice choice)
    {
        LauncherHunterChoice before = Capture();
        LauncherPrefs.LastHunter = choice.Hunter;
        LauncherPrefs.LastColor = choice.Suit;
        if (!LauncherPrefs.TrySave())
        {
            LauncherPrefs.LastHunter = before.Hunter;
            LauncherPrefs.LastColor = before.Suit;
            throw new InvalidOperationException("Hunter profile could not be saved.");
        }
    }
}
#endif
