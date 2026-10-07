using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Durable authoritative match reporting.
    ///
    /// The dedicated server captures facts from the same simulation that owns
    /// damage and the scoreboard, writes an outbox record before touching the
    /// network, and posts it to Supabase in the background. A failed request
    /// leaves the file in place for the next retry/restart.
    /// </summary>
    internal static class CareerReportOutbox
    {
        private const string DefaultUrl =
            "https://hwcjaygoistufktorbmf.supabase.co/functions/v1/career-report";
        private const string PublishableKey =
            "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        private static int _draining;
        private static int _probing;
        private static int _warnedDisabled;

        private static string DirectoryPath => Path.Combine(
            Platform.AppPaths.UserDataDirectory, "career-outbox");

        private static string ReportUrl =>
            Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_REPORT_URL")
            ?? DefaultUrl;

        private static string ServerKey =>
            Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY")
            ?? "";

        public static bool Enabled => ServerKey.Length >= 32;

        public static void Start()
        {
            if (!Enabled)
            {
                if (Interlocked.Exchange(ref _warnedDisabled, 1) == 0)
                {
                    Console.WriteLine("[career] reporting disabled: "
                        + "PROJECT_PRIME_CAREER_SERVER_KEY is not configured");
                }
                return;
            }
            Console.WriteLine($"[career] reporting enabled: {ReportUrl}");
            Kick();
            Probe();
        }

        public static void Enqueue(CareerMatchReport report)
        {
            if (report.ContainsBots) return;
            if (!Enabled)
            {
                Start();
                return;
            }
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, $"{report.MatchId:N}.json");
                string temp = path + ".tmp";
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, Json);
                if (bytes.Length > 512 * 1024)
                {
                    Console.WriteLine($"[career] report {report.MatchId} exceeds the 512 KiB limit");
                    return;
                }
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
                Console.WriteLine($"[career] queued authoritative match {report.MatchId}");
                Kick();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[career] could not spool match {report.MatchId}: {ex.Message}");
            }
        }

        private static void Kick()
        {
            if (!Enabled || Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
            {
                return;
            }
            _ = Task.Run(DrainAsync);
        }

        private static void Probe()
        {
            if (!Enabled || Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
                return;

            FileStream? lease = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string marker = Path.Combine(DirectoryPath, ".probe-stamp");
                string gate = Path.Combine(DirectoryPath, ".probe-lock");
                try
                {
                    lease = new FileStream(gate, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    Interlocked.Exchange(ref _probing, 0);
                    return;
                }

                if (File.Exists(marker)
                    && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker)
                        < TimeSpan.FromMinutes(5))
                {
                    lease.Dispose();
                    Interlocked.Exchange(ref _probing, 0);
                    return;
                }
                File.WriteAllText(marker, DateTime.UtcNow.Ticks.ToString(
                    CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lease?.Dispose();
                Interlocked.Exchange(ref _probing, 0);
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, ReportUrl);
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ServerKey);
                    request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
                    using HttpResponseMessage response = await Http.SendAsync(request)
                        .ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Console.WriteLine(response.IsSuccessStatusCode
                        ? "[career] reporter credential accepted"
                        : $"[career] reporter probe refused ({(int)response.StatusCode}): {Trim(body, 240)}");
                }
                catch (Exception ex) when (ex is HttpRequestException
                    or TaskCanceledException or IOException)
                {
                    Console.WriteLine($"[career] reporter probe deferred: {ex.Message}");
                }
                finally
                {
                    lease?.Dispose();
                    Interlocked.Exchange(ref _probing, 0);
                }
            });
        }

        private static async Task DrainAsync()
        {
            FileStream? drainLease = null;
            try
            {
                System.IO.Directory.CreateDirectory(DirectoryPath);
                try
                {
                    drainLease = new FileStream(
                        Path.Combine(DirectoryPath, ".drain-lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    return;
                }

                // Holding the cross-process drain lock proves no live sender
                // owns an old .sending-* claim. Recover any file left behind
                // by a child/process crash before enumerating normal reports.
                foreach (string stale in Directory.EnumerateFiles(
                    DirectoryPath, "*.json.sending-*"))
                {
                    int marker = stale.LastIndexOf(".sending-", StringComparison.Ordinal);
                    if (marker <= 0) continue;
                    string original = stale[..marker];
                    try
                    {
                        if (!File.Exists(original)) File.Move(stale, original);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (string path in System.IO.Directory.EnumerateFiles(
                    DirectoryPath, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    // Claim by rename before reading. Every hosted child may
                    // kick the same durable outbox, but only one process can
                    // successfully move a file out of the enumerable set.
                    string claim = path + ".sending-" + Environment.ProcessId.ToString(
                        CultureInfo.InvariantCulture);
                    try { File.Move(path, claim, overwrite: false); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    byte[] bytes;
                    try
                    {
                        bytes = await File.ReadAllBytesAsync(claim).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        RestoreClaim(claim, path);
                        Console.WriteLine($"[career] could not read {Path.GetFileName(path)}: {ex.Message}");
                        continue;
                    }

                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, ReportUrl);
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ServerKey);
                        request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
                        request.Content = new ByteArrayContent(bytes);
                        request.Content.Headers.ContentType =
                            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                        using HttpResponseMessage response = await Http.SendAsync(request)
                            .ConfigureAwait(false);
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            File.Delete(claim);
                            Console.WriteLine($"[career] accepted {Path.GetFileNameWithoutExtension(path)}");
                            continue;
                        }

                        int status = (int)response.StatusCode;
                        Console.WriteLine($"[career] report refused ({status}): {Trim(body, 240)}");
                        // An operator can repair a gateway/key configuration. Keep
                        // authoritative reports durable until that repair is made.
                        if (status is 400 or 409 or 413)
                        {
                            File.Move(claim, path + ".rejected", overwrite: true);
                        }
                        else
                        {
                            RestoreClaim(claim, path);
                        }
                        // A configuration/auth error will refuse every file.
                        if (status is 401 or 403) break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException
                        or TaskCanceledException or IOException)
                    {
                        RestoreClaim(claim, path);
                        Console.WriteLine($"[career] report delivery deferred: {ex.Message}");
                        break;
                    }
                }
            }
            finally
            {
                drainLease?.Dispose();
                Interlocked.Exchange(ref _draining, 0);
            }
        }

        private static void RestoreClaim(string claim, string original)
        {
            try
            {
                if (File.Exists(claim) && !File.Exists(original))
                    File.Move(claim, original);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Trim(string text, int max)
        {
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= max ? text : text[..max] + "...";
        }
    }

    /// <summary>
    /// Short-lived proof that a verified Hunter License is actually connected
    /// to this dedicated authority. The social service uses this proof before
    /// it will let that account advertise the lobby to friends.
    /// </summary>
    internal static class SocialLobbyMembershipReporter
    {
        private const string DefaultUrl =
            "https://hwcjaygoistufktorbmf.supabase.co/functions/v1/social-lobby-membership";
        private const string PublishableKey =
            "sb_publishable_EVT45OPl638kA_j8vZ0ebg_sw3aWaVz";
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static string Url =>
            Environment.GetEnvironmentVariable("PROJECT_PRIME_SOCIAL_LOBBY_MEMBERSHIP_URL")
            ?? DefaultUrl;
        private static string ServerKey =>
            Environment.GetEnvironmentVariable("PROJECT_PRIME_CAREER_SERVER_KEY")
            ?? "";

        public static bool Enabled => ServerKey.Length >= 32;

        public static Task<bool> HeartbeatAsync(
            ulong authorityEpoch, uint clientId, string careerTicket,
            CancellationToken cancellationToken = default)
            => SendAsync("heartbeat", authorityEpoch, clientId, careerTicket, cancellationToken);

        public static Task<bool> LeaveAsync(
            ulong authorityEpoch, uint clientId, string careerTicket,
            CancellationToken cancellationToken = default)
            => SendAsync("leave", authorityEpoch, clientId, careerTicket, cancellationToken);

        private static async Task<bool> SendAsync(
            string action, ulong authorityEpoch, uint clientId, string careerTicket,
            CancellationToken cancellationToken)
        {
            if (!Enabled || authorityEpoch == 0 || clientId == 0
                || careerTicket.Length is < 20 or > 768)
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Url);
                request.Headers.TryAddWithoutValidation(
                    "Authorization", "Bearer " + ServerKey);
                request.Headers.TryAddWithoutValidation("apikey", PublishableKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        action,
                        authority_epoch = authorityEpoch.ToString(
                            CultureInfo.InvariantCulture),
                        client_id = clientId,
                        career_ticket = careerTicket
                    }),
                    Encoding.UTF8, "application/json");

                using HttpResponseMessage response =
                    await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return true;

                Console.WriteLine($"[social] lobby membership {action} refused "
                    + $"({(int)response.StatusCode}): {TrimMembership(text, 220)}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is HttpRequestException
                or TaskCanceledException or IOException)
            {
                Console.WriteLine($"[social] lobby membership {action} deferred: {ex.Message}");
            }
            return false;
        }

        private static string TrimMembership(string text, int max)
        {
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= max ? text : text[..max] + "...";
        }
    }

    internal sealed class CareerMatchReport
    {
        public int Version { get; set; } = 2;
        public Guid MatchId { get; set; }
        public ushort WireMatchId { get; set; }
        public Guid ServerIncarnation { get; set; }
        public string BuildVersion { get; set; } = "";
        public int ProtocolVersion { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public DateTimeOffset EndedAtUtc { get; set; }
        public long PlayedTicks { get; set; }
        public string EndReason { get; set; } = "completed";
        public string RoomKey { get; set; } = "";
        public int Mode { get; set; }
        public uint Rules { get; set; }
        public bool Teams { get; set; }
        public int TeamCount { get; set; }
        public bool ContainsBots { get; set; }
        public bool AccountingComplete { get; set; } = true;
        public bool RatingEligible { get; set; }
        public List<CareerParticipantReport> Participants { get; set; } = new();
    }

    internal sealed class CareerParticipantReport
    {
        public Guid ParticipantId { get; set; }
        public uint ClientId { get; set; }
        public string CareerTicket { get; set; } = "";
        public long JoinedTicks { get; set; }
        public long LeftTicks { get; set; }
        public DateTimeOffset SegmentEndedAtUtc { get; set; }
        public string DisplayName { get; set; } = "Player";
        public int Hunter { get; set; }
        public bool SingleHunter { get; set; } = true;
        public int Team { get; set; }
        public bool StartedMatch { get; set; }
        public bool Departed { get; set; }
        public long PlayedTicks { get; set; }
        public int Standing { get; set; } = 7;
        public int TeamStanding { get; set; } = 7;
        public bool Won { get; set; }
        public bool Tied { get; set; }
        public CareerMetricsReport Metrics { get; set; } = new();
    }

    internal sealed class CareerMetricsReport
    {
        public long Kills { get; set; }
        public long Deaths { get; set; }
        public long Assists { get; set; }
        public long Damage { get; set; }
        public long Headshots { get; set; }
        public long OctolithScores { get; set; }
        public long NodesCaptured { get; set; }
        public long KillsAsPrime { get; set; }
        public long LongestKillStreak { get; set; }
        public long[] BeamKills { get; set; } = new long[9];
    }

    public sealed partial class DedicatedServer
    {
        private readonly Guid _careerServerIncarnation = Guid.NewGuid();
        private CareerMatchState? _careerMatch;

        private sealed class SocialLobbyMembershipState
        {
            public Task<bool>? Pending;
            public double LastStarted = Double.NegativeInfinity;
            public ulong Epoch;
            public uint ClientId;
            public string Ticket = "";
            public bool Verified;
        }

        private readonly Dictionary<Peer, SocialLobbyMembershipState>
            _socialLobbyMembership = new();
        private const double SocialLobbyMembershipSeconds = 20;

        private void PumpSocialLobbyMembership(double now)
        {
            bool lobby = SessionPolicy == ServerSessionPolicy.Lobby
                && _phase == SessionPhase.Lobby
                && _authorityEpoch != 0
                && SocialLobbyMembershipReporter.Enabled;

            var active = new HashSet<Peer>(_peers);
            foreach (Peer peer in _socialLobbyMembership.Keys.ToArray())
            {
                if (!active.Contains(peer))
                    _socialLobbyMembership.Remove(peer);
            }

            foreach (Peer peer in _peers)
            {
                if (!_socialLobbyMembership.TryGetValue(
                    peer, out SocialLobbyMembershipState? state))
                {
                    state = new SocialLobbyMembershipState();
                    _socialLobbyMembership.Add(peer, state);
                }

                if (state.Pending is { IsCompleted: true } completed)
                {
                    state.Pending = null;
                    try { state.Verified = completed.GetAwaiter().GetResult(); }
                    catch (Exception) { state.Verified = false; }
                }

                bool identityReady = peer.ClientId != 0
                    && peer.CareerTicket.StartsWith("pp1.", StringComparison.Ordinal);
                if (!lobby || !identityReady)
                {
                    if (state.Verified && state.Pending == null
                        && state.Epoch != 0 && state.ClientId != 0
                        && state.Ticket.Length > 0)
                    {
                        state.Pending = SocialLobbyMembershipReporter.LeaveAsync(
                            state.Epoch, state.ClientId, state.Ticket);
                    }
                    state.Verified = false;
                    continue;
                }

                bool identityChanged = state.Epoch != _authorityEpoch
                    || state.ClientId != peer.ClientId
                    || !String.Equals(state.Ticket, peer.CareerTicket,
                        StringComparison.Ordinal);
                if (identityChanged)
                {
                    state.Epoch = _authorityEpoch;
                    state.ClientId = peer.ClientId;
                    state.Ticket = peer.CareerTicket;
                    state.Verified = false;
                    state.LastStarted = Double.NegativeInfinity;
                }

                if (state.Pending == null
                    && now - state.LastStarted >= SocialLobbyMembershipSeconds)
                {
                    state.LastStarted = now;
                    state.Pending = SocialLobbyMembershipReporter.HeartbeatAsync(
                        _authorityEpoch, peer.ClientId, peer.CareerTicket);
                }
            }
        }

        private void SocialLobbyMembershipLeaving(Peer peer)
        {
            if (!_socialLobbyMembership.Remove(
                peer, out SocialLobbyMembershipState? state))
                return;

            if (state.Epoch == 0 || state.ClientId == 0 || state.Ticket.Length == 0)
                return;

            _ = SocialLobbyMembershipReporter.LeaveAsync(
                state.Epoch, state.ClientId, state.Ticket);
        }

        private void SocialLobbyMembershipIdentityChanged(Peer peer)
        {
            if (!_socialLobbyMembership.TryGetValue(
                peer, out SocialLobbyMembershipState? state))
            {
                state = new SocialLobbyMembershipState();
                _socialLobbyMembership.Add(peer, state);
            }
            state.LastStarted = Double.NegativeInfinity;
            state.Verified = false;
        }

        private sealed class CareerMatchState
        {
            public Guid MatchId = Guid.NewGuid();
            public ushort WireMatchId;
            public long StartedFrame;
            public DateTimeOffset StartedAtUtc;
            public string RoomKey = "";
            public GameMode Mode;
            public MatchModifierFlags Rules;
            public bool Teams;
            public int TeamCount;
            public bool RatingEligible;
            public readonly List<CareerParticipantState> Participants = new();
            public readonly Dictionary<Peer, CareerParticipantState> Active = new();
            public bool SegmentLimitReached;
        }

        private sealed class CareerParticipantState
        {
            public Guid ParticipantId = Guid.NewGuid();
            public uint ClientId;
            public string CareerTicket = "";
            public Guid? TicketSubjectHint;
            public long JoinedTicks;
            public long LeftTicks;
            public DateTimeOffset SegmentEndedAtUtc;
            public string DisplayName = "Player";
            public int Hunter;
            public bool SingleHunter = true;
            public int Team;
            public bool StartedMatch;
            public bool Active;
            public bool Departed;
            public int Slot = -1;
            public long JoinedFrame;
            public CareerCounterSnapshot Start;
            public CareerMetricsReport Metrics = new();
            public long PlayedTicks;
            public int Standing = 7;
            public int TeamStanding = 7;
        }

        private readonly record struct CareerCounterSnapshot(
            long Kills, long Deaths, long Assists, long Damage, long Headshots,
            long OctolithScores, long NodesCaptured, long KillsAsPrime,
            long LongestKillStreak, long[] BeamKills);

        private long CareerFrame => _sim?.Frames ?? 0;

        /// <summary>
        /// Begin only once the authoritative room transition has completed.
        /// Starting earlier would snapshot the previous map's counters and make
        /// the new match look like they ran backwards when NetRoomChange clears them.
        /// </summary>
        private void EnsureCareerMatchStarted(double now)
        {
            if (_botAssistedMatch || _careerMatch != null || _sim == null || _phase != SessionPhase.InMatch
                || !_peers.Any(peer => !peer.Spectating) || !NetRoomChange.GameplayReady)
            {
                return;
            }

            CareerMatchStats.Reset();
            MatchDefinition match = CurrentDefinition;
            _careerMatch = new CareerMatchState
            {
                WireMatchId = _matchId,
                StartedFrame = CareerFrame,
                StartedAtUtc = DateTimeOffset.UtcNow
                    - TimeSpan.FromSeconds(Math.Max(0, now - _matchStarted)),
                RoomKey = match.RoomKey,
                Mode = match.Mode, Rules = match.Rules,
                Teams = GameState.IsTeamMode(match.Mode),
                TeamCount = Math.Max(1, LobbyRules.TeamCount(match)),
                // The public continuous rotation is the verified rules lane.
                // Player-created persistent lobbies still build career history,
                // but never change rating points.
                RatingEligible = SessionPolicy == ServerSessionPolicy.Continuous
                    && Mods.Multiplayer.CareerMatchEligibility.IsStandard(match, _botAssistedMatch)
            };
            foreach (Peer peer in _peers)
            {
                CareerActivate(peer, startedMatch: true);
            }
            Console.WriteLine($"[career] tracking match {_careerMatch.MatchId} "
                + $"(wire {_matchId}) on {match.RoomKey}");
        }

        private uint CareerKey(Peer peer)
            => peer.ClientId != 0 ? peer.ClientId : 0x80000000u | (uint)(peer.SlotIndex + 1);

        // The report body is bounded independently of the eight concurrent slots.
        // A segment is an admission/account lifetime, never a process-random ClientId lifetime.
        private const int MaximumCareerSegments = 128;

        private void CareerActivate(Peer peer, bool startedMatch)
        {
            if (_botAssistedMatch || peer.Spectating) return;
            CareerMatchState? match = _careerMatch;
            if (match == null) return;
            uint key = CareerKey(peer);
            if (match.Active.TryGetValue(peer, out var active) && active.Active) return;
            if (match.Participants.Count >= MaximumCareerSegments)
            {
                if (!match.SegmentLimitReached) Console.WriteLine("[career] segment limit reached; retaining the bounded completed report as Practice");
                match.SegmentLimitReached = true;
                return;
            }
            var p = new CareerParticipantState
            {
                ClientId = peer.ClientId != 0 ? peer.ClientId : key,
                StartedMatch = startedMatch,
                CareerTicket = peer.CareerTicket,
                TicketSubjectHint = CareerTicketSubjectHint(peer.CareerTicket),
                DisplayName = peer.Name.Length > 0 ? peer.Name : $"Player {peer.SlotIndex + 1}",
                Hunter = Math.Clamp((int)peer.Hunter, 0, Launcher.Hunters.Playable - 1),
                Team = Math.Clamp((int)peer.TeamIndex, 0, 7),
                Slot = peer.SlotIndex,
                JoinedFrame = CareerFrame,
                JoinedTicks = Math.Max(0, CareerFrame - match.StartedFrame),
                Start = CareerCounters(peer.SlotIndex),
                Active = true
            };
            match.Participants.Add(p);
            match.Active[peer] = p;
        }

        // This untrusted hint only splits accounting lifetimes; it authorizes nothing.
        // The backend verifies the complete HMAC-signed ticket and client binding.
        private static Guid? CareerTicketSubjectHint(string ticket)
        {
            if (ticket.Length > 768) return null;
            string[] parts = ticket.Split('.');
            if (parts.Length != 3 || parts[0] != "pp1") return null;
            try
            {
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                using JsonDocument document = JsonDocument.Parse(Convert.FromBase64String(payload));
                return document.RootElement.TryGetProperty("sub", out JsonElement sub)
                    && sub.ValueKind == JsonValueKind.String && Guid.TryParse(sub.GetString(), out Guid subject)
                    ? subject : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
        }

        private void CareerIdentityChanged(Peer peer, int previousHunter)
        {
            CareerTicketChanged(peer);
            if (_careerMatch == null || !_careerMatch.Active.TryGetValue(peer, out var p)) return;
            if (previousHunter != peer.Hunter) p.SingleHunter = false;
            p.Hunter = Math.Clamp((int)peer.Hunter, 0, Launcher.Hunters.Playable - 1);
            p.Team = Math.Clamp((int)peer.TeamIndex, 0, 7);
            p.DisplayName = peer.Name;
        }

        private void CareerTicketChanged(Peer peer)
        {
            SocialLobbyMembershipIdentityChanged(peer);
            CareerMatchState? match = _careerMatch;
            if (match == null || !match.Active.TryGetValue(peer, out var p)
                || p.CareerTicket == peer.CareerTicket && p.ClientId == CareerKey(peer)) return;
            Guid? subject = CareerTicketSubjectHint(peer.CareerTicket);
            // Only a refresh with the same subject hint can replace a frozen ticket.
            // Invalid/empty tickets and changed accounts cannot inherit prior counters.
            if (p.ClientId == CareerKey(peer) && p.TicketSubjectHint != null && p.TicketSubjectHint == subject)
            {
                p.CareerTicket = peer.CareerTicket;
                return;
            }
            if (p.ClientId == CareerKey(peer) && p.CareerTicket.Length == 0 && p.PlayedTicks == 0 && CareerFrame == p.JoinedFrame)
            {
                p.CareerTicket = peer.CareerTicket;
                p.TicketSubjectHint = subject;
                return;
            }
            CareerCaptureSegment(p);
            p.Departed = true;
            match.Active.Remove(peer);
            CareerMatchStats.ForgetSlot(peer.SlotIndex);
            CareerActivate(peer, startedMatch: false);
        }

        private void CareerPeerRoleChanged(Peer peer)
        {
            if (_careerMatch == null) return;
            if (peer.Spectating) CareerPeerLeaving(peer);
            else if (_phase == SessionPhase.InMatch) CareerActivate(peer, startedMatch: false);
        }

        private void CareerPeerJoined(Peer peer)
        {
            if (_careerMatch != null && _phase == SessionPhase.InMatch)
                CareerActivate(peer, startedMatch: false);
        }

        private void CareerPeerLeaving(Peer peer)
        {
            SocialLobbyMembershipLeaving(peer);
            if (_careerMatch == null) return;
            if (_careerMatch.Active.Remove(peer, out var p))
            {
                CareerCaptureSegment(p);
                p.Departed = true;
            }
            CareerMatchStats.ForgetSlot(peer.SlotIndex);
        }

        private CareerCounterSnapshot CareerCounters(int slot)
        {
            long[] beams = new long[9];
            if ((uint)slot < PlayerEntity.SlotCapacity)
            {
                for (int beam = 0; beam < beams.Length; beam++)
                    beams[beam] = Math.Max(0, GameState.BeamKills[slot, beam]);
                return new CareerCounterSnapshot(
                    Math.Max(0, GameState.Kills[slot]),
                    Math.Max(0, GameState.Deaths[slot]),
                    Math.Max(0, CareerMatchStats.Assists[slot]),
                    Math.Max(0, CareerMatchStats.Damage[slot]),
                    Math.Max(0, GameState.HeadshotKills[slot]),
                    Math.Max(0, GameState.OctolithScores[slot]),
                    Math.Max(0, GameState.NodesCaptured[slot]),
                    Math.Max(0, GameState.KillsAsPrime[slot]),
                    Math.Max(0, CareerMatchStats.LongestKillStreak[slot]),
                    beams);
            }
            return new CareerCounterSnapshot(0,0,0,0,0,0,0,0,0,beams);
        }

        private static long Delta(long current, long start) => Math.Max(0, current - start);

        private void CareerCaptureSegment(CareerParticipantState p)
        {
            if (!p.Active || p.Slot < 0) return;
            CareerCounterSnapshot now = CareerCounters(p.Slot);
            p.Metrics.Kills += Delta(now.Kills, p.Start.Kills);
            p.Metrics.Deaths += Delta(now.Deaths, p.Start.Deaths);
            p.Metrics.Assists += Delta(now.Assists, p.Start.Assists);
            p.Metrics.Damage += Delta(now.Damage, p.Start.Damage);
            p.Metrics.Headshots += Delta(now.Headshots, p.Start.Headshots);
            p.Metrics.OctolithScores += Delta(now.OctolithScores, p.Start.OctolithScores);
            p.Metrics.NodesCaptured += Delta(now.NodesCaptured, p.Start.NodesCaptured);
            p.Metrics.KillsAsPrime += Delta(now.KillsAsPrime, p.Start.KillsAsPrime);
            p.Metrics.LongestKillStreak = Math.Max(p.Metrics.LongestKillStreak,
                now.LongestKillStreak);
            for (int beam = 0; beam < p.Metrics.BeamKills.Length; beam++)
                p.Metrics.BeamKills[beam] += Delta(now.BeamKills[beam], p.Start.BeamKills[beam]);
            p.PlayedTicks += Math.Max(0, CareerFrame - p.JoinedFrame);
            p.LeftTicks = Math.Max(p.JoinedTicks, CareerFrame - (_careerMatch?.StartedFrame ?? CareerFrame));
            p.SegmentEndedAtUtc = DateTimeOffset.UtcNow;
            p.Active = false;
        }

        private void CompleteCareerMatch(double now, string reason)
        {
            if (_botAssistedMatch)
            {
                Console.WriteLine("[career] bot-assisted match; Hunter License reporting suppressed");
                AbandonCareerMatch();
                return;
            }
            CareerMatchState? match = _careerMatch;
            if (match == null || _sim == null)
            {
                // Never manufacture a zero-stat report by taking the baseline
                // at the same instant the round ends. If tracking could not
                // start while gameplay was actually ready, skip this result.
                Console.WriteLine("[career] no authoritative career baseline; match not reported");
                _careerMatch = null;
                return;
            }

            GameState.UpdateStandings();
            foreach (Peer peer in _peers)
            {
                if (peer.Spectating) continue;
                CareerTicketChanged(peer);
                if (!match.Active.TryGetValue(peer, out CareerParticipantState? p))
                {
                    CareerActivate(peer, startedMatch: false);
                    if (!match.Active.TryGetValue(peer, out p)) continue;
                }
                p.DisplayName = peer.Name.Length > 0 ? peer.Name : p.DisplayName;
                p.Hunter = Math.Clamp((int)peer.Hunter, 0, Launcher.Hunters.Playable - 1);
                p.Team = Math.Clamp((int)peer.TeamIndex, 0, 7);
                p.Standing = GameState.Teams
                    ? Math.Clamp(GameState.TeamStandings[peer.SlotIndex], 0, 7)
                    : Math.Clamp(GameState.Standings[peer.SlotIndex], 0, 7);
                p.TeamStanding = Math.Clamp(GameState.Standings[peer.SlotIndex], 0, 7);
                CareerCaptureSegment(p);
                p.Departed = false;
            }

            var reports = new List<CareerParticipantReport>(match.Participants.Count);
            foreach (CareerParticipantState p in match.Participants)
            {
                reports.Add(new CareerParticipantReport
                {
                    ParticipantId = p.ParticipantId,
                    ClientId = p.ClientId,
                    CareerTicket = p.CareerTicket,
                    JoinedTicks = p.JoinedTicks, LeftTicks = p.LeftTicks,
                    SegmentEndedAtUtc = p.SegmentEndedAtUtc,
                    DisplayName = SanitizeCareerName(p.DisplayName),
                    Hunter = p.Hunter,
                    SingleHunter = p.SingleHunter,
                    Team = p.Team,
                    StartedMatch = p.StartedMatch,
                    Departed = p.Departed,
                    PlayedTicks = Math.Min(p.PlayedTicks,
                        Math.Max(0, CareerFrame - match.StartedFrame)),
                    Standing = p.Standing,
                    TeamStanding = p.TeamStanding,
                    Metrics = p.Metrics
                });
            }

            foreach (CareerParticipantReport p in reports)
            {
                if (!p.StartedMatch || p.Departed) continue;
                int rank = match.Teams ? p.TeamStanding : p.Standing;
                List<CareerParticipantReport> opponents = reports.Where(o =>
                    o.StartedMatch && o.ClientId != p.ClientId
                    && (!match.Teams || o.Team != p.Team)).ToList();
                if (opponents.Count == 0) continue;
                p.Tied = opponents.Any(o => !o.Departed
                    && (match.Teams ? o.TeamStanding : o.Standing) == rank);
                p.Won = rank == 0 && !p.Tied;
            }

            if (reports.Count == 0)
            {
                AbandonCareerMatch();
                return;
            }
            var report = new CareerMatchReport
            {
                MatchId = match.MatchId,
                WireMatchId = match.WireMatchId,
                ServerIncarnation = _careerServerIncarnation,
                BuildVersion = Update.BuildVersion.Display,
                ProtocolVersion = NetConfig.ProtocolVersion,
                StartedAtUtc = match.StartedAtUtc,
                EndedAtUtc = DateTimeOffset.UtcNow,
                PlayedTicks = Math.Max(0, CareerFrame - match.StartedFrame),
                EndReason = "completed",
                RoomKey = match.RoomKey,
                Mode = (int)match.Mode, Rules = (uint)match.Rules,
                Teams = match.Teams,
                TeamCount = match.TeamCount,
                ContainsBots = _botAssistedMatch,
                AccountingComplete = !match.SegmentLimitReached,
                RatingEligible = match.RatingEligible && !_botAssistedMatch && !match.SegmentLimitReached,
                Participants = reports
            };
            CareerReportOutbox.Enqueue(report);
            _careerMatch = null;
            CareerMatchStats.Reset();
        }

        private void AbandonCareerMatch()
        {
            _careerMatch = null;
            CareerMatchStats.Reset();
        }

        private static string SanitizeCareerName(string name)
        {
            var text = new StringBuilder(Math.Min(name.Length, 64));
            foreach (char c in name)
            {
                if (!Char.IsControl(c)) text.Append(c);
                if (text.Length == 64) break;
            }
            return text.Length == 0 ? "Player" : text.ToString();
        }

        private void HandleCareerIdentity(ReceivedPacket packet, double now)
        {
            Peer? peer = Find(packet.Sender);
            if (peer == null || packet.Payload.Length is < 20 or > 768) return;
            for (int i = 0; i < packet.Payload.Length; i++)
            {
                byte b = packet.Payload[i];
                if (b < 0x21 || b > 0x7e) return;
            }
            string ticket = Encoding.ASCII.GetString(packet.Payload);
            if (!ticket.StartsWith("pp1.", StringComparison.Ordinal)) return;
            peer.LastSeen = now;
            if (peer.CareerTicket == ticket) return;
            peer.CareerTicket = ticket;
            CareerTicketChanged(peer);
        }
    }
}
