using System;
using MphRead.Mods.MapGen;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Threading;

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
        internal static ReplayBootstrap FromState(ReplayReplicaState state)
        {
            if (state.Configuration is not { } session) return new();
            byte[] packet = new byte[1 + SessionStatePacket.Size];
            packet[0] = (byte)PacketType.SessionState;
            session.Write(packet.AsSpan(1));
            return new() { Packets = new[] { packet } };
        }
        internal static ReplayBootstrap FromConstruction(ReplayReplicaCheckpoint checkpoint)
        {
            var state = new ReplayReplicaState();
            state.RestoreCheckpoint(checkpoint);
            return FromState(state);
        }
    }

    internal sealed class ReplayMetadata
    {
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
        public IReadOnlyList<ReplayEvent> Events { get; set; } = Array.Empty<ReplayEvent>();
        public ushort HashSchema { get; set; }
        public string HashBuildId { get; set; } = "";
        public IReadOnlyList<ReplayExpectedHash> ExpectedHashes { get; set; } = Array.Empty<ReplayExpectedHash>();
        public bool BuildMatches => BuildId == (typeof(ReplayMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
    }

    internal static class ReplayMapIdentity
    {
        // Legacy packet recordings have no world graph to restore. Extracted
        // ranges explicitly preserve their original detached construction and
        // every hidden lead-in fact; this is never a replacement for a missing
        // world-origin capsule in an ordinary v4 recording.
        internal static ReplayReplicaCheckpoint? PacketOrigin(ReplayMetadata metadata)
        {
            if (metadata.FormatVersion != 4 || metadata.Type != ReplayType.Clip
                || metadata.WorldCheckpoint.Length != 0 || metadata.OriginRecordingFrame != 0
                || metadata.Bootstrap.Packets.Count != 1) return null;
            byte[] packet = metadata.Bootstrap.Packets[0];
            if (packet.Length < 2 || packet[0] != 253) return null;
            var construction = ReplayTimelineArchive.ReadConstruction(packet);
            ValidateOriginConstruction(metadata, construction);
            return construction;
        }
        internal static void ValidateOriginConstruction(ReplayMetadata metadata, ReplayReplicaCheckpoint construction)
        {
            var detached = new ReplayReplicaState();
            detached.RestoreCheckpoint(construction);
            if (detached.Match is not { } match || match.RoomKey != metadata.RoomKey || match.Mode != (byte)metadata.Mode)
                throw new InvalidDataException("Replay origin has no matching room and mode.");
            if (detached.Configuration is { } config
                && (config.Match.RoomKey != match.RoomKey || config.Match.Mode != (GameMode)match.Mode
                    || config.MatchId != match.MatchId || config.AuthorityEpoch != match.AuthorityEpoch))
                throw new InvalidDataException("Replay origin configuration differs from its match.");
        }
        internal static SessionStatePacket? CustomSession(ReplayMetadata metadata)
        {
            foreach (byte[] packet in metadata.Bootstrap.Packets)
                if (packet.Length == 1 + SessionStatePacket.Size && packet[0] == (byte)PacketType.SessionState
                    && SessionStatePacket.TryRead(packet.AsSpan(1), out var session)
                    && session.Match.MapIdentity.IsCustom && session.Match.RoomKey == metadata.RoomKey) return session;
            if (PacketOrigin(metadata) is { } packetOrigin)
            {
                var state = new ReplayReplicaState(); state.RestoreCheckpoint(packetOrigin);
                if (state.Configuration is { } config && config.Match.MapIdentity.IsCustom
                    && config.Match.RoomKey == metadata.RoomKey) return config;
            }
            // Early v4 writers omitted bootstrap. Read the detached construction
            // capsule to recover immutable package identity before loading a Scene.
            if (metadata.WorldCheckpoint.Length > 0)
            {
                using var world = Replay.ReplayWorldCheckpoint.FromBytes(metadata.WorldCheckpoint, metadata.BuildId);
                var state = new ReplayReplicaState();
                state.RestoreCheckpoint(world.ConstructionState());
                if (state.Configuration is { } rules && rules.Match.MapIdentity.IsCustom
                    && rules.Match.RoomKey == metadata.RoomKey) return rules;
            }
            return null;
        }

        internal static void PrepareExactPackage(ReplayMetadata metadata)
        {
            using var prepared = PrepareExactPackageDetached(metadata);
            CommitExactPackage(prepared);
        }

        // Detached archive download/verification/build is safe on a bounded
        // preparation worker. Runtime registry and generated-file publication
        // remain an explicit operation on the scene owner.
        internal static PreparedMapInstallation? PrepareExactPackageDetached(ReplayMetadata metadata,
            CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (StudioReplay.StudioReplayResources.Current is { } privateResources)
            {
                privateResources.Prepare(metadata, cancellation);
                return null;
            }
            if (CustomSession(metadata) is not { } session) return null;
            var identity = session.Match.MapIdentity.Content(metadata.RoomKey);
            bool haveArchive = CustomRooms.Installed.HasExact(identity);
            if (haveArchive && Validate(metadata) == ReplayOpenResult.Success) return null;
            MapRuntimeUsage.RequireInstallationAllowed(identity.RoomKey);
            string configured = NetworkMapIdentity.ConfiguredDownloadSource();
            string address = haveArchive || string.IsNullOrWhiteSpace(session.MapDownloadSource) ? configured : session.MapDownloadSource;
            if (new Uri(address).IsLoopback && new Uri(address) != new Uri(configured))
                throw new InvalidDataException("The replay's local Community address differs from your configured service.");
            using var client = new MapCommunityClient(address);
            return haveArchive && CustomRooms.Installed.TryGet(identity.MapId,out var local)
                ? MapPackageInstaller.PrepareAsync(local.PackagePath,identity,cancellation).GetAwaiter().GetResult()
                : client.PrepareExactAsync(identity, cancellation).GetAwaiter().GetResult();
        }

        internal static void CommitExactPackage(PreparedMapInstallation? prepared,
            CancellationToken cancellation = default)
        {
            if (prepared == null) return;
            var installed = prepared.Commit(CustomRooms.UserMapDirectory, cancellation: cancellation);
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
            if (metadata.CustomMapIdentity is { } identity
                && !(StudioReplay.StudioReplayResources.Current?.Matches(identity) ?? CustomRooms.Installed.HasExact(identity)))
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
