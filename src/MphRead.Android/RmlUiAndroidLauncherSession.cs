#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;

namespace MphRead.Droid;

internal sealed record RmlUiAndroidLauncherGeometry(int Width, int Height);
internal sealed record RmlUiAndroidLauncherOptions(Task<string> AssetRoot, string[] Rooms, RmlUiOfflineRules Rules,
    Action<GameView, Guid> Opened,
    Action<GameView, RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest>> OfflineRequested,
    Action<GameView, Guid, bool> Closed,
    Action<GameView, RmlUiAndroidLocalWitness> SceneRetired,
    Func<GameView, Guid, RmlUiAdventureSaveChange, CancellationToken, Task<RmlUiAdventureSaveResult>> SaveChangeRequested,
    Func<GameView, Guid, RmlUiOfflineRulesChange, Action<RmlUiOfflineRules?>, CancellationToken, Task<RmlUiOfflineRulesSaveResult>> RuleChangeRequested);

/// <summary>Configured local launcher only, rendered by the existing GameView EGL owner.</summary>
internal sealed class RmlUiAndroidLauncherSession
{
    private readonly RmlUiAndroidDrawListSession _session = new();
    private readonly RmlUiAndroidLauncherOptions _options;
    private readonly GameView _view;
    private readonly RmlUiOfflineRulesCache _rules;
    private readonly CancellationTokenSource _ruleSaveStop = new();
    private RmlUiPageManager? _manager;
    private RmlUiLauncherPages? _pages;
    private RmlUiMenuController? _controller;
    private RmlUiAndroidLocalHunterPage? _hunter;
    private NativeCampaignShipPage? _ship;
    private long _revision;
    private int _back, _stop, _focused = 1;
    private bool _leased, _inputSuspended;
    private volatile bool _opened, _disposed;
    private bool _quit, _closedNotified, _shutdownSucceeded;
    private RmlUiAndroidLauncherGeometry _geometry = new(0, 0);
    internal RmlUiAndroidLauncherAdmission<RmlUiAndroidLocalLaunchRequest, LaunchPlan> Admission { get; } = new();
    internal RmlUiAndroidInputQueue Input => _session.Input;
    internal RmlUiAndroidLauncherGeometry Geometry => Volatile.Read(ref _geometry);
    internal bool OwnsInput => Volatile.Read(ref _stop) == 0 && !_disposed
        && Admission.Current.State is RmlUiAndroidLauncherAdmissionState.Open or RmlUiAndroidLauncherAdmissionState.AwaitingUi;
    internal bool Opened => _opened && !_disposed && Volatile.Read(ref _stop) == 0;
    internal bool CanManageSaves(Guid lifetime) => Opened && lifetime == Lifetime
        && Volatile.Read(ref _focused) != 0 && Admission.Current.State == RmlUiAndroidLauncherAdmissionState.Open;
    internal Guid Lifetime => Admission.Current.Lifetime;
    internal void NotifySceneRetired(RmlUiAndroidLocalWitness witness) => _options.SceneRetired(_view, witness);
    internal RmlUiAndroidLauncherSession(GameView view, RmlUiAndroidLauncherOptions options)
        => (_view, _options, _rules) = (view, options, new(options.Rules));
    internal void RequestBack() => Interlocked.Exchange(ref _back, 1);
    internal void SuspendInput() => Input.Suspend();
    internal void SetFocused(bool focused)
    {
        Volatile.Write(ref _focused, focused ? 1 : 0);
        if (!focused) SuspendInput();
    }
    internal void RequestStop()
    { Volatile.Write(ref _stop, 1); _ruleSaveStop.Cancel(); Admission.Retire(); SuspendInput(); }
    internal bool IsCurrent(RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> witness)
        => OwnsInput && Admission.IsCurrent(witness);
    internal bool TryAccept(RmlUiAndroidLauncherRequest<RmlUiAndroidLocalLaunchRequest> witness, LaunchPlan plan)
    {
        if (!OwnsInput || !witness.Request.MatchesPlan(plan, LauncherPrefs.LastColor)
            || !Admission.TryAccept(witness, plan)) return false;
        return true;
    }
    internal bool TryTakePlan(out LaunchPlan plan, out RmlUiAndroidLocalLaunchRequest request)
    {
        bool accepted = Admission.Current.State == RmlUiAndroidLauncherAdmissionState.Accepted;
        if (Admission.TryTakePlan(DisposeOwner, out plan, out request)) return true;
        request = default;
        if (accepted && _disposed && !_shutdownSucceeded)
            throw new InvalidOperationException("Native launcher could not retire safely before local match creation.");
        return false;
    }
    internal void SurfaceLost()
    {
        if (!_opened || _disposed) return;
        try { _session.SuspendInput(); _inputSuspended = true; }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        { Console.WriteLine("[android/rmlui] launcher surface release: " + error.Message); CloseToLegacy(quit: false); }
    }
    internal void SurfaceReady()
    {
        if (!_opened || _disposed || !_inputSuspended || Volatile.Read(ref _focused) == 0) return;
        try { _session.ResumeInput(); _inputSuspended = false; }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        { Console.WriteLine("[android/rmlui] launcher surface resume: " + error.Message); CloseToLegacy(quit: false); }
    }
    internal bool Render(int width, int height, float density)
    {
        if (_disposed) return false;
        if (Volatile.Read(ref _stop) != 0) { DisposeOwner(); return false; }
        try
        {
            if (NetSession.Active || DemoPlayback.IsActive)
                throw new InvalidOperationException("A local launcher cannot replace the current network or replay owner.");
            if (!_opened) Open(width, height, density);
            if (Volatile.Read(ref _stop) != 0) { DisposeOwner(); return false; }
            if (_rules.Read() == null)
            {
                Console.WriteLine("[android/rmlui] local settings owner unavailable; returning to the existing launcher");
                CloseToLegacy(quit: false); return false;
            }
            if (Volatile.Read(ref _focused) == 0 && !_inputSuspended) SurfaceLost();
            else if (Volatile.Read(ref _focused) != 0 && _inputSuspended) SurfaceReady();
            if (_disposed) return false;
            if (Interlocked.Exchange(ref _back, 0) != 0) Back();
            if (_disposed) return false;
            if (_quit) { CloseToLegacy(quit: true); return false; }
            Project();
            _ship?.Refresh();
            _session.Render(width, height, density);
            if (_ship == null && _hunter == null) _pages!.AfterUpdate();
            Volatile.Write(ref _geometry, new(width, height));
            for (int i = 0; i < 64 && Volatile.Read(ref _stop) == 0
                && Admission.Current.State != RmlUiAndroidLauncherAdmissionState.Accepted
                && _session.Host.TryTakeIntent(out var intent); i++)
            {
                if (_ship != null)
                {
                    _ship.Handle(intent);
                    ApplyShipEffect();
                    continue;
                }
                if (_hunter != null)
                {
                    _hunter.Handle(intent);
                    if (_hunter.Closed) { _hunter.Dispose(); _hunter = null; _pages!.Resume(); }
                    continue;
                }
                if (!_pages!.HandleIntent(intent, out var accepted) || accepted.Kind == 0) continue;
                if (accepted.Kind == RmlUiIntentKind.HunterOpen)
                {
                    _pages.Suspend(); _hunter = new(_manager!); _hunter.Open();
                    _pages.PresentChrome(_manager!.Page);
                    continue;
                }
                _controller!.Dispatch(accepted.Kind, accepted.Argument);
                if (_quit) break;
            }
            if (_quit) { CloseToLegacy(quit: true); return false; }
            Project(); _session.Input.Refresh(_session.Host);
            return true;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        {
            Console.WriteLine("[android/rmlui] local launcher fallback: " + error.Message);
            CloseToLegacy(quit: false); return false;
        }
    }
    private void Open(int width, int height, float density)
    {
        if (!RmlUiAndroidPauseNativeLease.TryAcquire(this)) throw new InvalidOperationException("Native UI has not retired its previous owner.");
        _leased = true;
        if (!_session.Initialize(width, height, density, _options.AssetRoot.GetAwaiter().GetResult()))
            throw new InvalidOperationException("Native local launcher could not initialize.");
        _manager = new(_session.Host);
        _pages = new(_session.Host, _manager, RmlUiLauncherCapabilities.ConfiguredLocalAdventureSaves);
        _controller = new(new Services(this));
        Project(); _pages.ShowBaseline();
        if (MatchStart.RecentShipDock is { } dock && !MatchStart.HasPendingCommit)
            OpenShip(dock.Slot, (uint)LauncherPrefs.LastHunter < 7 ? LauncherPrefs.LastHunter : Hunter.Samus);
        _session.Input.Refresh(_session.Host);
        _opened = true; Volatile.Write(ref _geometry, new(width, height));
        _options.Opened(_view, Lifetime);
    }
    private void Project()
    {
        var snapshot = _controller!.Tick();
        if (snapshot.Revision > _revision)
        {
            foreach (var binding in snapshot.Bindings)
                if (binding.Value.Kind == RmlUiBindingKind.Text) _pages!.SetText(binding.Key, binding.Value.Text);
                else _pages!.SetBool(binding.Key, binding.Value.Boolean);
            _revision = snapshot.Revision;
        }
        _pages!.Flush();
    }
    private void Back()
    {
        if (Admission.Current.State == RmlUiAndroidLauncherAdmissionState.AwaitingUi)
        { CloseToLegacy(quit: false); return; }
        if (_ship != null) { _ship.Back(); ApplyShipEffect(); return; }
        if (_hunter != null)
        {
            if (_hunter.Back()) { _hunter.Dispose(); _hunter = null; _pages!.Resume(); }
            return;
        }
        if (_pages!.Back(out var intent))
        { if (intent.Kind != 0) _controller!.Dispatch(intent.Kind, intent.Argument); }
        else _quit = true;
    }
    private void CloseToLegacy(bool quit)
    {
        if (_closedNotified) return;
        _closedNotified = true;
        Guid lifetime = Lifetime;
        DisposeOwner();
        _options.Closed(_view, lifetime, quit);
    }
    internal bool DisposeOwner()
    {
        if (_disposed) return _shutdownSucceeded;
        _disposed = true; RequestStop();
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
            { Console.WriteLine("[android/rmlui] launcher cleanup: " + error.Message); }
        }
        Cleanup(_session.SuspendInput);
        Cleanup(() => _ship?.Dispose()); _ship = null;
        Cleanup(() => _hunter?.Dispose()); _hunter = null;
        Cleanup(() => _controller?.Dispose()); _controller = null;
        Cleanup(() => _pages?.Dispose()); _pages = null;
        Cleanup(() => _manager?.Dispose()); _manager = null;
        bool shutdown = false;
        try { _session.Shutdown(); shutdown = _shutdownSucceeded = true; }
        catch (Exception error) when (error is not (OutOfMemoryException or AccessViolationException))
        { Console.WriteLine("[android/rmlui] launcher native shutdown: " + error.Message); }
        finally { if (_leased) RmlUiAndroidPauseNativeLease.Release(this, shutdown); _leased = false; }
        return _shutdownSucceeded;
    }

    private bool Request(RmlUiAndroidLocalLaunchRequest request)
    {
        if (MatchStart.HasPendingCommit || Menu.NeededSave != SaveWhen.Never
            || !Admission.TryRequest(request, out var witness)) return false;
        _options.OfflineRequested(_view, witness);
        return true;
    }

    private bool OpenShip(byte slot, Hunter hunter)
    {
        if (_ship != null || _hunter != null || (uint)hunter >= 7 || !GameFiles.Ready || MatchStart.HasPendingCommit
            || MatchStart.RecentShipDock is not { } dock || dock.Slot != slot) return false;
        _pages!.Suspend();
        _ship = new(_session.Host, _manager!, new NativeCampaignShipController<LaunchPlan>(
            new CurrentNativeCampaignShipBackend(), new(dock.Slot, (int)dock.Planet), (int)hunter));
        _ship.Open();
        return true;
    }

    private void ApplyShipEffect()
    {
        if (_ship is not { } ship || !ship.TryTakeEffect(out var effect)) return;
        ship.Dispose(); _ship = null;
        _controller!.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
        Project(); _pages!.Resume();
        if (effect.Launch && RmlUiAndroidLocalLaunchRequest.TryShip(effect, out var request)) Request(request);
    }

    private sealed class Services(RmlUiAndroidLauncherSession owner) : IRmlUiMenuServices
    {
        public HubSnapshot Hub() => HubState.Capture();
        public string BuildVersion() => Mods.Update.BuildVersion.Current?.ToString(3) ?? "LOCAL";
        public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false,
            "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
        public IReadOnlyList<string> BuiltInArenaKeys() => owner._options.Rooms;
        public RmlUiOfflineRules? ReadOfflineRules() => owner.CanManageSaves(owner.Lifetime) ? owner._rules.Read() : null;
        public bool OfflineRuleSavesAreAsync => true;
        public async Task<RmlUiOfflineRulesSaveResult> SaveOfflineRulesAsync(RmlUiOfflineRulesChange change, CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, owner._ruleSaveStop.Token);
            return await owner._options.RuleChangeRequested(owner._view, owner.Lifetime, change,
                owner._rules.Publish, linked.Token).ConfigureAwait(false);
        }
        public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
        {
            if (!GameFiles.Ready || !request.ValidateForm(owner._options.Rooms)) return Task.FromResult("INVALID OR UNAVAILABLE LOCAL MATCH");
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                bool valid = MapModeCapabilities.Supports(request.RoomKey, request.Mode,
                    MatchWorldProfile.Resolve(request.Bots + 1), out string reason,
                    request.Bots + 1, allowLocalSource: true);
                token.ThrowIfCancellationRequested();
                return valid ? "" : string.IsNullOrWhiteSpace(reason) ? "THAT ARENA CANNOT RUN THE SELECTED MATCH" : reason;
            }, token);
        }
        public bool LaunchOffline(RmlUiOfflineRequest request)
            => owner.Request(RmlUiAndroidLocalLaunchRequest.ForOffline(request));
        public RmlUiAdventureSlot[] ReadAdventureSlots()
            => Enumerable.Range(1, AdventureSave.SlotCount).Select(slot => RmlUiAndroidLocalLaunchRequest.ReadSlot((byte)slot)).ToArray();
        public bool LaunchAdventure(RmlUiAdventureSlot slot, bool newGame, Hunter hunter)
            => RmlUiAndroidLocalLaunchRequest.TryAdventure(slot, newGame, hunter, out var request) && owner.Request(request);
        public bool CanOpenCampaignShip(byte slot) => GameFiles.Ready && !MatchStart.HasPendingCommit
            && MatchStart.RecentShipDock is { } dock && dock.Slot == slot;
        public bool OpenCampaignShip(byte slot, Hunter hunter) => owner.OpenShip(slot, hunter);
        public bool CanManageAdventureSaves => owner.CanManageSaves(owner.Lifetime)
            && owner._ship == null && owner._hunter == null && !MatchStart.HasPendingCommit && Menu.NeededSave == SaveWhen.Never;
        public bool AdventureSaveChangesAreAsync => true;
        public Task<RmlUiAdventureSaveResult> ManageAdventureSaveAsync(RmlUiAdventureSaveChange change, CancellationToken token)
            => owner._options.SaveChangeRequested(owner._view, owner.Lifetime, change, token);
        public bool NavigateTo(RmlUiRouteArgument route) => route == RmlUiRouteArgument.Home;
        public void Quit() => owner._quit = true;
        public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "LOCAL PLAY ONLY", Array.Empty<RmlUiServerCandidate>()));
        public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
        public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "LOCAL PLAY ONLY"));
        public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, "LOCAL PLAY ONLY"));
        public bool AcceptJoin(LaunchPlan plan) => false;
        public bool OpenCreateLobby() => false;
        public bool OpenStudio() => false;
        public bool SetReady() => false;
        public bool StartMatch() => false;
        public bool LeaveLobby() => false;
        public bool NextHunter() => false;
        public bool NextSuit() => false;
        public bool OpenLobbyDetails() => false;
        public RmlUiRuleDraft? ReadRules() => null;
        public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = "LOCAL PLAY ONLY"; return false; }
    }
}
#endif
