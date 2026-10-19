using System;

namespace MphRead.Mods.Launcher;

/// <summary>Home-only empty party pedestals in normalized framebuffer UV coordinates.</summary>
internal static class LauncherHomePartyPads
{
    // Far center first, then the two nearer wings. These are deliberately
    // smaller than the occupied Lobby pads and never alter Lobby formation.
    internal static (float X, float Y, float RadiusX, float RadiusY) At(int seat) => seat switch
    {
        0 => (.500f, .535f, .048f, .016f),
        1 => (.340f, .620f, .060f, .020f),
        2 => (.660f, .620f, .060f, .020f),
        _ => throw new ArgumentOutOfRangeException(nameof(seat))
    };

    // Feed the same structured geometry to the shared GL/modern shader source.
    // The edge fades completely at q=1.05 (visible radius < 1.025 * radius).
    internal static string ShaderSelection => "seat == 0 ? " + Literal(At(0))
        + " : (seat == 1 ? " + Literal(At(1)) + " : " + Literal(At(2)) + ")";

    private static string Literal((float X, float Y, float RadiusX, float RadiusY) pad)
        => FormattableString.Invariant($"vec4({pad.X:0.000}, {pad.Y:0.000}, {pad.RadiusX:0.000}, {pad.RadiusY:0.000})");
}
