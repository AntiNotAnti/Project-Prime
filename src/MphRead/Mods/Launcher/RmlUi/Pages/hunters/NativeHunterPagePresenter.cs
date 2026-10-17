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
    private RmlUiDocumentToken _page;
    private long _revision;
    private bool _disposed;

    public bool Closed { get; private set; }
    public bool OpenFullLicenseRequested { get; private set; }
    public RmlUiDocumentToken PreviewDocument => _disposed ? default : _page;
    public (Hunter Hunter, int Suit, CosmeticLoadout Cosmetics) PreviewSelection
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var selected = _controller.Current().Choice;
            return (selected.Hunter, selected.Suit, _cosmetics.Current().Draft);
        }
    }
    public bool CanNavigateAway => !_controller.Current().Dirty
        && !_cosmetics.Current().Dirty && !_cosmetics.Current().Busy;

    public NativeHunterPagePresenter(RmlUiPageManager pages, ILauncherHunterChoiceStore store)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new LauncherHunterChoiceController(store);
        _cosmetics = new LauncherCosmeticDraftController(new CurrentCosmeticEquipService(),
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
        bool accepted = intent.Kind switch
        {
            RmlUiIntentKind.HunterSelect => SelectHunter(intent.Argument),
            RmlUiIntentKind.HunterSuit => _controller.SelectSuit(intent.Argument),
            RmlUiIntentKind.HunterApply => _controller.Apply(),
            RmlUiIntentKind.HunterResetPreview => Discard(),
            RmlUiIntentKind.HunterCancel => CloseIfClean(),
            RmlUiIntentKind.HunterOpen => FullIfClean(),
            RmlUiIntentKind.HunterSkinNext => _cosmetics.NextSkin(),
            RmlUiIntentKind.HunterArmorNext => _cosmetics.NextArmor(),
            RmlUiIntentKind.HunterDeathNext => _cosmetics.NextDeath(),
            RmlUiIntentKind.HunterCosmeticsReset => _cosmetics.Reset(),
            RmlUiIntentKind.HunterCosmeticsSave => _cosmetics.Equip(),
            _ => false
        };
        if (accepted) Refresh();
        return accepted;
    }

    private bool SelectHunter(int index)
    {
        if (_cosmetics.Current().Dirty || _cosmetics.Current().Busy) return false;
        if (!_controller.SelectHunter(index)) return false;
        return _cosmetics.SwitchHunter(_controller.Current().Choice.Hunter);
    }

    private bool Discard()
    {
        if (_cosmetics.Current().Busy) return false;
        _cosmetics.Reset();
        _controller.Discard();
        _cosmetics.SwitchHunter(_controller.Current().Choice.Hunter);
        return true;
    }

    public void Pump()
    {
        if (_disposed) return;
        var previous = _cosmetics.Current();
        _cosmetics.Pump();
        if (previous != _cosmetics.Current()) Refresh();
    }

    private bool CloseIfClean()
    {
        if (!CanNavigateAway) return false;
        Closed = true;
        return true;
    }

    private bool FullIfClean()
    {
        if (!CanNavigateAway) return false;
        OpenFullLicenseRequested = true;
        return true;
    }

    private void Refresh()
    {
        var state = _controller.Current();
        var cosmetic = _cosmetics.Current();
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_hunter_name"] = RmlUiBindingValue.FromText(
                state.Choice.Hunter.ToString().ToUpperInvariant()),
            ["native_hunter_status"] = RmlUiBindingValue.FromText(state.Status),
            ["native_hunter_hint"] = RmlUiBindingValue.FromText(
                "SUIT " + (state.Choice.Suit + 1) + " // YOUR NEXT DEPLOYMENT"),
            ["disabled:hunter_apply"] = RmlUiBindingValue.FromBoolean(!state.Dirty),
            ["hunter_skin"] = RmlUiBindingValue.FromText("SKIN // " + cosmetic.SkinName),
            ["hunter_armor"] = RmlUiBindingValue.FromText("ARMOR EFFECT // " + cosmetic.ArmorName),
            ["hunter_death"] = RmlUiBindingValue.FromText("DEATH PRESENTATION // " + cosmetic.DeathName),
            ["hunter_cosmetics_state"] = RmlUiBindingValue.FromText(
                cosmetic.Busy ? cosmetic.Status
                : cosmetic.Dirty ? "LOCAL DRAFT // NOT EQUIPPED"
                : cosmetic.PendingSync ? "EQUIPPED LOCALLY // ACCOUNT SYNC PENDING"
                : cosmetic.Status),
            ["disabled:hunter_skin"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_armor"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_death"] = RmlUiBindingValue.FromBoolean(cosmetic.Busy),
            ["disabled:hunter_cosmetics_save"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || !cosmetic.Dirty),
            ["disabled:hunter_cosmetics_reset"] = RmlUiBindingValue.FromBoolean(
                cosmetic.Busy || !cosmetic.Dirty)
        };
        for (int i = 0; i < LauncherHunterChoiceController.Playable.Length; i++)
            data["class:hunter_choice" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                LauncherHunterChoiceController.Playable[i] == state.Choice.Hunter);
        for (int i = 0; i < 4; i++)
            data["class:hunter_suit" + i + ":selected"] = RmlUiBindingValue.FromBoolean(
                i == state.Choice.Suit);
        _pages.Present(_page, ++_revision, data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
        _controller.Dispose();
        _cosmetics.Dispose();
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
