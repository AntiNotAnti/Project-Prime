#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.StudioIntegration;

/// <summary>Game-owned package publication and launch operations; all mutation is dispatched to the game owner.</summary>
public sealed class GameStudioBroker
{
    private readonly Func<Func<StudioGameResult>, CancellationToken, Task<StudioGameResult>> _dispatch;
    private readonly Func<string?> _checkPublication;
    private readonly Func<MapDefinition, StudioPlaytestOptions, Guid, bool, StudioGameResult> _launch;
    private readonly Func<Guid, bool, StudioGameResult> _playtest;
    private readonly Func<bool, CancellationToken, Task<string>> _ticket;

    public GameStudioBroker(Func<Func<StudioGameResult>, CancellationToken, Task<StudioGameResult>> dispatch,
        Func<string?> checkPublication, Func<MapDefinition, StudioPlaytestOptions, Guid, bool, StudioGameResult> launch,
        Func<Guid, bool, StudioGameResult> playtest, Func<bool, CancellationToken, Task<string>> ticket)
    {
        _dispatch = dispatch; _checkPublication = checkPublication; _launch = launch; _playtest = playtest; _ticket = ticket;
    }

    public static StudioMapIdentity ToWireIdentity(MapContentIdentity identity)
        => new(identity.MapId, identity.RoomKey, identity.ContentHash.ToString(), identity.PackageHash.ToString());
    public static MapContentIdentity FromWireIdentity(StudioMapIdentity identity)
    {
        if (identity.Validate() is { } error) throw new InvalidDataException(error);
        return new(identity.MapId, identity.RoomKey, MapHash256.Parse(identity.ContentHash), MapHash256.Parse(identity.PackageHash), true);
    }

    public async Task<StudioGameResult> HandleAsync(StudioGameRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Validate() is { } invalid) return StudioGameResult.Rejected(invalid);
        string gameVersion = typeof(GameStudioBroker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "local";
        if (!StringComparer.Ordinal.Equals(gameVersion, request.StudioVersion))
            return StudioGameResult.Rejected("Studio <-> ProjectPrime application version mismatch. Update both applications together.")
                with { GameVersion = gameVersion, StudioVersion = request.StudioVersion };
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (request.Command == StudioGameCommand.Diagnostics)
                return new(true, GameVersion: gameVersion, StudioVersion: request.StudioVersion);
            if (request.Command == StudioGameCommand.CommunityTicket)
            {
                string ticket;
                try { ticket = await _ticket(request.RefreshTicket, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { return StudioGameResult.Rejected("Sign in through Project Prime to obtain a Community publishing ticket."); }
                // Only the existing narrow capability may leave the game process.
                if (ticket is not { Length: > 5 and < 8192 } || !ticket.StartsWith("ppm1.", StringComparison.Ordinal))
                    return StudioGameResult.Rejected("The game did not return a valid Community map ticket.");
                return new(true, CommunityTicket: ticket);
            }
            if (request.Command is StudioGameCommand.PlaytestStatus or StudioGameCommand.StopPlaytest)
                return await _dispatch(() => _playtest(request.PlaytestId, request.Command == StudioGameCommand.StopPlaytest), cancellationToken).ConfigureAwait(false);

            var ready = await _dispatch(() => _checkPublication() is { } reason
                ? StudioGameResult.Rejected(reason, deferred: true)
                : MapRuntimeUsage.IsInUse(request.Identity!.RoomKey)
                    ? StudioGameResult.Rejected("A scene or preparation is still using this map. Close it before installing this package.", deferred: true)
                    : new(true), cancellationToken).ConfigureAwait(false);
            if (!ready.Accepted) return ready;
            MapContentIdentity required = FromWireIdentity(request.Identity!);
            // Copy/verify/build/stage privately on workers. Commit rechecks every fence on the owner thread.
            using PreparedMapInstallation prepared = await MapPackageInstaller.PrepareAsync(request.PackagePath!, required,
                cancellationToken, CustomRooms.UserMapDirectory).ConfigureAwait(false);
            if (!prepared.Identity.Matches(required)) return StudioGameResult.Rejected("The prepared map identity changed.");
            return await _dispatch(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_checkPublication() is { } reason) return StudioGameResult.Rejected(reason, deferred: true);
                MapDefinition definition = prepared.Commit(CustomRooms.UserMapDirectory, cancellation: cancellationToken);
                Metadata.RegisterDownloadedMap(definition);
                if (!CustomRooms.Installed.HasExact(required))
                    return StudioGameResult.Rejected("The installed map identity does not match the requested package.");
                if (request.Command == StudioGameCommand.InstallMapPackage) return new(true, Identity: ToWireIdentity(required));
                StudioGameResult launched = _launch(definition, request.Options ?? new(), Guid.NewGuid(), request.Command == StudioGameCommand.HostMap);
                return launched with { Identity = ToWireIdentity(required) };
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MapPublicationBusyException ex) { return StudioGameResult.Rejected(ex.Message, deferred: true); }
        catch (OperationCanceledException) { return StudioGameResult.Rejected("The game broker request was cancelled."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return StudioGameResult.Rejected(ex.Message); }
    }
}
#endif
