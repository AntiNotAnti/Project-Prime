using System;

namespace MphRead.Telemetry
{
    public enum TelemetryKind : byte { Position, Spawn, Damage, Kill, Death, World, Award, MatchSemantic }

    /// <summary>
    /// Score components from the authoritative enhanced spawn selection. This
    /// is absent for Classic policy and old format-1 captures, preserving
    /// backward-compatible telemetry decoding.
    /// </summary>
    public readonly record struct SpawnSelectionTelemetry(int EntityId, float Score,
        float NearestEnemyDistanceSquared, int VisibleEnemies, int FacingEnemies,
        int NearbyEnemies, float DeathPenalty, float UsePenalty, float FriendlyBonus,
        float ObjectivePenalty, float ResourcePenalty, float HazardPenalty,
        float ReservationPenalty, bool ImmediateHazard, bool CooldownFallback,
        bool HazardFallback, bool TeamFallback);

    // Slot/life are local to this file. No account, network, name, or IP identifier is exported.
    public readonly record struct TelemetryEvent(uint Tick, TelemetryKind Kind, byte Slot, uint Life,
        float X, float Y, float Z, byte Team = 255, byte Hunter = 255, byte Weapon = 255,
        int Value = 0, uint Subject = 0, byte OtherSlot = 255, float? EnemyDistance = null,
        int? VisibleEnemies = null, byte OtherHunter = 255, uint SemanticId = 0,
        SpawnSelectionTelemetry? SpawnSelection = null);

    public sealed record MatchTelemetry(int Format, Guid Id, string Room, MatchMode Mode, uint MatchId,
        uint StartTick, uint EndTick, bool Completed, int DroppedEvents, TelemetryEvent[] Events)
    {
        public const int CurrentFormat = 1;
        public const int MaxEvents = 131072;
    }
}
