using System;

namespace MphRead
{
    internal enum BlastDamageTargetKind : byte
    {
        None,
        Body,
        HalfturretHead
    }

    /// <summary>
    /// A pre-filtered point considered by splash damage.  The caller owns all
    /// entity, team, lag-compensation, and occlusion checks; keeping those
    /// facts as booleans makes selection deterministic and allocation-free.
    /// </summary>
    internal readonly struct BlastDamageCandidate
    {
        internal BlastDamageCandidate(int ownerSlot, float distance,
            bool eligible, bool visible)
        {
            OwnerSlot = ownerSlot;
            Distance = distance;
            Eligible = eligible;
            Visible = visible;
        }

        internal int OwnerSlot { get; }
        internal float Distance { get; }
        internal bool Eligible { get; }
        internal bool Visible { get; }

        internal bool IsValid
            => Eligible && Visible && Distance >= 0 && float.IsFinite(Distance);
    }

    internal readonly struct BlastDamageSelection
    {
        private BlastDamageSelection(int ownerSlot, BlastDamageTargetKind kind,
            float distance)
        {
            OwnerSlot = ownerSlot;
            Kind = kind;
            Distance = distance;
        }

        internal int OwnerSlot { get; }
        internal BlastDamageTargetKind Kind { get; }
        internal float Distance { get; }
        internal bool IsValid => Kind != BlastDamageTargetKind.None;
        internal bool IsHalfturret => Kind == BlastDamageTargetKind.HalfturretHead;

        internal static BlastDamageSelection None
            => new(-1, BlastDamageTargetKind.None, 0);

        internal static BlastDamageSelection Body(in BlastDamageCandidate candidate)
            => new(candidate.OwnerSlot, BlastDamageTargetKind.Body,
                candidate.Distance);

        internal static BlastDamageSelection HalfturretHead(
            in BlastDamageCandidate candidate)
            => new(candidate.OwnerSlot, BlastDamageTargetKind.HalfturretHead,
                candidate.Distance);
    }

    /// <summary>
    /// Central splash target policy.  This intentionally contains no entity
    /// references or collections, so it can be used from the fixed simulation
    /// loop without introducing hot-tick allocations.
    /// </summary>
    internal static class BlastDamagePolicy
    {
        /// <summary>
        /// Source inspection of the current collision path shows that Trace
        /// and Noxus alternate forms already receive ordinary player splash in
        /// Classic.  Keep that behavior in both profiles; Balanced does not
        /// add an immunity or otherwise filter the existing body path.
        /// </summary>
        internal static bool AllowsAltFormSplash(Hunter hunter)
            => hunter == Hunter.Trace || hunter == Hunter.Noxus;

        internal static bool AllowsAltFormSplash(MatchBalanceContext balance,
            Hunter hunter)
            => AllowsAltFormSplash(hunter);

        internal static bool AllowsPlayerSplash(MatchBalanceContext balance,
            Hunter hunter, bool altForm)
        {
            // Keep the current body path for every form. The explicit
            // Trace/Noxus branch records the source-confirmed Classic
            // behavior without accidentally making other existing forms
            // immune while a future profile is added.
            if (altForm && (hunter == Hunter.Trace || hunter == Hunter.Noxus))
            {
                return AllowsAltFormSplash(balance, hunter);
            }
            return true;
        }

        /// <summary>
        /// Balanced exposes the Weavel head/turret as a second splash point.
        /// The existing body point remains available in both profiles.
        /// </summary>
        internal static bool AllowsHalfturretHeadSplash(
            MatchBalanceContext balance, Hunter hunter)
            => balance != null && balance.IsBalanced && hunter == Hunter.Weavel;

        /// <summary>
        /// Select at most one point for an owner.  A direct body or turret hit
        /// suppresses the owner entirely; ties prefer body to retain the
        /// existing body-only behavior and to make replay selection stable.
        /// </summary>
        internal static BlastDamageSelection SelectTarget(int directOwnerSlot,
            in BlastDamageCandidate body, in BlastDamageCandidate head)
        {
            if (body.OwnerSlot == directOwnerSlot
                || head.OwnerSlot == directOwnerSlot)
            {
                return BlastDamageSelection.None;
            }
            bool bodyValid = body.IsValid;
            bool headValid = head.IsValid;
            if (!bodyValid)
            {
                return headValid
                    ? BlastDamageSelection.HalfturretHead(head)
                    : BlastDamageSelection.None;
            }
            if (!headValid || body.Distance <= head.Distance)
            {
                return BlastDamageSelection.Body(body);
            }
            return BlastDamageSelection.HalfturretHead(head);
        }
    }
}
