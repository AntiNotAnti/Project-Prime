using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Launcher;

/// <summary>
/// A native-UI-independent lobby creation request. Only the established
/// dedicated host pool or local server process may become authority.
/// </summary>
public readonly record struct LobbyCreationRequest(string Name, string Player,
    string RoomKey, GameMode Mode, MatchFormat Format, Hunter Hunter, bool Dedicated)
{
    public bool Validate(out string error)
    {
        error = "";
        if (String.IsNullOrWhiteSpace(RoomKey) || !Metadata.IsBuiltInRoom(RoomKey))
            error = "Choose a supported built-in multiplayer arena.";
        else if (String.IsNullOrWhiteSpace(Name) || Name.Length > 48)
            error = "Enter a lobby name of 1 to 48 characters.";
        else if (String.IsNullOrWhiteSpace(Player))
            error = "Enter your Hunter name.";
        else if (Dedicated && OperatingSystem.IsAndroid())
            error = "Android requires a remote authoritative host.";
        else if (!Multiplayer.MapModeCapabilities.Supports(RoomKey, Mode,
            LobbyRules.ResolveWorldProfile(new MatchDefinition
            { RoomKey = RoomKey, Mode = Mode, Format = Format }, PlayerEntity.SlotCapacity),
            out error, PlayerEntity.SlotCapacity))
        {
            if (error.Length == 0) error = "That arena does not support this match type.";
        }
        else if (LobbyRules.ValidateDefinition(new MatchDefinition
            {
                RoomKey = RoomKey,
                MapIdentity = NetworkMapIdentity.ForRoom(RoomKey),
                Mode = Mode, Format = Format
            }, out error) != LobbyResultCode.Ok)
        {
            if (error.Length == 0) error = "That lobby format is invalid.";
        }
        return error.Length == 0;
    }
}

public readonly record struct LobbyCreationOutcome(bool Joined, LaunchPlan Plan, string Error);

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
        LobbyCreationRequest request, CancellationToken cancellation = default)
    {
        if (!GameFiles.Ready)
            return new(false, default, "Configure the game files before hosting.");
        if (!request.Validate(out string error))
            return new(false, default, error);
        cancellation.ThrowIfCancellationRequested();

        (int seconds, ushort goal) = InitialLimits(request.Mode);
        List<(string RoomKey, GameMode Mode)> rotation = new() { (request.RoomKey, request.Mode) };
        if (request.Dedicated)
            return await StartLocal(request, rotation, seconds, goal, cancellation).ConfigureAwait(false);
        return await StartHosted(request, rotation, seconds, goal, cancellation).ConfigureAwait(false);
    }

    private static async Task<LobbyCreationOutcome> StartLocal(
        LobbyCreationRequest request, List<(string RoomKey, GameMode Mode)> rotation,
        int seconds, ushort goal, CancellationToken token)
    {
        if (OperatingSystem.IsAndroid())
            return new(false, default, "Android does not support local dedicated server processes.");
        if (!LocalServer.Ready)
            return new(false, default, "Install the dedicated server package in the existing lobby creator.");
        int port = await Task.Run(() => LocalServer.Start(request.Name, rotation,
            maxPlayers: PlayerEntity.SlotCapacity, timeLimit: seconds,
            pointGoal: goal, masterHost: LauncherPrefs.MasterHost,
            masterPort: LauncherPrefs.MasterPort, listed: LauncherPrefs.ListHostedGame,
            cancel: token, lobby: true, requestedPort: NetConfig.DefaultPort), token).ConfigureAwait(false);
        if (port < 0)
            return new(false, default, LocalServer.LastError ?? "Dedicated server did not start.");

        // Once a server starts it has its own authority and may be joined by
        // other clients. Do not kill that process on an unrelated local join
        // error; present the endpoint for manual retry as the legacy UI does.
        bool joined = await Task.Run(() => NetLaunch.Connect("127.0.0.1", port,
            request.Player, request.Hunter, ownerToken: LocalServer.OwnerToken)).ConfigureAwait(false);
        if (!joined)
        {
            NetSession.Stop();
            return new(false, default, NetLaunch.LastJoinError
                + $" // Server is still at 127.0.0.1:{port}.");
        }
        SavePreferences(request, "127.0.0.1", port, LaunchKind.Online, seconds, goal);
        return new(true, new LaunchPlan
        {
            Kind = LaunchKind.Online, Hunter = request.Hunter,
            PlayerName = request.Player, RoomKey = "", Mode = request.Mode, Port = port,
            Lobby = new LobbyContext(request.Name, $"Local server // port {port}", true)
        }, "");
    }

    private static async Task<LobbyCreationOutcome> StartHosted(
        LobbyCreationRequest request, List<(string RoomKey, GameMode Mode)> rotation,
        int seconds, ushort goal, CancellationToken token)
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
            // directory query. Once sent, allow the reply and local join to
            // finish, even if the user has since hidden the page.
            HostedGame game = await Task.Run(() => NetMasterClient.RequestGame(
                candidate.Host, candidate.Port, request.RoomKey, request.Mode,
                timeLimit: seconds, pointGoal: goal, maxPlayers: PlayerEntity.SlotCapacity,
                serverName: request.Name, rotation: rotation, policy: ServerSessionPolicy.Lobby))
                .ConfigureAwait(false);
            if (!game.Started && TransientHostFailure(game.Reason))
            {
                await Task.Delay(250).ConfigureAwait(false);
                game = await Task.Run(() => NetMasterClient.RequestGame(
                    candidate.Host, candidate.Port, request.RoomKey, request.Mode,
                    timeLimit: seconds, pointGoal: goal, maxPlayers: PlayerEntity.SlotCapacity,
                    serverName: request.Name, rotation: rotation, policy: ServerSessionPolicy.Lobby))
                    .ConfigureAwait(false);
            }
            if (!game.Started)
            {
                failures.Add(candidate.Label + ": " + game.Reason);
                continue;
            }

            bool joined = await Task.Run(() => NetLaunch.Connect(game.Host, game.Port,
                request.Player, request.Hunter, ownerToken: game.OwnerToken)).ConfigureAwait(false);
            if (!joined)
            {
                NetSession.Stop();
                NetHostSession.Stop();
                return new(false, default, NetLaunch.LastJoinError);
            }
            SavePreferences(request, game.Host, game.Port, LaunchKind.Host, seconds, goal);
            return new(true, new LaunchPlan
            {
                Kind = LaunchKind.Host, Hunter = request.Hunter,
                PlayerName = request.Player, RoomKey = "", Mode = request.Mode,
                Port = game.Port, Lobby = new LobbyContext(request.Name, $"{game.Host}:{game.Port}")
            }, "");
        }
        return new(false, default, "No host opened a lobby: " +
            String.Join(" / ", failures.Take(3)));
    }

    private static void SavePreferences(LobbyCreationRequest request,
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

    private static bool TransientHostFailure(string reason) =>
        reason.Contains("busy", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("already running", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("already in use", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("port ", StringComparison.OrdinalIgnoreCase)
            && reason.Contains("in use", StringComparison.OrdinalIgnoreCase);
}
