using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

// Read-only RPS export. Raw received intents describe authored input, never proof
// of payment. A result is authoritative only when a ReplayShotFact accompanies it.
internal static class ReplayFidelityDiagnostics
{
    internal readonly record struct Vector(float X, float Y, float Z)
    {
        internal static Vector From(Vector3 value) => new(value.X, value.Y, value.Z);
    }
    internal readonly record struct Pose(Vector Origin, Vector Aim, Vector Direction, byte Charge);
    internal sealed record Row(ushort Match, ulong Epoch, int Slot, ushort Generation, ushort Life,
        uint Shot, string Stage, uint RecordingFrame, uint SourceFrame, uint? AuthorityFrame,
        byte Weapon, Pose? Pose, ushort? DamageEvent, byte? Victim, ushort? VictimGeneration,
        ushort? VictimLife, uint? Damage, bool? Lethal, bool? Headshot);
    internal const int MaximumRows = 200000;
    internal static Row Fire(int slot, in IntentPacket intent, in FireEvent fire, uint frame)
        => new(intent.MatchId, intent.AuthorityEpoch, slot, intent.SlotGeneration,
            fire.SourceLife != 0 ? fire.SourceLife : intent.LifeId, fire.ShotId,
            "authored-input", frame, fire.SourceFrame, null, fire.Weapon,
            fire.HasPose ? new(Vector.From(fire.Origin), Vector.From(fire.Aim),
                Vector.From(fire.Direction), fire.Charge) : null, null, null, null, null, null, null, null);
    internal static Row Outcome(in ReplayShotFact fact, uint frame)
        => new(fact.MatchId, fact.AuthorityEpoch, fact.ShooterSlot, fact.ShooterGeneration,
            fact.ShooterLifeId, fact.ShotId, "authority-outcome", frame, fact.LaunchFrame,
            fact.ResolveTick, fact.Weapon, null, fact.DamageEventId, fact.VictimSlot,
            fact.VictimGeneration, fact.VictimLifeId, fact.Damage, fact.Lethal, fact.Headshot);

    internal static string Export(string path)
    {
        // Hold one read handle for hashing and decoding. DemoReader owns another
        // handle; final hash equality refuses a concurrently replaced source.
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Convert.ToHexString(SHA256.HashData(source));
        using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("Replay cannot be opened.");
        var rows = new List<Row>();
        void Add(Row row)
        {
            if (rows.Count == MaximumRows) throw new InvalidDataException("Fidelity export row budget exceeded.");
            rows.Add(row);
        }
        while (reader.ReadNext() is { } record)
        {
            var packet = ReplayIdentityCompatibility.Convert(record.Data, reader.ProtocolVersion);
            if (packet.Length > 2 && packet[0] == (byte)PacketType.SlotIntent && packet[1] < 8)
            {
                var intent = IntentPacket.Read(packet[2..]);
                if (!NetFireEvents.Validate(intent)) continue;
                for (int i = 0; i < intent.FireEventCount; i++) Add(Fire(packet[1], intent, intent.FireEvents[i], record.Frame));
            }
            else if (packet.Length > 1 && packet[0] == (byte)PacketType.ReplayShotFact)
            {
                for (int offset = 1; offset + ReplayShotFactPacket.Size <= packet.Length; offset += ReplayShotFactPacket.Size)
                    if (ReplayShotFactPacket.TryRead(packet.Slice(offset, ReplayShotFactPacket.Size), out var fact))
                        Add(Outcome(fact, record.Frame));
            }
        }
        source.Position = 0;
        if (hash != Convert.ToHexString(SHA256.HashData(source))) throw new IOException("Replay changed during export.");
        return JsonSerializer.Serialize(new
        {
            Schema = 1, SourceSha256 = hash, Protocol = reader.ProtocolVersion, Format = reader.FormatVersion,
            Decoder = (reader.Metadata?.Integrity ?? ReplayIntegrity.Unknown).ToString(),
            Provenance = new
            {
                Identity = "match + authority epoch + slot + generation + life + shot; damage event and victim distinguish outcomes",
                AuthoredInput = "recorded shooter intent; not proof of acceptance or authoritative muzzle",
                AuthorityOutcome = "accepted ReplayShotFact; resolve tick is authority clock, recording frame is delivery clock",
                Pose = "authored muzzle, pre-spread aim, final direction and charge; absent fields remain null",
                OwnerCamera = "unavailable in replay packets; optional owner sidecar is local presentation only"
            },
            Rows = rows.Distinct().OrderBy(x => x.Match).ThenBy(x => x.Epoch).ThenBy(x => x.Slot)
                .ThenBy(x => x.Generation).ThenBy(x => x.Life).ThenBy(x => x.Shot)
                .ThenBy(x => x.Stage, StringComparer.Ordinal).ThenBy(x => x.DamageEvent).ThenBy(x => x.RecordingFrame)
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
