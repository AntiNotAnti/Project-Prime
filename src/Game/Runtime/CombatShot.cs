namespace MphRead.Mods.Network
{
    public enum LagCompensationMode { None, HistoricalTrace, ProjectileCatchUp, HomingProjectileCatchUp }
    public readonly record struct CombatShot(CombatActor Actor, uint CommandSequence, uint ProcessedServerTick,
        uint ViewServerTick, uint ActionServerTick, uint RewindTicks, LagCompensationMode Mode = LagCompensationMode.None)
    {
        public bool IsValid => Actor.IsValid;
        public bool Affinity { get; init; }
        public bool SourceAltForm { get; init; }
        public byte SourceWeapon { get; init; } = 255;
        // Historical traces advance their target timeline with normal shot age.
        // A projectile fast-forwarded to the present must use current targets instead.
        public uint GetHistoricalTick(uint currentServerTick)
            => unchecked(ActionServerTick + (currentServerTick - ProcessedServerTick));
    }

}
