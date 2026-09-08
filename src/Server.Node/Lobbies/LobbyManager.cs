using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Security.Cryptography;
using MphRead.Identity;
using System.Text;
using FruityPrime.Server.Shared;
using MphRead;

namespace FruityPrime.Server.Node.Lobbies;

public sealed record LobbyIdentity(Guid SessionId, Guid PlayerId, string DisplayName);
public sealed class LobbyCommandException(string code, string message) : Exception(message)
{ public string Code { get; } = code; }

/// <summary>Serialized lightweight lobby authority. No gameplay Scene or simulation ownership.</summary>
public sealed partial class LobbyManager
{
    private sealed class Lobby(Guid id, LobbyCreate command, Guid owner)
    {
        public Guid Id = id;
        public LobbyCreate Rules = command;
        public Guid Owner = owner;
        public long Revision;
        public LobbyPhase Phase = LobbyPhase.Open;
        public string MapKey = "";
        public MatchMode Mode = MatchMode.Battle;
        public int BotCount;
        public int? TimeLimitSeconds;
        public Guid? MatchId;
        public Dictionary<Guid, LobbyMember> Members = [];
        public Queue<LobbyChatEntry> Chat = [];
        public LobbySnapshot Snapshot() => new(Id, Rules.Name, Rules.Visibility, Owner, Phase, Revision,
            Rules.PlayerLimit, Rules.ObserverLimit, Members.Values.ToImmutableArray(), Chat.ToImmutableArray(), MapKey, Mode, MatchId, BotCount, TimeLimitSeconds);
    }
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Lobby> _lobbies = [];
    private readonly Dictionary<Guid, Guid> _membership = [];
    private readonly int _maximumLobbies;
    private bool _admissionClosed;
    public void CloseAdmission() { lock (_gate) _admissionClosed = true; }
    private readonly ConcurrentDictionary<Guid, LobbySnapshot> _notifications = [];
    private readonly Channel<bool> _notificationSignal = Channel.CreateBounded<bool>(1);
    public async IAsyncEnumerable<LobbySnapshot> ReadNotifications([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var _ in _notificationSignal.Reader.ReadAllAsync(cancellationToken))
            foreach (var pair in _notifications.ToArray())
                if (_notifications.TryRemove(pair)) yield return pair.Value;
    }
    public LobbyManager(int maximumLobbies = 256)
    {
        if (maximumLobbies is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumLobbies));
        _maximumLobbies = maximumLobbies;
    }
    public int Count { get { lock (_gate) return _lobbies.Count; } }
    public LobbySnapshot? ForSession(Guid sessionId)
    { lock (_gate) return _membership.TryGetValue(sessionId, out var id) ? _lobbies[id].Snapshot() : null; }
    public object Execute(LobbyIdentity identity, NodeCommand command)
    {
        lock (_gate)
        {
            if (TryExecuteRoundCommand(identity, command, out var roundResponse)) return roundResponse;
            switch (command)
            {
                case LobbyList list:
                    if (list.Offset < 0 || list.Limit is < 1 or > 16) throw Error("invalid", "Invalid page bounds.");
                    var rows = _lobbies.Values.Where(l => l.Rules.Visibility == LobbyVisibility.Public)
                        .OrderBy(l => l.Id).Skip(list.Offset).Take(list.Limit + 1).ToArray();
                    return new LobbyListSnapshot(rows.Take(list.Limit).Select(l => new LobbyListEntry(l.Id, l.Rules.Name,
                        l.Phase, l.Members.Values.Count(m => !m.Observer), l.Rules.PlayerLimit,
                        l.Members.Values.Count(m => m.Observer), l.Revision)).ToImmutableArray(), rows.Length > list.Limit ? list.Offset + list.Limit : null);
                case LobbyCreate create:
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    RequireUnjoined(identity.SessionId);
                    if (!Text(create.Name, 64) || !Enum.IsDefined(create.Visibility)
                        || create.PlayerLimit is < 1 or > 8 || create.ObserverLimit is < 0 or > 16)
                        throw Error("invalid", "Invalid lobby settings.");
                    if (_lobbies.Count >= _maximumLobbies) throw Error("capacity", "Lobby capacity reached.");
                    var created = new Lobby(Guid.NewGuid(), create, identity.SessionId);
                    _lobbies.Add(created.Id, created);
                    return Join(created, identity, false);
                case LobbyJoin join:
                    if (_admissionClosed) throw Error("draining", "Node is draining.");
                    RequireUnjoined(identity.SessionId);
                    if (!_lobbies.TryGetValue(join.LobbyId, out var target)) throw Error("not_found", "Lobby not found.");
                    Revision(target, join.ExpectedRevision);
                    if (target.Phase != LobbyPhase.Open) throw Error("phase", "Lobby roster is frozen.");
                    return Join(target, identity, join.Observer);
                case LobbyLeave leave:
                    var leaving = RequireLobby(identity.SessionId);
                    Revision(leaving, leave.ExpectedRevision);
                    var left = leaving.Id;
                    LeaveCore(identity.SessionId);
                    return new LobbyLeft(left);
                default:
                    var lobby = RequireLobby(identity.SessionId);
                    var member = lobby.Members[identity.SessionId];
                    long revision = command switch
                    {
                        LobbySetReady c => c.ExpectedRevision, LobbySelectHunter c => c.ExpectedRevision,
                        LobbyRequestTeam c => c.ExpectedRevision, LobbyChat c => c.ExpectedRevision,
                        LobbyConfigure c => c.ExpectedRevision, _ => -1
                    };
                    Revision(lobby, revision);
                    if (lobby.Phase != LobbyPhase.Open && command is not LobbyChat) throw Error("phase", "Lobby settings are frozen.");
                    switch (command)
                    {
                        case LobbyConfigure configure:
                            if (lobby.Owner != identity.SessionId) throw Error("owner", "Only the owner may configure the lobby.");
                            if (!Text(configure.MapKey, 128) || !Enum.IsDefined(configure.Mode)) throw Error("invalid", "Invalid map or mode.");
                            if (configure.BotCount < 0 || configure.BotCount + lobby.Members.Values.Count(m => !m.Observer) > lobby.Rules.PlayerLimit
                                || configure.TimeLimitSeconds is < 1 or > 3600) throw Error("invalid", "Invalid bot count or time limit.");
                            _ = MatchRules.CreateDefault(configure.Mode, configure.MapKey);
                            lobby.MapKey = configure.MapKey; lobby.Mode = configure.Mode;
                            lobby.BotCount = configure.BotCount; lobby.TimeLimitSeconds = configure.TimeLimitSeconds;
                            foreach (var item in lobby.Members.ToArray()) lobby.Members[item.Key] = item.Value with { Ready = false };
                            break;
                        case LobbySetReady ready:
                            if (member.Observer) throw Error("role", "Observers cannot ready.");
                            if (member.Ready == ready.Ready) return lobby.Snapshot();
                            lobby.Members[identity.SessionId] = member with { Ready = ready.Ready };
                            break;
                        case LobbySelectHunter hunter:
                            if (member.Observer || !Enum.IsDefined(hunter.Hunter) || hunter.Hunter > Hunter.Guardian)
                                throw Error("invalid", "Invalid hunter selection.");
                            if (member.Hunter == hunter.Hunter) return lobby.Snapshot();
                            lobby.Members[identity.SessionId] = member with { Hunter = hunter.Hunter, Ready = false };
                            break;
                        case LobbyRequestTeam team:
                            if (member.Observer || team.Team > 1) throw Error("invalid", "Invalid team.");
                            if (member.Team == team.Team) return lobby.Snapshot();
                            lobby.Members[identity.SessionId] = member with { Team = team.Team, Ready = false };
                            break;
                        case LobbyChat chat:
                            if (!Text(chat.Text, 256)) throw Error("invalid", "Invalid chat text.");
                            lobby.Chat.Enqueue(new(lobby.Revision + 1, member.SessionId, member.DisplayName, chat.Text));
                            while (lobby.Chat.Count > 16) lobby.Chat.Dequeue();
                            break;
                        default: throw Error("unsupported", "Unsupported command.");
                    }
                    return Publish(lobby);
            }
        }
    }
    public void Disconnect(Guid sessionId) { lock (_gate) LeaveCore(sessionId); }
    public MatchSpec PrepareMatch(Guid ownerSession, long expectedRevision, ContentIdentity content, NodeId nodeId, Guid incarnation)
    {
        lock (_gate)
        {
            var lobby = RequireLobby(ownerSession);
            if (_admissionClosed) throw Error("draining", "Node is draining.");
            Revision(lobby, expectedRevision);
            if (lobby.Owner != ownerSession) throw Error("owner", "Only the owner may start a match.");
            if (lobby.Phase != LobbyPhase.Open || lobby.MapKey != content.MapKey) throw Error("phase", "Lobby is not configured for this content.");
            ValidateRoundStart(lobby.Id);
            var players = lobby.Members.Values.Where(m => !m.Observer).ToArray();
            if (players.Length == 0 || players.Any(m => !m.Ready)) throw Error("not_ready", "All players must be ready.");
            var seats = ImmutableArray.CreateBuilder<RosterSeat>();
            foreach (var member in players)
            {
                seats.Add(new((byte)seats.Count, new PlayerId(member.PlayerId), null, member.DisplayName, member.Hunter,
                    member.Team, SeatRole.Player, false));
            }
            for (int bot = 0; bot < lobby.BotCount; bot++)
                seats.Add(new((byte)seats.Count, null, null, "Bot" + (bot + 1), Hunter.Samus, (byte)((players.Length + bot) % 2), SeatRole.Bot, false));
            if (lobby.Mode.IsTeamMode() && seats.Select(s => s.Team).Distinct().Count() != 2)
                throw Error("teams", "Both teams need players.");
            int observerSeat = 8;
            foreach (var member in lobby.Members.Values.Where(m => m.Observer))
                seats.Add(new((byte)observerSeat++, new PlayerId(member.PlayerId), null, member.DisplayName, member.Hunter, member.Team, SeatRole.Observer, false));
            var rules = MatchRules.CreateDefault(lobby.Mode, lobby.MapKey).With(maxPlayers: lobby.Rules.PlayerLimit);
            if (lobby.TimeLimitSeconds is { } seconds) rules = rules.With(timeLimit: TimeSpan.FromSeconds(seconds));
            var spec = new MatchSpec(new(Guid.NewGuid()), new(lobby.Id), nodeId, incarnation,
                rules, content,
                MatchTrustClass.Community, null, null, seats.ToImmutable(), lobby.BotCount == 0 ? BotFillPolicy.Disabled : BotFillPolicy.FillVacancies,
                lobby.Rules.ObserverLimit > 0 ? ObserverPolicy.Allowed : ObserverPolicy.Disabled,
                ReplayPolicy.Record, TelemetryPolicy.Record,
                BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)));
            spec = ApplyRoundIdentity(lobby.Id, spec);
            spec.Validate();
            lobby.MatchId = spec.MatchId.Value; lobby.Phase = LobbyPhase.StartingMatch; Publish(lobby);
            return spec;
        }
    }
    public bool MatchReady(MatchPlacement placement)
    {
        lock (_gate)
        {
            var lobby = _lobbies.Values.SingleOrDefault(l => l.MatchId == placement.MatchId.Value);
            if (lobby == null || lobby.Phase != LobbyPhase.StartingMatch) return false;
            lobby.Phase = LobbyPhase.InMatch; Publish(lobby); return true;
        }
    }
    public bool MatchEnded(MatchId matchId, bool interrupted)
    {
        lock (_gate)
        {
            var lobby = _lobbies.Values.SingleOrDefault(l => l.MatchId == matchId.Value);
            if (lobby == null || lobby.Phase is not (LobbyPhase.StartingMatch or LobbyPhase.InMatch)) return false;
            lobby.Phase = LobbyPhase.PostMatch; OnRoundEnded(lobby.Id); Publish(lobby); return true;
        }
    }
    public LobbySnapshot ReturnToLobby(Guid ownerSession, long expectedRevision)
    {
        lock (_gate)
        {
            var lobby = RequireLobby(ownerSession); Revision(lobby, expectedRevision);
            if (lobby.Owner != ownerSession) throw Error("owner", "Only the owner may reopen the lobby.");
            if (lobby.Phase != LobbyPhase.PostMatch) throw Error("phase", "Match has not ended.");
            lobby.Phase = LobbyPhase.Open; lobby.MatchId = null;
            foreach (var item in lobby.Members.ToArray()) lobby.Members[item.Key] = item.Value with { Ready = false };
            return Publish(lobby);
        }
    }
    private static void Revision(Lobby lobby, long expected)
    { if (lobby.Revision != expected) throw Error("stale_revision", "Lobby changed; use the latest snapshot."); }
    private LobbySnapshot Join(Lobby lobby, LobbyIdentity identity, bool observer)
    {
        int count = lobby.Members.Values.Count(m => m.Observer == observer);
        if (count >= (observer ? lobby.Rules.ObserverLimit : lobby.Rules.PlayerLimit - lobby.BotCount)) throw Error("capacity", "Lobby role capacity reached.");
        lobby.Members.Add(identity.SessionId, new(identity.SessionId, identity.PlayerId, identity.DisplayName, Hunter.Samus, 0, false, observer));
        _membership.Add(identity.SessionId, lobby.Id);
        return Publish(lobby);
    }
    private void LeaveCore(Guid sessionId)
    {
        if (!_membership.Remove(sessionId, out var lobbyId)) return;
        var lobby = _lobbies[lobbyId];
        lobby.Members.Remove(sessionId);
        if (lobby.Members.Count == 0) { OnLobbyRemoved(lobbyId); _lobbies.Remove(lobbyId); _notifications.TryRemove(lobbyId, out _); return; }
        if (lobby.Owner == sessionId) lobby.Owner = lobby.Members.Keys.First();
        Publish(lobby);
    }
    private LobbySnapshot Publish(Lobby lobby)
    {
        lobby.Revision++;
        var snapshot = lobby.Snapshot();
        // One latest snapshot per bounded lobby, one wake signal. A slow network
        // consumer cannot run callbacks under the authority lock or grow a queue.
        _notifications[lobby.Id] = snapshot;
        _notificationSignal.Writer.TryWrite(true);
        return snapshot;
    }
    private Lobby RequireLobby(Guid sessionId) => _membership.TryGetValue(sessionId, out var id) ? _lobbies[id] : throw Error("not_joined", "Join a lobby first.");
    private void RequireUnjoined(Guid id) { if (_membership.ContainsKey(id)) throw Error("already_joined", "Leave the current lobby first."); }
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl) && Encoding.UTF8.GetByteCount(text) <= maximum;
    private static LobbyCommandException Error(string code, string message) => new(code, message);
}
