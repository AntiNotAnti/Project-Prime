using System;
using System.Collections.Generic;
using System.Threading.Channels;

namespace MphRead.Admin;

public enum AdminCommandKind
{
    LockRoster, UnlockRoster, AssignTeam, ForceSpectator, SelectPreset, ReadyCheck,
    ConfirmReady, StartCountdown, CancelBeforeStart, PauseBetweenRounds, Resume,
    Kick, Mute, Unmute, ForceReplay, SetRoundIdentity
}

public sealed record AdminCommand(Guid RequestId, AdminCommandKind Kind, DateTimeOffset IssuedAtUtc, ulong? ConnectionId = null,
    int? TeamIndex = null, string? Preset = null, string? RoomKey = null, string? TournamentId = null, string? RoundId = null);
public sealed record AdminCommandResult(Guid RequestId, string State, string Message, uint? AppliedTick = null);

/// <summary>Bounded admission from HTTP workers; the simulation owner alone executes commands.
/// Exact retries receive the same result. Expired queued commands never execute later.</summary>
public sealed class AdminCommandQueue
{
    private sealed record Entry(AdminCommand Command, DateTimeOffset Deadline)
    {
        public AdminCommandResult Result { get; set; } = new(Command.RequestId, "queued", "Waiting for the server owner.");
    }
    private readonly object _gate = new();
    private readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(32)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Queue<Guid> _completed = new();
    private readonly TimeProvider _clock;
    private bool _closed;

    public AdminCommandQueue(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public AdminCommandResult Submit(AdminCommand command)
    {
        lock (_gate)
        {
            if (command.RequestId == Guid.Empty || !Enum.IsDefined(command.Kind) || !ValidShape(command))
                return new(command.RequestId, "rejected", "A nonempty request UUID and known command are required.");
            while (_completed.Count > 0 && _entries[_completed.Peek()].Deadline < _clock.GetUtcNow())
                _entries.Remove(_completed.Dequeue());
            if (_entries.TryGetValue(command.RequestId, out var existing))
                return existing.Command == command ? existing.Result : new(command.RequestId, "conflict", "Request UUID already identifies a different command.");
            if (command.IssuedAtUtc.Offset != TimeSpan.Zero || command.IssuedAtUtc < _clock.GetUtcNow().AddSeconds(-30)
                || command.IssuedAtUtc > _clock.GetUtcNow().AddSeconds(2))
                return new(command.RequestId, "expired", "Command issue time must be within the current30-second window.");
            if (_entries.Count >= 256) return new(command.RequestId, "unavailable", "Admin result window is full; retry after existing commands expire.");
            if (_closed) return new(command.RequestId, "unavailable", "Server admin is stopping.");
            var entry = new Entry(command, command.IssuedAtUtc.AddSeconds(30));
            if (!_queue.Writer.TryWrite(entry)) return new(command.RequestId, "unavailable", "Admin command queue is full.");
            _entries.Add(command.RequestId, entry); return entry.Result;
        }
    }

    private static bool ValidShape(AdminCommand command)
    {
        bool needsConnection = command.Kind is AdminCommandKind.AssignTeam or AdminCommandKind.ForceSpectator
            or AdminCommandKind.ConfirmReady or AdminCommandKind.Kick or AdminCommandKind.Mute or AdminCommandKind.Unmute;
        if (needsConnection != command.ConnectionId.HasValue || command.ConnectionId == 0
            || (command.Kind == AdminCommandKind.AssignTeam) != command.TeamIndex.HasValue) return false;
        if (command.Kind == AdminCommandKind.SelectPreset)
        {
            if (command.Preset is not { Length: > 0 and <= 32 } || command.RoomKey?.Length > 128) return false;
        }
        else if (command.Preset != null || command.RoomKey != null) return false;
        return command.Kind == AdminCommandKind.SetRoundIdentity
            ? command.TournamentId is { Length: > 0 and <= 64 } && command.RoundId is { Length: > 0 and <= 64 }
            : command.TournamentId == null && command.RoundId == null;
    }

    public AdminCommandResult? Find(Guid id)
    {
        lock (_gate) return _entries.TryGetValue(id, out var entry) ? entry.Result : null;
    }

    public bool Drain(uint tick, Func<AdminCommand, AdminCommandResult> execute)
    {
        bool processed = false;
        for (int i = 0; i < 4 && _queue.Reader.TryRead(out var entry); i++)
        {
            processed = true;
            AdminCommandResult result;
            if (_clock.GetUtcNow() > entry.Deadline)
                result = new(entry.Command.RequestId, "expired", "Command expired before the owner could apply it.");
            else
            {
                try { result = execute(entry.Command) with { RequestId = entry.Command.RequestId, AppliedTick = tick }; }
                catch (ArgumentException) { result = new(entry.Command.RequestId, "rejected", "Invalid command parameters.", tick); }
                catch (InvalidOperationException) { result = new(entry.Command.RequestId, "rejected", "Command is invalid in the current server state.", tick); }
            }
            lock (_gate)
            {
                entry.Result = result; _completed.Enqueue(entry.Command.RequestId);
            }
        }
        return processed;
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true; _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var entry))
                entry.Result = new(entry.Command.RequestId, "unavailable", "Server stopped before applying the command.");
        }
    }
}
