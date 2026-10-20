using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Cosmetics;

namespace MphRead.Mods.Launcher;

/// <summary>
/// Read/commit through the existing cosmetics persistence and authenticated
/// Hunter License pipeline. The UI does not mint entitlement or wire IDs.
/// </summary>
public interface ILauncherCosmeticEquipService
{
    CosmeticLoadout Capture(Hunter hunter);
    bool PendingSync(Hunter hunter);
    Task<string> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken token);
}

public sealed record LauncherCosmeticSnapshot(Hunter Hunter, CosmeticLoadout Draft,
    CosmeticLoadout Equipped, bool Dirty, bool Busy, bool PendingSync,
    string SkinName, string ArmorName, string DeathName, string BeamName, string BeamToneName, string Status);

/// <summary>
/// Bounded, owner-thread native cosmetic draft. All async completions are
/// accepted by Pump on the owner, with a generation and cancellation fence.
/// </summary>
public sealed class LauncherCosmeticDraftController : IDisposable
{
    private readonly ILauncherCosmeticEquipService _service;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private Hunter _hunter;
    private CosmeticLoadout _original, _draft;
    private CancellationTokenSource? _cancel;
    private Task<string>? _save;
    private CosmeticLoadout? _submitted;
    private int _epoch;
    private bool _disposed;
    private string _status = "CHOOSE COSMETICS // EQUIP TO SAVE";

    public LauncherCosmeticDraftController(ILauncherCosmeticEquipService service, Hunter hunter)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        if ((uint)hunter >= 7) throw new ArgumentOutOfRangeException(nameof(hunter));
        _hunter = hunter;
        _original = CosmeticCatalog.Resolve(hunter, service.Capture(hunter));
        _draft = _original;
    }

    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Native cosmetic drafts belong to the launcher owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public LauncherCosmeticSnapshot Current()
    {
        Verify();
        return new(_hunter, _draft, _original, _draft != _original, _save != null,
            _service.PendingSync(_hunter),
            CosmeticCatalog.ResolveSkin(_draft.SkinKey, _hunter).DisplayName,
            CosmeticCatalog.ResolveArmor(_draft.ArmorEffectKey).DisplayName,
            CosmeticCatalog.ResolveDeath(_draft.DeathEffectKey).DisplayName,
            CosmeticCatalog.ResolveBeam(_draft.BeamEffectKey).DisplayName,
            BeamPalette.Names[_draft.BeamColor],
            _status);
    }

    public bool SwitchHunter(Hunter hunter)
    {
        Verify();
        if ((uint)hunter >= 7 || _save != null || _draft != _original) return false;
        if (hunter == _hunter) return true;
        _hunter = hunter;
        _original = CosmeticCatalog.Resolve(hunter, _service.Capture(hunter));
        _draft = _original;
        _status = "CHOOSE COSMETICS FOR " + hunter.ToString().ToUpperInvariant();
        return true;
    }

    private bool Step(string kind)
    {
        Verify();
        if (_save != null) return false;
        string[] keys = kind switch
        {
            "skin" => CosmeticCatalog.Skins.Where(x => x.Hunter == null || x.Hunter == _hunter)
                .Select(x => x.Key).ToArray(),
            "armor" => CosmeticCatalog.ArmorEffects.Select(x => x.Key).ToArray(),
            "death" => CosmeticCatalog.DeathPresentations.Select(x => x.Key).ToArray(),
            "beam" => CosmeticCatalog.BeamEffects.Select(x => x.Key).ToArray(),
            _ => Array.Empty<string>()
        };
        if (keys.Length == 0) return false;
        string key = kind switch
        {
            "skin" => _draft.SkinKey, "armor" => _draft.ArmorEffectKey,
            "beam" => _draft.BeamEffectKey, _ => _draft.DeathEffectKey
        };
        int current = Array.IndexOf(keys, key);
        string next = keys[(current + 1) % keys.Length];
        _draft = kind switch
        {
            "skin" => _draft with { SkinKey = next },
            "armor" => _draft with { ArmorEffectKey = next },
            "beam" => _draft with { BeamEffectKey = next },
            _ => _draft with { DeathEffectKey = next }
        };
        _status = "LOCAL COSMETIC DRAFT // NOT EQUIPPED";
        return true;
    }

    public bool NextSkin() => Step("skin");
    public bool NextArmor() => Step("armor");
    public bool NextDeath() => Step("death");
    public bool NextBeam() => Step("beam");
    public bool SelectSkin(int index)
    {
        Verify();
        if (_save != null || index < 0) return false;
        var choices = CosmeticCatalog.Skins.Where(x => x.Hunter == null || x.Hunter == _hunter).ToArray();
        if (index >= choices.Length) return false;
        _draft = _draft with { SkinKey = choices[index].Key };
        _status = "LOCAL SKIN DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool SelectArmor(int index)
    {
        Verify();
        if (_save != null || index < 0 || index >= CosmeticCatalog.ArmorEffects.Count) return false;
        _draft = _draft with { ArmorEffectKey = CosmeticCatalog.ArmorEffects[index].Key };
        _status = "LOCAL ARMOR DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool SelectDeath(int index)
    {
        Verify();
        if (_save != null || index < 0 || index >= CosmeticCatalog.DeathPresentations.Count) return false;
        _draft = _draft with { DeathEffectKey = CosmeticCatalog.DeathPresentations[index].Key };
        _status = "LOCAL DEATH DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool SelectBeam(int index)
    {
        Verify();
        if (_save != null || index < 0 || index >= CosmeticCatalog.BeamEffects.Count) return false;
        _draft = _draft with { BeamEffectKey = CosmeticCatalog.BeamEffects[index].Key };
        _status = "LOCAL BEAM DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool SelectBeamColor(int index)
    {
        Verify();
        if (_save != null || index < 0 || index >= BeamPalette.Names.Count) return false;
        _draft = _draft with { BeamColor = (byte)index };
        _status = "LOCAL BEAM COLOR DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool NextBeamColor()
    {
        Verify();
        if (_save != null) return false;
        _draft = _draft with { BeamColor = (byte)((_draft.BeamColor + 1) % BeamPalette.Names.Count) };
        _status = "LOCAL BEAM DRAFT // NOT EQUIPPED";
        return true;
    }

    /// <summary>Stage the native loadout; persistence still requires explicit Equip.</summary>
    public bool SetDefaultLoadout()
    {
        Verify();
        if (_save != null) return false;
        _draft = CosmeticLoadout.Default;
        _status = "DEFAULT COSMETIC DRAFT // EQUIP TO SAVE";
        return true;
    }

    public bool Reset()
    {
        Verify();
        if (_save != null) return false;
        _original = CosmeticCatalog.Resolve(_hunter, _service.Capture(_hunter));
        _draft = _original;
        _status = "COSMETIC DRAFT DISCARDED";
        return true;
    }

    /// <summary>Validate the current draft without changing it or starting persistence.</summary>
    public bool CanEquip(out string reason)
    {
        Verify();
        reason = "";
        if (_save != null) reason = "SAVING COSMETICS // WAIT BEFORE EQUIPPING";
        else if (_draft != _original && CosmeticCatalog.Resolve(_hunter, _service.Capture(_hunter)) != _original)
            reason = "LOADOUT CHANGED ELSEWHERE // DISCARD TO REFRESH";
        else if (CosmeticCatalog.Resolve(_hunter, _draft) != _draft)
            reason = "INVALID COSMETIC KEY // SELECT AN AVAILABLE OPTION";
        return reason.Length == 0;
    }

    public bool Equip()
    {
        Verify();
        if (!CanEquip(out string reason)) { _status = reason; return false; }
        if (_draft == _original)
        {
            _status = _service.PendingSync(_hunter)
                ? "ALREADY EQUIPPED LOCALLY // ACCOUNT SYNC PENDING" : "ALREADY EQUIPPED";
            return true;
        }
        CosmeticLoadout toSubmit = _draft;
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        ++_epoch;
        _submitted = toSubmit;
        try
        {
            _save = _service.EquipAsync(_hunter, toSubmit, cancel.Token)
                ?? throw new InvalidOperationException("Cosmetic equip service returned no task.");
            _status = "EQUIPPING LOCALLY // CHECKING ACCOUNT SYNC";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _cancel = null; cancel.Dispose(); _save = null; _submitted = null;
            _status = "EQUIP FAILED // " + error.Message;
            return false;
        }
    }

    /// <summary>Never block the native/GL owner while HTTPS is in flight.</summary>
    public void Pump()
    {
        Verify();
        Task<string>? task = _save;
        if (task == null || !task.IsCompleted) return;
        _save = null;
        _cancel?.Dispose();
        _cancel = null;
        CosmeticLoadout submitted = _submitted ?? _draft;
        _submitted = null;
        CosmeticLoadout persisted = CosmeticCatalog.Resolve(_hunter, _service.Capture(_hunter));
        if (persisted == submitted)
        {
            // Local persistence can succeed even when subsequent synchronization fails.
            _original = persisted;
            _draft = persisted;
            _status = task.IsCanceled ? "EQUIPPED LOCALLY // ACCOUNT SYNC CANCELLED"
                : task.IsFaulted ? "EQUIPPED LOCALLY // ACCOUNT SYNC FAILED: " +
                    (task.Exception?.GetBaseException().Message ?? "UNKNOWN ERROR")
                : task.Result;
        }
        else
        {
            // Keep both the submitted draft and its original CAS base. An external
            // write must still require Discard rather than becoming our new baseline.
            _draft = submitted;
            _status = task.IsCanceled ? "EQUIP CANCELLED // DRAFT KEPT"
                : task.IsFaulted ? "EQUIP FAILED // DRAFT KEPT: " +
                    (task.Exception?.GetBaseException().Message ?? "UNKNOWN ERROR")
                : "LOADOUT NOT EQUIPPED // DRAFT KEPT // " + task.Result;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Verify();
        _disposed = true;
        _epoch++;
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
        _submitted = null;
        _save = null; // asynchronous callbacks never mutate controller state
    }
}
