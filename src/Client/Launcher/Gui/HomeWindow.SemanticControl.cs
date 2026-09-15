using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Testing;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class HomeWindow
{
    /// <summary>Creates the optional control owner without exposing shell controllers.</summary>
    internal SemanticControlAdapter? CreateSemanticControlAdapter()
        => SemanticControlAdapter.TryCreate(this);

    /// <summary>
    /// Development-only command owner for the persistent launcher window.
    /// This type deliberately lives behind HomeWindow: callers receive no
    /// GatewayController, PlayController, navigator, or mutable runtime
    /// object. Every mutation is routed through the existing owner and is
    /// revalidated on the UI thread immediately before the owner call.
    /// </summary>
    internal sealed class SemanticControlAdapter : IAsyncDisposable
    {
        private const string EnableVariable = "PRIME_E2E_CONTROL_ENABLE";
        private const string GlobalEnableVariable = "PRIME_E2E_ENABLE";
        private const string EndpointVariable = "PRIME_E2E_CONTROL_ENDPOINT";
        private const string TokenVariable = "PRIME_E2E_CONTROL_TOKEN_FILE";
        private const string SlotVariable = "PRIME_E2E_CLIENT_SLOT";
        private const string LegacySlotVariable = "PRIME_E2E_CLIENT_ID";
        private const string EndpointAVariable = "PRIME_E2E_CONTROL_ENDPOINT_A";
        private const string EndpointBVariable = "PRIME_E2E_CONTROL_ENDPOINT_B";
        private const string TokenAVariable = "PRIME_E2E_CLIENT_A_TOKEN_FILE";
        private const string TokenBVariable = "PRIME_E2E_CLIENT_B_TOKEN_FILE";

        private readonly HomeWindow _owner;
        private readonly SemanticControlPhaseGuard _phaseGuard;
        private readonly SemanticControlDispatcher _dispatcher;
        private readonly SemanticControlServer _server;
        private readonly SemanticSyntheticInputOwner _syntheticInput;
        private readonly SemanticRuntimeCaptureOwner _runtimeCapture;
        private MphRead.SdlGameHost? _sceneHost;
        private int _disposed;

        private SemanticControlAdapter(HomeWindow owner,
            SemanticControlServerOptions options, string clientSlot)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _phaseGuard = new SemanticControlPhaseGuard(CurrentIdentity());
            _syntheticInput = new SemanticSyntheticInputOwner(_phaseGuard.Current);
            _runtimeCapture = new SemanticRuntimeCaptureOwner(_phaseGuard.Current,
                Environment.GetEnvironmentVariable("PRIME_E2E_RUN_DIR"), clientSlot);
            _dispatcher = new SemanticControlDispatcher(DispatchUiAsync,
                DispatchGameplayAsync);
            _server = new SemanticControlServer(options, _phaseGuard,
                (request, cancellationToken) => _dispatcher.DispatchAsync(
                    request, cancellationToken));

            _owner._view.Gateway.Changed += OwnerStateChanged;
            _owner._view.Gateway.IdentityChanged += OwnerIdentityChanged;
            _owner._view.Play.Changed += OwnerPlayChanged;
            _owner._view.Online.Changed += OwnerOnlineChanged;
            _owner._view.Online.Flow.Changed += OwnerFlowChanged;
        }

        /// <summary>
        /// Creates the adapter only when an explicit development switch is
        /// present. Missing endpoint/token configuration is an operator error
        /// once enabled; it must not silently turn into an unauthenticated
        /// listener or a fake E2E client.
        /// </summary>
        internal static SemanticControlAdapter? TryCreate(HomeWindow owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            if (!Enabled()) return null;

            var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [EnableVariable] = Environment.GetEnvironmentVariable(EnableVariable),
                [GlobalEnableVariable] = Environment.GetEnvironmentVariable(GlobalEnableVariable),
                [EndpointVariable] = Environment.GetEnvironmentVariable(EndpointVariable),
                [TokenVariable] = Environment.GetEnvironmentVariable(TokenVariable),
                [SlotVariable] = Environment.GetEnvironmentVariable(SlotVariable),
                [LegacySlotVariable] = Environment.GetEnvironmentVariable(LegacySlotVariable),
                [EndpointAVariable] = Environment.GetEnvironmentVariable(EndpointAVariable),
                [EndpointBVariable] = Environment.GetEnvironmentVariable(EndpointBVariable),
                [TokenAVariable] = Environment.GetEnvironmentVariable(TokenAVariable),
                [TokenBVariable] = Environment.GetEnvironmentVariable(TokenBVariable)
            };
            if (!TryResolveConfiguration(environment, out string endpoint,
                    out string tokenFile))
                throw new InvalidOperationException(
                    "Semantic controls are enabled but endpoint/token configuration is incomplete.");
            if (!TryResolveClientSlot(environment, out string clientSlot))
                throw new InvalidOperationException(
                    "Semantic controls are enabled but PRIME_E2E_CLIENT_SLOT is not a or b.");

            var options = new SemanticControlServerOptions(
                Enabled: true,
                Endpoint: endpoint,
                TokenFile: tokenFile);
            var adapter = new SemanticControlAdapter(owner, options, clientSlot);
            adapter._server.Start();
            return adapter;
        }

        internal void AttachSceneHost(MphRead.SdlGameHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (_sceneHost != null && !ReferenceEquals(_sceneHost, host))
                _sceneHost.DetachSemanticControlOwners(_syntheticInput, _runtimeCapture);
            _sceneHost = host;
            host.AttachSemanticControlOwners(_syntheticInput, _runtimeCapture);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner._view.Gateway.Changed -= OwnerStateChanged;
            _owner._view.Gateway.IdentityChanged -= OwnerIdentityChanged;
            _owner._view.Play.Changed -= OwnerPlayChanged;
            _owner._view.Online.Changed -= OwnerOnlineChanged;
            _owner._view.Online.Flow.Changed -= OwnerFlowChanged;
            _sceneHost?.DetachSemanticControlOwners(_syntheticInput, _runtimeCapture);
            _sceneHost = null;
            _syntheticInput.Shutdown();
            _runtimeCapture.Shutdown();
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        private static bool Enabled()
            => IsOne(Environment.GetEnvironmentVariable(EnableVariable))
                || IsOne(Environment.GetEnvironmentVariable(GlobalEnableVariable));

        internal static bool TryResolveConfiguration(
            IReadOnlyDictionary<string, string?> environment,
            out string endpoint, out string tokenFile)
        {
            ArgumentNullException.ThrowIfNull(environment);
            endpoint = "";
            tokenFile = "";
            if (!IsOne(Get(environment, EnableVariable))
                && !IsOne(Get(environment, GlobalEnableVariable))) return false;
            endpoint = ResolveVariable(environment, EndpointVariable,
                EndpointAVariable, EndpointBVariable) ?? "";
            tokenFile = ResolveVariable(environment, TokenVariable,
                TokenAVariable, TokenBVariable) ?? "";
            return !String.IsNullOrWhiteSpace(endpoint)
                && !String.IsNullOrWhiteSpace(tokenFile);
        }

        private static bool IsOne(string? value)
            => StringComparer.Ordinal.Equals(value?.Trim(), "1");

        private static string? ResolveVariable(
            IReadOnlyDictionary<string, string?> environment,
            string generic, string slotA, string slotB)
        {
            string? direct = Get(environment, generic);
            if (!String.IsNullOrWhiteSpace(direct)) return direct;
            string? slot = Get(environment, SlotVariable)
                ?? Get(environment, LegacySlotVariable);
            if (StringComparer.OrdinalIgnoreCase.Equals(slot, "a"))
                return Get(environment, slotA);
            if (StringComparer.OrdinalIgnoreCase.Equals(slot, "b"))
                return Get(environment, slotB);
            return null;
        }

        private static string? Get(
            IReadOnlyDictionary<string, string?> environment, string name)
            => environment.TryGetValue(name, out string? value) ? value : null;

        private static bool TryResolveClientSlot(
            IReadOnlyDictionary<string, string?> environment, out string slot)
        {
            slot = (Get(environment, SlotVariable)
                ?? Get(environment, LegacySlotVariable))?.Trim().ToLowerInvariant() ?? "";
            return slot is "a" or "b";
        }

        private void OwnerStateChanged(object? sender, EventArgs args)
            => RefreshPhaseIdentity();

        private void OwnerIdentityChanged(object? sender, EventArgs args)
            => RefreshPhaseIdentity();

        private void OwnerPlayChanged(object? sender, EventArgs args)
            => RefreshPhaseIdentity();

        private void OwnerOnlineChanged()
            => RefreshPhaseIdentity();

        private void OwnerFlowChanged(ClientSessionPhase phase)
            => RefreshPhaseIdentity();

        private SemanticControlIdentity RefreshPhaseIdentity()
        {
            SemanticControlIdentity identity = CurrentIdentity();
            _phaseGuard.Advance(identity);
            _syntheticInput.AdvanceIdentity(identity);
            _runtimeCapture.AdvanceIdentity(identity);
            return identity;
        }

        private SemanticControlIdentity CurrentIdentity()
        {
            NodeControlClient? node = _owner._view.Online.Node;
            NodeControlClient.ViewState state = node?.State
                ?? new NodeControlClient.ViewState();
            NodeSessionSnapshot? session = state.Session;
            Guid? matchId = state.Handoff?.MatchId
                ?? state.JoinedMatchId
                ?? state.Lobby?.CurrentMatchId
                ?? _owner._view.Online.Match?.MatchId;
            ulong membership = state.Lobby?.SelfMembershipGeneration.Value ?? 0;
            string phase = String.Join(":", new[]
            {
                _owner._view.Online.Flow.Phase.ToString(),
                _owner._view.CurrentRoute.ToString(),
                "l" + state.LifecycleEpoch.Value.ToString(CultureInfo.InvariantCulture),
                "h" + state.HandoffGeneration.Value.ToString(CultureInfo.InvariantCulture),
                "m" + membership.ToString(CultureInfo.InvariantCulture)
            });
            return new SemanticControlIdentity(
                session?.SessionId.ToString("N") ?? "no-session",
                matchId?.ToString("N") ?? "no-match",
                phase);
        }

        private async ValueTask<SemanticControlResponse> DispatchUiAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            return await RunOnUiAsync(
                () => ExecuteUiAsync(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        private ValueTask<SemanticControlResponse> DispatchGameplayAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            return request.Command switch
            {
                SemanticControlCommand.SubmitMovement
                    => SubmitMovementAsync(request, cancellationToken),
                SemanticControlCommand.SubmitFire
                    => SubmitFireAsync(request, cancellationToken),
                SemanticControlCommand.CaptureFrame
                    => CaptureFrameAsync(request, cancellationToken),
                _ => ValueTask.FromResult(Rejected(request, "command-unavailable"))
            };
        }

        private ValueTask<SemanticControlResponse> SubmitMovementAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (request.Identity != _syntheticInput.Identity)
                return ValueTask.FromResult(Stale(request));
            if (!SemanticControlProtocol.TryGetNumber(request, "x", out double x)
                || !SemanticControlProtocol.TryGetNumber(request, "y", out double y))
                return ValueTask.FromResult(Rejected(request, "invalid-arguments"));
            if (!TryDuration(request, out int duration))
                return ValueTask.FromResult(Rejected(request, "invalid-arguments"));
            SemanticSyntheticInputSubmission submission = _syntheticInput.SubmitMovement(
                request.Identity, x, y, duration, cancellationToken);
            return ValueTask.FromResult(MapSubmission(request, submission));
        }

        private ValueTask<SemanticControlResponse> SubmitFireAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (request.Identity != _syntheticInput.Identity)
                return ValueTask.FromResult(Stale(request));
            if (!SemanticControlProtocol.TryGetBoolean(request, "pressed",
                    out bool pressed) || !TryDuration(request, out int duration))
                return ValueTask.FromResult(Rejected(request, "invalid-arguments"));
            // The optional weapon field remains a compatibility argument for
            // existing drivers. Weapon selection is owned by production
            // bindings and is not synthesized by this first lifecycle.
            if (request.Arguments.TryGetValue("weapon", out JsonElement weapon)
                && weapon.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                return ValueTask.FromResult(Rejected(request, "invalid-arguments"));
            SemanticSyntheticInputSubmission submission = _syntheticInput.SubmitFire(
                request.Identity, pressed, duration, cancellationToken);
            return ValueTask.FromResult(MapSubmission(request, submission));
        }

        private async ValueTask<SemanticControlResponse> CaptureFrameAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (request.Identity != _runtimeCapture.Identity)
                return Stale(request);
            if (!SemanticControlProtocol.TryGetString(request, "label",
                    out string label))
                return Rejected(request, "invalid-arguments");
            SemanticRuntimeCaptureSubmission submission = _runtimeCapture.TrySubmit(
                request.Identity, label, cancellationToken, out Task<SemanticRuntimeCaptureCompletion>? completion);
            if (submission != SemanticRuntimeCaptureSubmission.Accepted)
                return MapSubmission(request, submission);
            SemanticRuntimeCaptureCompletion result = await completion!.WaitAsync(
                cancellationToken).ConfigureAwait(false);
            if (!result.Accepted)
                return Rejected(request, result.Error ?? "capture-failed");
            return Success(request, JsonSerializer.SerializeToElement(new
            {
                label,
                path = result.Path
            }));
        }

        private static bool TryDuration(SemanticControlRequest request, out int duration)
        {
            duration = SemanticSyntheticInputOwner.DefaultDurationMilliseconds;
            if (!request.Arguments.ContainsKey("durationMs")) return true;
            if (!SemanticControlProtocol.TryGetNumber(request, "durationMs",
                    out double number) || number != Math.Truncate(number)
                || number < 1 || number > SemanticSyntheticInputOwner.MaximumDurationMilliseconds)
                return false;
            duration = (int)number;
            return true;
        }

        private static SemanticControlResponse MapSubmission(
            SemanticControlRequest request, SemanticSyntheticInputSubmission submission)
            => submission switch
            {
                SemanticSyntheticInputSubmission.Accepted => Success(request),
                SemanticSyntheticInputSubmission.QueueFull => Rejected(request, "queue-full"),
                SemanticSyntheticInputSubmission.StaleIdentity => Stale(request),
                SemanticSyntheticInputSubmission.Canceled => Rejected(request, "canceled"),
                SemanticSyntheticInputSubmission.InvalidArguments
                    => Rejected(request, "invalid-arguments"),
                _ => Rejected(request, "gameplay-input-unavailable")
            };

        private static SemanticControlResponse MapSubmission(
            SemanticControlRequest request, SemanticRuntimeCaptureSubmission submission)
            => submission switch
            {
                SemanticRuntimeCaptureSubmission.Accepted => Success(request),
                SemanticRuntimeCaptureSubmission.QueueFull => Rejected(request, "queue-full"),
                SemanticRuntimeCaptureSubmission.StaleIdentity => Stale(request),
                SemanticRuntimeCaptureSubmission.Canceled => Rejected(request, "canceled"),
                SemanticRuntimeCaptureSubmission.InvalidArguments
                    => Rejected(request, "invalid-arguments"),
                _ => Rejected(request, "runtime-capture-unavailable")
            };

        private async Task<SemanticControlResponse> ExecuteUiAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0 || _owner.IsClosed)
                return Rejected(request, "launcher-closed");
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return request.Command switch
                {
                    SemanticControlCommand.GetShellState
                        => Success(request, ShellStatePayload()),
                    SemanticControlCommand.GetDiagnostics
                        => Success(request, DiagnosticsPayload()),
                    SemanticControlCommand.EnterGuestMode
                        => await EnterGuestAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.OpenPlay
                        => await OpenPlayAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.CreateLobby
                        => await CreateLobbyAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.JoinLobby
                        => await JoinLobbyAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.SetReady
                        => await SetReadyAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.SelectHunter
                        => await SelectHunterAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.StartMatch
                        => await StartMatchAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.VoteRematch
                        => await VoteRematchAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.VoteReturnLobby
                        => await VoteReturnLobbyAsync(request, cancellationToken)
                            .ConfigureAwait(true),
                    SemanticControlCommand.CaptureFrame
                        => Rejected(request, "command-unavailable"),
                    _ => Rejected(request, "command-unavailable")
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ArgumentException)
            {
                return Rejected(request, "invalid-arguments");
            }
            catch (InvalidOperationException)
            {
                return Rejected(request, "owner-rejected");
            }
        }

        private async Task<SemanticControlResponse> EnterGuestAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            cancellationToken.ThrowIfCancellationRequested();
            bool accepted = await _owner._view.Gateway.UseGuestAsync(
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return accepted ? Success(request) : Rejected(request, "owner-rejected");
        }

        private async Task<SemanticControlResponse> OpenPlayAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_owner._view.TryOpenPlayFromSemanticControl())
                return Rejected(request, "ui-not-ready");

            // Route navigation is an intentional phase transition owned by
            // this command. Advance the guard before the existing browse owner
            // runs so a successful transition is never reported stale.
            RefreshPhaseIdentity();
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.BrowseLobbiesAsync(
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> CreateLobbyAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            string name = TryString(request, "name") ?? "E2E Lobby";
            int playerLimit = 8;
            if (request.Arguments.ContainsKey("playerLimit")
                && !TryInteger(request, "playerLimit", 1,
                    MultiplayerLimits.MaxPlayers, out playerLimit))
                return Rejected(request, "invalid-arguments");
            int observerLimit = 16;
            if (request.Arguments.ContainsKey("observerLimit")
                && !TryInteger(request, "observerLimit", 0,
                    MultiplayerLimits.MaxObservers, out observerLimit))
                return Rejected(request, "invalid-arguments");
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.HostLobbyAsync(name, playerLimit,
                observerLimit, LobbySeatPolicy.ImmediateSeat,
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> JoinLobbyAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            if (!TryGuid(request, "lobbyId", out Guid lobbyId))
                return Rejected(request, "invalid-arguments");

            await _owner._view.Play.BrowseLobbiesAsync(
                cancellationToken).ConfigureAwait(true);
            if (!ValidateCurrent(request)) return Stale(request);
            LobbyListEntry? entry = _owner._view.Play.State.Lobbies?.Lobbies
                .FirstOrDefault(value => value.LobbyId == lobbyId);
            if (entry == null) return Rejected(request, "lobby-not-found");

            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.JoinLobbyAsync(lobbyId, entry.Revision,
                cancellationToken: cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> SetReadyAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            if (!SemanticControlProtocol.TryGetBoolean(request, "ready",
                    out bool ready)) return Rejected(request, "invalid-arguments");
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.SetReadyAsync(ready,
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> SelectHunterAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            string? hunterName = TryString(request, "hunter");
            if (!Enum.TryParse(hunterName, ignoreCase: false, out Hunter hunter)
                || !PlayableHunterCatalog.IsPlayable(hunter))
                return Rejected(request, "invalid-arguments");
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.SelectLobbyHunterAsync(hunter,
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> StartMatchAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.StartMatchAsync(
                cancellationToken).ConfigureAwait(true);
            // StartMatch intentionally causes the launcher to leave the shell
            // and enter MatchStart. The owner transition is the authority; do
            // not compare the old identity after it completes.
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> VoteRematchAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            if (!AcceptVote(request)) return Rejected(request, "accept-required");
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.RematchAsync(
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private async Task<SemanticControlResponse> VoteReturnLobbyAsync(
            SemanticControlRequest request, CancellationToken cancellationToken)
        {
            if (!ValidateCurrent(request)) return Stale(request);
            if (!HasConnectedNode()) return Rejected(request, "node-not-ready");
            if (!AcceptVote(request)) return Rejected(request, "accept-required");
            NodeRoundSnapshot round = _owner._view.Play.State.Round
                ?? throw new InvalidOperationException("No post-match vote is available.");
            LobbyVoteEntry? option = round.Options.FirstOrDefault(value =>
                value.Choice == LobbyVoteChoice.ReturnToLobby);
            if (option == null) return Rejected(request, "return-vote-unavailable");

            // Resolve the authoritative ballot option from the current round,
            // then use the existing vote owner. Never call ReturnToLobbyAsync:
            // that command bypasses the post-match ballot contract.
            if (!ValidateCurrent(request)) return Stale(request);
            cancellationToken.ThrowIfCancellationRequested();
            await _owner._view.Play.CastPostMatchVoteAsync(option.Id,
                cancellationToken).ConfigureAwait(true);
            RefreshPhaseIdentity();
            return Success(request, ShellStatePayload());
        }

        private bool ValidateCurrent(SemanticControlRequest request)
        {
            SemanticControlIdentity current = RefreshPhaseIdentity();
            return request.Identity == current;
        }

        private bool HasConnectedNode()
            => _owner._view.Online.Node is { Connected: true, Session: not null };

        private static bool AcceptVote(SemanticControlRequest request)
            => SemanticControlProtocol.TryGetBoolean(request, "accept",
                out bool accept) && accept;

        private JsonElement ShellStatePayload()
        {
            NodeControlClient? node = _owner._view.Online.Node;
            NodeControlClient.ViewState state = node?.State
                ?? new NodeControlClient.ViewState();
            NodeSessionSnapshot? session = state.Session;
            LobbySnapshot? lobby = state.Lobby;
            LobbyMember? member = session == null || lobby == null
                ? null
                : lobby.Members.FirstOrDefault(value =>
                    value.SessionId == session.SessionId);
            SemanticControlIdentity identity = RefreshPhaseIdentity();
            return JsonSerializer.SerializeToElement(new
            {
                identity = new
                {
                    sessionId = identity.SessionId,
                    matchId = identity.MatchId,
                    phaseId = identity.PhaseId
                },
                route = _owner._view.CurrentRoute.ToString(),
                clientPhase = _owner._view.Online.Flow.Phase.ToString(),
                gatewayPhase = _owner._view.Gateway.State.Phase.ToString(),
                nodeConnected = node?.Connected == true,
                sessionId = session?.SessionId.ToString("N"),
                lobbyId = lobby?.LobbyId.ToString("N"),
                lobbyRevision = lobby?.Revision,
                lobbyPhase = lobby?.Phase.ToString(),
                ready = member?.Ready,
                hunter = member?.Hunter.ToString(),
                matchId = state.Handoff?.MatchId.ToString("N")
                    ?? state.JoinedMatchId?.ToString("N"),
                round = state.Round == null ? null : new
                {
                    ballotRevision = state.Round.BallotRevision,
                    ownVote = state.Round.OwnVote,
                    options = state.Round.Options.Select(option => new
                    {
                        id = option.Id,
                        choice = option.Choice.ToString(),
                        mapKey = option.MapKey,
                        mode = option.Mode.ToString()
                    }).ToArray()
                }
            });
        }

        private JsonElement DiagnosticsPayload()
            => JsonSerializer.SerializeToElement(new
            {
                semanticControls = "enabled",
                gameplayInput = _syntheticInput.IsHostAttached
                    ? "synthetic-bindings" : "unavailable",
                runtimeCapture = _runtimeCapture.IsHostAttached
                    && _runtimeCapture.HasEvidenceRoot
                    ? "async-sdl-readback" : "unavailable",
                aim = "unavailable; no deterministic semantic look source",
                workerIdentity = "unavailable; Node handoff does not expose Worker identity",
                authoritative = MphRead.Mods.Network.AuthoritativePlay.Current
                    ?.ReadSemanticDiagnostics(),
                transport = OperatingSystem.IsWindows()
                    ? "current-user-named-pipe" : "owner-only-unix-socket",
                identity = CurrentIdentity()
            });

        private static string? TryString(SemanticControlRequest request,
            string name)
            => SemanticControlProtocol.TryGetString(request, name,
                out string value) ? value : null;

        private static bool TryInteger(SemanticControlRequest request, string name,
            int minimum, int maximum, out int value)
        {
            value = 0;
            if (!SemanticControlProtocol.TryGetNumber(request, name,
                    out double number)
                || number != Math.Truncate(number)
                || number < minimum || number > maximum) return false;
            value = (int)number;
            return true;
        }

        private static bool TryGuid(SemanticControlRequest request, string name,
            out Guid value)
        {
            value = Guid.Empty;
            string? text = TryString(request, name);
            return text != null && Guid.TryParse(text, out value)
                && value != Guid.Empty;
        }

        private static SemanticControlResponse Success(
            SemanticControlRequest request, JsonElement? payload = null)
            => SemanticControlResponse.Success(request.CommandId, payload);

        private static SemanticControlResponse Rejected(
            SemanticControlRequest request, string error)
            => SemanticControlResponse.Rejected(request.CommandId, error);

        private static SemanticControlResponse Stale(
            SemanticControlRequest request)
            => Rejected(request, "stale-identity");

        private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> operation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            cancellationToken.ThrowIfCancellationRequested();
            if (Dispatcher.UIThread.CheckAccess())
                return await operation().ConfigureAwait(true);

            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<T>)state!).TrySetCanceled(),
                completion);
            try
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }
                    try
                    {
                        T result = await operation().ConfigureAwait(true);
                        completion.TrySetResult(result);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                    }
                    catch (Exception error)
                    {
                        completion.TrySetException(error);
                    }
                }, DispatcherPriority.Normal);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            return await completion.Task.ConfigureAwait(false);
        }
    }
}
