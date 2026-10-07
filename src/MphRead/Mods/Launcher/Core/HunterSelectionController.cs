using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher.Core
{
    public readonly record struct HunterCosmeticOption(string Key, string Name, string Description,
        bool Unlocked, string UnavailableReason);

    public sealed record HunterSelectionProfile(CosmeticLoadout Equipped, bool PendingSync,
        ImmutableArray<SkinContext> AvailableCustomModelModes, bool CustomModelsEnabled,
        string CustomModelMessage, string VisibilityMessage);

    public readonly record struct HunterEquipResult(bool Synced, string Message);

    /// <summary>Persistence and account boundary; no renderer or controls are required.</summary>
    public interface IHunterSelectionBackend
    {
        Hunter PreferredHunter { get; }
        byte PreferredColor { get; }
        void SaveIdentity(Hunter hunter, byte color);
        HunterSelectionProfile Capture(Hunter hunter);
        bool IsUnlocked(Hunter hunter, CosmeticDefinition cosmetic, out string reason);
        Task<HunterEquipResult> EquipAsync(Hunter hunter, CosmeticLoadout loadout, CancellationToken cancellationToken);
    }

    public sealed record HunterSelectionSnapshot
    {
        public Guid Lifetime { get; init; }
        public ulong Version { get; init; }
        public Hunter Hunter { get; init; }
        public byte Color { get; init; }
        public Hunter? AcknowledgedHunter { get; init; }
        public byte? AcknowledgedColor { get; init; }
        public bool IdentityPending { get; init; }
        public bool CanApplyIdentity { get; init; }
        public ImmutableArray<Hunter> AllowedHunters { get; init; } = ImmutableArray<Hunter>.Empty;
        public CosmeticLoadout Draft { get; init; } = CosmeticLoadout.Default;
        public CosmeticLoadout Equipped { get; init; } = CosmeticLoadout.Default;
        public CosmeticLoadout PreviewLoadout => CompareNative ? CosmeticLoadout.Default : Draft;
        public ImmutableArray<HunterCosmeticOption> Skins { get; init; } = ImmutableArray<HunterCosmeticOption>.Empty;
        public ImmutableArray<HunterCosmeticOption> ArmorEffects { get; init; } = ImmutableArray<HunterCosmeticOption>.Empty;
        public ImmutableArray<HunterCosmeticOption> DeathPresentations { get; init; } = ImmutableArray<HunterCosmeticOption>.Empty;
        public SkinContext PreviewMode { get; init; }
        public float Yaw { get; init; }
        public float Zoom { get; init; } = 1;
        public bool LoopDeath { get; init; }
        public int DeathRequest { get; init; }
        public bool CompareNative { get; init; }
        public bool Saving { get; init; }
        public bool PendingSync { get; init; }
        public bool CanEquip { get; init; }
        public string Status { get; init; } = "";
        public string CommandError { get; init; } = "";
        public string VisibilityMessage { get; init; } = "";
        public ImmutableArray<SkinContext> AvailableCustomModelModes { get; init; } = ImmutableArray<SkinContext>.Empty;
        public bool CustomModelsEnabled { get; init; }
        public string CustomModelMessage { get; init; } = "";
    }

    /// <summary>Local drafts survive server refreshes. Only explicit apply/equip commands persist them.</summary>
    public sealed class HunterSelectionController : IDisposable
    {
        private readonly int _ownerThread = Environment.CurrentManagedThreadId;
        private readonly Guid _lifetime = Guid.NewGuid();
        private readonly IHunterSelectionBackend _backend;
        private readonly LobbySessionController? _lobby;
        private readonly Dictionary<Hunter, CosmeticLoadout> _drafts = new();
        private readonly ConcurrentQueue<(Guid Lifetime, Hunter Hunter, CosmeticLoadout Loadout, HunterEquipResult Result)> _completed = new();
        private readonly CancellationTokenSource _cancellation = new();
        private Hunter _hunter;
        private byte _color;
        private SkinContext _mode;
        private float _yaw, _zoom = 1;
        private bool _loopDeath, _compareNative, _saving, _disposed;
        private int _deathRequest;
        private string _status = "", _error = "";
        private (Hunter Hunter, byte Color)? _appliedIdentity;
        private ulong _version;
        private HunterSelectionSnapshot _snapshot = new();

        public HunterSelectionController(LobbySessionController? lobby = null, IHunterSelectionBackend? backend = null)
        {
            _lobby = lobby;
            _backend = backend ?? new LauncherHunterSelectionBackend();
            Hunter preferred = lobby?.Snapshot().LocalHunter ?? _backend.PreferredHunter;
            _hunter = (uint)preferred < 7 ? preferred : Hunter.Samus;
            if ((uint)preferred >= 7)
                _error = $"The saved Hunter ({preferred}) is unavailable. Previewing Samus; choose and apply a valid Hunter.";
            _color = (byte)((lobby?.Snapshot().LocalColor ?? _backend.PreferredColor) & 3);
            EnsureDraft();
            Refresh();
        }

        public HunterSelectionSnapshot Snapshot { get { EnsureOwner(); return _snapshot; } }

        public void Pump()
        {
            EnsureOwner();
            if (_disposed) return;
            while (_completed.TryDequeue(out var completion))
            {
                if (completion.Lifetime != _lifetime) continue;
                _saving = false;
                // A user may continue previewing another Hunter while this save completes.
                if (completion.Hunter == _hunter) _status = completion.Result.Message;
                if (!completion.Result.Synced) _error = completion.Result.Message;
            }
            if (_appliedIdentity.HasValue && _lobby != null && !_lobby.Snapshot().IdentityPending)
            { _status = _lobby.Snapshot().IdentityMessage; _appliedIdentity = null; }
            Refresh();
        }

        public LobbyActionResult SelectHunter(Hunter hunter) => Action(() =>
        {
            if (!AllowedHunters().Contains(hunter)) return Reject("That Hunter is unavailable under the current lobby rules.");
            _hunter = hunter; EnsureDraft();
            if (_mode == SkinContext.Halfturret && hunter != Hunter.Weavel) _mode = SkinContext.Biped;
            ResetPreviewCore(); _status = "";
            return LobbyActionResult.Ok;
        });

        public LobbyActionResult SelectSuit(byte color) => Action(() =>
        {
            if (color > 3) return Reject("Choose one of the four existing suit colors.");
            _color = color; return LobbyActionResult.Ok;
        });

        public LobbyActionResult SelectSkin(string key) => Select(CosmeticCatalog.Skins
            .Where(s => s.Hunter == null || s.Hunter == _hunter), key, value => _drafts[_hunter] = Draft with { SkinKey = value });
        public LobbyActionResult SelectArmor(string key) => Select(CosmeticCatalog.ArmorEffects, key,
            value => _drafts[_hunter] = Draft with { ArmorEffectKey = value });
        public LobbyActionResult SelectDeath(string key) => Select(CosmeticCatalog.DeathPresentations, key,
            value => { _drafts[_hunter] = Draft with { DeathEffectKey = value }; _deathRequest++; });

        public LobbyActionResult SetPreviewMode(SkinContext mode) => Action(() =>
        {
            if (mode is not (SkinContext.Biped or SkinContext.ViewModel or SkinContext.AltForm)
                && !(mode == SkinContext.Halfturret && _hunter == Hunter.Weavel))
                return Reject("That preview mode is unavailable for this Hunter.");
            _mode = mode; ResetPreviewCore(); return LobbyActionResult.Ok;
        });
        public LobbyActionResult Rotate(float delta) => Action(() =>
        {
            if (!Single.IsFinite(delta)) return Reject("Rotation must be a finite value.");
            _yaw = (_yaw + delta) % 360; return LobbyActionResult.Ok;
        });
        public LobbyActionResult Zoom(float delta) => Action(() =>
        {
            if (!Single.IsFinite(delta)) return Reject("Zoom must be a finite value.");
            _zoom = Math.Clamp(_zoom + delta, 0.5f, 2.5f); return LobbyActionResult.Ok;
        });
        public LobbyActionResult SetLoopDeath(bool value) => Action(() =>
        { _loopDeath = value; if (value) _deathRequest++; return LobbyActionResult.Ok; });
        public LobbyActionResult PreviewDeath() => Action(() => { _deathRequest++; return LobbyActionResult.Ok; });
        public LobbyActionResult CompareNative(bool value) => Action(() =>
        { _compareNative = value; ResetPreviewCore(); return LobbyActionResult.Ok; });
        public LobbyActionResult ResetPreview() => Action(() => { ResetPreviewCore(); return LobbyActionResult.Ok; });
        public LobbyActionResult ResetLoadout() => Action(() =>
        { _drafts[_hunter] = CosmeticLoadout.Default; ResetPreviewCore(); _status = ""; return LobbyActionResult.Ok; });

        public LobbyActionResult ApplyIdentity() => Action(() =>
        {
            if (!AllowedHunters().Contains(_hunter)) return Reject("Choose a Hunter allowed by the current lobby rules.");
            LobbyActionResult result;
            if (_lobby != null)
                result = _lobby.Dispatch(_lobby.Intent(LobbyIntentKind.Identify) with { Hunter = _hunter, Color = _color });
            else { _backend.SaveIdentity(_hunter, _color); result = LobbyActionResult.Ok; }
            if (result.Accepted)
            {
                if (_lobby != null) _appliedIdentity = (_hunter, _color);
                _status = _lobby == null ? "Hunter and suit saved for the next session." : "Waiting for server to confirm Hunter and suit.";
            }
            return result;
        });

        public LobbyActionResult Equip() => Action(() =>
        {
            if (_saving) return Reject("Wait for the current cosmetic save to finish.");
            foreach (CosmeticDefinition definition in DefinitionsFor(Draft))
                if (!_backend.IsUnlocked(_hunter, definition, out string reason)) return Reject(reason);
            _saving = true; _status = "Saving cosmetics…";
            Hunter hunter = _hunter; CosmeticLoadout submitted = Draft;
            try { _ = CompleteEquipAsync(hunter, submitted, _backend.EquipAsync(hunter, submitted, _cancellation.Token)); }
            catch (Exception ex) { _saving = false; return Reject("Cosmetic save failed: " + ex.Message); }
            return LobbyActionResult.Ok;
        });

        private async Task CompleteEquipAsync(Hunter hunter, CosmeticLoadout loadout, Task<HunterEquipResult> work)
        {
            HunterEquipResult result;
            try { result = await work.ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { result = new(false, "LOCAL / NOT SYNCED — " + ex.Message); }
            if (!_cancellation.IsCancellationRequested) _completed.Enqueue((_lifetime, hunter, loadout, result));
        }

        private LobbyActionResult Select<T>(IEnumerable<T> definitions, string key, Action<string> select) where T : CosmeticDefinition
            => Action(() =>
            {
                CosmeticDefinition? definition = definitions.FirstOrDefault(value => value.Key == key);
                if (definition == null) return Reject("That cosmetic is unavailable for the selected Hunter.");
                if (!_backend.IsUnlocked(_hunter, definition, out string reason)) return Reject(reason);
                select(key); _compareNative = false; _status = ""; return LobbyActionResult.Ok;
            });

        private LobbyActionResult Action(Func<LobbyActionResult> action)
        {
            EnsureOwner();
            if (_disposed) return LobbyActionResult.Reject("This Hunter selection has closed.");
            LobbyActionResult result = action();
            _error = result.Accepted ? "" : result.Message;
            Refresh();
            return result;
        }

        private LobbyActionResult Reject(string message) => LobbyActionResult.Reject(message);
        private CosmeticLoadout Draft => _drafts[_hunter];
        private void EnsureDraft() { if (!_drafts.ContainsKey(_hunter)) _drafts[_hunter] = _backend.Capture(_hunter).Equipped; }
        private void ResetPreviewCore() { _yaw = 0; _zoom = 1; _loopDeath = false; }
        private ImmutableArray<Hunter> AllowedHunters() => HunterRules.Pool(_lobby?.Snapshot().Match?.LowTier ?? false).ToImmutableArray();
        private IEnumerable<CosmeticDefinition> DefinitionsFor(CosmeticLoadout loadout)
        {
            yield return CosmeticCatalog.ResolveSkin(loadout.SkinKey, _hunter);
            yield return CosmeticCatalog.ResolveArmor(loadout.ArmorEffectKey);
            yield return CosmeticCatalog.ResolveDeath(loadout.DeathEffectKey);
        }
        private ImmutableArray<HunterCosmeticOption> Options(IEnumerable<CosmeticDefinition> definitions) => definitions
            .Select(value => { bool unlocked = _backend.IsUnlocked(_hunter, value, out string reason);
                return new HunterCosmeticOption(value.Key, value.DisplayName, value.Description, unlocked, unlocked ? "" : reason); }).ToImmutableArray();

        private void Refresh()
        {
            HunterSelectionProfile profile = _backend.Capture(_hunter);
            LobbySnapshot? lobby = _lobby?.Snapshot();
            ImmutableArray<Hunter> allowed = AllowedHunters();
            HunterSelectionSnapshot next = new()
            {
                Lifetime = _lifetime, Version = _version, Hunter = _hunter, Color = _color,
                AllowedHunters = allowed, AcknowledgedHunter = lobby?.AcknowledgedLocalHunter,
                AcknowledgedColor = lobby?.AcknowledgedLocalColor, IdentityPending = lobby?.IdentityPending == true,
                CanApplyIdentity = allowed.Contains(_hunter) && (lobby == null || LobbyPresentation.From(lobby).CanChangeIdentity),
                Draft = Draft, Equipped = profile.Equipped, Saving = _saving, PendingSync = profile.PendingSync,
                CanEquip = !_saving && !_disposed && DefinitionsFor(Draft).All(value => _backend.IsUnlocked(_hunter, value, out _)),
                PreviewMode = _mode, Yaw = _yaw, Zoom = _zoom,
                LoopDeath = _loopDeath, DeathRequest = _deathRequest, CompareNative = _compareNative,
                Status = _saving ? "Saving cosmetics…" : _status.Length > 0 ? _status
                    : Draft != profile.Equipped ? "PREVIEW / NOT EQUIPPED"
                    : profile.PendingSync ? "EQUIPPED LOCALLY / NOT SYNCED" : "EQUIPPED",
                CommandError = _error, VisibilityMessage = profile.VisibilityMessage,
                Skins = Options(CosmeticCatalog.Skins.Where(value => value.Hunter == null || value.Hunter == _hunter)),
                ArmorEffects = Options(CosmeticCatalog.ArmorEffects), DeathPresentations = Options(CosmeticCatalog.DeathPresentations),
                AvailableCustomModelModes = profile.AvailableCustomModelModes, CustomModelsEnabled = profile.CustomModelsEnabled,
                CustomModelMessage = profile.CustomModelMessage
            };
            bool unchanged = next.AllowedHunters.SequenceEqual(_snapshot.AllowedHunters)
                && next.Skins.SequenceEqual(_snapshot.Skins) && next.ArmorEffects.SequenceEqual(_snapshot.ArmorEffects)
                && next.DeathPresentations.SequenceEqual(_snapshot.DeathPresentations)
                && next.AvailableCustomModelModes.SequenceEqual(_snapshot.AvailableCustomModelModes)
                && (next with { AllowedHunters = _snapshot.AllowedHunters, Skins = _snapshot.Skins,
                    ArmorEffects = _snapshot.ArmorEffects, DeathPresentations = _snapshot.DeathPresentations,
                    AvailableCustomModelModes = _snapshot.AvailableCustomModelModes }) == _snapshot;
            if (!unchanged) _snapshot = next with { Version = ++_version };
        }

        private void EnsureOwner()
        { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Hunter selection must be accessed on its owner thread."); }

        public void Dispose()
        {
            EnsureOwner();
            if (_disposed) return;
            _disposed = true; _cancellation.Cancel(); _cancellation.Dispose();
            while (_completed.TryDequeue(out _)) { }
            _snapshot = _snapshot with { CanEquip = false, CanApplyIdentity = false, Saving = false };
        }
    }
}
