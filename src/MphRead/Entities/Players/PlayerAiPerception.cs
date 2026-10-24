using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private struct BotContact
        {
            public bool Known;
            public bool HeardOnly;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 SmoothedObservedVelocity;
            public BotOpponentModel Opponent;
            public int FramesRemaining;
            public int InitialFrames;
            public int FramesSinceSight;
            public int HearingCooldown;
            public bool VisibleNow;
        }

        private readonly BotContact[] _botContacts = new BotContact[PlayerEntity.SlotCapacity];

        private void ResetBotPerception() => Array.Clear(_botContacts);

        private void BotForgetContact(int slot)
        {
            if ((uint)slot < (uint)_botContacts.Length)
            {
                _botContacts[slot] = default;
            }
        }

        private void BotObserveVisual(PlayerEntity observed, int memoryFrames)
        {
            int slot = observed.SlotIndex;
            if ((uint)slot >= (uint)_botContacts.Length)
            {
                return;
            }
            ref BotContact contact = ref _botContacts[slot];
            bool freshSight = !contact.Known || contact.HeardOnly
                || contact.FramesSinceSight > 5;
            contact.SmoothedObservedVelocity = BotPerceptionMath.SmoothObservedVelocity(
                contact.SmoothedObservedVelocity, observed.Speed, freshSight);
            contact.Opponent.Observe(_player.Position, observed.Position,
                observed.Speed, observed.Flags1.TestFlag(PlayerFlags1.Grounded));
            contact.Known = true;
            contact.HeardOnly = false;
            contact.Position = observed.Position;
            contact.Velocity = observed.Speed;
            contact.FramesRemaining = Math.Max(1, memoryFrames);
            contact.InitialFrames = contact.FramesRemaining;
            contact.FramesSinceSight = 0;
            contact.VisibleNow = true;
            contact.HearingCooldown = 0;
        }

        private bool BotObserveSound(PlayerEntity observed, int memoryFrames, bool fromDamage)
        {
            int slot = observed.SlotIndex;
            if ((uint)slot >= (uint)_botContacts.Length)
            {
                return false;
            }
            ref BotContact contact = ref _botContacts[slot];
            if (!fromDamage && contact.HearingCooldown > 0)
            {
                return false;
            }
            // A recent exact *past* sighting is more informative than sound.
            // Do not overwrite last-seen velocity with an imprecise noise cue.
            if (contact.Known && !contact.HeardOnly && contact.FramesSinceSight < 18)
            {
                contact.FramesRemaining = Math.Max(contact.FramesRemaining, memoryFrames);
                return true;
            }

            Vector3 approximate = BotPerceptionMath.QuantizedSoundCue(_player.Position, observed.Position);
            contact.Known = true;
            contact.HeardOnly = true;
            contact.Position = approximate;
            contact.Velocity = Vector3.Zero;
            contact.SmoothedObservedVelocity = Vector3.Zero;
            contact.FramesRemaining = Math.Max(1, memoryFrames);
            contact.InitialFrames = contact.FramesRemaining;
            contact.FramesSinceSight = 1000;
            contact.VisibleNow = false;
            contact.HearingCooldown = fromDamage ? 10 : 30;
            return true;
        }

        /// <summary>
        /// Run once on the authority simulation. Sight is exact *only while
        /// sighted*. Hearing stores a coarse cue, and visual memory extrapolates
        /// at most 12 simulation frames after LOS is broken.
        /// </summary>
        private void BotObserveContacts(BotTacticalTuning tuning)
        {
            for (int i = 0; i < _botContacts.Length; i++)
            {
                ref BotContact contact = ref _botContacts[i];
                if (contact.Known) contact.Opponent.DecayWithoutSight();
                contact.VisibleNow = false;
                if (contact.HearingCooldown > 0) contact.HearingCooldown--;
                if (contact.FramesRemaining > 0) contact.FramesRemaining--;
                if (contact.FramesSinceSight < 1000) contact.FramesSinceSight++;
                if (contact.FramesRemaining <= 0)
                {
                    contact.Known = false;
                    contact.Opponent = default;
                }
            }

            float hearingSqr = tuning.HearingRange * tuning.HearingRange;
            foreach (PlayerEntity other in _scene.GetPlayerEntities())
            {
                if (!TacticalTargetValid(other))
                {
                    BotForgetContact(other.SlotIndex);
                    if (_tacticalThreatSlot == other.SlotIndex)
                    {
                        _tacticalThreatSlot = -1;
                        _tacticalThreatPosition = Vector3.Zero;
                        _tacticalThreatFrames = 0;
                        _tacticalThreatWeight = 0;
                    }
                    continue;
                }
                if (TacticalCanSee(other))
                {
                    BotObserveVisual(other, tuning.TargetMemoryFrames);
                    continue;
                }

                if (!other.Flags2.TestFlag(PlayerFlags2.Shooting)
                    || Vector3.DistanceSquared(other.Position, _player.Position) > hearingSqr
                    || !BotObserveSound(other, tuning.TargetMemoryFrames / 2, fromDamage: false))
                {
                    continue;
                }
                if ((_tacticalThreatFrames <= 0 || _tacticalThreatWeight < 12)
                    && BotTryKnownPosition(other, out Vector3 cue, out _))
                {
                    _tacticalThreatSlot = other.SlotIndex;
                    _tacticalThreatPosition = cue;
                    _tacticalThreatFrames = Math.Max(_tacticalThreatFrames,
                        tuning.TargetMemoryFrames / 2);
                    // A shot is a search hint, not a precision targeting grant.
                    _tacticalThreatWeight = Math.Max(_tacticalThreatWeight, 8);
                }
            }
        }

        private bool BotTryKnownPosition(PlayerEntity observed,
            out Vector3 knownPosition, out float confidence)
        {
            knownPosition = Vector3.Zero;
            confidence = 0;
            int slot = observed.SlotIndex;
            if ((uint)slot >= (uint)_botContacts.Length)
            {
                return false;
            }
            ref BotContact contact = ref _botContacts[slot];
            if (!contact.Known || contact.FramesRemaining <= 0)
            {
                return false;
            }
            confidence = BotPerceptionMath.Confidence(contact.FramesRemaining,
                contact.InitialFrames, contact.HeardOnly);
            knownPosition = contact.HeardOnly || contact.VisibleNow
                ? contact.Position
                : BotPerceptionMath.RememberedPosition(
                    contact.Position, contact.Velocity, contact.FramesSinceSight);
            return true;
        }

        private Vector3 BotAimObservedVelocity(PlayerEntity observed)
        {
            if (_player.BotLevel < 2 || (uint)observed.SlotIndex >= (uint)_botContacts.Length)
            {
                return observed.Speed / 2f;
            }
            ref BotContact contact = ref _botContacts[observed.SlotIndex];
            if (!contact.Known || contact.HeardOnly || !contact.VisibleNow)
            {
                return observed.Speed / 2f;
            }
            // The aim policy already requires direct LOS. Highly erratic
            // visible strafing reduces long-lead overshoots; the bot does
            // not predict a future unseen reversal or acquire hidden velocity.
            Vector3 velocity = (observed.Speed * .65f
                + contact.SmoothedObservedVelocity * .35f) / 2f;
            return velocity * BotOpponentTactics.LeadScale(contact.Opponent);
        }

        private bool BotTryVisibleOpponentModel(PlayerEntity observed, out BotOpponentModel model)
        {
            model = default;
            int slot = observed.SlotIndex;
            if (_player.BotLevel < 2 || (uint)slot >= (uint)_botContacts.Length)
                return false;
            ref BotContact contact = ref _botContacts[slot];
            if (!contact.Known || contact.HeardOnly || !contact.VisibleNow
                || contact.Opponent.ObservedFrames < 24)
            {
                return false;
            }
            model = contact.Opponent;
            return true;
        }

        private void BotInvestigatePosition(Vector3 lastKnown)
        {
            if (_player.IsAltForm || _player.IsMorphing || _player.IsUnmorphing)
            {
                return;
            }
            Vector3 direction = (lastKnown - _player.Position).WithY(0);
            if (direction.LengthSquared < 2.25f)
            {
                return;
            }
            if (_scene.GameState.Multiplayer && _player.BotLevel >= 2
                && BotTryFollowStrategicGoal(lastKnown))
            {
                // Tactical contacts contain last-seen or coarse heard positions,
                // never today's hidden opponent transform. Directed native
                // routing lets a skilled bot investigate around actual cover.
                BotRecordRouteProgress();
                return;
            }
            direction.Normalize();
            if (!TacticalCanMove(direction))
            {
                // Let the native navigation tree find a route around cover.
                return;
            }

            // Native navigation may have left a stale directional request. An
            // investigation is one authoritative, local move, not a second
            // movement writer competing with those existing flags.
            _buttons.A.IsDown = _buttons.Y.IsDown = false;
            _buttons.X.IsDown = _buttons.B.IsDown = false;
            Vector3 forward = _player.FacingVector.WithY(0);
            if (forward.LengthSquared <= 0.0001f)
            {
                return;
            }
            forward.Normalize();
            Vector3 right = new(forward.Z, 0, -forward.X);
            float f = Vector3.Dot(direction, forward);
            float r = Vector3.Dot(direction, right);
            // Do not create an input that is too diagonal or negligible to be
            // useful: preserve one strong component for native animation.
            if (MathF.Abs(f) >= 0.35f)
            {
                _buttons.X.IsDown = f > 0;
                _buttons.B.IsDown = f < 0;
            }
            if (MathF.Abs(r) >= 0.35f)
            {
                _buttons.A.IsDown = r > 0;
                _buttons.Y.IsDown = r < 0;
            }
            _tacticalMovementActive = true;
            _tacticalSteeringActive = true;
        }
    }
}
