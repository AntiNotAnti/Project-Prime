using System;

namespace MphRead.Mods.Physics
{
    /// <summary>Conversions of native per-tick rates, never absolute velocity,
    /// caps or impulses. Callers must separately preserve update ordering and
    /// collision semantics. No render clock participates in these operations.</summary>
    public static class FrameRateMath
    {
        public static float HalfStepDamping(float nativeFactor)
        {
            UnitInterval(nativeFactor);
            return MathF.Sqrt(nativeFactor);
        }

        public static float HalfStepInterpolation(float nativeAmount)
        {
            UnitInterval(nativeAmount);
            // Rationalized form avoids cancellation for very small amounts.
            return nativeAmount / (1 + MathF.Sqrt(1 - nativeAmount));
        }

        public static float ScaleLinearPerTick(float nativeValue)
        {
            Finite(nativeValue);
            return nativeValue * 0.5f;
        }

        public static float ConvertDamping(float nativeFactor, float nativeHz, float targetHz)
        {
            UnitInterval(nativeFactor);
            return (float)Math.Pow(nativeFactor, Ratio(nativeHz, targetHz));
        }

        public static float ConvertInterpolation(float nativeAmount, float nativeHz, float targetHz)
        {
            UnitInterval(nativeAmount);
            double ratio = Ratio(nativeHz, targetHz);
            if (nativeAmount == 0 || nativeAmount == 1) return nativeAmount;
            // log(1-a) ~ -a below double's subtraction resolution.
            double logarithm = nativeAmount < 1e-8f ? -nativeAmount : Math.Log(1 - (double)nativeAmount);
            double exponent = ratio * logarithm;
            return (float)(Math.Abs(exponent) < 1e-8 ? -exponent : 1 - Math.Exp(exponent));
        }

        public static float ConvertLinearRate(float nativeAmount, float nativeHz, float targetHz)
        {
            Finite(nativeAmount);
            double result = nativeAmount * Ratio(nativeHz, targetHz);
            if (Math.Abs(result) > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(nativeAmount));
            return (float)result;
        }

        private static double Ratio(float nativeHz, float targetHz)
        {
            Finite(nativeHz);
            Finite(targetHz);
            if (nativeHz <= 0 || targetHz <= 0) throw new ArgumentOutOfRangeException(nameof(targetHz));
            return (double)nativeHz / targetHz;
        }

        private static void UnitInterval(float value)
        {
            Finite(value);
            if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static void Finite(float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
