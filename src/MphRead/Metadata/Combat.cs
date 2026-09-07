using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Entities;

namespace MphRead
{
    public static partial class Metadata
    {
        public static float GetDamageMultiplier(Effectiveness effectiveness)
        {
            int index = (int)effectiveness;
            if ((uint)index >= DamageMultipliers.Count)
            {
                throw new IndexOutOfRangeException();
            }
            return DamageMultipliers[index];
        }

        public static readonly IReadOnlyList<float> DamageMultipliers = new float[4] { 0, 0.5f, 1, 2 };

        public static void LoadEffectiveness(int value, Effectiveness[] dest)
        {
            Debug.Assert(value >= 0);
            LoadEffectiveness((uint)value, dest);
        }

        public static void LoadEffectiveness(uint value, Effectiveness[] dest)
        {
            Debug.Assert(dest.Length == 9);
            dest[0] = (Effectiveness)(value & 3);
            dest[1] = (Effectiveness)((value >> 2) & 3);
            dest[2] = (Effectiveness)((value >> 4) & 3);
            dest[3] = (Effectiveness)((value >> 6) & 3);
            dest[4] = (Effectiveness)((value >> 8) & 3);
            dest[5] = (Effectiveness)((value >> 10) & 3);
            dest[6] = (Effectiveness)((value >> 12) & 3);
            dest[7] = (Effectiveness)((value >> 14) & 3);
            dest[8] = (Effectiveness)((value >> 16) & 3);
        }

    }
}
