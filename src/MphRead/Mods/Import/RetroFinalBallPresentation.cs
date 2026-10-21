// Adapted from Second Hunt VeinBall/HdBall at41452f2d053fcfa679e33c00a73d120a53f59e61.
// MIT, Copyright2020 NoneGiven; see repository LICENSE. No game assets included.
using System;
using System.IO;

namespace MphRead.Mods.Import;

// Detached private texture variants. Original material archives/pixels remain
// immutable; only the separately owned composite receives these copies.
public static class RetroFinalBallPresentation
{
    private static void Validate(byte[] rgba)
    {
        if (rgba == null || rgba.Length is < 4 or > 4 * 1024 * 1024 || rgba.Length % 4 != 0)
            throw new InvalidDataException("Complete bounded RGBA pixels required for Final ball presentation.");
    }

    public static void ShellTexture(byte[] rgba)
    {
        Validate(rgba);
        for (int i = 3; i < rgba.Length; i += 4)
        {
            double t = Math.Clamp((rgba[i] / 255.0 - .43) / .57, 0, 1);
            rgba[i] = (byte)Math.Round((.45 + .5 * t) * 255);
        }
    }

    public static void VentTexture(byte[] rgba)
    {
        Validate(rgba);
        for (int i = 0; i < rgba.Length; i += 4)
        {
            double l = (rgba[i] + rgba[i + 1] + rgba[i + 2]) / (3 * 255.0);
            double k = Math.Clamp((l - .18) / .3, 0, 1) * .35;
            for (int channel = 0; channel < 3; channel++)
                rgba[i + channel] = (byte)Math.Clamp(rgba[i + channel] * k, 0, 255);
        }
    }

    public static void LineTexture(byte[] rgba)
    {
        Validate(rgba);
        double[] tint = { .35, .6, 1 };
        for (int i = 0; i < rgba.Length; i += 4)
        {
            double l = (rgba[i] + rgba[i + 1] + rgba[i + 2]) / (3 * 255.0);
            double line = l < .5 ? Math.Pow(Math.Clamp((l - .10) / .24, 0, 1), 1.5) : 0;
            for (int channel = 0; channel < 3; channel++)
            {
                double color = .6 * tint[channel] + .4 * rgba[i + channel] / 255.0;
                rgba[i + channel] = (byte)Math.Clamp(color * line * .65 * 255, 0, 255);
            }
        }
    }

    // The native source-appearance owner supplies completed unpaused60Hz time.
    // No wall clock, display refresh or gameplay state participates.
    public static double HeartScale(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 1_000_000)
            throw new InvalidDataException("Finite bounded nonnegative source material time required.");
        double phase = seconds / .8 % 1;
        double G(double center, double width) => Math.Exp(-Math.Pow((phase - center) / width, 2));
        return 1 + .3 * G(.26, .07) + .15 * G(.55, .06) - .3 * G(.10, .05) - .18 * G(.42, .045);
    }

    // Canonical integer cycle for the existing60Hz owner. The donor's Gaussian
    // endpoints differ; floating seconds/.8 can select the previous endpoint
    // at e.g.2.4 seconds. Every completed48-step cycle must start identically.
    public static double HeartScaleAtStep(long completedSteps)
    {
        if(completedSteps<0)throw new InvalidDataException("Nonnegative completed source steps required.");
        return HeartScale((completedSteps%48)/60d);
    }
}
