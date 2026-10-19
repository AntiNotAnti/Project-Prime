using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace MphRead.Mods.Launcher;

public sealed record NativeSocialPrivacy(string Presence, string Activity, string Invites, bool DoNotDisturb);
public sealed record NativePresencePerson(string PrimeId, string DisplayName, string Activity,
    string RoomKey, string LobbyId, bool Joinable, DateTimeOffset LastSeen);
public sealed record NativePresenceDirectory(string SelfPrimeId, NativeSocialPrivacy Privacy,
    NativePresencePerson[] People)
{
    // Optional opaque local authority witness; never bound to a document.
    [JsonIgnore]
    internal object? AccountWitness { get; init; }
}
public sealed record NativePresenceView(long Revision, bool Busy, string Status, NativePresenceDirectory? Directory);
public interface INativePresenceService
{
    bool IsCurrent(NativePresenceDirectory directory) => true;
    Task<NativePresenceDirectory> ReadAsync(CancellationToken cancellation);
    Task<NativePresenceDirectory> HeartbeatAsync(string self, Guid session, string activity,
        string room, CancellationToken cancellation);
    Task<NativePresenceDirectory> PrivacyAsync(string self, NativeSocialPrivacy expected,
        NativeSocialPrivacy next, CancellationToken cancellation);
    Task LeaveAsync(string self, Guid session, CancellationToken cancellation);
}

/// <summary>Owner-thread projection; HTTPS stays on workers, presence expires after45s.</summary>
public sealed class NativePresenceController : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly INativePresenceService _service;
    private readonly TimeProvider _clock;
    private readonly Guid _session = Guid.NewGuid();
    private readonly ConcurrentQueue<(int Generation,NativePresenceDirectory? Data)> _results = new();
    private CancellationTokenSource? _active;
    private NativePresenceDirectory? _data;
    private int _generation;
    private long _revision = 1;
    private bool _disposed, _busy;
    private DateTimeOffset _next, _expires;
    private string _status = "PRESENCE NOT VERIFIED";
    private string _sentActivity = "", _sentRoom = "";
    private string _advertisedSelf = "";

    public NativePresenceController(INativePresenceService service, TimeProvider? clock = null)
    { _service = service; _clock = clock ?? TimeProvider.System; }
    private void Owner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Presence belongs to the UI owner thread.");
    }
    private void Retire()
    {
        ++_generation;
        try { _active?.Cancel(); } catch (ObjectDisposedException) { }
        _active = null;
    }
    private void Start(Func<CancellationToken,Task<NativePresenceDirectory>> work, string status)
    {
        Retire();
        int generation = _generation;
        var cancellation = new CancellationTokenSource();
        _active = cancellation; _busy = true; _status = status; ++_revision;
        _ = Task.Run(async () =>
        {
            NativePresenceDirectory? data = null;
            try { data = await work(cancellation.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
            if (!cancellation.IsCancellationRequested) _results.Enqueue((generation,data));
            cancellation.Dispose();
        });
    }
    public static bool ValidPrivacy(NativeSocialPrivacy? value) => value != null
        && value.Presence is "everyone" or "friends" or "hidden"
        && value.Activity is "everyone" or "friends" or "private"
        && value.Invites is "everyone" or "friends" or "nobody";
    public static bool ValidDirectory(NativePresenceDirectory? value) => value != null
        && NativeSocialFriendsController.ValidPrimeId(value.SelfPrimeId) && ValidPrivacy(value.Privacy)
        && value.People != null && value.People.Length <= 1024
        && value.People.All(p => p != null && NativeSocialFriendsController.ValidPrimeId(p.PrimeId)
            && !p.PrimeId.Equals(value.SelfPrimeId,StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(p.DisplayName)
            && p.DisplayName.Length <= 128 && p.Activity is "online" or "menu" or "lobby" or "in_match" or "spectating"
            && p.RoomKey != null && p.RoomKey.Length <= 128 && p.LobbyId != null
            && (p.LobbyId.Length == 0 || Guid.TryParse(p.LobbyId,out _))
            && (!p.Joinable || p.Activity == "lobby" && p.LobbyId.Length != 0)
            && p.LastSeen != default && p.LastSeen <= DateTimeOffset.MaxValue.AddSeconds(-45))
        && value.People.Select(p => p.PrimeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.People.Length;

    public void Refresh()
    {
        Owner(); _data = null; _sentActivity = _sentRoom = "";
        Start(_service.ReadAsync, "VERIFYING PRESENCE AND PRIVACY");
    }
    public NativePresenceView Current()
    { Owner(); RetireChangedAccount(); return new(_revision,_busy,_status,_data); }
    private void RetireChangedAccount()
    {
        if (_data == null || _service.IsCurrent(_data)) return;
        Retire(); _data = null; _busy = false; _next = default;
        _sentActivity = _sentRoom = _advertisedSelf = "";
        _status = "PRESENCE ACCOUNT CHANGED // VERIFY AGAIN"; ++_revision;
    }
    public bool SetPrivacy(NativeSocialPrivacy next)
    {
        Owner();
        RetireChangedAccount();
        if (_busy || _data is not { } current || !ValidPrivacy(next) || next == current.Privacy) return false;
        Start(async token =>
        {
            var data = await _service.PrivacyAsync(current.SelfPrimeId,current.Privacy,next,token).ConfigureAwait(false);
            if (data.SelfPrimeId != current.SelfPrimeId || data.Privacy != next)
                throw new InvalidOperationException("Privacy was not verified.");
            return data;
        }, "VERIFYING PRIVACY CHANGE");
        _data = null;
        return true;
    }
    public NativePresenceView Tick(string activity = "menu", string room = "")
    {
        Owner();
        RetireChangedAccount();
        DateTimeOffset now = _clock.GetUtcNow();
        while (_results.TryDequeue(out var result))
        {
            if (result.Generation != _generation) continue;
            _active = null; _busy = false; ++_revision;
            _data = ValidDirectory(result.Data) && _service.IsCurrent(result.Data!)
                ? result.Data! with { People = result.Data!.People.ToArray() } : null;
            _status = _data == null ? "PRESENCE UNAVAILABLE // CHECK ACCOUNT AND SERVICE" : "SERVER-VERIFIED PRESENCE AND PRIVACY";
            _next = now.AddSeconds(15); _expires = now.AddSeconds(45);
        }
        if (_data != null && now >= _expires)
        { _data = null; _status = "PRESENCE EXPIRED"; ++_revision; }
        if (_data is { } valid)
        {
            var people = valid.People.Where(p => p.LastSeen.AddSeconds(45) > now).ToArray();
            if (people.Length != valid.People.Length)
            { _data = valid with { People = people }; ++_revision; }
        }
        if (!_busy && now >= _next)
        {
            if (_data is { } current)
            {
                string sentActivity = activity is "online" or "menu" or "lobby" or "in_match" or "spectating" ? activity : "menu";
                string sentRoom = room != null && room.Length <= 128 ? room : "";
                _sentActivity = sentActivity; _sentRoom = sentRoom;
                _advertisedSelf = current.SelfPrimeId;
                Start(async token =>
                {
                    var next = await _service.HeartbeatAsync(current.SelfPrimeId,_session,sentActivity,sentRoom,token).ConfigureAwait(false);
                    if (next.SelfPrimeId != current.SelfPrimeId) throw new InvalidOperationException("Presence account changed.");
                    return next;
                }, "SYNCING PRESENCE");
            }
            else Start(_service.ReadAsync, "VERIFYING PRESENCE AND PRIVACY");
        }
        // Activity transitions are observed on the render owner; the next
        // frame may schedule a heartbeat, never an HTTPS call on this thread.
        if (!_busy && _data != null && (_sentActivity != activity || _sentRoom != room))
            _next = now;
        return new(_revision,_busy,_status,_data);
    }
    public void Dispose()
    {
        if (_disposed) return;
        Owner(); Retire(); _disposed = true;
        string self = _advertisedSelf; _data = null; _advertisedSelf = "";
        if (self.Length != 0)
            _ = Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _service.LeaveAsync(self,_session,timeout.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { }
            });
    }
}
