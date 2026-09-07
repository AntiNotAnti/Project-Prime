using MphRead.Mods.Network;

namespace MphRead.Combat
{
    public readonly record struct KillFeedEntry(uint Tick, CombatActor Killer, CombatActor Victim, string Text);
}
