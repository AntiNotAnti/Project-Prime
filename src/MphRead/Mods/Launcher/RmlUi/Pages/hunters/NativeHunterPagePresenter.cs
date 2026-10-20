#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
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
    private string _saveMessage = "";
    private int _category;
    private readonly RmlUiHost? _host;
    private bool _previewDrag;
    private float _dragX;
    private string? _restoreFocus;

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
        ILauncherCosmeticEquipService? cosmeticService = null, RmlUiHost? host = null)
    {
        _host = host;
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new LauncherHunterChoiceController(store);
        _cosmetics = new LauncherCosmeticDraftController(cosmeticService ?? new CurrentCosmeticEquipService(),
            _controller.Current().Choice.Hunter);
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("native-hunter",
            "pages/hunters/native-picker.rml", "hunter_choice0"));
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_disposed || intent.Document != _page || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        int beamSelection = intent.Argument;
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.HunterSelect => SelectHunter(intent.Argument),
            RmlUiIntentKind.HunterSuit => !_cosmetics.Current().Busy && _controller.SelectSuit(intent.Argument),
            RmlUiIntentKind.HunterApply => ApplyLoadout(),
            RmlUiIntentKind.HunterResetPreview => Discard(),
            RmlUiIntentKind.HunterCancel => CloseIfClean(),
            RmlUiIntentKind.HunterOpen => FullIfClean(),
            RmlUiIntentKind.HunterSkinNext => ChangeCosmetics(_cosmetics.NextSkin),
            RmlUiIntentKind.HunterArmorNext => ChangeCosmetics(_cosmetics.NextArmor),
            RmlUiIntentKind.HunterDeathNext => NextDeath(),
            RmlUiIntentKind.HunterBeamNext => ChangeCosmetics(_cosmetics.NextBeam),
            RmlUiIntentKind.HunterBeamColorNext => ChangeCosmetics(_cosmetics.NextBeamColor),
            RmlUiIntentKind.HunterBeamSelect => ChangeCosmetics(() => _cosmetics.SelectBeam(beamSelection)),
            RmlUiIntentKind.HunterBeamColorSelect => ChangeCosmetics(() => _cosmetics.SelectBeamColor(beamSelection)),
            RmlUiIntentKind.HunterSkinSelect => ChangeCosmetics(() => _cosmetics.SelectSkin(beamSelection)),
            RmlUiIntentKind.HunterArmorSelect => ChangeCosmetics(() => _cosmetics.SelectArmor(beamSelection)),
            RmlUiIntentKind.HunterDeathSelect => SelectDeath(beamSelection),
            RmlUiIntentKind.HunterCosmeticCategory => SelectCategory(beamSelection),
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
            if (intent.Kind != RmlUiIntentKind.HunterApply) _saveMessage = "";
            Refresh();
        }
        return accepted;
    }

    private bool ApplyLoadout()
    {
        var result = LauncherLoadoutApply.Apply(_controller, _cosmetics);
        _saveMessage = result.Status;
        if (result.Accepted) _restoreFocus = "hunter_category" + _category;
        // An authorized command is consumed even when persistence fails, so the
        // explicit failure/partial-save status is presented and the draft survives.
        return true;
    }

    private bool SelectCategory(int category)
    {
        if (category is < 0 or > 4) return false;
        _category = category;
        return true;
    }

    private bool SelectDeath(int index)
    {
        if (!ChangeCosmetics(() => _cosmetics.SelectDeath(index))) return false;
        return _preview.PreviewDeath();
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
        if (_pages.Top != _page) CancelPreviewGesture();
        var previous = _cosmetics.Current();
        _cosmetics.Pump();
        if (previous != _cosmetics.Current())
        {
            _preview.ClearComparison();
            if (previous.Busy && !_cosmetics.Current().Busy) _saveMessage = _cosmetics.Current().Status;
            Refresh();
        }
        else if (_cosmeticVisibility != CosmeticVisibility()) Refresh();
    }

    public void AfterUpdate()
    {
        if (_restoreFocus == null) return;
        if (PreviewForeground && String.IsNullOrEmpty(_host!.FocusedElement()))
            _host.FocusDocument(_page, _restoreFocus);
        _restoreFocus = null;
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
                _navigationMessage.Length > 0 ? _navigationMessage : _saveMessage.Length > 0 ? _saveMessage
                    : state.Dirty || cosmetic.Dirty ? "LOADOUT DRAFT // EQUIP OR DISCARD" : "LOADOUT EQUIPPED"),
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
            ["disabled:hunter_discard"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || (!state.Dirty && !cosmetic.Dirty)),
            ["disabled:hunter_apply"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy || (!state.Dirty && !cosmetic.Dirty)),
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
        {
            data["class:hunter_suit" + i + ":selected"] = RmlUiBindingValue.FromBoolean(i == state.Choice.Suit);
            data["disabled:hunter_suit" + i] = RmlUiBindingValue.FromBoolean(cosmetic.Busy);
        }
        for (int i = 0; i < 5; i++)
        {
            data["class:hunter_category" + i + ":selected"] = RmlUiBindingValue.FromBoolean(i == _category);
            data["visible:hunter_catalog" + i] = RmlUiBindingValue.FromBoolean(i == _category);
        }
        void Option(string prefix, int index, string label, bool selected, string? swatch = null)
        {
            string id = "hunter_" + prefix + "_option" + index;
            data["hunter_" + prefix + "_label" + index] = RmlUiBindingValue.FromText(label);
            data["class:" + id + ":selected"] = RmlUiBindingValue.FromBoolean(selected);
            data["disabled:" + id] = RmlUiBindingValue.FromBoolean(cosmetic.Busy);
            data["visible:hunter_" + prefix + "_swatch" + index] = RmlUiBindingValue.FromBoolean(swatch != null);
            if (swatch != null) data["color:hunter_" + prefix + "_swatch" + index] = RmlUiBindingValue.FromText(swatch);
        }
        var skins = CosmeticCatalog.Skins.Where(x => x.Hunter == null || x.Hunter == cosmetic.Hunter).ToArray();
        for (int i = 0; i < skins.Length; i++)
            Option("skin", i, skins[i].DisplayName, cosmetic.Draft.SkinKey == skins[i].Key);
        for (int i = 0; i < CosmeticCatalog.ArmorEffects.Count; i++)
        {
            var item = CosmeticCatalog.ArmorEffects[i];
            Option("armor", i, item.DisplayName, cosmetic.Draft.ArmorEffectKey == item.Key, Hex(item.PrimaryColor));
        }
        for (int i = 0; i < CosmeticCatalog.DeathPresentations.Count; i++)
        {
            var item = CosmeticCatalog.DeathPresentations[i];
            Option("death", i, item.DisplayName, cosmetic.Draft.DeathEffectKey == item.Key, Hex(item.LightEffect));
        }
        for (int i = 0; i < CosmeticCatalog.BeamEffects.Count; i++)
        {
            var item = CosmeticCatalog.BeamEffects[i];
            Option("beam", i, item.DisplayName, cosmetic.Draft.BeamEffectKey == item.Key, Hex(item.PrimaryColor));
            Option("beam_color", i, BeamPalette.Names[i], cosmetic.Draft.BeamColor == i,
                Hex(BeamPalette.Resolve((byte)i, CosmeticCatalog.ResolveBeam(cosmetic.Draft.BeamEffectKey).PrimaryColor)));
        }
        _pages.Present(_page, ++_revision, data);
    }

    private static string Hex(OpenTK.Mathematics.Vector3 color)
        => $"#{Math.Clamp((int)(color.X * 255), 0, 255):x2}{Math.Clamp((int)(color.Y * 255), 0, 255):x2}{Math.Clamp((int)(color.Z * 255), 0, 255):x2}";

    private bool PreviewForeground => !_disposed && _host != null && _pages.Top == _page
        && _host.CurrentInputDocument == _page;

    private bool PreviewContains(float x, float y)
        => PreviewForeground && Single.IsFinite(x) && Single.IsFinite(y)
            && _host!.TryGetElementBounds(_page, "native_hunter_preview", out var left, out var top, out var width, out var height)
            && x >= left && y >= top && x < left + width && y < top + height;

    public void CancelPreviewGesture() => _previewDrag = false;

    public bool PreviewPointerButton(float x, float y, bool down)
    {
        if (!down) { bool held = _previewDrag; CancelPreviewGesture(); return held; }
        CancelPreviewGesture();
        if (!PreviewContains(x, y)) return false;
        _previewDrag = true; _dragX = x; return true;
    }

    public bool PreviewPointerMove(float x, float y)
    {
        if (!_previewDrag) return false;
        if (!PreviewForeground || !Single.IsFinite(x) || !Single.IsFinite(y))
        { CancelPreviewGesture(); return false; }
        if (!_host!.TryGetElementBounds(_page, "native_hunter_preview", out _, out _, out var width, out _))
        { CancelPreviewGesture(); return false; }
        _preview.RotateBy(Math.Clamp((x - _dragX) / width, -1, 1) * MathF.Tau);
        _dragX = x;
        return true;
    }

    public bool PreviewWheel(float x, float y, double delta)
    {
        if (!PreviewContains(x, y) || !Double.IsFinite(delta)) return false;
        _preview.ZoomBy((float)Math.Clamp(delta * .05, -.4, .4));
        Refresh(); return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelPreviewGesture();
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
