using System;
namespace MphRead.Mods.MatchEvents;
internal enum MatchSemanticEventType : byte
{
    MatchStarted, CountdownStarted, MatchEnded, PlayerSpawned, PlayerKilled, PlayerAssisted,
    PlayerSuicide, WeaponFired, Headshot, ObjectivePickedUp, ObjectiveDropped, ObjectiveCaptured,
    ObjectiveDefended, NodeContested, NodeCaptured, PrimeChanged, MatchPoint, OvertimeStarted
}
[Flags]
internal enum MatchSemanticEventFlags : byte { None = 0, Headshot = 1, FriendlyFire = 2, Environment = 4, Prime = 8, Carrier = 16, AltForm = 32, Turret = 64 }
internal readonly record struct MatchSemanticActor(byte Slot, ushort SlotGeneration, ushort Life)
{
    internal static MatchSemanticActor None => new(byte.MaxValue, 0, 0);
    internal bool IsPlayer => Slot < 8 && SlotGeneration != 0 && Life != 0;
}
// Preserve existing protocol identity widths (match ushort, authority ulong).
internal readonly record struct MatchSemanticEvent(uint EventId, ushort MatchId, ulong AuthorityEpoch,
    uint Tick, MatchSemanticEventType Type, MatchSemanticActor Actor, MatchSemanticActor Target,
    int Weapon = -1, MatchSemanticEventFlags Flags = 0, int EntityId = -1, int Value = 0);
