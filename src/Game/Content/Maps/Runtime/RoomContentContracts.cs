using System;
using System.Collections.Immutable;

namespace MphRead.Mods.MapGen;

public readonly record struct GameplayContentIdentity
{
    public GameplayContentIdentity(string buildVersion, byte protocolVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);
        if (protocolVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        BuildVersion = buildVersion;
        ProtocolVersion = protocolVersion;
    }

    public string BuildVersion { get; }
    public byte ProtocolVersion { get; }

    public override string ToString() => $"{BuildVersion}:{ProtocolVersion}";

    public static string Current(string buildVersion, byte protocolVersion)
        => new GameplayContentIdentity(buildVersion, protocolVersion).ToString();

    public static string Tool(string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        return "tool:" + purpose.Trim().ToLowerInvariant();
    }
}

public sealed record RoomContentRequirement
{
    public RoomContentRequirement(MapContentIdentity contentIdentity,
        string? artifactHash = null, long? packageSize = null,
        string? matchContentHash = null)
    {
        ArgumentNullException.ThrowIfNull(contentIdentity);
        ContentIdentity = contentIdentity;
        if ((artifactHash == null) != (packageSize == null))
            throw new ArgumentException(
                "Artifact hash and package size must be supplied together.");
        ArtifactHash = artifactHash == null ? null
            : MapHash.Validate(artifactHash, nameof(artifactHash));
        MatchContentHash = matchContentHash == null ? null
            : MapHash.Validate(matchContentHash, nameof(matchContentHash));
        if (packageSize is < 1 or > 268_435_456)
            throw new ArgumentOutOfRangeException(nameof(packageSize));
        PackageSize = packageSize;
    }

    public MapContentIdentity ContentIdentity { get; }
    public string? ArtifactHash { get; }
    public long? PackageSize { get; }
    public string? MatchContentHash { get; }
}

public enum RoomContentPurpose
{
    Match,
    Replay,
    EditorPlaytest,
    Thumbnail,
    Audit,
    Inspection
}

public sealed record RoomContentRequest
{
    public RoomContentRequest(string roomKey,
        RoomContentRequirement? requiredMap, string gameplayIdentity,
        RoomContentPurpose purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameplayIdentity);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        RoomKey = roomKey;
        RequiredMap = requiredMap;
        GameplayIdentity = gameplayIdentity;
        Purpose = purpose;
    }

    public string RoomKey { get; }
    public RoomContentRequirement? RequiredMap { get; }
    public string GameplayIdentity { get; }
    public RoomContentPurpose Purpose { get; }
}

public interface RoomContentPreparationResult
{
    public sealed record BaseGame(RuntimeRoomRegistration Room)
        : RoomContentPreparationResult;

    public sealed record Ready(RuntimeRoomRegistration Room,
        MatchContentSnapshot Snapshot) : RoomContentPreparationResult;

    public interface Failure : RoomContentPreparationResult
    {
        string RoomKey { get; }
        ImmutableArray<MapDiagnostic> Diagnostics { get; }
    }

    public sealed record Missing(string MissingRoomKey,
        RoomContentRequirement? Requirement,
        ImmutableArray<MapDiagnostic> Issues)
        : Failure
    {
        public string RoomKey => MissingRoomKey;
        public ImmutableArray<MapDiagnostic> Diagnostics => Issues;
    }

    public sealed record Invalid(string InvalidRoomKey,
        ImmutableArray<MapDiagnostic> Issues)
        : Failure
    {
        public string RoomKey => InvalidRoomKey;
        public ImmutableArray<MapDiagnostic> Diagnostics => Issues;
    }

    public sealed record Unsupported(string UnsupportedRoomKey,
        ImmutableArray<MapDiagnostic> Issues)
        : Failure
    {
        public string RoomKey => UnsupportedRoomKey;
        public ImmutableArray<MapDiagnostic> Diagnostics => Issues;
    }
}
