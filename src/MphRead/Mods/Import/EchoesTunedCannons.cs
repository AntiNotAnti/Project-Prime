using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// Locally-imported Echoes kinetic orb cannons. Tuned launch coordinates were
/// authored and published as JSON recipes by Second Hunt (MIT, pinned at
/// c16a907b3658c2fd4079e394a73711596a85caee), not extracted ROM meshes.
/// This experimental gameplay changes map identity, never the existing
/// collision/visual previews, shared packages or online rules.
/// </summary>
public static class EchoesTunedCannons
{
    public sealed record Cannon(float[] Pad, float[] Launch, ushort LockFrames, bool AllowAlt, float[]? Landed = null);

    private static readonly IReadOnlyDictionary<string, Cannon[]> Source =
        new Dictionary<string, Cannon[]>(StringComparer.Ordinal)
        {
        ["M01_SidehopperStation"] = new[]
        {
            new Cannon(new[] { -46.23339f, 0.5700778f, 47.138607f }, new[] { 1.3040819f, 0.7176989f, -0.14031129f }, (ushort)0, true, new[] { 54.995243f, 0.5972228f, 36.253807f }),
            new Cannon(new[] { -51.788284f, 0.09506512f, 3.4242074f }, new[] { 0.45901713f, 0.4545466f, -0.20653926f }, (ushort)0, true, new[] { -39.190907f, 6.357451f, -2.1048052f }),
            new Cannon(new[] { 46.233387f, 0.57007813f, 47.138607f }, new[] { -1.3040814f, 0.7176989f, -0.14031127f }, (ushort)0, true, new[] { -54.995235f, 0.59697866f, 36.253807f }),
            new Cannon(new[] { 51.740917f, 0.09507239f, 3.411096f }, new[] { -0.4697438f, 0.45306242f, -0.22217214f }, (ushort)0, true, new[] { 39.0863f, 6.373569f, -2.525479f }),
        },
        ["M02_Spires"] = new[]
        {
            new Cannon(new[] { -36.124413f, 1.88344f, -98.133f }, new[] { 0.29129726f, 1.3624663f, 1.0997179f }, (ushort)0, true, new[] { 5.5556865f, 4.944336f, 57.997124f }),
            new Cannon(new[] { 36.136845f, 1.8595515f, -98.094124f }, new[] { -0.4486117f, 0.9474795f, 1.6261724f }, (ushort)0, true, new[] { -8.798656f, 4.944336f, 62.09285f }),
            new Cannon(new[] { -0.013689959f, 4.190143f, 35.575123f }, new[] { 0.0005911026f, 0.78069174f, -1.4337074f }, (ushort)0, true, new[] { 0.18472826f, 2.5863104f, -87.78179f }),
        },
        ["M05_SpiderComplex"] = new[]
        {
            new Cannon(new[] { 0.25444722f, 2.1184006f, -11.15086f }, new[] { -0.001903675f, 1.0193071f, 0.113543406f }, (ushort)0, true, new[] { 0.026644893f, 24.014893f, -3.529263f }),
            new Cannon(new[] { 0.46501717f, 2.0847597f, 29.971025f }, new[] { 0.0034679342f, 0.9998456f, -0.33972666f }, (ushort)0, true, new[] { 0.7372228f, 24.014893f, 4.6509333f }),
        },
        ["M06_ShootingGallery"] = new[]
        {
            new Cannon(new[] { -38.518528f, 2.3703718f, -11.878377f }, new[] { 0.24563637f, 0.5734752f, 0.005758212f }, (ushort)0, true, new[] { -27.709175f, 8.796143f, -11.706489f }),
            new Cannon(new[] { -0.000020345053f, 2.6538177f, -59.25926f }, new[] { 0.005039713f, 0.56997025f, 0.27380025f }, (ushort)0, true, new[] { 0.22712156f, 8.796143f, -46.918842f }),
            new Cannon(new[] { 38.511528f, 2.3703718f, -11.878377f }, new[] { -0.23763046f, 0.5717202f, 0.005818058f }, (ushort)0, true, new[] { 28.217178f, 8.796143f, -11.626366f }),
        }
        };

    public static IReadOnlyCollection<string> ArenaKeys { get; } =
        Array.AsReadOnly(Source.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray());

    public static int TotalCount => Source.Values.Sum(x => x.Length);

    public static int CountFor(string key) =>
        Source.TryGetValue(key, out Cannon[]? cannons) ? cannons.Length : 0;

    public static MapDefinition CreateVariant(MapDefinition visualPreview, string arenaKey)
    {
        ArgumentNullException.ThrowIfNull(visualPreview);
        if (!Source.TryGetValue(arenaKey, out Cannon[]? cannons) ||
            cannons.Length == 0)
            throw new InvalidDataException("No verified Echoes cannon recipe for this arena.");
        EchoesArenaSources.Arena arena = EchoesArenaSources.All.Single(x => x.Key == arenaKey);
        if (!visualPreview.LocalSourceOnly ||
            visualPreview.Name != "VISUAL ECHOES " + arena.Name.ToUpperInvariant() ||
            visualPreview.MapId == Guid.Empty ||
            visualPreview.Capabilities?.MaxPlayers != 1 ||
            !visualPreview.Geometry.OfType<MapMesh>().Any(m => m.CollisionOnly) ||
            !visualPreview.Geometry.OfType<MapMesh>().Any(m => !m.CollisionOnly))
            throw new InvalidDataException("Tuned cannons require a private, solo-only Echoes visual preview.");

        MapDefinition result = MapProjectSerializer.Clone(visualPreview);
        result.MapId = StableId("project-prime/echoes/tuned-cannons/v1/" +
            visualPreview.MapId.ToString("N"));
        result.Name = "CANNON ECHOES " + arena.Name.ToUpperInvariant();
        MapValidator.RequireRuntimeName(result.Name);
        result.InGameName = arena.Name + " (cannon playtest)";
        result.Description = "Local source-world visual geometry, independent original collision, and experimental kinetic orb cannon launches. Single-player only.";
        for (int i = 0; i < cannons.Length; i++)
        {
            Cannon c = cannons[i];
            if (c.Pad.Length != 3 || c.Launch.Length != 3 ||
                !c.Pad.All(float.IsFinite) || !c.Launch.All(float.IsFinite))
                throw new InvalidDataException("Nonfinite or malformed authored Echoes cannon data.");
            float speed = MathF.Sqrt(c.Launch.Sum(v => v * v));
            if (!float.IsFinite(speed) || speed < 0.01f || speed > 10f)
                throw new InvalidDataException("Echoes cannon velocity exceeds accepted range.");
            // The stock Project Prime MapBuilder accepts direction plus
            // speed. Existing physics determines the launch; this is a
            // locally opt-in playtest until real-device landings are measured.
            result.JumpPads.Add(new MapJumpPad
            {
                Id = StableId(result.MapId.ToString("N") + "/orb/" + i),
                Label = "Echoes tuned orb cannon " + (i + 1),
                Position = (float[])c.Pad.Clone(),
                Vector = (float[])c.Launch.Clone(),
                Speed = speed,
                Size = new[] { 3f, 2f, 3f },
                ModelId = 0,
                CooldownTime = 5,
                ControlLockTime = c.LockFrames
            });
        }
        // The upstream converter snaps starts/pickups to nearby walkable
        // source collision. Keep the original map tiers immutable: this
        // corrected placement belongs only to the new cannon-playtest ID.
        EchoesFloorPlacement.SnapSpawnAndPickupFeet(result, maxDrop: 3f);
        if (result.Capabilities?.MaxPlayers != 1 || !result.LocalSourceOnly)
            throw new InvalidDataException("A tuned Echoes source map cannot be shared or used in multiplayer.");
        return result;
    }


    /// <summary>Return defensive copies of published source cannon pad/landing
    /// pairs for physics/authoring acceptance tools.</summary>
    public static IReadOnlyList<(float[] Pad, float[] Landed)> Landings(string key)
    {
        if (!Source.TryGetValue(key, out Cannon[]? entries))
            return Array.Empty<(float[], float[])>();
        return entries.Where(x => x.Landed is { Length: 3 })
            .Select(x => ((float[])x.Pad.Clone(), (float[])x.Landed!.Clone())).ToArray();
    }

    /// <summary>Add one-way bot links in a NEW local map identity. A failed
    /// source landing is never guessed from nearby visual geometry.</summary>
    public static MapDefinition CreateRoutedVariant(MapDefinition cannonMap, string arenaKey)
    {
        ArgumentNullException.ThrowIfNull(cannonMap);
        if (!Source.TryGetValue(arenaKey, out Cannon[]? source) || source.Length == 0)
            throw new InvalidDataException("No tuned cannon routes exist for this arena.");
        EchoesArenaSources.Arena arena = EchoesArenaSources.All.Single(x => x.Key == arenaKey);
        if (!cannonMap.LocalSourceOnly ||
            cannonMap.Name != "CANNON ECHOES " + arena.Name.ToUpperInvariant() ||
            cannonMap.MapId == Guid.Empty || cannonMap.Capabilities?.MaxPlayers != 1 ||
            cannonMap.JumpPads.Count != source.Length)
            throw new InvalidDataException("Only a private solo cannon playtest can generate bot routes.");
        MapDefinition routed = MapProjectSerializer.Clone(cannonMap);
        routed.MapId = StableId("project-prime/echoes/routed-v1/" + cannonMap.MapId.ToString("N"));
        routed.Name = "ROUTED ECHOES " + arena.Name.ToUpperInvariant();
        MapValidator.RequireRuntimeName(routed.Name);
        routed.InGameName = arena.Name + " (bot route playtest)";
        routed.Description = "Locally imported kinetic cannon bot routes; unverified on real media and offline-only.";
        MapMesh[] floors = routed.Geometry.OfType<MapMesh>()
            .Where(x => x.Solid && x.CollisionOnly).ToArray();
        if (floors.Length == 0)
            throw new InvalidDataException("Routed cannon map has no original collision surfaces.");

        for (int i = 0; i < source.Length; i++)
        {
            Cannon cannon = source[i];
            if (cannon.Landed is not { Length: 3 })
                throw new InvalidDataException("Incomplete authored cannon arrival information.");
            MapJumpPad pad = routed.JumpPads[i];
            if (pad.Vector is not { Length: 3 } || pad.Target != null)
                throw new InvalidDataException("Routed cannon requires its original explicit launch vector.");
            if (!TryCreateLandingLink(floors, pad.Position, cannon.Landed,
                out MapNavigationLink? route))
                throw new InvalidDataException("Cannon " + (i + 1) + " of " + arena.Name +
                    " has no collision-supported pad or landing. Bot link not installed.");
            route!.Id = StableId(routed.MapId.ToString("N") + "/route/" + i);
            routed.NavigationLinks.Add(route);
        }
        return routed;
    }

    public static bool TryCreateLandingLink(IEnumerable<MapMesh> floors, float[] pad,
        float[] landing, out MapNavigationLink? route)
    {
        route = null;
        if (!EchoesFloorPlacement.Snap(floors, pad, maxDrop: 3f, out float[] from) ||
            !EchoesFloorPlacement.Snap(floors, landing, maxDrop: 4f, out float[] to))
            return false;
        float dx = from[0] - to[0], dy = from[1] - to[1], dz = from[2] - to[2];
        if (dx * dx + dy * dy + dz * dz < 0.25f) return false;
        route = new MapNavigationLink
        {
            Id = Guid.Empty, Kind = MapNavigationLinkKind.JumpPad,
            From = from, To = to, Bidirectional = false,
            FromNodeKind = MapNavigationAnchorKind.Auto,
            ToNodeKind = MapNavigationAnchorKind.Aerial
        };
        return true;
    }

    private static Guid StableId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
