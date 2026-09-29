using System;

namespace MphRead.Mods.Physics;

/// <summary>AMHE1 arithmetic used by the offline biped-kernel experiment.</summary>
internal static class NativeFixedMath
{
    internal static int Raw(float value) => checked((int)MathF.Round(value * 4096f));
    internal static float Float(int value) => value / 4096f;
    // ARM signed products in movement damping / contact pushout truncate toward zero.
    internal static float MultiplyTruncate(float a, float b)
        => Float(checked((int)((long)Raw(a) * Raw(b) / 4096)));
    // SDK FX_Mul and the traction cap add 0x800 before the arithmetic shift.
    // Negative halfway values therefore round toward positive infinity.
    internal static float MultiplyRound(float a, float b)
        => Float(checked((int)(((long)Raw(a) * Raw(b) + 2048) >> 12)));
    internal static float DotRound(float ax, float ay, float az, float bx, float by, float bz)
        => Float(checked((int)(((long)Raw(ax) * Raw(bx) + (long)Raw(ay) * Raw(by)
            + (long)Raw(az) * Raw(bz) + 2048) >> 12)));
    internal static float ProjectPlaneDistance(float x, float y, float z, float w)
        => MultiplyRound(x, MultiplyRound(x, w)) + MultiplyRound(y, MultiplyRound(y, w))
            + MultiplyRound(z, MultiplyRound(z, w));
    internal static float DivideRound(float a, float b)
    {
        long quotient = ((long)Raw(a) << 32) / Raw(b);
        return Float(checked((int)((quotient + 0x80000) >> 20)));
    }
    internal static float HorizontalMagnitude(float x, float z)
    {
        int sum = checked(Raw(MultiplyRound(x, x)) + Raw(MultiplyRound(z, z)));
        if (sum <= 0) return 0;
        return SquareRoot(Float(sum));
    }
    internal static float SquareRoot(float value)
    {
        int raw = Raw(value);
        if (raw <= 0) return 0;
        ulong root = IntegerSquareRoot((ulong)raw << 32);
        return Float(checked((int)((root + 512) >> 10)));
    }
    // Generated from the mathematical cosine, not copied from game assets.
    private static readonly int[] Cosine = CreateCosine();
    private static int[] CreateCosine()
    {
        var result = new int[4096];
        for (int i = 0; i < result.Length; i++)
            result[i] = (int)Math.Round(4096 * Math.Cos(i * (2 * Math.PI / 4096)));
        return result;
    }
    internal static float SwayFactor(float phase)
    {
        long degrees = (long)Raw(phase) * 180 + 180 * 4096;
        long angle = ((degrees * 0xB60B60B60BL >> 32) + 0x800) >> 12;
        int index = (int)((angle & 0xFFFF) >> 4);
        return Float((Cosine[index] + 4096) >> 1);
    }
    internal static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        long a = Raw(x), b = Raw(y), c = Raw(z);
        ulong sum = checked((ulong)(a * a + b * b + c * c));
        if (sum == 0) return (0, 0, 0);
        ulong quotient = (1UL << 56) / sum;
        ulong root = IntegerSquareRoot(checked(sum * 4));
        float Component(long value) => Float(checked((int)(((Int128)value * quotient * root + ((Int128)1 << 44)) >> 45)));
        return (Component(a), Component(b), Component(c));
    }
    // Integer implementation avoids platform-dependent sqrt rounding at ties.
    internal static ulong IntegerSquareRoot(ulong value)
    {
        ulong result = 0, bit = 1UL << 62;
        while (bit > value) bit >>= 2;
        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else result >>= 1;
            bit >>= 2;
        }
        return result;
    }
}
