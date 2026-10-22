using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher;

/// <summary>
/// A native-UI-independent lobby creation request. Only the established
/// dedicated host pool or local server process may become authority.
/// </summary>
public readonly record struct LobbyCreationRequest(string Name, string Player,
    string RoomKey, GameMode Mode, MatchFormat Format, Hunter Hunter, bool Dedicated,
    NativeCommunityHostSelection? CommunityMap = null, int MaxPlayers = PlayerEntity.SlotCapacity)
{
    public NetworkMapIdentity MapIdentity => CommunityMap is { } selection
        ? new(selection.Identity.MapId, selection.Identity.ContentHash,
            selection.Identity.PackageHash, NetworkMapFlags.Custom)
        : NetworkMapIdentity.ForRoom(RoomKey);

    // Safe on the render/owner thread: field and metadata checks only.
    // A full entity-layer map inspection does IO and may decode large world
    // packages. That expensive validation belongs in CreateAsync's worker.
    public bool ValidateForm(out string error)
    {
        error = "";
        if (String.IsNullOrWhiteSpace(RoomKey) || (CommunityMap == null && !Metadata.IsBuiltInRoom(RoomKey)))
            error = "Choose a supported built-in arena or an exact installed Community package.";
        else if (CommunityMap is { } selection && (!selection.Identity.IsCustom
            || selection.Identity.MapId == Guid.Empty || selection.Identity.ContentHash.IsZero
            || selection.Identity.PackageHash.IsZero || Metadata.IsBuiltInRoom(RoomKey)
            || !String.Equals(selection.Identity.RoomKey, RoomKey, StringComparison.Ordinal)
            || String.IsNullOrWhiteSpace(selection.ConfiguredRoot)
            || !String.Equals(selection.ConfiguredRoot, GameFiles.Root, StringComparison.Ordinal)))
            error = "The selected Community package or game-file configuration changed. Select it again.";
        else if (String.IsNullOrWhiteSpace(Name) || Name.Length > 48)
            error = "Enter a lobby name of 1 to 48 characters.";
        else if (String.IsNullOrWhiteSpace(Player))
            error = "Enter your Hunter name.";
        else if (MaxPlayers is < 2 or > PlayerEntity.SlotCapacity)
            error = "Choose between 2 and 8 player slots.";
        else if (Dedicated && OperatingSystem.IsAndroid())
            error = "Android requires a remote authoritative host.";
        else if (!Enum.IsDefined(Mode) || Mode is GameMode.None or GameMode.SinglePlayer
            || !Enum.IsDefined(Format))
            error = "Choose a valid multiplayer game type.";
        else if (LobbyRules.ValidateDefinition(new MatchDefinition
            {
                RoomKey = RoomKey, MapIdentity = MapIdentity,
                Mode = Mode, Format = Format
            }, out error) != LobbyResultCode.Ok)
        {
            if (error.Length == 0) error = "That lobby format is invalid.";
        }
        return error.Length == 0;
    }

    public bool Validate(out string error)
    {
        if (!ValidateForm(out error)) return false;
        if (CommunityMap is { } selection)
        {
            if (!CustomRooms.Installed.HasExact(selection.Identity))
            { error = "The installed Community archive changed. Reinstall the selected revision."; return false; }
            string roomKey = RoomKey;
            var definition = CustomRooms.Definitions.FirstOrDefault(map => map.MapId == selection.Identity.MapId
                && String.Equals(map.Name, roomKey, StringComparison.Ordinal));
            if (definition == null || !MapModeCapabilities.AllowsLocalSource(definition, false, out error))
            { if (error.Length == 0) error = "The selected Community map is no longer registered."; return false; }
        }
        var match = new MatchDefinition
        { RoomKey = RoomKey, MapIdentity = MapIdentity, Mode = Mode, Format = Format };
        if (!Multiplayer.MapModeCapabilities.Supports(RoomKey, Mode,
            LobbyRules.ResolveWorldProfile(match, MaxPlayers), out error, MaxPlayers))
        {
            if (error.Length == 0) error = "That arena does not support this match type.";
            return false;
        }
        return true;
    }
}

public readonly record struct LobbyCreationOutcome(bool Joined, LaunchPlan Plan, string Error,
    LobbyCreationPreferences? Preferences = null);

/// <summary>Detached preference write; consumed only after the current UI owner
/// accepts the exact created-lobby client. Contains no host admission token.</summary>
public sealed class LobbyCreationPreferences
{
    private readonly LobbyCreationRequest _request;
    private readonly string _endpoint;
    private readonly int _port, _seconds;
    private readonly LaunchKind _kind;
    private readonly ushort _goal;
    internal LobbyCreationPreferences(LobbyCreationRequest request,string endpoint,int port,LaunchKind kind,int seconds,ushort goal)
    { _request=request;_endpoint=endpoint;_port=port;_kind=kind;_seconds=seconds;_goal=goal; }
    internal void Commit() => LobbyCreationService.SavePreferences(_request,_endpoint,_port,_kind,_seconds,_goal);
}

/// <summary>
/// Shared application-layer work for future native and legacy launchers.
/// Remote hosting uses a confirmed HostPool candidate; dedicated uses the
/// existing LocalServer child process. This never creates a player-hosted
/// authoritative simulation.
/// </summary>
public static class LobbyCreationService
{
    public static (int TimeLimit, ushort Goal) InitialLimits(GameMode mode)
    {
        if (mode == GameMode.OneInTheChamber) return (0, 2);
        int seconds = mode is (GameMode.Relic or GameMode.Hardpoint or GameMode.HardpointTeams or GameMode.GunGame)
            && mode != LauncherPrefs.LastLobbyMode ? 600
            : Math.Clamp(LauncherPrefs.LastLobbyTimeLimitSeconds, 0, UInt16.MaxValue);
        ushort goal = mode == LauncherPrefs.LastLobbyMode
            ? (ushort)Math.Clamp(LauncherPrefs.LastLobbyGoal, 0, UInt16.MaxValue)
            : MatchGoalRules.DefaultValue(mode);
        return (seconds, goal);
    }

    public static async Task<LobbyCreationOutcome> CreateAsync(
        LobbyCreationRequest request, CancellationToken cancellation = default,
        NetClientJoinOwnership? ownership = null, Func<Func<bool>,bool>? initializeFence = null)
    {
        ownership ??= new NetClientJoinOwnership();
        string root = request.CommunityMap?.ConfiguredRoot ?? GameFiles.Root;
        using var files = GameFiles.TryUseConfiguredFiles(root);
        if (files == null || !GameFiles.Ready)
            return new(false, default, "Configure the game files before hosting.");
        if (!request.ValidateForm(out string error))
            return new(false, default, error);
        cancellation.ThrowIfCancellationRequested();
        // The real map-capability gate reads entity data. Never perform it on
        // the render thread that dispatched the native Create Lobby action.
        string capabilityError = await Task.Run(() =>
        {
            return request.Validate(out string reason) ? "" : reason;
        }, cancellation).ConfigureAwait(false);
        if (capabilityError.Length != 0)
            return new(false, default, capabilityError);
        cancellation.ThrowIfCancellationRequested();

        (int seconds, ushort goal) = InitialLimits(request.Mode);
        List<(string RoomKey, GameMode Mode)> rotation = new() { (request.RoomKey, request.Mode) };
        if (request.Dedicated)
            return await StartLocal(request, rotation, seconds, goal, cancellation, ownership, initializeFence).ConfigureAwait(false);
        return await StartHosted(request, rotation, seconds, goal, cancellation, ownership, initializeFence).ConfigureAwait(false);
    }

    private static async Task<LobbyCreationOutcome> StartLocal(
        LobbyCreationRequest request, List<(string RoomKey, GameMode Mode)> rotation,
        int seconds, ushort goal, CancellationToken token,
        NetClientJoinOwnership ownership, Func<Func<bool>,bool>? initializeFence)
    {
        if (OperatingSystem.IsAndroid())
            return new(false, default, "Android does not support local dedicated server processes.");
        if (!LocalServer.Ready)
            return new(false, default, "Install the dedicated server package in the existing lobby creator.");
        HostedMapPreparation? prepared = null;
        int port = -1;
        Guid localOwnerToken = default;
        string? localStartError = null;
        try
        {
            if (request.CommunityMap != null)
                prepared = await Task.Run(() => PrepareLocalPackage(request, token), token).ConfigureAwait(false);
            var started = await Task.Run(() =>
            {
                lock(LocalServer.StartLifecycleGate)
                {
                    token.ThrowIfCancellationRequested();
                    // Once spawned, finish bounded child startup independently
                    // of UI cancellation. The child may already have peers.
                    int startedPort=LocalServer.Start(request.Name,rotation,
                        maxPlayers:request.MaxPlayers,timeLimit:seconds,pointGoal:goal,
                        masterHost:LauncherPrefs.MasterHost,masterPort:LauncherPrefs.MasterPort,
                        listed:LauncherPrefs.ListHostedGame,cancel:CancellationToken.None,lobby:true,
                        requestedPort:NetConfig.DefaultPort,format:request.Format,
                        requiredMap:request.CommunityMap!=null ? request.MapIdentity : null,hostedMaps:prepared);
                    return (Port:startedPort,Owner:LocalServer.OwnerToken,Error:LocalServer.LastError);
                }
            },token).ConfigureAwait(false);
            port=started.Port;localOwnerToken=started.Owner;localStartError=started.Error;
        }
        finally
        {
            // A successful LocalServer launch owns its private library through
            // the existing child lifecycle. Failed preparation/start owns no child.
            if (port < 0 && prepared?.LibraryPath is { } library)
                HostedMapRequests.TryDeleteDirectory(library);
        }
        if (port < 0)
            return new(false, default, localStartError ?? "Dedicated server did not start.");

        // Once a server starts it has its own authority and may be joined by
        // other clients. Do not kill that process on an unrelated local join
        // error; present the endpoint for manual retry as the legacy UI does.
        token.ThrowIfCancellationRequested();
        bool joined = await Task.Run(() => NetLaunch.Connect("127.0.0.1", port,
            request.Player, request.Hunter, ownerToken: localOwnerToken, cancellationToken: token,
            ownership: ownership, initializeFence: initializeFence), token).ConfigureAwait(false);
        if (!joined)
        {
            ownership.TryStopCurrent();
            return new(false, default, NetLaunch.LastJoinError
                + $" // Server is still at 127.0.0.1:{port}.");
        }
        return new(true, new LaunchPlan
        {
            Kind = LaunchKind.Online, Hunter = request.Hunter,
            PlayerName = request.Player, RoomKey = "", Mode = request.Mode, Port = port,
            Lobby = new LobbyContext(request.Name, $"Local server // port {port}", true)
        }, "", new(request,"127.0.0.1",port,LaunchKind.Online,seconds,goal));
    }

    private static async Task<LobbyCreationOutcome> StartHosted(
        LobbyCreationRequest request, List<(string RoomKey, GameMode Mode)> rotation,
        int seconds, ushort goal, CancellationToken token,
        NetClientJoinOwnership ownership, Func<Func<bool>,bool>? initializeFence)
    {
        var found = new ConcurrentBag<HostCandidate>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        NetMasterClient.FindHosts(LauncherPrefs.MasterHost, LauncherPrefs.MasterPort,
            onFound: candidate => found.Add(candidate),
            onDone: () => completed.TrySetResult(true));
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new(false, default, "The host directory did not answer in time.");
        }
        token.ThrowIfCancellationRequested();

        HostCandidate[] candidates = found.Where(candidate => candidate.WillHost)
            .OrderBy(candidate => candidate.Latency < 0 ? int.MaxValue : candidate.Latency)
            .Take(8).ToArray();
        if (candidates.Length == 0)
            return new(false, default, "No authoritative host can create a lobby right now.");

        var failures = new List<string>();
        foreach (HostCandidate candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            // Remote RequestGame is a committed allocation, not a cancellable
            // directory query. Allow its reply to finish, then check the
            // original owner before initializing any local client.
            HostedGame game = await Task.Run(() => NetMasterClient.RequestGame(
                candidate.Host, candidate.Port, request.RoomKey, request.Mode,
                timeLimit: seconds, pointGoal: goal, maxPlayers: request.MaxPlayers,
                serverName: request.Name, rotation: rotation, policy: ServerSessionPolicy.Lobby,
                requiredMap: request.CommunityMap != null ? request.MapIdentity : null))
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!game.Started && TransientHostFailure(game.Reason))
            {
                await Task.Delay(250,token).ConfigureAwait(false);
                game = await Task.Run(() => NetMasterClient.RequestGame(
                    candidate.Host, candidate.Port, request.RoomKey, request.Mode,
                    timeLimit: seconds, pointGoal: goal, maxPlayers: request.MaxPlayers,
                    serverName: request.Name, rotation: rotation, policy: ServerSessionPolicy.Lobby,
                    requiredMap: request.CommunityMap != null ? request.MapIdentity : null))
                    .ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (!game.Started)
            {
                failures.Add(candidate.Label + ": " + game.Reason);
                continue;
            }

            bool joined = await Task.Run(() => NetLaunch.Connect(game.Host, game.Port,
                request.Player, request.Hunter, ownerToken: game.OwnerToken, cancellationToken: token,
                ownership: ownership, initializeFence: initializeFence),token).ConfigureAwait(false);
            if (!joined)
            {
                ownership.TryStopCurrent();
                return new(false, default, NetLaunch.LastJoinError);
            }
            return new(true, new LaunchPlan
            {
                Kind = LaunchKind.Host, Hunter = request.Hunter,
                PlayerName = request.Player, RoomKey = "", Mode = request.Mode,
                Port = game.Port, Lobby = new LobbyContext(request.Name, $"{game.Host}:{game.Port}")
            }, "", new(request,game.Host,game.Port,LaunchKind.Host,seconds,goal));
        }
        return new(false, default, "No host opened a lobby: " +
            String.Join(" / ", failures.Take(3)));
    }

    internal static void SavePreferences(LobbyCreationRequest request,
        string endpoint, int port, LaunchKind kind, int seconds, ushort goal)
    {
        LauncherPrefs.PlayerName = request.Player;
        LauncherPrefs.LastHunter = request.Hunter;
        LauncherPrefs.ServerAddress = endpoint;
        LauncherPrefs.ServerPort = port;
        LauncherPrefs.LastKind = (int)kind;
        LauncherPrefs.LastLobbyMode = request.Mode;
        LauncherPrefs.LastLobbyTimeLimitSeconds = seconds;
        LauncherPrefs.LastLobbyGoal = goal;
        LauncherPrefs.Save();
    }

    internal static HostedMapPreparation PrepareLocalPackage(LobbyCreationRequest request, CancellationToken token)
    {
        if (request.CommunityMap is not { } selection || !request.ValidateForm(out _)
            || !CustomRooms.Installed.TryGet(selection.Identity.MapId, out var installed)
            || !CustomRooms.Installed.HasExact(selection.Identity))
            throw new InvalidDataException("The reviewed Community package is no longer installed.");
        token.ThrowIfCancellationRequested();
        string id = Guid.NewGuid().ToString("N");
        string library = Path.Combine(HostedMapRequests.CacheDirectory, "native-lobbies", id);
        Directory.CreateDirectory(library);
        try
        {
            string archive = Path.Combine(library, selection.Identity.MapId.ToString("N") + ".ppmap");
            File.Copy(installed.PackagePath, archive, overwrite: false);
            token.ThrowIfCancellationRequested();
            if (!MapContentIdentity.FromPackage(archive).Matches(selection.Identity))
                throw new InvalidDataException("The Community archive changed while preparing the server.");
            return new(new[] { new HostedMapArchive(request.RoomKey, selection.Identity, archive) }, library, id);
        }
        catch { HostedMapRequests.TryDeleteDirectory(library); throw; }
    }

    private static bool TransientHostFailure(string reason) =>
        reason.Contains("busy", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("already running", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("already in use", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("port ", StringComparison.OrdinalIgnoreCase)
            && reason.Contains("in use", StringComparison.OrdinalIgnoreCase);
}
