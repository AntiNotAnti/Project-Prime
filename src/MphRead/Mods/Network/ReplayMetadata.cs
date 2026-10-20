using System;
using System.Text.Json.Serialization;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Replay;
using MphRead.Mods.MapGen;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public enum ReplayOpenResult
    {
        Success, FileMissing, InvalidMagic, UnsupportedFormat, ProtocolMismatch,
        Empty, Truncated, Corrupt, MissingMatchState, MapMissing, MapHashMismatch, IoError, StateMismatch
    }

    public enum ReplayIntegrity { Unknown, Healthy, Recovered, Truncated, Corrupt }
    public enum ReplayType : byte { FullMatch, Clip }
    public enum ReplayEventType : byte
    {
        PlayerSpawn, PlayerDeath, Kill, Damage, ScoreChanged, PlayerJoined,
        PlayerLeft, Objective, MatchStarted, MatchEnded, WeaponFired,
        Headshot, FlagCapture, NodeCapture, PrimeChanged, MatchPoint, Overtime,
        RelicPickup, RelicDrop, HardpointChanged, HardpointCaptured, GunGameAdvance,
    TokenSpawn, TokenConfirmed, TokenDenied, TokenBanked
    }

    public readonly record struct ReplayEvent(uint Frame, ReplayEventType Type,
        byte ActorSlot = byte.MaxValue, byte TargetSlot = byte.MaxValue, int Value = 0);
    internal readonly record struct ReplayPlayerInfo(byte Slot, byte Hunter, sbyte Team, string Name, bool IsBot = false, byte BotLevel = 0)
    {
        public string DisplayName => IsBot
            ? $"{Name} [BOT · {new[] { "EASY", "NORMAL", "HARD", "INSANE" }[Math.Clamp((int)BotLevel, 0, 3)]}]"
            : Name;
    }
    internal readonly record struct ReplayExpectedHash(uint Frame, string Value);

    // Bootstrap is composed of existing wire packets, applied by the usual session handlers.
    // It is not a serializer of engine objects or an alternative multiplayer model.
    internal sealed class ReplayBootstrap
    {
        public IReadOnlyList<byte[]> Packets { get; init; } = Array.Empty<byte[]>();
    }

    internal sealed class ReplayMetadata
    {
        // Presentation projection only: never part of an archive or its metadata JSON.
        [JsonIgnore]
        internal MovementModel? IndexedMovementModel { get; init; }
        [JsonIgnore]
        public MovementModel? RecordedMovementModel
        {
            get
            {
                if (IndexedMovementModel.HasValue) return IndexedMovementModel;
                if (!_movementResolved) { _resolvedMovement = ResolveMovementModel(); _movementResolved = true; }
                return _resolvedMovement;
            }
        }
        private bool _movementResolved;
        private MovementModel? _resolvedMovement;
        [JsonIgnore]
        public string MovementLabel => RecordedMovementModel is { } model
            ? MovementModelRules.DisplayName(model) : "Unknown";

        private MovementModel? ResolveMovementModel()
        {
            if (!ReplayIdentityCompatibility.Supports(ProtocolVersion)) return null;
            MovementModel? result = null;
            ushort? matchId = null;
            ulong? epoch = null;
            try
            {
                var packets = new List<byte[]>();
                void AddConstruction(ReplayReplicaCheckpoint checkpoint)
                {
                    var state = new ReplayReplicaState(); state.RestoreCheckpoint(checkpoint);
                    var match = state.Match ?? throw new InvalidDataException("Recorded construction has no match.");
                    if (state.Configuration is { Phase: not SessionPhase.Lobby } session
                        && (session.MatchId != match.MatchId || session.AuthorityEpoch != match.AuthorityEpoch
                            || session.Match.RoomKey != match.RoomKey
                            || session.Match.MovementModel != MovementModelRules.FromRules((MatchModifierFlags)match.RuleBits)))
                        throw new InvalidDataException("Recorded construction rules contradict match.");
                    byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
                    match.Write(packet.AsSpan(1)); packets.Add(packet);
                }
                if (WorldCheckpoint.Length != 0)
                {
                    using var world = ReplayWorldCheckpoint.FromBytes(WorldCheckpoint, BuildId);
                    AddConstruction(world.ConstructionState());
                }
                foreach (byte[] packet in Bootstrap.Packets)
                {
                    if (packet.Length != 0 && packet[0] == 253)
                        AddConstruction(ReplayTimelineArchive.ReadConstruction(packet));
                    else packets.Add(packet);
                }
                foreach (byte[] raw in packets)
                {
                    if (raw.Length == 0 || raw[0] != (byte)PacketType.MatchState
                        && raw[0] != (byte)PacketType.SessionState) continue;
                    var packet = ReplayIdentityCompatibility.Convert(raw, ProtocolVersion);
                    MovementModel model;
                    ushort id; ulong authority; string room;
                    if (packet.Length == 1 + SessionStatePacket.Size
                        && packet[0] == (byte)PacketType.SessionState
                        && SessionStatePacket.TryReadArchived(packet[1..], out var session))
                    {
                        // A mutable lobby is not a recorded match's effective rules.
                        if (session.Phase == SessionPhase.Lobby) continue;
                        model = session.Match.MovementModel;
                        id = session.MatchId; authority = session.AuthorityEpoch;
                        room = session.Match.RoomKey;
                    }
                    else if (packet.Length == 1 + MatchStatePacket.Size
                        && packet[0] == (byte)PacketType.MatchState)
                    {
                        var match = MatchStatePacket.Read(packet[1..]);
                        // MatchState has no TryRead; mirror its replay admission bounds.
                        if (String.IsNullOrEmpty(match.RoomKey) || !Enum.IsDefined(typeof(GameMode), match.Mode)
                            || !float.IsFinite(match.TimeRemaining) || !float.IsFinite(match.TimeElapsed)) return null;
                        var rules = (MatchModifierFlags)match.RuleBits;
                        if (!MovementModelRules.ValidRules(rules)) return null;
                        // MatchState omits format/team layout. Validate the modifier combinations
                        // it does carry; the session reader validates the complete definition.
                        var definition = new MatchDefinition
                        {
                            Mode = (GameMode)match.Mode,
                            Format = rules.HasFlag(MatchModifierFlags.VanillaDuelResources) ? MatchFormat.OneVsOne : MatchFormat.Auto,
                            MovementModel = MovementModelRules.FromRules(rules),
                            BalancedMode = rules.HasFlag(MatchModifierFlags.BalancedMode),
                            EnhancedHunters = rules.HasFlag(MatchModifierFlags.EnhancedHunters),
                            Fiesta = rules.HasFlag(MatchModifierFlags.Fiesta),
                            InstaGib = rules.HasFlag(MatchModifierFlags.InstaGib),
                            OneInTheChamber = rules.HasFlag(MatchModifierFlags.OneInTheChamber),
                            NoImperialist = rules.HasFlag(MatchModifierFlags.NoImperialist),
                            VanillaDuelResources = rules.HasFlag(MatchModifierFlags.VanillaDuelResources),
                            OctolithAutoReset = rules.HasFlag(MatchModifierFlags.OctolithAutoReset)
                        };
                        if (!MatchModifierRules.Validate(definition, out _, allowRetiredEnhanced: true)) return null;
                        model = definition.MovementModel;
                        id = match.MatchId; authority = match.AuthorityEpoch; room = match.RoomKey;
                    }
                    else return null;
                    if (ProtocolVersion >= 59 && (id == 0 || authority == 0)
                        || room != RoomKey || !MovementModelRules.IsDefined(model)
                        || result.HasValue && (result != model || matchId != id || epoch != authority)) return null;
                    result = model; matchId = id; epoch = authority;
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException)
            { return null; }
            return result ?? (ProtocolVersion < 59 ? MovementModel.Native : null);
        }
        public byte FormatVersion { get; init; } = 3;
        public byte ProtocolVersion { get; init; } = (byte)NetConfig.ProtocolVersion;
        public ushort TickRate { get; init; } = 60;
        public string BuildVersion { get; init; } = Update.BuildVersion.Display;
        public string BuildId { get; init; } = typeof(ReplayMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;
        public ReplayType Type { get; init; }
        public string RoomKey { get; init; } = "";
        public ulong MapHash { get; init; }
        // Protocol-bound bootstrap persists the complete custom identity and download source.
        public MapContentIdentity? CustomMapIdentity => ReplayMapIdentity.CustomSession(this)?.Match.MapIdentity.Content(RoomKey);
        public GameMode Mode { get; init; }
        public uint DurationFrames { get; set; }
        public ReplayIntegrity Integrity { get; set; }
        public bool Recovered { get; init; }
        public IReadOnlyList<ReplayPlayerInfo> Players { get; init; } = Array.Empty<ReplayPlayerInfo>();
        public ReplayBootstrap Bootstrap { get; init; } = new();
        // V4 starts from a detached world at exactly visible frame zero. Wire
        // ticks and decoder input ages keep their original recording clock.
        public uint OriginRecordingFrame { get; init; }
        public uint LeadInFrames { get; init; }
        public byte[] WorldCheckpoint { get; init; } = Array.Empty<byte>();
        // Consumer continuation policy is distinct from capsule producer provenance.
        public bool? IndexedFireContinuation { get; init; }
        // Format6 authority producer schema. Individual missing facts remain
        // unavailable; this is not a claim of complete native visual fidelity.
        public bool TimedShotCertificates { get; init; }
        public IReadOnlyList<ReplayEvent> Events { get; set; } = Array.Empty<ReplayEvent>();
        public ushort HashSchema { get; set; }
        public string HashBuildId { get; set; } = "";
        public IReadOnlyList<ReplayExpectedHash> ExpectedHashes { get; set; } = Array.Empty<ReplayExpectedHash>();
        public bool BuildMatches => BuildId == (typeof(ReplayMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
    }

    internal static class ReplayMapIdentity
    {
        internal static SessionStatePacket? CustomSession(ReplayMetadata metadata)
        {
            foreach (byte[] packet in metadata.Bootstrap.Packets)
                if (packet.Length == 1 + SessionStatePacket.Size && packet[0] == (byte)PacketType.SessionState
                    && SessionStatePacket.TryReadArchived(packet.AsSpan(1), out var session)
                    && session.Match.MapIdentity.IsCustom && session.Match.RoomKey == metadata.RoomKey) return session;
            return null;
        }

        internal static void PrepareExactPackage(ReplayMetadata metadata)
        {
            if (CustomSession(metadata) is not { } session) return;
            var identity = session.Match.MapIdentity.Content(metadata.RoomKey);
            bool haveArchive = CustomRooms.Installed.HasExact(identity);
            if (haveArchive && Validate(metadata) == ReplayOpenResult.Success) return;
            MapRuntimeUsage.RequireInstallationAllowed(identity.RoomKey);
            string configured = NetworkMapIdentity.ConfiguredDownloadSource();
            string address = haveArchive || string.IsNullOrWhiteSpace(session.MapDownloadSource) ? configured : session.MapDownloadSource;
            if (new Uri(address).IsLoopback && new Uri(address) != new Uri(configured))
                throw new InvalidDataException("The replay's local Community address differs from your configured service.");
            using var prepared = haveArchive && CustomRooms.Installed.TryGet(identity.MapId,out var local)
                ? MapPackageInstaller.PrepareAsync(local.PackagePath,identity).GetAwaiter().GetResult()
                : CommunityExactArchiveAcquisition.PrepareAsync(identity, address, default)
                    .GetAwaiter().GetResult();
            var installed = prepared.Commit(CustomRooms.UserMapDirectory);
            Metadata.RegisterDownloadedMap(installed);
        }

        // Hash the actual room binaries, not its filename or local absolute paths. Streaming
        // bounds memory and covers custom-map geometry, collision, entities and node layout.
        public static ulong Compute(string roomKey)
        {
            var (room, _) = Metadata.GetRoomByName(roomKey);
            if (room == null) return 0;
            string root;
            try { root = room.FirstHunt ? Paths.FhFileSystem : Paths.FileSystem; }
            catch (KeyNotFoundException) { return 0; } // Asset-free lobby/tools have no extraction configured.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024];
            Span<byte> size = stackalloc byte[8];
            foreach (string? relative in new[] { room.ModelPath, room.CollisionPath,
                room.EntityPath, room.NodePath, room.AnimationPath, room.TexturePath })
            {
                if (string.IsNullOrEmpty(relative)) continue;
                string path = Paths.Combine(root, relative);
                using var stream = File.OpenRead(path);
                BinaryPrimitives.WriteInt64LittleEndian(size, stream.Length);
                hash.AppendData(size);
                int read;
                while ((read = stream.Read(buffer)) > 0) hash.AppendData(buffer.AsSpan(0, read));
            }
            return BinaryPrimitives.ReadUInt64LittleEndian(hash.GetHashAndReset());
        }

        public static ReplayOpenResult Validate(ReplayMetadata metadata)
        {
            if (metadata.CustomMapIdentity is { } identity && !CustomRooms.Installed.HasExact(identity))
                return ReplayOpenResult.MapHashMismatch;
            // Zero identifies an unavailable hash (e.g. an asset-free format fixture).
            // Real recordings require a hash at Start, so never silently waive it there.
            if (metadata.MapHash == 0) return ReplayOpenResult.Success;
            try
            {
                ulong current = Compute(metadata.RoomKey);
                return current == 0 ? ReplayOpenResult.MapMissing : current == metadata.MapHash
                    ? ReplayOpenResult.Success : ReplayOpenResult.MapHashMismatch;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ReplayOpenResult.MapMissing;
            }
        }
    }
}
