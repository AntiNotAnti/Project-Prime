using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

// File-only phase envelope. Syntax admission does not apply facts or establish
// their match, clock, capability or ordering; the archive owner checks those.
internal static class ReplayPostWorldRecord
{
    internal const byte Tag = 249;
    internal const int HeaderBytes = 8;
    internal const int MaximumBytes = ReplayCustomWeaponFact.MaximumBytes + HeaderBytes;

    internal static byte[] Encode(ReadOnlySpan<byte> packet, ReplayCustomWeaponContext? context)
    {
        ValidatePayload(packet, context);
        var bytes = new byte[HeaderBytes + packet.Length];
        bytes[0] = Tag; bytes[1] = 1; bytes[2] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), packet.Length);
        packet.CopyTo(bytes.AsSpan(HeaderBytes));
        return bytes;
    }

    internal static byte[] Read(ReadOnlySpan<byte> packet, ReplayCustomWeaponContext? context)
        => ValidateAndGetPayload(packet, context).ToArray();

    internal static ReadOnlySpan<byte> ValidateAndGetPayload(ReadOnlySpan<byte> packet, ReplayCustomWeaponContext? context)
    {
        if (packet.Length is <= HeaderBytes or > MaximumBytes || packet[0] != Tag
            || packet[1] != 1 || packet[2] != 1 || packet[3] != 0
            || BinaryPrimitives.ReadInt32LittleEndian(packet[4..]) != packet.Length - HeaderBytes)
            throw new InvalidDataException("Invalid post-world replay envelope.");
        var payload = packet[HeaderBytes..];
        ValidatePayload(payload, context);
        return payload;
    }

    private static void ValidatePayload(ReadOnlySpan<byte> packet, ReplayCustomWeaponContext? context)
    {
        if (packet.Length is < 1 or > ReplayCustomWeaponFact.MaximumBytes)
            throw new InvalidDataException("Invalid post-world replay payload size.");
        if (packet[0] != ReplayCustomWeaponFact.Tag && packet.Length > NetConfig.MaxSnapshotSize)
            throw new InvalidDataException("Post-world native facts retain their native packet budget.");
        try
        {
            bool valid;
            if (packet[0] == ReplayCustomWeaponFact.Tag)
                valid = context != null && ReplayCustomWeaponFact.TryRead(packet, context, out _);
            else if (packet[0] == ReplayTimedShotScope.Tag) valid = ReplayTimedShotScope.TryRead(packet, out _);
            else if (packet[0] == ReplayTimedShotFact.Tag) valid = ReplayTimedShotFact.TryRead(packet, out _);
            else if (packet[0] == 254)
            {
                var record = ReplayTimelineArchive.DecodeMarker(0, packet.ToArray());
                record.Release(); valid = true;
            }
            else
            {
                var payload = packet[1..];
                valid = (PacketType)packet[0] switch
                {
                    PacketType.MatchState => payload.Length == MatchStatePacket.Size && ValidMatch(payload),
                    PacketType.SessionState => SessionStatePacket.TryRead(payload, out _, validateDefinition: false, allowRetiredEnhanced: true),
                    PacketType.Roster => RosterPacket.TryRead(payload, out _),
                    PacketType.Snapshot => ValidSnapshot(payload),
                    PacketType.SlotIntent => ValidIntent(payload),
                    PacketType.ReplayWorld => ReplayAuthorityWire.ValidPart(payload),
                    PacketType.CosmeticState => ValidCosmetics(payload),
                    PacketType.Chat => payload.Length == ChatPacket.Size && ValidChat(payload),
                    PacketType.ReplayShotFact => ReplayShotFactPacket.TryRead(payload, out _),
                    PacketType.MatchSemanticEvent => Mods.MatchEvents.MatchSemanticEventPacket.TryRead(packet, out _),
                    PacketType.MatchAward => Mods.MatchEvents.MatchAwardPacket.TryRead(packet, out _),
                    PacketType.TimedGrantBootstrap => ReplayGrantBootstrap.TryRead(payload, out _, out _),
                    PacketType.TimedGrant => TimedGrantFact.TryRead(payload, out _),
                    PacketType.PickupPrediction => ReplayPickupPrediction.TryRead(payload, out _, out _),
                    PacketType.PickupClaim => PickupClaim.TryRead(payload, out _),
                    PacketType.PickupVerdict => PickupVerdict.TryRead(payload, out _),
                    PacketType.PickupItem => PickupItemFact.TryRead(payload, out _),
                    PacketType.TurretFireOutcome => TurretFireOutcome.TryRead(payload, out _),
                    PacketType.BombEvent => BombEvent.TryRead(payload, out _),
                    PacketType.BombState => BombStatePage.TryRead(payload, out _),
                    _ => false
                };
            }
            if (!valid) throw new InvalidDataException("Unsupported or malformed post-world replay payload.");
        }
        catch (Exception error) when (error is ArgumentException or IndexOutOfRangeException or EndOfStreamException or OverflowException)
        { throw new InvalidDataException("Malformed post-world replay payload.", error); }
    }

    private static bool ValidMatch(ReadOnlySpan<byte> payload)
    {
        var match = MatchStatePacket.Read(payload);
        return match.MatchId != 0 && match.AuthorityEpoch != 0
            && !string.IsNullOrEmpty(match.RoomKey) && Enum.IsDefined(typeof(GameMode), match.Mode)
            && float.IsFinite(match.TimeRemaining) && float.IsFinite(match.TimeElapsed)
            && Movement.MovementModelRules.ValidRules((MatchModifierFlags)match.RuleBits);
    }
    private static bool ValidChat(ReadOnlySpan<byte> payload)
    { _ = ChatPacket.Read(payload); return true; }
    private static bool ValidIntent(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1 + IntentPacket.TouchCanonicalSize || payload[0] >= RosterPacket.MaxSlots) return false;
        var intent = IntentPacket.Read(payload[1..]);
        return intent.MatchId != 0 && intent.AuthorityEpoch != 0 && intent.SlotGeneration != 0 && intent.LifeId != 0
            && (!intent.HasFireEvents || NetFireEvents.Validate(intent));
    }
    private static bool ValidCosmetics(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0 || payload.Length % CosmeticStatePacket.Size != 0
            || payload.Length > RosterPacket.MaxSlots * CosmeticStatePacket.Size) return false;
        for (int at = 0; at < payload.Length; at += CosmeticStatePacket.Size)
            if (!CosmeticStatePacket.TryRead(payload.Slice(at, CosmeticStatePacket.Size), out _)) return false;
        return true;
    }
    private static bool ValidSnapshot(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < SnapshotHeader.Size) return false;
        var header = SnapshotHeader.Read(payload);
        if (header.PlayerCount > RosterPacket.MaxSlots) return false;
        int tail = SnapshotHeader.Size + header.PlayerCount * PlayerState.Size;
        int health = tail + NetMatchTimeSync.Size;
        if (health > payload.Length || !NetMatchTimeSync.Validate(payload.Slice(tail, NetMatchTimeSync.Size))
            || !NetHealthSync.Validate(payload[health..])
            || BinaryPrimitives.ReadUInt16LittleEndian(payload[health..]) != header.MatchId) return false;
        int seen = 0;
        for (int i = 0; i < header.PlayerCount; i++)
        {
            var row = payload.Slice(SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size);
            if (!ParkourWallVisualCodec.TryReadGrapple(row.Slice(PlayerState.Protocol80Size, PlayerGrappleState.Size), out _, out _)) return false;
            var player = PlayerState.Read(row);
            if (player.SlotIndex >= RosterPacket.MaxSlots || (seen & (1 << player.SlotIndex)) != 0) return false;
            seen |= 1 << player.SlotIndex;
        }
        return true;
    }
}
