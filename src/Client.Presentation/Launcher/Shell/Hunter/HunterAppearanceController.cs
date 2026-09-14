using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Cosmetics;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Launcher.Gui;

public sealed record HunterAppearanceState(
    Hunter Hunter,
    CosmeticLoadout Equipped,
    CosmeticLoadout Preview,
    ImmutableArray<SkinDefinition> Skins,
    ImmutableArray<ArmorEffectDefinition> ArmorEffects,
    ImmutableArray<DeathEffectDefinition> DeathEffects,
    string? Error = null)
{
    public bool Dirty => Preview != Equipped;
}

public interface IHunterAppearanceStore
{
    CosmeticLoadout Load(Hunter hunter);
    void Save(Hunter hunter, CosmeticLoadout loadout);
}

internal readonly record struct AuthenticatedCosmeticOperation(
    AccountSession Account,
    PlayerId Principal,
    long IdentityGeneration)
{
    internal bool IsAccountCurrent(AccountSession? currentAccount,
        PlayerId? currentPrincipal, long currentGeneration)
        => ReferenceEquals(Account, currentAccount)
            && Account.IsSignedIn
            && Account.Identity?.PlayerId == Principal
            && currentPrincipal == Principal
            && currentGeneration == IdentityGeneration;
}

/// <summary>
/// Guest/offline per-Hunter appearance storage. Stable cosmetic keys are kept
/// in a small local document and invalid data always falls back to defaults.
/// </summary>
public sealed class LocalHunterAppearanceStore : IHunterAppearanceStore
{
    private const int MaximumFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _path;

    public LocalHunterAppearanceStore(string? path = null)
        => _path = path ?? Path.Combine(GameFiles.Root, "guest-cosmetic-loadouts.json");

    public CosmeticLoadout Load(Hunter hunter)
    {
        ValidateHunter(hunter);
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists)
                return CosmeticLoadout.DefaultFor(hunter);
            if (file.Length is <= 0 or > MaximumFileBytes)
                return GuestFallback(hunter, "invalid preference size");
            AppearanceDocument? document = JsonSerializer.Deserialize<AppearanceDocument>(
                File.ReadAllBytes(_path), JsonOptions);
            string key = HunterKey(hunter);
            if (document?.Hunters?.TryGetValue(key, out CosmeticLoadout loadout) == true
                && CosmeticId.IsValid(loadout.SkinKey)
                && CosmeticId.IsValid(loadout.ArmorEffectKey)
                && CosmeticId.IsValid(loadout.DeathEffectKey))
            {
                return loadout;
            }
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or JsonException)
        {
            return GuestFallback(hunter, error.GetType().Name);
        }
        return GuestFallback(hunter, "invalid preference data");
    }

    public void Save(Hunter hunter, CosmeticLoadout loadout)
    {
        ValidateHunter(hunter);
        if (!CosmeticId.IsValid(loadout.SkinKey)
            || !CosmeticId.IsValid(loadout.ArmorEffectKey)
            || !CosmeticId.IsValid(loadout.DeathEffectKey))
        {
            throw new ArgumentException("Appearance loadout contains an invalid stable key.",
                nameof(loadout));
        }

        AppearanceDocument document = LoadDocument();
        document.Hunters[HunterKey(hunter)] = loadout;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumFileBytes)
            throw new InvalidOperationException("Appearance preferences exceed the local size limit.");

        string? directory = Path.GetDirectoryName(_path);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temporary = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private AppearanceDocument LoadDocument()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists)
                return new AppearanceDocument();
            if (file.Length is <= 0 or > MaximumFileBytes)
                return GuestDocumentFallback("invalid preference size");
            AppearanceDocument? document = JsonSerializer.Deserialize<AppearanceDocument>(
                File.ReadAllBytes(_path), JsonOptions);
            return document?.Hunters == null
                ? GuestDocumentFallback("invalid preference data") : document;
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or JsonException)
        {
            return GuestDocumentFallback(error.GetType().Name);
        }
    }

    private static void ValidateHunter(Hunter hunter)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
    }

    private static string HunterKey(Hunter hunter)
        => hunter.ToString().ToLowerInvariant();

    private static CosmeticLoadout GuestFallback(Hunter hunter, string reason)
    {
        DebugLog.Line("cosmetics/skin",
            $"Guest appearance fallback for {hunter}: {reason}.");
        return CosmeticLoadout.DefaultFor(hunter);
    }

    private static AppearanceDocument GuestDocumentFallback(string reason)
    {
        DebugLog.Line("cosmetics/skin",
            $"Guest appearance document fallback: {reason}.");
        return new AppearanceDocument();
    }

    private sealed class AppearanceDocument
    {
        public int Format { get; set; } = 1;
        public Dictionary<string, CosmeticLoadout> Hunters { get; set; }
            = new(StringComparer.Ordinal);
    }
}

/// <summary>
/// Mutable selection owner for the otherwise immutable Hunter Appearance
/// presentation. It performs no Backend or Node calls.
/// </summary>
public sealed class HunterAppearanceController
{
    private readonly IHunterAppearanceStore _guestStore;
    private readonly Dictionary<Hunter, CosmeticLoadout> _accountLoadouts = [];
    private readonly ImmutableArray<SkinDefinition> _skins;
    private readonly ImmutableArray<ArmorEffectDefinition> _armorEffects;
    private readonly ImmutableArray<DeathEffectDefinition> _deathEffects;
    private HunterAppearanceState _state;
    private PlayerId? _accountPrincipal;

    public HunterAppearanceController(IEnumerable<SkinDefinition> skins,
        IEnumerable<ArmorEffectDefinition> armorEffects,
        IEnumerable<DeathEffectDefinition> deathEffects,
        IHunterAppearanceStore? store = null,
        Hunter initialHunter = Hunter.Samus)
    {
        ArgumentNullException.ThrowIfNull(skins);
        ArgumentNullException.ThrowIfNull(armorEffects);
        ArgumentNullException.ThrowIfNull(deathEffects);
        _guestStore = store ?? new LocalHunterAppearanceStore();
        _skins = skins.OrderBy(value => value.Id).ThenBy(value => value.Key,
            StringComparer.Ordinal).ToImmutableArray();
        _armorEffects = new[]
            {
                new ArmorEffectDefinition(0, CosmeticKeys.NoArmorEffect, "None")
            }
            .Concat(armorEffects.Where(value => value.Id != 0))
            .OrderBy(value => value.Id).ThenBy(value => value.Key,
                StringComparer.Ordinal).ToImmutableArray();
        _deathEffects = new[]
            {
                new DeathEffectDefinition(0, CosmeticKeys.DefaultDeathEffect,
                    "Default", 1, DeathBodyMode.Default)
            }
            .Concat(deathEffects.Where(value => value.Id != 0))
            .OrderBy(value => value.Id).ThenBy(value => value.Key,
                StringComparer.Ordinal).ToImmutableArray();
        _state = CreateState(initialHunter);
    }

    public HunterAppearanceController(CosmeticCatalog catalog,
        IHunterAppearanceStore? store = null,
        Hunter initialHunter = Hunter.Samus)
        : this((catalog ?? throw new ArgumentNullException(nameof(catalog))).Skins.Values,
            catalog.ArmorEffects.Values, catalog.DeathEffects.Values, store, initialHunter)
    {
    }

    public HunterAppearanceState State => _state;
    public bool UsesAccountStorage => _accountPrincipal.HasValue;
    public PlayerId? AccountPrincipal => _accountPrincipal;
    public event EventHandler? Changed;

    public void UseGuestStorage()
    {
        if (!_accountPrincipal.HasValue) return;
        _accountPrincipal = null;
        _accountLoadouts.Clear();
        Publish(CreateState(_state.Hunter));
    }

    public void UseAccountStorage(PlayerId principal)
    {
        if (principal.IsEmpty) throw new ArgumentException(
            "An account principal is required.", nameof(principal));
        if (_accountPrincipal == principal) return;
        _accountPrincipal = principal;
        _accountLoadouts.Clear();
        Publish(CreateState(_state.Hunter));
    }

    public CosmeticLoadout GetEquipped(Hunter hunter)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
        if (_state.Hunter == hunter) return _state.Equipped;
        ImmutableArray<SkinDefinition> hunterSkins = SkinsFor(hunter);
        return Normalize(hunter, Load(hunter), hunterSkins,
            _armorEffects, DeathsFor(hunter));
    }

    public void SelectHunter(Hunter hunter)
    {
        if (_state.Hunter == hunter) return;
        Publish(CreateState(hunter));
    }

    public void SelectPreviousSkin() => SelectSkin(-1);
    public void SelectNextSkin() => SelectSkin(1);
    public void SelectPreviousArmorEffect() => SelectArmorEffect(-1);
    public void SelectNextArmorEffect() => SelectArmorEffect(1);
    public void SelectPreviousDeathEffect() => SelectDeathEffect(-1);
    public void SelectNextDeathEffect() => SelectDeathEffect(1);

    public void Equip()
    {
        CosmeticLoadout normalized = Normalize(_state.Hunter, _state.Preview,
            _state.Skins, _armorEffects, _deathEffects);
        try
        {
            Save(_state.Hunter, normalized);
            Publish(_state with { Equipped = normalized, Preview = normalized, Error = null });
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            Publish(_state with { Error = error.Message });
        }
    }

    /// <summary>Imports a Backend-authoritative selection into account-scoped
    /// memory. It is never persisted to or recovered from the guest store.</summary>
    public void ApplyAuthoritative(Hunter hunter, CosmeticLoadout loadout)
    {
        if (!_accountPrincipal.HasValue)
            throw new InvalidOperationException(
                "An authenticated appearance principal is required.");
        ImmutableArray<SkinDefinition> hunterSkins = SkinsFor(hunter);
        ImmutableArray<DeathEffectDefinition> hunterDeaths = DeathsFor(hunter);
        CosmeticLoadout normalized = Normalize(hunter, loadout, hunterSkins,
            _armorEffects, hunterDeaths);
        if (normalized != loadout)
            throw new ArgumentException("The official cosmetic loadout is invalid for this Hunter.",
                nameof(loadout));
        _accountLoadouts[hunter] = normalized;
        if (_state.Hunter == hunter)
            Publish(_state with { Equipped = normalized, Preview = normalized, Error = null });
    }

    public void ResetPreview()
        => Publish(_state with { Preview = _state.Equipped, Error = null });

    private HunterAppearanceState CreateState(Hunter hunter)
    {
        if (hunter is < Hunter.Samus or > Hunter.Weavel)
            throw new ArgumentOutOfRangeException(nameof(hunter));
        ImmutableArray<SkinDefinition> hunterSkins = SkinsFor(hunter);
        ImmutableArray<DeathEffectDefinition> hunterDeaths = DeathsFor(hunter);
        CosmeticLoadout loadout = Normalize(hunter, Load(hunter), hunterSkins,
            _armorEffects, hunterDeaths);
        return new HunterAppearanceState(hunter, loadout, loadout, hunterSkins,
            _armorEffects, hunterDeaths);
    }

    private ImmutableArray<SkinDefinition> SkinsFor(Hunter hunter)
        => new[]
        {
            new SkinDefinition(0, CosmeticKeys.DefaultSkin(hunter), "Base", hunter)
        }
        .Concat(_skins.Where(value => value.Hunter == hunter && value.Id != 0))
        .ToImmutableArray();

    private ImmutableArray<DeathEffectDefinition> DeathsFor(Hunter hunter)
        => _deathEffects.Where(value => value.Hunter == null
            || value.Hunter == hunter).ToImmutableArray();

    private void SelectSkin(int delta)
    {
        string key = Cycle(_state.Skins, _state.Preview.SkinKey, delta,
            value => value.Key, CosmeticKeys.DefaultSkin(_state.Hunter));
        Publish(_state with { Preview = _state.Preview with { SkinKey = key }, Error = null });
    }

    private void SelectArmorEffect(int delta)
    {
        string key = Cycle(_armorEffects, _state.Preview.ArmorEffectKey, delta,
            value => value.Key, CosmeticKeys.NoArmorEffect);
        Publish(_state with { Preview = _state.Preview with { ArmorEffectKey = key }, Error = null });
    }

    private void SelectDeathEffect(int delta)
    {
        string key = Cycle(_state.DeathEffects, _state.Preview.DeathEffectKey, delta,
            value => value.Key, CosmeticKeys.DefaultDeathEffect);
        Publish(_state with { Preview = _state.Preview with { DeathEffectKey = key }, Error = null });
    }

    private static string Cycle<T>(ImmutableArray<T> values, string current,
        int delta, Func<T, string> key, string fallback)
    {
        if (values.IsDefaultOrEmpty) return fallback;
        int index = -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (String.Equals(key(values[i]), current, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        index = index < 0 ? 0 : (index + delta % values.Length + values.Length) % values.Length;
        return key(values[index]);
    }

    private static CosmeticLoadout Normalize(Hunter hunter, CosmeticLoadout requested,
        ImmutableArray<SkinDefinition> skins,
        ImmutableArray<ArmorEffectDefinition> armor,
        ImmutableArray<DeathEffectDefinition> deaths)
    {
        string skin = skins.Any(value => value.Key == requested.SkinKey)
            ? requested.SkinKey : CosmeticKeys.DefaultSkin(hunter);
        if (!skins.Any(value => value.Key == skin) && !skins.IsDefaultOrEmpty)
            skin = skins[0].Key;
        string armorKey = armor.Any(value => value.Key == requested.ArmorEffectKey)
            ? requested.ArmorEffectKey : CosmeticKeys.NoArmorEffect;
        string death = deaths.Any(value => value.Key == requested.DeathEffectKey)
            ? requested.DeathEffectKey : CosmeticKeys.DefaultDeathEffect;
        return new CosmeticLoadout(skin, armorKey, death);
    }

    private void Publish(HunterAppearanceState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private CosmeticLoadout Load(Hunter hunter)
        => _accountPrincipal.HasValue
            ? _accountLoadouts.TryGetValue(hunter, out CosmeticLoadout loadout)
                ? loadout : CosmeticLoadout.DefaultFor(hunter)
            : _guestStore.Load(hunter);

    private void Save(Hunter hunter, CosmeticLoadout loadout)
    {
        if (_accountPrincipal.HasValue) _accountLoadouts[hunter] = loadout;
        else _guestStore.Save(hunter, loadout);
    }
}
