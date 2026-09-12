using System.Text.Json;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.WorkerSoak;

/// <summary>Fixed, bounded scenario inputs shared by the soak and capacity tools.</summary>
public sealed record SoakRosterOptions(int Players, int Bots, int Observers)
{
    public int ActiveSeats => Players + Bots;
    public int RosterSeats => ActiveSeats + Observers;

    public void Validate()
    {
        if (Players is < 1 or > 8 || Bots is < 0 or > 7 || Observers is < 0 or > 16
            || ActiveSeats is < 1 or > 8 || RosterSeats > 32)
            throw new ArgumentException("The soak roster must fit the bounded lobby and Worker roster capacities.");
    }
}

public sealed record SoakRequirements(
    bool RequireNoFailures,
    bool RequireDrain,
    bool RequireCrashRecovery,
    bool RequireReconnectRecovery,
    bool RequireOutageRecovery,
    bool RequireRematchRecovery)
{
    public void Validate(SoakScenarioOptions scenario)
    {
        if (RequireCrashRecovery && (!scenario.CrashesEnabled || scenario.CrashSeconds == 0))
            throw new ArgumentException("Crash recovery is required but crash injection is disabled.");
        if (RequireReconnectRecovery && !scenario.ReconnectsEnabled)
            throw new ArgumentException("Reconnect recovery is required but reconnects are disabled.");
        if (RequireOutageRecovery && !scenario.OutagesEnabled)
            throw new ArgumentException("Outage recovery is required but outages are disabled.");
        if (RequireRematchRecovery && !scenario.RematchesEnabled)
            throw new ArgumentException("Rematch recovery is required but rematches are disabled.");
    }
}

public sealed record SoakScenarioOptions(
    int Seconds,
    int MatchesPerWorker,
    int Lanes,
    int Workers,
    int RoundSeconds,
    int CrashSeconds,
    bool OutagesEnabled,
    bool ReconnectsEnabled,
    bool RematchesEnabled,
    int RematchEvery,
    ReplayPolicy ReplayPolicy,
    int ObserverDelaySeconds,
    SoakRosterOptions Roster)
{
    public bool CrashesEnabled => CrashSeconds > 0;
    public int TargetMatches => checked(MatchesPerWorker * Workers);
    public int TargetClientPeers => checked(TargetMatches * (Roster.Players + Roster.Observers));

    public bool HasRequiredTraffic(long inputs, long observerSnapshots, long playingSnapshots)
        => inputs > 0 && playingSnapshots > 0 && (Roster.Observers == 0 || observerSnapshots > 0);

    public void Validate()
    {
        if (Seconds is < 1 or > 172800 || MatchesPerWorker is < 1 or > 64 || Lanes is < 1 or > 64
            || Workers is < 1 or > 8 || RoundSeconds is < 1 or > 3600 || CrashSeconds < 0)
            throw new ArgumentException("Invalid bounded soak scenario.");
        if (RematchesEnabled && (RematchEvery < 1 || RematchEvery > 4096))
            throw new ArgumentException("Invalid bounded rematch interval.");
        if (!Enum.IsDefined(ReplayPolicy) || ObserverDelaySeconds is < 0 or > 30)
            throw new ArgumentException("Replay policy or observer delay is invalid.");
        Roster.Validate();
        if ((Seconds / Math.Max(1, RoundSeconds) + 1L) * MatchesPerWorker * Workers > 3500)
            throw new ArgumentException("Soak would exhaust bounded receipt history; increase round seconds or reduce density.");
    }

    public static SoakScenarioOptions From(Dictionary<string, string> args)
    {
        int Number(string key, int fallback) => args.TryGetValue(key, out var value)
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
        bool Flag(string key, bool fallback) => args.TryGetValue(key, out var value)
            ? bool.Parse(value) : fallback;
        ReplayPolicy Replay(string key, ReplayPolicy fallback) => args.TryGetValue(key, out var value)
            ? value.ToLowerInvariant() switch
            {
                "disabled" or "off" => ReplayPolicy.Disabled,
                "record" or "on" => ReplayPolicy.Record,
                _ => throw new ArgumentException("Replay policy must be disabled/off or record/on.")
            }
            : fallback;
        int rematchEvery = Number("--rematch-every", 4);
        var scenario = new SoakScenarioOptions(
            Number("--seconds", 300), Number("--matches", 4), Number("--lanes", 2), Number("--workers", 2),
            Number("--round-seconds", 30), Number("--crash-seconds", 0), Flag("--outages", true),
            Flag("--reconnects", true), Flag("--rematches", false), rematchEvery,
            Replay("--replay-policy", ReplayPolicy.Record), Number("--observer-delay-seconds", 0),
            new SoakRosterOptions(Number("--players", 1), Number("--bots", 2), Number("--observers", 1)));
        scenario.Validate();
        return scenario;
    }
}

public static class SoakRecoveryPolicy
{
    // Reconnects are deliberately injected while a match still has enough
    // authoritative clock remaining for a bounded admission/recovery cycle.
    // The value is a harness bound, not a gameplay timeout.
    public const double ReconnectRecoveryDeadlineSeconds = 10;
    public const double PreferredReconnectStartSeconds = 5;

    public static bool ShouldReconnect(bool crashPlanned, bool sameWorkerAsCrashVictim)
        => !crashPlanned || !sameWorkerAsCrashVictim;

    public static bool ShouldInterruptOnWorkerExit(bool terminalCompleted)
        => !terminalCompleted;

    public static bool IsReconnectPeerReady(bool connected, bool playing, bool freshProgress,
        double? authoritativeRemainingSeconds, double recoveryDeadlineSeconds = ReconnectRecoveryDeadlineSeconds)
        => connected && playing && freshProgress && authoritativeRemainingSeconds is { } remaining
            && double.IsFinite(remaining) && remaining >= recoveryDeadlineSeconds;

    public static bool IsReconnectBatchReady(IReadOnlyList<SoakReconnectPeerEvidence> peers,
        double recoveryDeadlineSeconds = ReconnectRecoveryDeadlineSeconds)
        => peers.Count > 0 && peers.All(peer => IsReconnectPeerReady(peer.Connected, peer.Playing,
            peer.FreshProgress, peer.RemainingSeconds, recoveryDeadlineSeconds));
}

/// <summary>Sanitized, immutable evidence for one client seat at a reconnect boundary.</summary>
public sealed record SoakReconnectPeerEvidence(byte SeatId, string Role, int Generation, string Phase,
    double? RemainingSeconds, bool Connected, bool Playing, bool FreshProgress, uint ServerTick);

public sealed record SoakReconnectRecoveryEvidence(SoakReconnectPeerEvidence Injection,
    SoakReconnectPeerEvidence Final);

public sealed record SoakReconnectReadiness(bool Ready, string Reason, double RecoveryDeadlineSeconds,
    IReadOnlyList<SoakReconnectPeerEvidence> Peers);

public sealed record SoakReconnectBatch(Guid MatchId, double RecoveryDeadlineSeconds,
    IReadOnlyList<SoakReconnectPeerEvidence> Peers)
{
    public int Attempts => Peers.Count;
}

/// <summary>Bounded, immediate evidence for events that can make a run unusable before summary.json exists.
/// Values are deliberately reduced to sanitized IDs/statuses; no exception or report payload is written.</summary>
internal sealed class CriticalEventLog : IDisposable
{
    private const int MaximumEvents = 8192;
    private const long MaximumBytes = 4L * 1024 * 1024;
    private const int ReservedFatalEvents = 8;
    private const long ReservedFatalBytes = 64L * 1024;
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private int _events;
    private long _bytes;

    public CriticalEventLog(string directory)
    {
        Directory.CreateDirectory(directory);
        _writer = new StreamWriter(Path.Combine(directory, "critical-events.jsonl"), false)
        { AutoFlush = true };
    }

    public void Write(string kind, string component, string? workerId = null, string? incarnation = null,
        string? matchId = null, string? status = null, string? phase = null, double? elapsed = null)
    {
        lock (_gate)
        {
            var item = new
            {
                utc = DateTimeOffset.UtcNow,
                process = "Harness+Node+Backend",
                kind = Token(kind, 64),
                component = Token(component, 64),
                workerId = TokenOrNull(workerId, 64),
                incarnation = TokenOrNull(incarnation, 64),
                matchId = TokenOrNull(matchId, 64),
                status = TokenOrNull(status, 64),
                phase = TokenOrNull(phase, 64),
                elapsed = elapsed is { } value && double.IsFinite(value) && value >= 0 ? value : (double?)null
            };
            string line = JsonSerializer.Serialize(item);
            long recordBytes = line.Length * 2L + 2;
            bool fatal = string.Equals(kind, "fatal", StringComparison.Ordinal);
            int eventLimit = fatal ? MaximumEvents : MaximumEvents - ReservedFatalEvents;
            long byteLimit = fatal ? MaximumBytes : MaximumBytes - ReservedFatalBytes;
            if (_events >= eventLimit || _bytes + recordBytes > byteLimit)
                throw new IOException(fatal ? "Critical fatal-event evidence capacity exhausted." :
                    "Critical event evidence capacity exhausted before the fatal-event reserve.");
            _writer.WriteLine(line); _events++; _bytes += recordBytes;
        }
    }

    public bool TryWriteFatal(string component, string? workerId = null, string? incarnation = null,
        string? matchId = null, string? status = null, string? phase = null, double? elapsed = null)
    {
        try
        {
            Write("fatal", component, workerId, incarnation, matchId, status, phase, elapsed);
            return true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[worker-soak] fatal critical-event append failed: " + error.GetType().Name);
            return false;
        }
    }

    private static string? TokenOrNull(string? value, int maximum) => value == null ? null : Token(value, maximum);
    private static string Token(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var chars = value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':').Take(maximum).ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    public void Dispose() { lock (_gate) _writer.Dispose(); }
}

public enum SoakOccupancyPhase { Warmup, Normal, Turnover, Disruption }

public sealed record SoakOccupancySample(double Logical, double PlayingPeers, double ConnectedPeers, SoakOccupancyPhase Phase);

public static class SoakOccupancy
{
    public static SoakOccupancySample Sample(
        IReadOnlyCollection<SoakClientSnapshot> actors,
        int activeMatches,
        int targetMatches,
        int targetClientPeers,
        bool turnover,
        bool disruption)
    {
        double logical = targetMatches == 0 ? 0 : Math.Clamp(activeMatches / (double)targetMatches, 0, 1);
        int playing = actors.Sum(actor => actor.PlayingPeers);
        int connected = actors.Sum(actor => actor.ConnectedPeers);
        double playingRatio = targetClientPeers == 0 ? 0 : Math.Clamp(playing / (double)targetClientPeers, 0, 1);
        double connectedRatio = targetClientPeers == 0 ? 0 : Math.Clamp(connected / (double)targetClientPeers, 0, 1);
        var phase = disruption ? SoakOccupancyPhase.Disruption
            : turnover ? SoakOccupancyPhase.Turnover
            : actors.Any(actor => !actor.AllPeersConnected || !actor.HasProgress) ? SoakOccupancyPhase.Warmup
            : SoakOccupancyPhase.Normal;
        return new(logical, playingRatio, connectedRatio, phase);
    }
}
