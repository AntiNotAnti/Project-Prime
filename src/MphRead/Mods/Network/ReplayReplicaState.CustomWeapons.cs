using System;
using System.IO;

namespace MphRead.Mods.Network;

internal sealed partial class ReplayReplicaState
{
    internal ReplayCustomWeaponDescriptor? CustomWeaponDescriptor { get; private set; }
    internal ReplayCustomWeaponState? CustomWeapons { get; private set; }
    internal bool HasRecordedSceneBaseline => _hasSnapshot && _rosterRevision.HasValue;
    private const MatchModifierFlags CustomRuleBits = MatchModifierFlags.RiftlineM7Available | MatchModifierFlags.PackagedWeaponAvailable;
    internal void EnableCustomInspection(ReplayCustomWeaponDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (CustomWeapons != null || Match.HasValue || AcceptedPackets != 0)
            throw new InvalidOperationException("Custom inspection context must precede replica facts.");
        var context = descriptor.InspectContext();
        CustomWeaponDescriptor = descriptor; CustomWeapons = new(context);
    }
    private bool CustomMatchAllowed(in MatchStatePacket match)
    {
        var bits = (MatchModifierFlags)match.RuleBits & CustomRuleBits;
        if (CustomWeaponDescriptor == null) return bits == 0;
        var bootstrap = CustomWeaponDescriptor.Bootstrap;
        return bits == CustomRuleBits && match.MatchId == bootstrap.World.Start.MatchId
            && match.AuthorityEpoch == bootstrap.World.Start.AuthorityEpoch
            && StringComparer.OrdinalIgnoreCase.Equals(match.RoomKey, CustomWeaponDescriptor.Map.RoomKey);
    }
    private bool CustomConfigurationAllowed(in SessionStatePacket configuration)
    {
        bool custom = configuration.Match.RiftlineM7Available || configuration.Match.PackagedWeaponAvailable;
        return CustomWeaponDescriptor == null ? !custom : configuration.Match.RiftlineM7Available && configuration.Match.PackagedWeaponAvailable
            && configuration.MatchId == CustomWeaponDescriptor.Bootstrap.World.Start.MatchId
            && configuration.AuthorityEpoch == CustomWeaponDescriptor.Bootstrap.World.Start.AuthorityEpoch
            && configuration.StartGeneration == CustomWeaponDescriptor.Bootstrap.World.Start.StartGeneration
            && configuration.Match.MapIdentity.Content(configuration.Match.RoomKey).Matches(CustomWeaponDescriptor.Map);
    }
    private static bool ReadReplicaConfiguration(ReadOnlySpan<byte> bytes, bool custom, out SessionStatePacket value)
        => custom ? SessionStatePacket.TryRead(bytes, out value, allowRetiredEnhanced: true) : SessionStatePacket.TryReadArchived(bytes, out value);
    private bool AcceptCustomWeaponRecord(ReadOnlySpan<byte> bytes, uint frame)
    {
        if (CustomWeapons == null || Match is not { } match || !CustomMatchAllowed(match)
            || !ReplayCustomWeaponFact.TryRead(bytes, CustomWeapons.Context, out var fact))
            throw new InvalidDataException("Custom replay facts require their exact inspection contract and match.");
        bool accepted = CustomWeapons.Accept(fact!, frame);
        RecordingFrame = frame; if (accepted) AcceptedPackets++; else IgnoredPackets++;
        return accepted;
    }
    private void WriteCustomWeaponCheckpoint(BinaryWriter writer)
    {
        if (CustomWeapons is not { CompleteForRestore: true } state || CustomWeaponDescriptor == null
            || Match is not { } match || !CustomMatchAllowed(match) || Configuration is { } rules && !CustomConfigurationAllowed(rules)
            || state.RecordingFrame > RecordingFrame)
            throw new InvalidDataException("Replica custom checkpoint requires a complete exact accepted scope.");
        byte[] descriptor = CustomWeaponDescriptor.CopyBytes(), checkpoint = state.CaptureCheckpointBytes();
        writer.Write(descriptor.Length); writer.Write(descriptor); writer.Write(checkpoint.Length); writer.Write(checkpoint);
    }
    private void ReadCustomWeaponCheckpoint(BinaryReader reader)
    {
        byte[] Read(int maximum)
        {
            int count = reader.ReadInt32();
            if (count <= 0 || count > maximum) throw new InvalidDataException("Invalid custom replica appendix length.");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new InvalidDataException("Truncated custom replica appendix."); return bytes;
        }
        if (!ReplayCustomWeaponDescriptor.TryRead(Read(ReplayCustomWeaponDescriptor.MaximumBytes), out var descriptor))
            throw new InvalidDataException("Invalid custom replica inspection descriptor.");
        var context = descriptor!.InspectContext();
        var state = ReplayCustomWeaponState.RestoreCheckpointBytes(Read(ReplayCustomWeaponState.MaximumCheckpointBytes), context);
        CustomWeaponDescriptor = descriptor; CustomWeapons = state;
        if (Match is not { } match || !CustomMatchAllowed(match) || Configuration is { } rules && !CustomConfigurationAllowed(rules)
            || state.RecordingFrame > RecordingFrame) throw new InvalidDataException("Custom replica appendix contradicts its match or clocks.");
    }
    // Explicit detached inspection only; native archives/worlds never call this.
    internal void RestoreCustomCheckpoint(ReplayReplicaCheckpoint checkpoint)
    {
        var restored = ReplayReplicaCheckpointLayout.DecodeUnique(checkpoint.Bytes, layout =>
        {
            if (!layout.Has(ReplayReplicaCheckpointFeatures.CustomWeapons)) throw new InvalidDataException("Custom replica contract required.");
            return DecodeCheckpoint(checkpoint, layout);
        });
        CommitCheckpoint(restored);
    }
    internal static ReplayReplicaCheckpointLayout InspectCustomCheckpointLayout(ReplayReplicaCheckpoint checkpoint)
        => ReplayReplicaCheckpointLayout.DecodeUnique(checkpoint.Bytes, layout =>
        {
            if (!layout.Has(ReplayReplicaCheckpointFeatures.CustomWeapons)) throw new InvalidDataException("Custom replica contract required.");
            DecodeCheckpoint(checkpoint, layout); return layout;
        });
}
