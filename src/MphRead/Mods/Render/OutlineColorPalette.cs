using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Four fixed team slots and eight FFA server slots, never scoreboard order.</summary>
public sealed class OutlineColorSettings
{
    public string[] Teams { get; set; } = ["#FF4040", "#4080FF", "#40FF80", "#FFD040"];
    public string[] Players { get; set; } =
        ["#FF4040", "#4080FF", "#40FF80", "#FFD040",
         "#D040FF", "#40FFFF", "#FF8040", "#FF80C0"];

    public OutlineColorSettings Copy() => new()
    {
        Teams = (string[])Teams.Clone(),
        Players = (string[])Players.Clone()
    };

    public void Validate()
    {
        if (Teams is not { Length: 4 } || Players is not { Length: 8 })
            throw new FormatException("Outline colors require four team slots and eight FFA slots.");
        foreach (string[] group in new[] { Teams, Players })
        {
            for (int i = 0; i < group.Length; i++)
            {
                string hex = (group[i] ?? "").Trim().TrimStart('#');
                if (hex.Length != 6 || !uint.TryParse(hex,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                    throw new FormatException("Each outline color must be #RRGGBB.");
                group[i] = "#" + hex.ToUpperInvariant();
            }
        }
    }
}

/// <summary>
/// Parse/validate color JSON only when settings are loaded or committed.
/// The renderer reads immutable, already-resolved RGB slot arrays. No
/// per-frame parsing, forced brightening or new through-wall render passes.
/// </summary>
public static class OutlineColorPalette
{
    private sealed record Snapshot(OutlineColorSettings Settings, Vector4[] Teams, Vector4[] Players, string Json);
    private static Snapshot _current = Prepare(new OutlineColorSettings());

    private static Snapshot Prepare(OutlineColorSettings input)
    {
        OutlineColorSettings settings = input.Copy();
        settings.Validate();
        static Vector4[] Resolve(string[] names)
        {
            return Array.ConvertAll(names, hex =>
            {
                uint rgb = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new Vector4(((rgb >> 16) & 255) / 255f,
                    ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
            });
        }
        return new Snapshot(settings, Resolve(settings.Teams), Resolve(settings.Players),
            JsonSerializer.Serialize(settings));
    }

    public static OutlineColorSettings CopyCurrent() => Volatile.Read(ref _current).Settings.Copy();
    public static string Json
    {
        get => Volatile.Read(ref _current).Json;
        set { if (TryParse(value, out var colors)) Publish(colors!); }
    }
    public static bool TryParse(string? value, out OutlineColorSettings? colors)
    {
        colors = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<OutlineColorSettings>(value,
                new JsonSerializerOptions { MaxDepth = 8 });
            parsed?.Validate();
            colors = parsed;
            return parsed != null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }
    public static void Publish(OutlineColorSettings settings) =>
        Volatile.Write(ref _current, Prepare(settings));

    internal static Vector4 Color(bool teams, int index)
    {
        Snapshot snapshot = Volatile.Read(ref _current);
        Vector4[] palette = teams ? snapshot.Teams : snapshot.Players;
        return (uint)index < (uint)palette.Length
            ? palette[index] : new Vector4(.6f, .6f, .6f, 1);
    }
}
