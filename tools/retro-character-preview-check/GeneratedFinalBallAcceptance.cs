using MphRead.Mods.Import;

// Invented pixels only. Literal RGB/heartbeat oracles were calculated separately
// from Second Hunt 41452f2d VeinBall filters and HdBall.Beat, never game payloads.
internal static class GeneratedFinalBallAcceptance
{
    private static int _checks;
    private const int MaximumBytes = 4 * 1024 * 1024;
    internal static void Run()
    {
        Shell(); Filters(); Heart(); Refusals();
        Console.WriteLine($"PASS: Final ball detached texture presentation and source-time heartbeat ({_checks} checks)");
    }

    private static void Shell()
    {
        byte[] pixels = Enumerable.Range(0, 256).SelectMany(a => new byte[] { (byte)a, (byte)(255 - a), 73, (byte)a }).ToArray();
        byte[] original = pixels.ToArray();
        RetroFinalBallPresentation.ShellTexture(pixels);
        for (int a = 0; a < 256; a++)
        {
            // Above alpha109 the affine map is exactly (200*a+4233)/228.
            // Its odd numerator never has a half-integer tie; integer arithmetic
            // independently checks nearest-byte rounding instead of truncation.
            int expected = a <= 109 ? 115 : (200 * a + 4233 + 114) / 228;
            Check(pixels[a * 4 + 3] == expected && pixels.AsSpan(a * 4, 3).SequenceEqual(original.AsSpan(a * 4, 3)),
                "shell alpha ramp and untouched RGB: " + a);
        }
        Check(pixels[3] == 115 && pixels[^1] == 242 && pixels[110 * 4 + 3] == 115
            && pixels[111 * 4 + 3] == 116, "shell facing/rim endpoints and low-alpha clamp transition");
    }

    private static void Filters()
    {
        (byte[] Input, byte[] Vent, byte[] Line)[] cases =
        {
            (new byte[]{0,0,0},new byte[]{0,0,0},new byte[]{0,0,0}),
            (new byte[]{25,25,25},new byte[]{0,0,0},new byte[]{0,0,0}),
            (new byte[]{26,26,26},new byte[]{0,0,0},new byte[]{0,0,0}),
            (new byte[]{45,45,45},new byte[]{0,0,0},new byte[]{8,12,19}),
            (new byte[]{46,46,46},new byte[]{0,0,0},new byte[]{9,13,21}),
            (new byte[]{86,86,86},new byte[]{15,15,15},new byte[]{56,80,119}),
            (new byte[]{87,87,87},new byte[]{16,16,16},new byte[]{57,82,122}),
            (new byte[]{122,122,122},new byte[]{42,42,42},new byte[]{66,91,131}),
            (new byte[]{123,123,123},new byte[]{43,43,43},new byte[]{66,91,131}),
            (new byte[]{127,127,127},new byte[]{44,44,44},new byte[]{67,92,132}),
            (new byte[]{128,128,128},new byte[]{44,44,44},new byte[]{0,0,0}),
            (new byte[]{180,180,180},new byte[]{62,62,62},new byte[]{0,0,0}),
            (new byte[]{255,255,255},new byte[]{89,89,89},new byte[]{0,0,0}),
            (new byte[]{255,0,0},new byte[]{45,0,0},new byte[]{96,57,95}),
            (new byte[]{0,255,0},new byte[]{0,45,0},new byte[]{33,120,95}),
            (new byte[]{0,0,255},new byte[]{0,0,45},new byte[]{33,57,158}),
            (new byte[]{40,90,120},new byte[]{6,15,20},new byte[]{41,76,120}),
            (new byte[]{1,90,180},new byte[]{0,18,36},new byte[]{35,83,146})
        };
        foreach (byte alpha in new byte[] { 0, 1, 127, 255 })
        {
            byte[] pixels = cases.SelectMany(c => c.Input.Append(alpha)).ToArray();
            byte[] vents = pixels.ToArray(), lines = pixels.ToArray();
            RetroFinalBallPresentation.VentTexture(vents); RetroFinalBallPresentation.LineTexture(lines);
            for (int i = 0; i < cases.Length; i++)
            {
                Check(vents.AsSpan(i * 4, 3).SequenceEqual(cases[i].Vent) && vents[i * 4 + 3] == alpha,
                    "vent brightness gate, saturation and RGB truncation preserve alpha: " + i);
                Check(lines.AsSpan(i * 4, 3).SequenceEqual(cases[i].Line) && lines[i * 4 + 3] == alpha,
                    "line nonlinear gate, blue tint and bright-speck removal preserve alpha: " + i);
            }
            Check(pixels.SequenceEqual(cases.SelectMany(c => c.Input.Append(alpha))), "filter copies do not mutate invented original pixels");
        }
    }

    private static void Heart()
    {
        // These retain the other lobes' Gaussian tails: phase0 is not exactly1,
        // and pulse centres are not precisely the extrema. Static authoring's
        // 2.3 geometry enlargement is deliberately absent from this time factor.
        (double Seconds, double Expected)[] samples =
        {
            (0, .9945056142689929), (.04, .8896731905897934), (.08, .7016149317225753),
            (.12, .9150271661004347), (.16, 1.1384009889748925), (.208, 1.2999887041432328),
            (.24, 1.216279779863255), (.28, 1.0414312679976971), (.336, .8229868237783395),
            (.38, .9910535855276109), (.44, 1.1499572749138791), (.5, 1.0314417078984202),
            (.6, 1.0000022418007788), (.7, 1.000000000000027), (.8, .9945056142689929),
            (1, 1.2939024651412447), (1_000_000, .9945056142689929)
        };
        foreach (var sample in samples)
            Check(Math.Abs(RetroFinalBallPresentation.HeartScale(sample.Seconds) - sample.Expected) < 2e-14,
                "independent heartbeat literal at source seconds " + sample.Seconds);
        for (int tick = 0; tick < 48; tick++)
            for (int cycle = 1; cycle <= 4; cycle++)
                Check(Math.Abs(RetroFinalBallPresentation.HeartScale(tick / 60d)
                    - RetroFinalBallPresentation.HeartScale((tick + cycle * 48) / 60d)) < 2e-14,
                    "heartbeat repeats every48 completed60Hz source ticks");
        double minimum = double.PositiveInfinity, maximum = 0, minTime = 0, maxTime = 0;
        for (int i = 0; i < 12_000; i++)
        {
            double seconds = i / 15_000d, value = RetroFinalBallPresentation.HeartScale(seconds);
            Check(double.IsFinite(value) && value >= .69 && value <= 1.31, "positive bounded heartbeat over complete dense period");
            if (value < minimum) { minimum = value; minTime = seconds; }
            if (value > maximum) { maximum = value; maxTime = seconds; }
        }
        Check(Math.Abs(minimum - .701592341758178) < 1e-6 && minTime is > .0795 and < .0799
            && Math.Abs(maximum - 1.2999887105553125) < 1e-6 && maxTime is > .2078 and < .2082,
            "independently sampled global squeeze and rebound extrema retain donor amplitude and phase");
        Check(RetroFinalBallPresentation.HeartScale(.04) > RetroFinalBallPresentation.HeartScale(.08)
            && RetroFinalBallPresentation.HeartScale(.12) > RetroFinalBallPresentation.HeartScale(.08)
            && RetroFinalBallPresentation.HeartScale(.28) > RetroFinalBallPresentation.HeartScale(.336)
            && RetroFinalBallPresentation.HeartScale(.38) > RetroFinalBallPresentation.HeartScale(.336)
            && RetroFinalBallPresentation.HeartScale(.44) > RetroFinalBallPresentation.HeartScale(.38)
            && RetroFinalBallPresentation.HeartScale(.44) > RetroFinalBallPresentation.HeartScale(.5),
            "separate lub/dub squeezes, smaller second rebound and refill survive time mapping");
    }

    private static void Refusals()
    {
        foreach (var filter in new (string Name, Action<byte[]> Apply)[] {
            ("shell",RetroFinalBallPresentation.ShellTexture), ("vent",RetroFinalBallPresentation.VentTexture),
            ("line",RetroFinalBallPresentation.LineTexture) })
        {
            Refuse(() => filter.Apply(null!), filter.Name + " null pixels");
            foreach (int length in new[] { 0, 1, 2, 3, 5, 6, 7, MaximumBytes + 1, MaximumBytes + 4 })
            {
                byte[] invalid = new byte[length]; if (length > 0) invalid[0] = 197;
                if (length > 3) invalid[3] = 255;
                byte[] before = invalid.ToArray();
                Refuse(() => filter.Apply(invalid), filter.Name + " invalid complete bounded RGBA length " + length);
                Check(invalid.SequenceEqual(before), filter.Name + " validates whole input before any pixel mutation");
            }
            byte[] single = { 12, 34, 56, 78 }; filter.Apply(single);
            byte[] maximum = new byte[MaximumBytes]; maximum[^1] = 255; filter.Apply(maximum);
            Check(maximum.Length == MaximumBytes, filter.Name + " accepts exact one-pixel and4MiB boundaries");
        }
        foreach (double seconds in new[] { -double.Epsilon, -1, double.NaN, double.NegativeInfinity,
            double.PositiveInfinity, Math.BitIncrement(1_000_000d), double.MaxValue })
            Refuse(() => RetroFinalBallPresentation.HeartScale(seconds), "invalid source heartbeat seconds");
    }
    private static void Refuse(Action action, string label)
    {
        try { action(); } catch (InvalidDataException) { _checks++; return; }
        throw new InvalidDataException("Expected presentation rejection: " + label);
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidDataException("Final ball presentation: " + label);
        _checks++;
    }
}
