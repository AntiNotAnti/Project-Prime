using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private sealed record PendingLobbyMap(Peer Peer, LobbyCommandPacket Command,
        CancellationTokenSource Cancel, Task<PreparedMapInstallation> Preparation);

    private PendingLobbyMap? _pendingLobbyMap;

    private bool TryPrepareLobbyMap(Peer peer, LobbyCommandPacket command)
    {
        if (_pendingLobbyMap is { } pending && pending.Peer == peer
            && pending.Command.CommandId == command.CommandId) return true;
        var match = command.Configuration.Match;
        if (command.Type != LobbyCommandType.UpdateMatch || !match.MapIdentity.IsCustom
            || ValidateLobbyCommandAccess(peer, command, out _) != LobbyResultCode.Ok
            || LobbyRules.ValidateDefinition(match, out _) != LobbyResultCode.Ok
            || Metadata.IsBuiltInRoom(match.RoomKey)) return false;
        var identity = match.MapIdentity.Content(match.RoomKey);
        if (CustomRooms.Installed.HasExact(identity)) return false;
        if (_pendingLobbyMap != null)
        {
            ReplyLobbyCommand(peer, command, CacheLobbyResult(peer, command,
                LobbyResultCode.ServerBusy, "The server is already preparing a Community map."));
            return true;
        }
        var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string source = MapDownloadSource;
        _pendingLobbyMap = new(peer, command, cancel, Task.Run(async () =>
        {
            MapValidator.RequireRuntimeName(match.RoomKey);
            using var client = new MapCommunityClient(source);
            return await client.PrepareExactAsync(identity, cancel.Token).ConfigureAwait(false);
        }));
        return true;
    }

    private void PumpLobbyMapPreparation(double now)
    {
        if (_pendingLobbyMap is not { } pending) return;
        var peer = pending.Peer;
        var access = ValidateLobbyCommandAccess(peer, pending.Command, out string reason);
        if (!_peers.Contains(peer) || access != LobbyResultCode.Ok)
        {
            CancelLobbyMapPreparation();
            if (_peers.Contains(peer)) ReplyLobbyCommand(peer, pending.Command,
                CacheLobbyResult(peer, pending.Command, access, reason));
            return;
        }
        if (!pending.Preparation.IsCompleted) return;
        _pendingLobbyMap = null;
        LobbyResultCode code;
        try
        {
            using var prepared = pending.Preparation.GetAwaiter().GetResult();
            pending.Cancel.Token.ThrowIfCancellationRequested();
            // Workers prepare private files. Only the network loop publishes
            // runtime metadata, after checking the command is still authorized.
            var installed = prepared.Commit(CustomRooms.UserMapDirectory);
            Metadata.RegisterDownloadedMap(installed);
            code = ExecuteLobbyCommand(peer, pending.Command, now, out reason);
        }
        catch (Exception ex)
        {
            code = LobbyResultCode.MapUnavailable;
            reason = ex is OperationCanceledException ? "Server map preparation timed out. Try again."
                : "Server map preparation failed: " + ex.GetBaseException().Message;
            Log("[lobby] " + reason);
        }
        finally { pending.Cancel.Dispose(); }
        ReplyLobbyCommand(peer, pending.Command, CacheLobbyResult(peer, pending.Command, code, reason));
    }

    private void CancelLobbyMapPreparation()
    {
        if (_pendingLobbyMap is not { } pending) return;
        _pendingLobbyMap = null;
        pending.Cancel.Cancel();
        _ = pending.Preparation.ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
            else _ = task.Exception;
            pending.Cancel.Dispose();
        }, TaskScheduler.Default);
    }
}
