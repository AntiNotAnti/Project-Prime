using System;
using System.Collections.Immutable;

namespace MphRead
{
    /// <summary>
    /// The authoritative playable Hunter policy shared by simulation, clients,
    /// bots, and account-facing services. <see cref="Hunter.Random"/> is a
    /// selector sentinel and is intentionally not part of this catalog.
    /// </summary>
    public readonly record struct PlayableHunterDefinition(
        Hunter Hunter,
        BeamType AffinityWeapon,
        bool SupportsAltForm,
        bool SupportsReplicatedAltAttack,
        int RecolorCount,
        int AltRecolorCount = 6);

    public static class PlayableHunterCatalog
    {
        public const Hunter First = Hunter.Samus;
        public const Hunter Last = Hunter.Guardian;

        public static readonly ImmutableArray<Hunter> All =
            ImmutableArray.Create(
                Hunter.Samus,
                Hunter.Kanden,
                Hunter.Trace,
                Hunter.Sylux,
                Hunter.Noxus,
                Hunter.Spire,
                Hunter.Weavel,
                Hunter.Guardian);

        public static readonly ImmutableArray<PlayableHunterDefinition> Definitions =
            ImmutableArray.Create(
                new PlayableHunterDefinition(Hunter.Samus, BeamType.Missile, true, false, 6),
                new PlayableHunterDefinition(Hunter.Kanden, BeamType.VoltDriver, true, false, 6),
                new PlayableHunterDefinition(Hunter.Trace, BeamType.Imperialist, true, true, 6),
                new PlayableHunterDefinition(Hunter.Sylux, BeamType.ShockCoil, true, false, 6),
                // Noxus's top attack remains a local/authoritative ability;
                // the generic replicated-active-attack bit is not used for it.
                new PlayableHunterDefinition(Hunter.Noxus, BeamType.Judicator, true, false, 6),
                new PlayableHunterDefinition(Hunter.Spire, BeamType.Magmaul, true, true, 6),
                new PlayableHunterDefinition(Hunter.Weavel, BeamType.Battlehammer, true, true, 6),
                new PlayableHunterDefinition(Hunter.Guardian, BeamType.PowerBeam, true, true, 6, 5));

        public static int Count => All.Length;

        public static bool IsPlayable(Hunter hunter)
            => hunter >= First && hunter <= Last;

        public static bool IsSelectorValue(Hunter hunter)
            => IsPlayable(hunter) || hunter == Hunter.Random;

        public static Hunter FromIndex(int index)
        {
            if ((uint)index >= (uint)All.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return All[index];
        }

        public static bool TryFromIndex(int index, out Hunter hunter)
        {
            if ((uint)index < (uint)All.Length)
            {
                hunter = All[index];
                return true;
            }
            hunter = default;
            return false;
        }

        public static Hunter ResolveRandom(Hunter hunter, int randomIndex)
            => hunter == Hunter.Random ? FromIndex(randomIndex) : hunter;

        public static PlayableHunterDefinition Get(Hunter hunter)
        {
            if (!IsPlayable(hunter))
            {
                throw new ArgumentOutOfRangeException(nameof(hunter), hunter,
                    "The selector sentinel is not a playable Hunter.");
            }
            return Definitions[(int)hunter];
        }

        public static BeamType GetAffinityWeapon(Hunter hunter)
            => Get(hunter).AffinityWeapon;

        /// <summary>
        /// Validate a canonical biped recolor. This is intentionally separate
        /// from alt-model palette resolution because Guardian's biped has six
        /// authored recolors even though Psycho Bit has five.
        /// </summary>
        public static int ValidateRecolor(Hunter hunter, int requested)
        {
            if (!IsPlayable(hunter))
            {
                throw new ArgumentOutOfRangeException(nameof(hunter));
            }
            int count = Get(hunter).RecolorCount;
            if ((uint)requested >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(requested), requested,
                    "The requested biped recolor is not authored for this Hunter.");
            }
            return requested;
        }

        /// <summary>
        /// Resolve a canonical six-slot team palette into Psycho Bit's five
        /// authored alt-model recolors. PlayerEntity.Recolor deliberately
        /// remains canonical for the Hunter biped and gameplay state.
        /// </summary>
        public static int ResolveAltRecolor(Hunter hunter, int requested)
        {
            ValidateRecolor(hunter, requested);
            int count = Get(hunter).AltRecolorCount;
            if (hunter == Hunter.Guardian)
            {
                return requested switch
                {
                    4 => 3,
                    5 => 4,
                    >= 0 and < 5 => requested,
                    _ => throw new ArgumentOutOfRangeException(nameof(requested), requested,
                        "Guardian recolors are authored for indices 0 through 4; team slots 4 and 5 map explicitly.")
                };
            }
            if ((uint)requested >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(requested), requested,
                    "The requested alt-model recolor is not authored for this Hunter.");
            }
            return requested;
        }
    }
}
