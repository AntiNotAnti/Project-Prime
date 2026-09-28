namespace MphRead.Mods.Multiplayer
{
    public static class WeaponResourceRules
    {
        private static readonly ItemType[] Pool = { ItemType.VoltDriver, ItemType.Battlehammer,
            ItemType.Judicator, ItemType.Magmaul, ItemType.ShockCoil };

        // FNV-1a over a canonical room key and stable spawn identity. No simulation
        // RNG is consumed; reconstructing a room or seeking produces the same layout.
        // Deliberately map-stable across rounds, including historical offline rounds.
        public static ItemType Replacement(string room, int spawnId)
        {
            uint hash = 2166136261;
            foreach (char c in room.ToUpperInvariant()) hash = unchecked((hash ^ c) * 16777619);
            for (int shift = 0; shift < 32; shift += 8)
                hash = unchecked((hash ^ (byte)(spawnId >> shift)) * 16777619);
            return Pool[hash % (uint)Pool.Length];
        }
        public static bool AllowsBeam(BeamType beam, bool instaGib, bool noImperialist) =>
            (!instaGib || beam == BeamType.Imperialist) && (!noImperialist || beam != BeamType.Imperialist);

        public static ItemType Resolve(ItemType type, bool noImperialist, string room, int spawnId) =>
            noImperialist && type == ItemType.Imperialist ? Replacement(room, spawnId) : type;

        public static BeamType ResolveBeam(BeamType beam, bool noImperialist, string room, int identity)
        {
            if (!noImperialist || beam != BeamType.Imperialist) return beam;
            return Replacement(room, identity) switch {
                ItemType.VoltDriver => BeamType.VoltDriver, ItemType.Battlehammer => BeamType.Battlehammer,
                ItemType.Judicator => BeamType.Judicator, ItemType.Magmaul => BeamType.Magmaul,
                _ => BeamType.ShockCoil };
        }
    }
}
