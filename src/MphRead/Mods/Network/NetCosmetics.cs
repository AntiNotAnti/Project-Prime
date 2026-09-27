using System;
using System.Buffers.Binary;
using MphRead.Entities;
using MphRead.Mods.Cosmetics;
namespace MphRead.Mods.Network
{
    // Additive protocol-24 extension. Every record is match/authority/occupant
    // fenced; batches contain whole records only. No paths or particle events.
    public readonly record struct CosmeticStatePacket(byte Slot, byte Hunter, ushort Generation,
        ushort Match, ulong Authority, uint Revision, ushort Skin, ushort Armor, ushort Death)
    {
        public const int Size = 24;
        public void Write(Span<byte> dest)
        {
            dest[0] = Slot; dest[1] = Hunter;
            BinaryPrimitives.WriteUInt16LittleEndian(dest[2..], Generation);
            BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], Match);
            BinaryPrimitives.WriteUInt64LittleEndian(dest[6..], Authority);
            BinaryPrimitives.WriteUInt32LittleEndian(dest[14..], Revision);
            BinaryPrimitives.WriteUInt16LittleEndian(dest[18..], Skin);
            BinaryPrimitives.WriteUInt16LittleEndian(dest[20..], Armor);
            BinaryPrimitives.WriteUInt16LittleEndian(dest[22..], Death);
        }
        public static bool TryRead(ReadOnlySpan<byte> src, out CosmeticStatePacket value)
        {
            value = default;
            if (src.Length != Size || src[0] >= PlayerEntity.SlotCapacity || src[1] >= 7) return false;
            value = new(src[0], src[1], BinaryPrimitives.ReadUInt16LittleEndian(src[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(src[4..]), BinaryPrimitives.ReadUInt64LittleEndian(src[6..]),
                BinaryPrimitives.ReadUInt32LittleEndian(src[14..]), BinaryPrimitives.ReadUInt16LittleEndian(src[18..]),
                BinaryPrimitives.ReadUInt16LittleEndian(src[20..]), BinaryPrimitives.ReadUInt16LittleEndian(src[22..]));
            return value.Generation != 0;
        }
        public static CosmeticStatePacket Create(int slot, Hunter hunter, ushort generation, ushort match,
            ulong authority, uint revision, CosmeticAppearance appearance) => new((byte)slot, (byte)hunter,
                generation, match, authority, revision, appearance.Skin.WireId, appearance.Armor.WireId, appearance.Death.WireId);
    }
    public sealed class NetCosmetics
    {
        public static NetCosmetics Live { get; } = new();
        private readonly CosmeticStatePacket[] _packets = new CosmeticStatePacket[PlayerEntity.SlotCapacity];
        private readonly CosmeticAppearance?[] _appearances = new CosmeticAppearance?[PlayerEntity.SlotCapacity];
        public uint Revision(int slot) => (uint)slot < _packets.Length ? _packets[slot].Revision : 0;
        public void Reset() { Array.Clear(_packets); Array.Clear(_appearances); }
        public CosmeticAppearance Get(int slot, Hunter hunter, ushort generation) =>
            (uint)slot < _packets.Length && generation != 0 && _packets[slot].Generation == generation
                && _packets[slot].Hunter == (byte)hunter ? _appearances[slot] ?? CosmeticAppearance.Default : CosmeticAppearance.Default;
        public bool Accept(CosmeticStatePacket packet, ushort match, ulong authority, ushort generation)
        {
            if (packet.Slot >= _packets.Length || packet.Hunter >= 7 || packet.Match != match || packet.Authority != authority
                || generation == 0 || packet.Generation != generation) return false;
            var prior = _packets[packet.Slot];
            if (prior.Generation == generation && prior.Match == match && prior.Authority == authority
                && !NetLifecycleTracker.Newer(packet.Revision, prior.Revision) && _appearances[packet.Slot] != null) return false;
            var appearance = new CosmeticAppearance((Hunter)packet.Hunter,
                CosmeticCatalog.FromWire((Hunter)packet.Hunter, packet.Skin, packet.Armor, packet.Death));
            _appearances[packet.Slot] = appearance;
            _packets[packet.Slot] = packet with { Skin = appearance.Skin.WireId, Armor = appearance.Armor.WireId, Death = appearance.Death.WireId };
            return true;
        }
        public int Write(Span<byte> dest, ushort match, ulong authority)
        {
            int offset = 0;
            for (int i = 0; i < _packets.Length; i++)
                if (_appearances[i] != null && _packets[i].Match == match && _packets[i].Authority == authority)
                { _packets[i].Write(dest[offset..]); offset += CosmeticStatePacket.Size; }
            return offset;
        }
    }
}
