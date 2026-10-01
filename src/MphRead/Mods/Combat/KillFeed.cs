using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Combat
{
    internal enum KillFeedKind : byte
    {
        Weapon,
        Alt,
        Bomb,
        Burn,
        Deathalt,
        Suicide,
        Environment
    }

    internal readonly record struct KillFeedEntry(
        int KillerSlot,
        int VictimSlot,
        int KillerTeam,
        int VictimTeam,
        string KillerName,
        string VictimName,
        BeamType Beam,
        KillFeedKind Kind,
        bool Headshot,
        bool FriendlyFire,
        long CreatedTicks)
    {
        internal double AgeSeconds
            => (Stopwatch.GetTimestamp() - CreatedTicks) / (double)Stopwatch.Frequency;
    }

    /// <summary>
    /// Scene-local presentation history for confirmed player deaths.
    ///
    /// Live multiplayer consumes canonical authority facts. Legacy recordings
    /// retain their original damage-derived adapter; historical generations use
    /// neutral labels when their occupant is no longer available.
    /// </summary>
    internal sealed class KillFeed
    {
        internal static int MaxVisible => Render.Hud.HudProfiles.Runtime.KillFeed.Rows;
        internal static double LifetimeSeconds => Render.Hud.HudProfiles.Runtime.KillFeed.Lifetime;
        internal const double FadeSeconds = 1;

        private const int Capacity = 10;
        private readonly List<KillFeedEntry> _entries = new(Capacity);

        internal IReadOnlyList<KillFeedEntry> Entries => _entries;

        internal void Record(Scene scene, PlayerEntity? attacker, PlayerEntity victim,
            BeamType beam, DamageFlags flags, bool fromHalfturret, BombEntity? bomb)
        {
            if (MatchEvents.MatchSemanticPresentation.Enabled(scene) || !Features.KillFeedEnabled || MphRead.Mods.Headless.Active
                || MphRead.Mods.ThumbnailMode.Active || !scene.GameState.Multiplayer)
            {
                return;
            }

            PruneExpired();

            int victimSlot = victim.SlotIndex;
            int killerSlot = attacker?.SlotIndex ?? -1;
            bool suicide = attacker == victim;
            bool friendlyFire = attacker != null && attacker != victim && scene.GameState.Teams
                && TeamRules.AreAllies(attacker.TeamIndex, victim.TeamIndex);

            KillFeedKind kind;
            if (attacker == null)
            {
                kind = KillFeedKind.Environment;
            }
            else if (suicide)
            {
                kind = KillFeedKind.Suicide;
            }
            else if (flags.TestFlag(DamageFlags.Deathalt))
            {
                kind = KillFeedKind.Deathalt;
            }
            else if (flags.TestFlag(DamageFlags.Burn))
            {
                kind = KillFeedKind.Burn;
            }
            else if (beam >= BeamType.PowerBeam && beam <= BeamType.OmegaCannon)
            {
                kind = KillFeedKind.Weapon;
            }
            else if (bomb != null)
            {
                kind = KillFeedKind.Bomb;
            }
            else
            {
                // Halfturret and direct alt-form hits arrive here. On a
                // remote client a non-beam authority replay names the attacker
                // as the source, so this fallback also preserves the useful
                // attribution without expanding DamageEvent or the protocol.
                _ = fromHalfturret;
                kind = KillFeedKind.Alt;
            }

            string victimName = scene.GameState.Nicknames[victimSlot];
            string killerName = attacker == null
                ? "WORLD"
                : scene.GameState.Nicknames[killerSlot];

            _entries.Insert(0, new KillFeedEntry(
                KillerSlot: killerSlot,
                VictimSlot: victimSlot,
                KillerTeam: attacker?.TeamIndex ?? -1,
                VictimTeam: victim.TeamIndex,
                KillerName: killerName,
                VictimName: victimName,
                Beam: beam,
                Kind: kind,
                Headshot: flags.TestFlag(DamageFlags.Headshot),
                FriendlyFire: friendlyFire,
                CreatedTicks: Stopwatch.GetTimestamp()));

            if (_entries.Count > Capacity)
            {
                _entries.RemoveRange(Capacity, _entries.Count - Capacity);
            }
        }

        internal void RecordCanonical(Scene scene, MatchEvents.MatchSemanticEvent fact)
        {
            if (!Features.KillFeedEnabled || Mods.Headless.Active || Mods.ThumbnailMode.Active || !scene.GameState.Multiplayer
                || !TryBuildCanonical(scene, fact, out var entry)) return;
            PruneExpired(); _entries.Insert(0, entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        internal static bool TryBuildCanonical(Scene scene, MatchEvents.MatchSemanticEvent fact, out KillFeedEntry entry)
        {
            entry = default;
            if (!MatchEvents.MatchDeathDetails.TryDecode(fact.Value, out var details)) return false;
            var killer = MatchEvents.MatchSemanticPresentation.Resolve(scene, fact.Actor);
            var victim = MatchEvents.MatchSemanticPresentation.Resolve(scene, fact.Target);
            // A historical generation never borrows the replacement occupant's name.
            string Name(MatchEvents.MatchSemanticActor actor, PlayerEntity? player)
                => !actor.IsPlayer ? "WORLD" : player == null ? $"PLAYER {actor.Slot + 1}" : scene.GameState.Nicknames[actor.Slot];
            entry = new(fact.Actor.IsPlayer ? fact.Actor.Slot : -1, fact.Target.Slot,
                details.KillerTeam, details.VictimTeam, Name(fact.Actor, killer), Name(fact.Target, victim),
                (BeamType)fact.Weapon, (KillFeedKind)details.Kind,
                (fact.Flags & MatchEvents.MatchSemanticEventFlags.Headshot) != 0,
                (fact.Flags & MatchEvents.MatchSemanticEventFlags.FriendlyFire) != 0, Stopwatch.GetTimestamp());
            return true;
        }

        internal void PruneExpired()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].AgeSeconds >= LifetimeSeconds)
                {
                    _entries.RemoveAt(i);
                }
            }
        }

        internal void Clear() => _entries.Clear();
    }
}
