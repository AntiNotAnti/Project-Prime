using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// Local-only Spires geometry draft using the player's own validated Echoes
/// MREA collision, not a redistributable .ppmap. This intentionally draws a
/// simple MPH-material blockout. Faithful Echoes materials, statics, dynamics,
/// nav links, jump pads and full multiplayer acceptance are later slices.
/// </summary>
public static class EchoesLocalMapDraft
{
    public const float UnitsPerEchoesMetre = 1.6f / 2.7f;
    public const int MaxFacesPerMesh = 4096;
    public const int MaxTotalFaces = 300000;

    public static MapDefinition BuildSpires(EchoesCollisionData collision,
        IReadOnlyList<EchoesScriptObject> actors, string verifiedArchiveSha1)
        => BuildArena(EchoesArenaSources.All.Single(x => x.Key == "M02_Spires"),
            collision, actors, verifiedArchiveSha1);

    public static MapDefinition BuildArena(EchoesArenaSources.Arena arena,
        EchoesCollisionData collision, IReadOnlyList<EchoesScriptObject> actors,
        string verifiedArchiveSha1)
    {
        if (!EchoesArenaSources.All.Contains(arena))
            throw new ArgumentException("Only six known Echoes multiplayer arenas are supported.", nameof(arena));
        if (verifiedArchiveSha1 is not { Length: 40 } ||
            !verifiedArchiveSha1.All(Uri.IsHexDigit))
            throw new ArgumentException("Locally verified source archive SHA-1 required.", nameof(verifiedArchiveSha1));
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(actors);
        if (collision.TriangleCount <= 0 || collision.TriangleCount > MaxTotalFaces ||
            collision.TriangleVertices.Length != collision.TriangleCount * 3 ||
            collision.SurfaceFlags.Length != collision.TriangleCount ||
            collision.Vertices.Length % 3 != 0)
            throw new InvalidDataException("Invalid Echoes collision source.");

        string key = arena.Key;
        string fingerprint = "project-prime/local/echoes/v1/" + key + "/" +
            verifiedArchiveSha1.ToLowerInvariant();
        var definition = new MapDefinition
        {
            FormatVersion = 3,
            MapId = StableId(fingerprint + "/map"),
            Name = "LOCAL ECHOES " + arena.Name.ToUpperInvariant(),
            InGameName = arena.Name + " (local blockout)",
            Version = "local-source-v1",
            Author = "Imported from local Echoes source",
            Description = "Local, nonredistributable visual/collision blockout derived from a user-provided Echoes GameCube image.",
            TextureSource = "MP3 PROVING GROUND",
            LocalSourceOnly = true,
            Capabilities = new MapCapabilities { SupportedModes = new List<string> { "Battle" }, MinPlayers = 1, MaxPlayers = 1 },
            Partitioning = new MapPartitionSettings { Enabled = true, PortalCulling = false },
            Materials = new List<MapMaterial>
            {
                new() { Id = StableId(fingerprint + "/fallback-material"),
                    Name = "local_source_mph_placeholder", SourceMaterial = 0, TexScale = 1f }
            }
        };

        float[][] positions = new float[collision.VertexCount][];
        float[] min = { float.MaxValue, float.MaxValue, float.MaxValue };
        float[] max = { float.MinValue, float.MinValue, float.MinValue };
        for (int v = 0; v < positions.Length; v++)
        {
            int at = v * 3;
            float[] point = ToPrime(collision.Vertices[at],
                collision.Vertices[at + 1], collision.Vertices[at + 2]);
            if (point.Any(x => !float.IsFinite(x) || Math.Abs(x) >= 524288))
                throw new InvalidDataException("Spires coordinates exceed Project Prime limits.");
            positions[v] = point;
            for (int c = 0; c < 3; c++)
            {
                min[c] = Math.Min(min[c], point[c]);
                max[c] = Math.Max(max[c], point[c]);
            }
        }

        // Bucket by collision surface so terrain and beam-through behavior do
        // not silently change when polygons are compiled into MapMesh chunks.
        var groups = new Dictionary<(string Terrain, bool IgnoreBeams), List<int>>();
        for (int tri = 0; tri < collision.TriangleCount; tri++)
        {
            ulong flags = collision.SurfaceFlags[tri];
            var kind = (Terrain(flags), (flags & EchoesCollisionSource.ShootThrough) != 0);
            if (!groups.TryGetValue(kind, out var bucket))
                groups[kind] = bucket = new List<int>();
            bucket.Add(tri);
        }

        int segment = 0, keptFaces = 0;
        foreach (var pair in groups.OrderBy(g => g.Key.Terrain, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.IgnoreBeams))
        {
            var triangles = pair.Value;
            for (int at = 0; at < triangles.Count; at += MaxFacesPerMesh)
            {
                var mesh = new MapMesh
                {
                    Id = StableId(fingerprint + "/geometry/" + segment),
                    Label = "Echoes collision visual proxy " + segment,
                    Layer = "Local Echoes collision",
                    Material = 0,
                    Terrain = pair.Key.Terrain,
                    IgnoreBeams = pair.Key.IgnoreBeams,
                    Solid = true,
                    Shade = 1f,
                    CollisionOnly = false
                };
                var index = new Dictionary<int, int>();
                int end = Math.Min(at + MaxFacesPerMesh, triangles.Count);
                for (int i = at; i < end; i++)
                {
                    int tri = triangles[i];
                    int[] ring = new int[3];
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int source = collision.TriangleVertices[tri * 3 + corner];
                        if ((uint)source >= positions.Length)
                            throw new InvalidDataException("Invalid Spires vertex reference.");
                        if (!index.TryGetValue(source, out int local))
                        {
                            local = mesh.Vertices.Count;
                            mesh.Vertices.Add(positions[source]);
                            index.Add(source, local);
                        }
                        ring[corner] = local;
                    }
                    float[] a = positions[collision.TriangleVertices[tri * 3]],
                        b = positions[collision.TriangleVertices[tri * 3 + 1]],
                        c = positions[collision.TriangleVertices[tri * 3 + 2]];
                    // MREA may retain degenerate surfaces. The collision
                    // engine cannot use a zero normal, so drop them explicitly.
                    float ax = b[0] - a[0], ay = b[1] - a[1], az = b[2] - a[2];
                    float bx = c[0] - a[0], by = c[1] - a[1], bz = c[2] - a[2];
                    float nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                    if (nx * nx + ny * ny + nz * nz < 1e-10f) continue;
                    mesh.Faces.Add(ring);
                    keptFaces++;
                }
                if (mesh.Faces.Count > 0) definition.Geometry.Add(mesh);
                segment++;
            }
        }
        if (keptFaces == 0)
            throw new InvalidDataException("Echoes arena has no usable collision triangles.");

        // The visual model and collision must share the same source coordinate
        // transform. Per-face surfacing is kept even for this graybox so ice
        // friction and beam-through flags are not flattened.
        float extent = max.Concat(min).Max(MathF.Abs);
        int scaleFactor = 4;
        while (extent >= 8f * MathF.Pow(2, scaleFactor))
        {
            if (++scaleFactor > 16) throw new InvalidDataException("Echoes arena fixed-point scale exceeded.");
        }
        definition.ScaleFactor = scaleFactor;
        definition.KillHeight = min[1] - 5;
        float dx = max[0] - min[0], dy = max[1] - min[1], dz = max[2] - min[2];
        definition.FarClip = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy + dz * dz) * 2, 350f, 50000f);

        var occupied = new List<float[]>();
        foreach (EchoesScriptObject actor in actors)
        {
            if (!actor.Active || !actor.HasTransform) continue;
            float[] place = ToPrime(actor.Transform[0], actor.Transform[1], actor.Transform[2]);
            if (!place.All(float.IsFinite) || occupied.Any(p =>
                Square(p[0] - place[0]) + Square(p[1] - place[1]) + Square(p[2] - place[2]) < .25f))
                continue;
            if (actor.Type == "SPWN")
            {
                occupied.Add(place);
                float yaw = (actor.Transform[5] + 180f) % 360f;
                if (yaw < 0) yaw += 360f;
                definition.Spawns.Add(new MapSpawn
                {
                    Id = StableId(fingerprint + "/spawn/" + actor.Id),
                    Position = place, Yaw = yaw, Team = -1
                });
            }
            else if (actor.Type == "PCKP")
            {
                string? item = actor.PickupKind switch
                {
                    0x29 => actor.PickupAmount >= 30 ? "HealthMedium" : "HealthSmall",
                    0x2c => "MissileSmall",
                    0x39 => "Cloak",
                    0x3a => "DoubleDamage",
                    _ => null
                };
                if (item == null) continue;
                occupied.Add(place);
                definition.Items.Add(new MapItem
                {
                    Id = StableId(fingerprint + "/pickup/" + actor.Id),
                    Position = place,
                    Type = item,
                    HasBase = false,
                    SpawnInterval = (ushort)(actor.PickupKind == 0x3a ? 1800 : 450)
                });
            }
        }
        if (definition.Spawns.Count < 2)
            throw new InvalidDataException(arena.Name + " requires at least two imported player spawns.");

        return definition;
    }

    private static float Square(float x) => x * x;

    private static float[] ToPrime(float x, float y, float z) =>
        new[] { x * UnitsPerEchoesMetre, z * UnitsPerEchoesMetre, -y * UnitsPerEchoesMetre };

    private static string Terrain(ulong flags)
    {
        // Echoes surface category bits match the documented 64-bit flags.
        // Phazon is rock terrain in the upstream conversion, not automatic
        // damage. Damage volumes remain future authoritative entity work.
        if ((flags & (1UL << 4)) != 0) return "Ice";
        if ((flags & (1UL << 10)) != 0) return "Snow";
        if ((flags & (1UL << 17)) != 0) return "Sand";
        if ((flags & ((1UL << 1) | (1UL << 3) | (1UL << 2) | (1UL << 7) |
            (1UL << 8) | (1UL << 13) | (1UL << 16) | (1UL << 22) | (1UL << 23))) != 0)
            return "Rock";
        return "Metal";
    }

    private static Guid StableId(string subject)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(subject));
        return new Guid(digest.AsSpan(0, 16));
    }
}
