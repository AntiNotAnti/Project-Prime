using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Network;

/// <summary>
/// Stable content required to reproduce the arena in a replay. Runtime room
/// IDs and package locations are intentionally absent.
/// </summary>
public sealed record ReplayMapIdentity
{
    public ReplayMapIdentity(string roomKey,
        MapContentIdentity? contentIdentity, string? matchContentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomKey);
        if (roomKey.Length > 128 || roomKey.Any(char.IsControl))
            throw new ArgumentException("Replay room key is invalid.",
                nameof(roomKey));
        if ((contentIdentity == null) != (matchContentHash == null))
            throw new ArgumentException(
                "Custom replay maps require both map and match content identities.");
        RoomKey = roomKey;
        ContentIdentity = contentIdentity;
        MatchContentHash = matchContentHash == null ? null
            : MapHash.Validate(matchContentHash, nameof(matchContentHash));
    }

    public string RoomKey { get; }
    public MapContentIdentity? ContentIdentity { get; }
    public string? MatchContentHash { get; }
    public bool IsCustom => ContentIdentity != null;

    public RoomContentRequirement? ToRoomContentRequirement()
        => ContentIdentity == null ? null
            : new RoomContentRequirement(ContentIdentity,
                matchContentHash: MatchContentHash);

    public static ReplayMapIdentity Capture(string roomKey)
    {
        MatchContentSnapshot? snapshot = ContentEnvironment.CurrentMatchContent;
        return snapshot == null
            ? new ReplayMapIdentity(roomKey, null, null)
            : new ReplayMapIdentity(roomKey, snapshot.MapIdentity,
                snapshot.MatchContentIdentity);
    }
}

/// <summary>Bounded deterministic binary encoding for a replay map record.</summary>
public static class ReplayMapIdentityCodec
{
    private const byte Format = 1;
    private const int MaximumEncodedBytes = 384;

    public static byte[] WriteRecord(ReplayMapIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string stableId = identity.ContentIdentity?.Identity.StableId ?? "";
        string version = identity.ContentIdentity?.Identity.Version.ToString() ?? "";
        string contentHash = identity.ContentIdentity?.ContentHash ?? "";
        string matchHash = identity.MatchContentHash ?? "";
        int payloadBytes = 2 + EncodedSize(identity.RoomKey)
            + EncodedSize(stableId) + EncodedSize(version)
            + EncodedSize(contentHash) + EncodedSize(matchHash);
        if (payloadBytes + 1 > MaximumEncodedBytes)
            throw new ArgumentException("Replay map identity is too large.",
                nameof(identity));
        byte[] record = new byte[payloadBytes + 1];
        record[0] = (byte)ReplayRecordKind.MapIdentity;
        record[1] = Format;
        record[2] = identity.IsCustom ? (byte)1 : (byte)0;
        int offset = 3;
        WriteString(record, ref offset, identity.RoomKey);
        WriteString(record, ref offset, stableId);
        WriteString(record, ref offset, version);
        WriteString(record, ref offset, contentHash);
        WriteString(record, ref offset, matchHash);
        return record;
    }

    public static bool TryReadRecord(ReadOnlySpan<byte> record,
        out ReplayMapIdentity? identity)
    {
        identity = null;
        if (record.Length is < 4 or > MaximumEncodedBytes
            || record[0] != (byte)ReplayRecordKind.MapIdentity
            || record[1] != Format || record[2] > 1)
            return false;
        int offset = 3;
        if (!TryReadString(record, ref offset, out string roomKey)
            || !TryReadString(record, ref offset, out string stableId)
            || !TryReadString(record, ref offset, out string version)
            || !TryReadString(record, ref offset, out string contentHash)
            || !TryReadString(record, ref offset, out string matchHash)
            || offset != record.Length)
            return false;
        try
        {
            bool custom = record[2] != 0;
            if (!custom && (stableId.Length != 0 || version.Length != 0
                    || contentHash.Length != 0 || matchHash.Length != 0))
                return false;
            if (custom && (stableId.Length == 0 || version.Length == 0
                    || contentHash.Length == 0 || matchHash.Length == 0))
                return false;
            MapContentIdentity? content = custom
                ? new MapContentIdentity(new MapIdentity(stableId,
                    MapVersion.Parse(version)), contentHash)
                : null;
            identity = new ReplayMapIdentity(roomKey, content,
                custom ? matchHash : null);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static int EncodedSize(string value)
        => checked(2 + Encoding.UTF8.GetByteCount(value));

    private static void WriteString(Span<byte> destination, ref int offset,
        string value)
    {
        int length = Encoding.UTF8.GetByteCount(value);
        if (length > UInt16.MaxValue)
            throw new ArgumentException("Replay map identity field is too large.");
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..],
            (ushort)length);
        offset += 2;
        offset += Encoding.UTF8.GetBytes(value, destination[offset..]);
    }

    private static bool TryReadString(ReadOnlySpan<byte> source, ref int offset,
        out string value)
    {
        value = "";
        if (offset + 2 > source.Length) return false;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        offset += 2;
        if (offset + length > source.Length) return false;
        try
        {
            value = new UTF8Encoding(false, true).GetString(
                source.Slice(offset, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        offset += length;
        return true;
    }
}
