using System;
using System.IO;
using MphRead.Mods.Input;

namespace MphRead.Entities
{
    /// <summary>Pure EU1.1 touch math, with native constants kept independent of presentation rate.</summary>
    internal static class MorphBallTouchRules
    {
        internal const float RollPerDsPixel = 5f / 4096f;
        internal const int BoostThresholdSquared = 8100;

        internal static (float X, float Z) Roll(short dx, short dy, float share, float scale,
            float forwardX, float forwardZ, float sideX, float sideZ)
            => ((-dy * forwardX - dx * sideX) * RollPerDsPixel * scale * share,
                (-dy * forwardZ - dx * sideZ) * RollPerDsPixel * scale * share);

        internal static (float X, float Z) Boost(short dx, short dy, float speed,
            float forwardX, float forwardZ, float sideX, float sideZ)
        {
            float magnitude = MathF.Sqrt((float)dx * dx + (float)dy * dy);
            if (magnitude <= 0) return (0, 0);
            float f = -dy * speed / magnitude, s = -dx * speed / magnitude;
            return (forwardX * f + sideX * s, forwardZ * f + sideZ * s);
        }

        internal enum Branch { Shoulder, Touch, SkipShoulder }
        internal static Branch Arbitrate(bool boosting, bool canTouch, in NativeTouchReport touch)
        {
            if (boosting || !canTouch || !touch.Continued) return Branch.Shoulder;
            int dx = touch.Delta4X, dy = touch.Delta4Y;
            return dx * dx + dy * dy > BoostThresholdSquared ? Branch.Touch : Branch.SkipShoulder;
        }
    }

    /// <summary>One sample decision, latched across both 60 Hz consumers.</summary>
    internal sealed class MorphBallBoostStateMachine
    {
        internal struct State
        {
            internal bool Boosting, CanTouchBoost;
            internal ushort Charge;
        }

        internal enum Fired { None, Touch, Shoulder }
        internal readonly record struct Result(Fired Kind, ushort Spent, MorphBallTouchRules.Branch Branch);
        private bool _latched;
        private uint _sequence;
        private MorphBallTouchRules.Branch _branch;
        public void Reset() => _latched = false;

        internal void WriteCheckpoint(BinaryWriter writer)
        {
            writer.Write(_latched);
            writer.Write(_sequence);
            writer.Write((byte)_branch);
        }

        internal void RestoreCheckpoint(BinaryReader reader)
        {
            _latched = reader.ReadBoolean();
            _sequence = reader.ReadUInt32();
            byte branch = reader.ReadByte();
            if (branch > 2) throw new InvalidDataException("Invalid native boost branch in replay.");
            _branch = (MorphBallTouchRules.Branch)branch;
        }

        internal Result Advance(ref State state, in NativeTouchReport touch, bool buttonHeld,
            ushort min, ushort max, bool alwaysFull)
        {
            bool fresh = !_latched || touch.SampleSequence != _sequence;
            if (fresh)
            {
                _sequence = touch.SampleSequence;
                _latched = true;
                if (!touch.Down) state.CanTouchBoost = true;
                _branch = MorphBallTouchRules.Arbitrate(state.Boosting, state.CanTouchBoost, touch);
            }
            switch (_branch)
            {
                case MorphBallTouchRules.Branch.Touch:
                    if (fresh)
                    {
                        state.Boosting = true;
                        state.CanTouchBoost = false;
                    }
                    return new(fresh ? Fired.Touch : Fired.None, 0, _branch);
                case MorphBallTouchRules.Branch.SkipShoulder:
                    return new(Fired.None, 0, _branch);
                default:
                    if (buttonHeld)
                    {
                        if (state.Charge < max) state.Charge++;
                        return new(Fired.None, 0, _branch);
                    }
                    ushort spent = state.Charge;
                    state.Charge = 0;
                    if (spent <= min) return new(Fired.None, 0, _branch);
                    state.Boosting = true;
                    return new(Fired.Shoulder, alwaysFull ? max : spent, _branch);
            }
        }
    }
}
