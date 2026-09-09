using System;

namespace MphRead.Telemetry
{
    public enum TelemetryKind : byte { Position, Spawn, Damage, Kill, Death, World, Award, MatchSemantic }

    // Slot/life are local to this file. No account, network, name, or IP identifier is exported.
    public readonly record struct TelemetryEvent(uint Tick, TelemetryKind Kind, byte Slot, uint Life,
        float X, float Y, float Z, byte Team = 255, byte Hunter = 255, byte Weapon = 255,
        int Value = 0, uint Subject = 0, byte OtherSlot = 255, float? EnemyDistance = null,
        int? VisibleEnemies = null, byte OtherHunter = 255, uint SemanticId = 0);

    public sealed record MatchTelemetry(int Format, Guid Id, string Room, MatchMode Mode, uint MatchId,
        uint StartTick, uint EndTick, bool Completed, int DroppedEvents, TelemetryEvent[] Events)
    {
        public const int CurrentFormat = 1;
        public const int MaxEvents = 131072;
    }
}
