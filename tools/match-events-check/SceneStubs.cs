// Minimal dependency substitutes exercise the production scene adapter without game content.
// These are F0 adapter checks, not a simulated gameplay or real-network acceptance test.
namespace MphRead
{
    internal sealed class Scene
    {
        internal Services Services { get; } = new();
        // This content-free adapter harness does not admit private weapon damage.
        internal PrivateWeaponStub? PirateBlaster => null;
        internal State GameState { get; } = new();
        internal Mods.MatchEvents.SceneMatchEvents MatchEvents { get; } = new();
        internal PlayerCollection Players { get; } = new();
        internal ulong FrameCount { get; set; }
    }
    internal sealed class PlayerCollection
    {
        internal System.Collections.Generic.List<Entities.PlayerEntity> Items { get; } = new();
    }
    internal sealed class PrivateWeaponStub
    {
        internal bool OwnsDamage(Entities.PlayerEntity victim, Entities.BeamProjectileEntity beam) => false;
    }
    internal sealed class Services { internal bool IsReplica { get; set; } }
    internal sealed class AssistStats { internal int[] Counts { get; } = new int[8]; internal void Assist(int slot) => Counts[slot]++; }
    internal sealed class State { internal AssistStats CtzStats { get; } = new(); internal bool Multiplayer { get; set; } = true; internal bool Teams { get; set; } }
    internal enum BeamType { None = -1, PowerBeam = 0, OmegaCannon = 8 }
}
namespace MphRead.Entities
{
    internal sealed class BeamProjectileEntity { }
    internal sealed class PlayerEntity
    {
        internal Scene? OwningScene { get; set; }
        internal int SlotIndex { get; set; }
        internal int TeamIndex { get; set; }
        internal int Health { get; set; } = 100;
        internal bool IsPrimeHunter { get; set; }
        internal object? OctolithFlag { get; set; }
    }
    internal enum DamageFlags { None = 0, Headshot = 1, FromAlt = 2, Deathalt = 4, Burn = 8 }
    internal static class FlagExtensions { internal static bool TestFlag(this DamageFlags flags, DamageFlags mask) => (flags & mask) != 0; }
}
namespace MphRead.Mods.Multiplayer
{
    internal static class TeamRules { internal static bool AreAllies(int left, int right) => left == right; }
}
namespace MphRead.Mods.CustomWeapons
{
    // Identity-only dependency of the production semantic adapter. This fixture
    // does not exercise custom weapon admission, simulation or damage authority.
    internal readonly record struct WeaponOwnerContext(ushort MatchId, ulong AuthorityEpoch,
        byte Slot, ushort Generation, ushort Life)
    {
        internal bool IsValid => MatchId != 0 && AuthorityEpoch != 0 && Slot < 8
            && Generation != 0 && Life != 0;
    }
}
namespace MphRead.Mods.Network
{
    internal readonly record struct ShotKey(ulong AuthorityEpoch, ushort MatchId, int ShooterSlot, ushort Generation, ushort LifeId, uint ShotId);
    internal static class NetSession
    {
        internal static bool Active { get; set; }
        internal static bool IsAuthority { get; set; }
        internal static ushort CurrentMatchId { get; set; } = 1;
        internal static ulong AuthorityEpoch { get; set; } = 1;
        internal static uint NetFrame { get; set; }
        internal static bool IsStarting { get; set; }
        internal static bool FreezeGameplay { get; set; }
    }
    internal static class NetPlayerLifecycle
    {
        internal static ushort[] Generations { get; } = { 1, 1, 1, 1, 1, 1, 1, 1 };
        internal static ushort Generation(int slot) => Generations[slot];
        internal static ushort Get(int slot) => 1;
    }
}

namespace MphRead.Mods.Network
{
    // The isolated F0 scene adapter compiles without the full combat authority.
    // The real journal updates Prime ownership in the game; this fixture only
    // verifies semantic-event publication, never inventory or shot admission.
    internal static class NetShotSourceJournal
    {
        internal static void PrimeChanged(MphRead.Scene scene) { }
    }

    internal static class MatchReportStats
    {
        internal static void AcceptSemantic(MphRead.Scene scene, in MphRead.Mods.MatchEvents.MatchSemanticEvent fact,
            MphRead.Mods.MatchEvents.MatchAwardEngine awards) { }
    }
}
