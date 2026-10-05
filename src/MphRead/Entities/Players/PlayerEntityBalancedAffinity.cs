using System;
using MphRead.Mods.Multiplayer;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private ushort _balancedDisruptImmunityTimer;
        private ulong _balancedLifeDrainWindowStart = UInt64.MaxValue;
        private byte _balancedLifeDrainWindowHeal;

        internal ushort ModBalancedDisruptImmunityTimer => _balancedDisruptImmunityTimer;

        internal void ModResetBalancedAffinityState()
        {
            _balancedDisruptImmunityTimer = 0;
            _balancedLifeDrainWindowStart = UInt64.MaxValue;
            _balancedLifeDrainWindowHeal = 0;
        }

        internal void ModTickBalancedAffinityState()
        {
            if (_balancedDisruptImmunityTimer > 0)
            {
                _balancedDisruptImmunityTimer--;
            }
        }

        internal bool ModTryBalancedDisrupt()
        {
            if (_balancedDisruptImmunityTimer > 0)
            {
                return false;
            }

            _disruptedTimer = BalancedModeRules.AffinityControlDurationFrames;
            _balancedDisruptImmunityTimer = (ushort)(
                BalancedModeRules.AffinityControlDurationFrames
                + BalancedModeRules.AffinityControlImmunityFrames);
            return true;
        }

        internal int ModBalancedLifeDrainHeal(int actualDamage)
        {
            int requested = BalancedModeRules.LifeDrainHealForActualDamage(actualDamage);
            if (requested <= 0)
            {
                return 0;
            }

            ulong frame = _scene.FrameCount;
            if (_balancedLifeDrainWindowStart == UInt64.MaxValue
                || frame - _balancedLifeDrainWindowStart >= 60)
            {
                _balancedLifeDrainWindowStart = frame;
                _balancedLifeDrainWindowHeal = 0;
            }

            int remaining = BalancedModeRules.SyluxLifeDrainPerSecondCap
                - _balancedLifeDrainWindowHeal;
            if (remaining <= 0)
            {
                return 0;
            }

            int heal = Math.Min(requested, remaining);
            _balancedLifeDrainWindowHeal += (byte)heal;
            return heal;
        }
    }
}
