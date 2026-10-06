using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public enum MapProportionalFalloff { Linear, Smooth, Sharp, Constant }
[JsonPolymorphic(TypeDiscriminatorPropertyName = "modifier")]
[JsonDerivedType(typeof(MapMirrorModifier), "mirror")]
[JsonDerivedType(typeof(MapArrayModifier), "array")]
public abstract record MapModelModifier(bool Enabled);
public sealed record MapMirrorModifier(int Axis, float Offset = 0, bool WeldPlane = true, bool IsEnabled = true)
    : MapModelModifier(IsEnabled);
public sealed record MapArrayModifier(int Count, [property: JsonConverter(typeof(MapModifierVectorConverter))] Vector3 Offset, bool IsEnabled = true)
    : MapModelModifier(IsEnabled);

public sealed class MapMeshModifierState
{
    public MapMesh Source { get; set; } = new();
    public List<MapModelModifier> Modifiers { get; set; } = new();
}

/// <summary>Canonical project JSON uses explicit finite triples for modifier vectors.</summary>
public sealed class MapModifierVectorConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("A modifier translation must be a finite XYZ triple.");
        float[] values = new float[3];
        for (int index = 0; index < values.Length; index++)
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetSingle(out values[index]) || !float.IsFinite(values[index]))
                throw new JsonException("A modifier translation must be a finite XYZ triple.");
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("A modifier translation must have three coordinates.");
        return new(values[0], values[1], values[2]);
    }
    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray(); writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y); writer.WriteNumberValue(value.Z); writer.WriteEndArray();
    }
}

/// <summary>Detached authoring operations. A caller commits the returned mesh as one document command.</summary>
public static class MapModelingEnhancements
{
    private const float Epsilon = 1e-5f;

    public static MapMesh ProportionalMove(MapMesh source, IEnumerable<int> vertices, Vector3 worldDelta,
        float worldRadius, MapProportionalFalloff falloff = MapProportionalFalloff.Smooth, bool connectedOnly = true, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (!Finite(worldDelta) || !float.IsFinite(worldRadius) || worldRadius <= 0 || !Enum.IsDefined(falloff))
            throw new ArgumentException("Proportional movement requires a finite delta and positive radius.");
        int[] selected = vertices.Distinct().Order().ToArray();
        if (selected.Length == 0 || selected.Any(index => (uint)index >= source.Vertices.Count))
            throw new ArgumentException("Select existing mesh vertices.");
        var chosen = selected.ToHashSet();
        var topology = new MapMeshTopology(source);
        var eligible = connectedOnly ? ConnectedVertices(topology, chosen, cancellation: cancellation) : Enumerable.Range(0, source.Vertices.Count).ToHashSet();
        Vector3[] positions = Enumerable.Range(0, source.Vertices.Count).Select(index => MapMeshEditing.VertexWorld(source, index)).ToArray();
        (long X, long Y, long Z) Cell(Vector3 point)
        {
            long Axis(float value)
            {
                double cell = Math.Floor((double)value / worldRadius);
                if (!double.IsFinite(cell) || cell < long.MinValue / 2d || cell > long.MaxValue / 2d)
                    throw new ArgumentException("The proportional radius is too small for these world coordinates.");
                return (long)cell;
            }
            return (Axis(point.X), Axis(point.Y), Axis(point.Z));
        }
        var seeds = selected.GroupBy(index => Cell(positions[index])).ToDictionary(group => group.Key,
            group => group.Select(index => positions[index]).ToArray());
        var result = Copy(source, cancellation: cancellation);
        foreach (int index in eligible)
        {
            cancellation.ThrowIfCancellationRequested();
            float weight = 1;
            if (!chosen.Contains(index))
            {
                var cell = Cell(positions[index]); double nearest = worldRadius;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (seeds.TryGetValue((cell.X + x, cell.Y + y, cell.Z + z), out var nearby))
                        foreach (var point in nearby)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            nearest = Math.Min(nearest, Vector3.Distance(positions[index], point));
                        }
                float t = 1 - (float)(nearest / worldRadius);
                if (t <= 0) continue;
                weight = falloff switch
                {
                    MapProportionalFalloff.Linear => t,
                    MapProportionalFalloff.Smooth => t * t * (3 - 2 * t),
                    MapProportionalFalloff.Sharp => t * t,
                    MapProportionalFalloff.Constant => 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(falloff))
                };
            }
            MapMeshEditing.SetVertexWorld(result, index, positions[index] + worldDelta * weight);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    /// <summary>Splits a complete quad ring; poles/nonquad continuation and contradictory orientation are rejected.</summary>
    public static MapMesh LoopCut(MapMesh source, MapEdge start, float percentage = .5f, int cuts = 1, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (!float.IsFinite(percentage) || percentage <= Epsilon || percentage >= 1 - Epsilon || cuts is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(percentage));
        var topology = new MapMeshTopology(source);
        if (topology.Uses(start).Count == 0) throw new InvalidOperationException("Select an existing edge for the quad ring.");
        var directions = new Dictionary<MapEdge, float> { [start] = percentage };
        var faces = new Dictionary<int, int>();
        var pending = new Queue<MapEdge>(); pending.Enqueue(start);
        while (pending.TryDequeue(out var edge))
            foreach (var use in topology.Uses(edge))
            {
                cancellation.ThrowIfCancellationRequested();
                var ring = source.Faces[use.Face];
                if (ring.Length != 4) throw new InvalidOperationException("Loop cut requires an uninterrupted quad strip; triangulated faces and poles stop the ring.");
                int corner = use.Corner;
                var opposite = new MapEdge(ring[(corner + 2) % 4], ring[(corner + 3) % 4]);
                float along = use.From == edge.A ? directions[edge] : 1 - directions[edge];
                float oppositeFraction = ring[(corner + 3) % 4] == opposite.A ? along : 1 - along;
                if (directions.TryGetValue(opposite, out float existing))
                {
                    if (cuts == 1 && MathF.Abs(existing - oppositeFraction) > Epsilon)
                        throw new InvalidOperationException("This quad ring twists the cut orientation. Use an even midpoint cut or repair its topology.");
                }
                else { directions[opposite] = oppositeFraction; pending.Enqueue(opposite); }
                faces.TryAdd(use.Face, corner);
            }
        var result = Copy(source, cancellation: cancellation);
        var inserted = SplitEdges(result, directions.ToDictionary(pair => pair.Key,
            pair => cuts == 1 ? new[] { pair.Value } : Enumerable.Range(1, cuts).Select(index => (float)index / (cuts + 1)).ToArray()), cancellation: cancellation);
        foreach (var pair in faces.OrderByDescending(pair => pair.Key))
        {
            cancellation.ThrowIfCancellationRequested();
            int face = pair.Key, corner = pair.Value; var original = source.Faces[face];
            int a = original[corner], b = original[(corner + 1) % 4], c = original[(corner + 2) % 4], d = original[(corner + 3) % 4];
            int[] Along(MapEdge edge, int from) => from == edge.A ? inserted[edge] : inserted[edge].Reverse().ToArray();
            var top = new[] { a }.Concat(Along(new(a, b), a)).Append(b).ToArray();
            var bottom = new[] { d }.Concat(Along(new(d, c), d)).Append(c).ToArray();
            var sourceUv = source.FaceTexcoords.Count > face ? source.FaceTexcoords[face] : null;
            float fraction = a == new MapEdge(a, b).A ? directions[new(a, b)] : 1 - directions[new(a, b)];
            float Fraction(int index) => index == 0 ? 0 : index == cuts + 1 ? 1 : cuts == 1 ? fraction : (float)index / (cuts + 1);
            float[] Uv(int from, int to, float t) => LerpUv(sourceUv![from], sourceUv[to], t);
            int material = Material(source, face); var projection = result.FaceUv.GetValueOrDefault(face);
            RemoveFace(result, face, cancellation: cancellation);
            for (int strip = 0; strip <= cuts; strip++)
                AddFace(result, new[] { top[strip], top[strip + 1], bottom[strip + 1], bottom[strip] }, material,
                    sourceUv == null ? null : new[] { Uv(corner, (corner + 1) % 4, Fraction(strip)),
                        Uv(corner, (corner + 1) % 4, Fraction(strip + 1)),
                        Uv((corner + 3) % 4, (corner + 2) % 4, Fraction(strip + 1)),
                        Uv((corner + 3) % 4, (corner + 2) % 4, Fraction(strip)) }, projection, cancellation: cancellation);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    /// <summary>Inserts a world-plane knife cut without discarding either side. Shared edges retain one vertex.</summary>
    public static MapMesh KnifeCut(MapMesh source, Vector3 worldNormal, float worldDistance, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (!Finite(worldNormal) || !float.IsFinite(worldDistance) || worldNormal.LengthSquared() < Epsilon * Epsilon)
            throw new ArgumentException("The knife requires a finite plane normal and distance.");
        float length = worldNormal.Length(); worldNormal /= length; worldDistance /= length;
        var result = Copy(source, cancellation: cancellation);
        float Distance(int index) => Vector3.Dot(MapMeshEditing.VertexWorld(result, index), worldNormal) - worldDistance;
        // Triangulate concave crossing polygons first; otherwise clipping can yield disconnected rings.
        var concave = Enumerable.Range(0, result.Faces.Count).Where(face =>
            result.Faces[face].Any(index => Distance(index) > Epsilon)
            && result.Faces[face].Any(index => Distance(index) < -Epsilon)
            && (!Convex(result, result.Faces[face], cancellation: cancellation) || result.Faces[face].Length > 30)).ToArray();
        if (concave.Length > 0) MapMeshEditing.Triangulate(result, concave);
        var topology = new MapMeshTopology(result);
        var crossing = topology.Edges.Where(edge => (Distance(edge.A) > Epsilon && Distance(edge.B) < -Epsilon)
            || (Distance(edge.A) < -Epsilon && Distance(edge.B) > Epsilon)).ToArray();
        SplitEdges(result, crossing.ToDictionary(edge => edge,
            edge => new[] { Distance(edge.A) / (Distance(edge.A) - Distance(edge.B)) }), cancellation: cancellation);
        int splits = 0;
        for (int face = result.Faces.Count - 1; face >= 0; face--)
        {
            cancellation.ThrowIfCancellationRequested();
            var ring = result.Faces[face];
            if (!ring.Any(index => Distance(index) > Epsilon) || !ring.Any(index => Distance(index) < -Epsilon)) continue;
            var positive = Enumerable.Range(0, ring.Length).Where(corner => Distance(ring[corner]) >= -Epsilon).ToArray();
            var negative = Enumerable.Range(0, ring.Length).Where(corner => Distance(ring[corner]) <= Epsilon).ToArray();
            if (positive.Length < 3 || negative.Length < 3 || ring.Count(index => MathF.Abs(Distance(index)) <= Epsilon) != 2)
                throw new InvalidOperationException("The plane creates an ambiguous or degenerate polygon cut. Move it away from a coplanar edge.");
            var uv = result.FaceTexcoords[face]; var projection = result.FaceUv.GetValueOrDefault(face);
            int material = Material(result, face); RemoveFace(result, face, cancellation: cancellation);
            foreach (var corners in new[] { positive, negative })
                AddFace(result, corners.Select(corner => ring[corner]).ToArray(), material,
                    uv == null ? null : corners.Select(corner => (float[])uv[corner].Clone()).ToArray(), projection, cancellation: cancellation);
            splits++;
        }
        if (splits == 0) throw new InvalidOperationException("The knife plane does not cross any mesh faces.");
        return Finish(source, result, cancellation: cancellation);
    }

    public static MapMesh Bridge(MapMesh source, IEnumerable<MapEdge> edges, int segments = 1, int twist = 0, int? material = null, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (segments is < 1 or > 32 || material < 0) throw new ArgumentOutOfRangeException(nameof(segments));
        var loops = Boundaries(source, edges, cancellation: cancellation);
        if (loops.Length != 2 || loops[0].Length != loops[1].Length || loops[0].Intersect(loops[1]).Any())
            throw new InvalidOperationException("Bridge requires two disjoint complete boundary loops with equal vertex counts.");
        int[] first = loops[0], second = loops[1].Reverse().ToArray(); int count = first.Length;
        int best = Enumerable.Range(0, count).OrderBy(offset => Enumerable.Range(0, count)
            .Sum(index => (double)Vector3.DistanceSquared(Point(source, first[index], cancellation: cancellation), Point(source, second[(index + offset) % count], cancellation: cancellation)))).First();
        int rotation = ((best + twist) % count + count) % count;
        second = Enumerable.Range(0, count).Select(index => second[(index + rotation) % count]).ToArray();
        var result = Copy(source, cancellation: cancellation); var rows = new List<int[]> { first };
        for (int segment = 1; segment < segments; segment++)
            rows.Add(Enumerable.Range(0, count).Select(index => AddVertex(result,
                Vector3.Lerp(Point(source, first[index], cancellation: cancellation), Point(source, second[index], cancellation: cancellation), (float)segment / segments), cancellation: cancellation)).ToArray());
        rows.Add(second);
        for (int segment = 0; segment < segments; segment++) for (int index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int next = (index + 1) % count;
            AddFace(result, new[] { rows[segment][index], rows[segment][next], rows[segment + 1][next], rows[segment + 1][index] },
                material ?? source.Material, new[] { new[] { (float)index / count, (float)segment / segments },
                    new[] { (float)(index + 1) / count, (float)segment / segments },
                    new[] { (float)(index + 1) / count, (float)(segment + 1) / segments },
                    new[] { (float)index / count, (float)(segment + 1) / segments } }, cancellation: cancellation);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    /// <summary>Coons quad patch over one planar convex even boundary; folded/branched boundaries are rejected.</summary>
    public static MapMesh GridFill(MapMesh source, IEnumerable<MapEdge> edges, int columns = 1, int? material = null, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation); var loops = Boundaries(source, edges, cancellation: cancellation);
        if (loops.Length != 1) throw new InvalidOperationException("Grid fill requires one complete boundary loop.");
        int[] loop = loops[0]; int rows = loop.Length / 2 - columns;
        if (loop.Length % 2 != 0 || columns < 1 || rows < 1 || material < 0)
            throw new InvalidOperationException("Grid fill requires an even boundary with at least one row and column.");
        var normal = Normal(source, loop, cancellation: cancellation);
        if (!Convex(source, loop, cancellation: cancellation) || loop.Any(index => MathF.Abs(Vector3.Dot(Point(source, index, cancellation: cancellation) - Point(source, loop[0], cancellation: cancellation), normal)) > .001f))
            throw new InvalidOperationException("Grid fill requires a planar convex boundary. Triangulate a concave cap with Fill instead.");
        var result = Copy(source, cancellation: cancellation); var grid = new int[rows + 1, columns + 1];
        for (int x = 0; x <= columns; x++) grid[0, x] = loop[x];
        for (int y = 1; y <= rows; y++) grid[y, columns] = loop[columns + y];
        for (int x = columns - 1; x >= 0; x--) grid[rows, x] = loop[columns + rows + columns - x];
        for (int y = rows - 1; y > 0; y--) grid[y, 0] = loop[2 * columns + rows + rows - y];
        Vector3 a = Point(source, grid[0, 0], cancellation: cancellation), b = Point(source, grid[0, columns], cancellation: cancellation),
            c = Point(source, grid[rows, columns], cancellation: cancellation), d = Point(source, grid[rows, 0], cancellation: cancellation);
        for (int y = 1; y < rows; y++) for (int x = 1; x < columns; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            float u = (float)x / columns, v = (float)y / rows;
            var surface = Vector3.Lerp(Point(source, grid[0, x], cancellation: cancellation), Point(source, grid[rows, x], cancellation: cancellation), v)
                + Vector3.Lerp(Point(source, grid[y, 0], cancellation: cancellation), Point(source, grid[y, columns], cancellation: cancellation), u)
                - Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v);
            grid[y, x] = AddVertex(result, surface, cancellation: cancellation);
        }
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            int[] face = { grid[y, x], grid[y, x + 1], grid[y + 1, x + 1], grid[y + 1, x] };
            if (Vector3.Dot(Normal(result, face, cancellation: cancellation), normal) <= Epsilon)
                throw new InvalidOperationException("The boundary produces a folded grid cell. Change grid dimensions or repair the boundary.");
            AddFace(result, face, material ?? source.Material, new[] { new[] { (float)x / columns, (float)y / rows },
                new[] { (float)(x + 1) / columns, (float)y / rows }, new[] { (float)(x + 1) / columns, (float)(y + 1) / rows },
                new[] { (float)x / columns, (float)(y + 1) / rows } }, cancellation: cancellation);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    /// <summary>Pure evaluation leaves both the serialized source mesh and the modifier description unchanged.</summary>
    public static MapMesh EvaluateModifiers(MapMesh source, IReadOnlyList<MapModelModifier> modifiers, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (modifiers.Count > 32) throw new ArgumentException("A mesh supports at most 32 modifiers.");
        var result = Copy(source, cancellation: cancellation);
        foreach (var modifier in modifiers)
        {
            cancellation.ThrowIfCancellationRequested();
            if (modifier == null) throw new ArgumentException("A modifier is missing.");
            if (!modifier.Enabled) continue;
            result = modifier switch
            {
                MapMirrorModifier mirror => Mirror(result, mirror, cancellation: cancellation),
                MapArrayModifier array => Array(result, array, cancellation: cancellation),
                _ => throw new ArgumentException("The modifier kind is unsupported.")
            };
        }
        return Finish(source, result, cancellation: cancellation);
    }

    public static MapMesh WithModifierStack(MapMesh source, IReadOnlyList<MapModelModifier> modifiers, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        // The project stores resolved runtime geometry plus one self-contained
        // authoring source. It does not create an unbounded provenance chain.
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(modifiers);
        if (modifiers.Count > 32) throw new ArgumentException("A mesh supports at most 32 modifiers.");
        var original = AuthoringSource(source.ModifierSource?.Source ?? source, source, cancellation: cancellation);
        var descriptions = JsonSerializer.Deserialize<List<MapModelModifier>>(JsonSerializer.Serialize(modifiers))
            ?? throw new InvalidOperationException("The modifier stack is missing.");
        var resolved = EvaluateModifiers(original, descriptions, cancellation: cancellation);
        resolved.ModifierSource = new() { Source = original, Modifiers = descriptions };
        cancellation.ThrowIfCancellationRequested();
        return resolved;
    }

    public static MapMesh BakeModifierStack(MapMesh source, CancellationToken cancellation = default) => Finish(source, AuthoringSource(source, cancellation: cancellation), cancellation: cancellation);

    public static void EnsureCanEditElements(MapMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.ModifierSource != null)
            throw new InvalidOperationException("Bake the modifier stack before editing its resolved topology or face UVs.");
    }

    /// <summary>Offsets one planar convex region by a constant world-space distance.</summary>
    public static MapMesh InsetRegionWidth(MapMesh source, IEnumerable<int> faces, float width, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (!float.IsFinite(width) || width <= Epsilon) throw new ArgumentOutOfRangeException(nameof(width));
        var selected = faces.Distinct().ToHashSet();
        if (selected.Count == 0 || selected.Any(face => (uint)face >= source.Faces.Count)) throw new ArgumentException("Select existing faces.");
        var topology = new MapMeshTopology(source);
        var boundary = selected.SelectMany(topology.EdgesOfFace).Distinct()
            .SelectMany(edge => topology.Uses(edge).Where(use => selected.Contains(use.Face)).ToArray() is { Length: 1 } uses ? uses : System.Array.Empty<MapMeshTopology.HalfEdge>()).ToArray();
        if (boundary.Length < 3 || boundary.GroupBy(use => use.From).Any(group => group.Count() != 1)
            || boundary.GroupBy(use => use.To).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Inset requires a simple open region boundary.");
        var next = boundary.ToDictionary(use => use.From, use => use.To);
        var ring = new List<int>(); int start = boundary[0].From, current = start;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            if (!next.ContainsKey(current) || ring.Contains(current)) throw new InvalidOperationException("Inset boundary is branched.");
            ring.Add(current); current = next[current];
        } while (current != start);
        if (ring.Count != boundary.Length) throw new InvalidOperationException("Inset supports one connected region without holes.");
        var vertices = selected.SelectMany(face => source.Faces[face]).Distinct().ToArray();
        var world = vertices.ToDictionary(vertex => vertex, vertex => MapMeshEditing.VertexWorld(source, vertex));
        Vector3 normal = Vector3.Zero;
        for (int index = 0; index < ring.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            normal += Vector3.Cross(world[ring[index]], world[ring[(index + 1) % ring.Count]]);
        }
        if (normal.LengthSquared() < Epsilon * Epsilon) throw new InvalidOperationException("Inset region has zero area.");
        normal = Vector3.Normalize(normal);
        if (vertices.Any(vertex => MathF.Abs(Vector3.Dot(world[vertex] - world[start], normal)) > .001f))
            throw new InvalidOperationException("Constant-width inset requires a planar region.");
        var offsets = vertices.ToDictionary(vertex => vertex, _ => Vector3.Zero);
        var boundaryVertices = ring.ToHashSet();
        for (int index = 0; index < ring.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int vertex = ring[index]; Vector3 previous = world[ring[(index + ring.Count - 1) % ring.Count]], point = world[vertex], following = world[ring[(index + 1) % ring.Count]];
            var incoming = Vector3.Normalize(point - previous); var outgoing = Vector3.Normalize(following - point);
            if (Vector3.Dot(Vector3.Cross(incoming, outgoing), normal) < -Epsilon)
                throw new InvalidOperationException("Constant-width inset requires a convex boundary.");
            var a = Vector3.Cross(normal, incoming); var b = Vector3.Cross(normal, outgoing);
            float denominator = 1 + Vector3.Dot(a, b);
            if (denominator <= Epsilon) throw new InvalidOperationException("Inset boundary folds back on itself.");
            offsets[vertex] = (a + b) * (width / denominator);
        }
        foreach (int vertex in ring)
            for (int index = 0; index < ring.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var a = world[ring[index]]; var b = world[ring[(index + 1) % ring.Count]];
                var inward = Vector3.Cross(normal, Vector3.Normalize(b - a));
                if (Vector3.Dot(world[vertex] + offsets[vertex] - a, inward) < width - .0001f)
                    throw new InvalidOperationException("Inset width collapses or crosses the region boundary.");
            }
        var neighbors = selected.SelectMany(topology.EdgesOfFace).Distinct()
            .SelectMany(edge => new[] { (From: edge.A, To: edge.B), (From: edge.B, To: edge.A) })
            .GroupBy(edge => edge.From).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).Distinct().ToArray());
        int[] interior = vertices.Where(vertex => !boundaryVertices.Contains(vertex)).ToArray();
        bool converged = interior.Length == 0;
        for (int iteration = 0; iteration < 2048 && !converged; iteration++)
        {
            cancellation.ThrowIfCancellationRequested();
            float largest = 0; var update = new Dictionary<int, Vector3>();
            foreach (int vertex in interior)
            {
                cancellation.ThrowIfCancellationRequested();
                var value = neighbors[vertex].Aggregate(Vector3.Zero, (sum, neighbor) => sum + offsets[neighbor]) / neighbors[vertex].Length;
                largest = MathF.Max(largest, Vector3.Distance(value, offsets[vertex])); update[vertex] = value;
            }
            foreach (var pair in update) offsets[pair.Key] = pair.Value;
            converged = largest <= Epsilon;
        }
        if (!converged) throw new InvalidOperationException("Inset interior relaxation did not converge. Split the region into smaller selections.");
        var result = Copy(source, cancellation: cancellation); var remap = vertices.ToDictionary(vertex => vertex,
            vertex => AddVertex(result, MapMeshEditing.WorldToLocal(source, world[vertex] + offsets[vertex]), cancellation: cancellation));
        foreach (int face in selected)
        {
            cancellation.ThrowIfCancellationRequested();
            result.Faces[face] = source.Faces[face].Select(vertex => remap[vertex]).ToArray();
        }
        foreach (var edge in boundary)
            AddFace(result, new[] { edge.From, edge.To, remap[edge.To], remap[edge.From] }, Material(source, edge.Face), cancellation: cancellation);
        return Finish(source, result, cancellation: cancellation);
    }

    /// <summary>Chamfers a manifold edge; endpoint fans of any regular degree are clipped and capped.</summary>
    public static MapMesh BevelEdge(MapMesh source, MapEdge edge, float width, int segments = 1, int? material = null, CancellationToken cancellation = default)
    {
        CheckSource(source, cancellation: cancellation);
        if (!float.IsFinite(width) || width <= Epsilon || segments is < 1 or > 16 || material < 0) throw new ArgumentOutOfRangeException(nameof(width));
        var topology = new MapMeshTopology(source); var uses = topology.Uses(edge);
        if (uses.Count != 2 || uses[0].From == uses[1].From) throw new InvalidOperationException("Bevel requires a consistently wound manifold edge.");
        if (topology.BoundaryEdges().Any(boundary => boundary.A == edge.A || boundary.B == edge.A || boundary.A == edge.B || boundary.B == edge.B))
            throw new InvalidOperationException("Bevel endpoints must have closed face fans.");
        var cutEdges = topology.Edges.Where(candidate => candidate != edge
            && (candidate.A == edge.A || candidate.B == edge.A || candidate.A == edge.B || candidate.B == edge.B)).ToArray();
        var result = Copy(source, cancellation: cancellation); var cutVertices = new Dictionary<MapEdge, int>();
        var fractions = new Dictionary<MapEdge, float[]>();
        foreach (var candidate in cutEdges)
        {
            cancellation.ThrowIfCancellationRequested();
            int endpoint = candidate.A == edge.A || candidate.A == edge.B ? candidate.A : candidate.B;
            float length = Vector3.Distance(MapMeshEditing.VertexWorld(source, candidate.A), MapMeshEditing.VertexWorld(source, candidate.B));
            if (width >= length * .49f) throw new InvalidOperationException("Bevel width reaches another corner. Reduce its width.");
            float fraction = endpoint == candidate.A ? width / length : 1 - width / length;
            fractions[candidate] = new[] { fraction };
        }
        foreach (var pair in SplitEdges(result, fractions, cancellation: cancellation)) cutVertices[pair.Key] = pair.Value[0];
        int SideVertex(int side, int endpoint)
        {
            var ring = source.Faces[uses[side].Face]; int corner = System.Array.IndexOf(ring, endpoint);
            int neighbor = ring[(corner + 1) % ring.Length];
            if (neighbor == edge.A || neighbor == edge.B) neighbor = ring[(corner + ring.Length - 1) % ring.Length];
            return cutVertices[new(endpoint, neighbor)];
        }
        var rows = new List<int[]> { new[] { SideVertex(0, edge.A), SideVertex(0, edge.B) } };
        var last = new[] { SideVertex(1, edge.A), SideVertex(1, edge.B) };
        for (int segment = 1; segment < segments; segment++) rows.Add(new[] {
            AddVertex(result, Vector3.Lerp(Point(result, rows[0][0], cancellation: cancellation), Point(result, last[0], cancellation: cancellation), (float)segment / segments), cancellation: cancellation),
            AddVertex(result, Vector3.Lerp(Point(result, rows[0][1], cancellation: cancellation), Point(result, last[1], cancellation: cancellation), (float)segment / segments), cancellation: cancellation) });
        rows.Add(last);
        var affected = topology.FacesOfVertex(edge.A).Concat(topology.FacesOfVertex(edge.B)).Distinct().ToArray();
        foreach (int face in affected)
        {
            cancellation.ThrowIfCancellationRequested();
            int[] ring = result.Faces[face]; var uv = result.FaceTexcoords[face];
            int[] keep = Enumerable.Range(0, ring.Length).Where(corner => ring[corner] != edge.A && ring[corner] != edge.B).ToArray();
            result.Faces[face] = keep.Select(corner => ring[corner]).ToArray();
            result.FaceTexcoords[face] = uv == null ? null : keep.Select(corner => uv[corner]).ToArray();
        }
        // A trivalent endpoint has no cap area. Its one fan face must carry
        // intermediate strip corners instead of leaving a collinear T-junction.
        for (int endpoint = 0; endpoint < 2; endpoint++)
        {
            cancellation.ThrowIfCancellationRequested();
            int original = endpoint == 0 ? edge.A : edge.B;
            var fan = topology.FacesOfVertex(original).Where(face => uses.All(use => use.Face != face)).ToArray();
            if (fan.Length != 1 || segments == 1) continue;
            int face = fan[0]; var ring = result.Faces[face].ToList(); int[] chain = rows.Select(row => row[endpoint]).ToArray();
            int corner = ring.IndexOf(chain[0]);
            if (corner < 0) throw new InvalidOperationException("Bevel endpoint fan is invalid.");
            if (ring[(corner + 1) % ring.Count] != chain[^1]) { System.Array.Reverse(chain); corner = ring.IndexOf(chain[0]); }
            if (ring[(corner + 1) % ring.Count] != chain[^1]) throw new InvalidOperationException("Bevel endpoint fan cannot carry the strip.");
            var uv = result.FaceTexcoords[face];
            if (uv != null)
            {
                var coordinates = uv.ToList(); var firstUv = uv[corner]; var lastUv = uv[(corner + 1) % uv.Length];
                coordinates.InsertRange(corner + 1, Enumerable.Range(1, segments - 1).Select(index => LerpUv(firstUv, lastUv, (float)index / segments)));
                result.FaceTexcoords[face] = coordinates.ToArray();
            }
            ring.InsertRange(corner + 1, chain.Skip(1).Take(segments - 1)); result.Faces[face] = ring.ToArray();
        }
        for (int segment = 0; segment < segments; segment++)
        {
            cancellation.ThrowIfCancellationRequested();
            int[] strip = { rows[segment][1], rows[segment][0], rows[segment + 1][0], rows[segment + 1][1] };
            if (uses[0].From != edge.A) System.Array.Reverse(strip);
            AddFace(result, strip, material ?? Material(source, uses[0].Face), cancellation: cancellation);
        }
        var endpoints = cutVertices.Values.Concat(rows.SelectMany(row => row)).ToHashSet();
        var capLoops = new MapMeshTopology(result).OpenBoundaries().Where(loop => loop.All(endpoints.Contains)).ToArray();
        foreach (var cap in capLoops)
        {
            cancellation.ThrowIfCancellationRequested();
            if (cap.Length > 32) throw new InvalidOperationException("The beveled endpoint exceeds the polygon corner budget.");
            AddFace(result, cap, material ?? source.Material, cancellation: cancellation);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    private static MapMesh AuthoringSource(MapMesh mesh, MapMesh? header = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mesh);
        header ??= mesh;
        return Copy(new MapMesh
        {
            Id = header.Id, Label = header.Label, Transform = header.Transform, Material = header.Material,
            Solid = header.Solid, Damaging = header.Damaging, Terrain = header.Terrain, Shade = header.Shade,
            Uv = header.Uv, FaceUv = mesh.FaceUv, Hidden = header.Hidden, Locked = header.Locked, Layer = header.Layer,
            CollisionOnly = header.CollisionOnly, Slipperiness = header.Slipperiness, ReflectBeams = header.ReflectBeams,
            IgnorePlayers = header.IgnorePlayers, IgnoreBeams = header.IgnoreBeams, IgnoreScan = header.IgnoreScan,
            Vertices = mesh.Vertices, Faces = mesh.Faces, FaceMaterials = mesh.FaceMaterials, FaceTexcoords = mesh.FaceTexcoords
        }, cancellation: cancellation);
    }

    private static MapMesh Mirror(MapMesh source, MapMirrorModifier modifier, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (modifier.Axis is < 0 or > 2 || !float.IsFinite(modifier.Offset)) throw new ArgumentException("Mirror requires a valid local axis and plane offset.");
        float Distance(int index) => source.Vertices[index][modifier.Axis] - modifier.Offset;
        bool positive = source.Vertices.Select((_, index) => Distance(index)).Any(value => value > Epsilon);
        bool negative = source.Vertices.Select((_, index) => Distance(index)).Any(value => value < -Epsilon);
        if (positive == negative) throw new InvalidOperationException("The mirror source must lie on one side of the plane. Use Knife and separate geometry first.");
        var result = Copy(source, cancellation: cancellation); int[] remap = new int[source.Vertices.Count];
        for (int index = 0; index < source.Vertices.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (modifier.WeldPlane && MathF.Abs(Distance(index)) <= Epsilon) { remap[index] = index; continue; }
            Vector3 point = Point(source, index, cancellation: cancellation); point[modifier.Axis] = 2 * modifier.Offset - point[modifier.Axis];
            remap[index] = AddVertex(result, point, cancellation: cancellation);
        }
        var caps = new List<int>();
        for (int face = 0; face < source.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (modifier.WeldPlane && source.Faces[face].All(index => MathF.Abs(Distance(index)) <= Epsilon)) { caps.Add(face); continue; }
            var uv = result.FaceTexcoords[face];
            AddFace(result, source.Faces[face].Reverse().Select(index => remap[index]).ToArray(), Material(source, face),
                uv?.Reverse().Select(point => (float[])point.Clone()).ToArray(), result.FaceUv.GetValueOrDefault(face), cancellation: cancellation);
        }
        foreach (int face in caps.OrderDescending()) RemoveFace(result, face, cancellation: cancellation);
        return Finish(source, result, cancellation: cancellation);
    }

    private static MapMesh Array(MapMesh source, MapArrayModifier modifier, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (modifier.Count is < 1 or > 128 || !Finite(modifier.Offset)
            || (modifier.Count > 1 && modifier.Offset.LengthSquared() < Epsilon * Epsilon))
            throw new ArgumentException("Array requires 1–128 copies and a finite nonzero local offset.");
        if ((long)source.Vertices.Count * modifier.Count > 65535 || (long)source.Faces.Count * modifier.Count > 65535)
            throw new InvalidOperationException("Array would exceed the mesh vertex or face budget.");
        var result = Copy(source, cancellation: cancellation);
        for (int copy = 1; copy < modifier.Count; copy++)
        {
            cancellation.ThrowIfCancellationRequested();
            int offset = result.Vertices.Count;
            for (int vertex = 0; vertex < source.Vertices.Count; vertex++) AddVertex(result, Point(source, vertex, cancellation: cancellation) + modifier.Offset * copy, cancellation: cancellation);
            for (int face = 0; face < source.Faces.Count; face++)
                AddFace(result, source.Faces[face].Select(index => index + offset).ToArray(), Material(source, face),
                    result.FaceTexcoords[face]?.Select(point => (float[])point.Clone()).ToArray(), result.FaceUv.GetValueOrDefault(face), cancellation: cancellation);
        }
        return Finish(source, result, cancellation: cancellation);
    }

    private static HashSet<int> ConnectedVertices(MapMeshTopology topology, HashSet<int> selected, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var adjacency = topology.Edges.SelectMany(edge => new[] { (From: edge.A, To: edge.B), (From: edge.B, To: edge.A) })
            .GroupBy(edge => edge.From).ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToArray());
        var seen = selected.ToHashSet(); var queue = new Queue<int>(selected);
        while (queue.TryDequeue(out int vertex))
        {
            cancellation.ThrowIfCancellationRequested();
            if (adjacency.TryGetValue(vertex, out var neighbors))
                foreach (int neighbor in neighbors)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (seen.Add(neighbor)) queue.Enqueue(neighbor);
                }
        }
        return seen;
    }

    private static int[][] Boundaries(MapMesh mesh, IEnumerable<MapEdge> edges, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var selected = edges.ToHashSet(); var topology = new MapMeshTopology(mesh);
        if (selected.Count == 0 || selected.Any(edge => topology.Uses(edge).Count != 1))
            throw new InvalidOperationException("Select only complete open boundary edges.");
        var loops = topology.OpenBoundaries().Where(loop => Enumerable.Range(0, loop.Length)
            .All(index => selected.Contains(new(loop[index], loop[(index + 1) % loop.Length])))).ToArray();
        if (loops.Sum(loop => loop.Length) != selected.Count)
            throw new InvalidOperationException("The selection contains an incomplete or branched boundary.");
        return loops;
    }
    private static MapMesh Copy(MapMesh source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = MapMeshEditing.Clone(source);
        while (result.FaceMaterials.Count < result.Faces.Count) result.FaceMaterials.Add(result.Material);
        while (result.FaceTexcoords.Count < result.Faces.Count) result.FaceTexcoords.Add(null);
        cancellation.ThrowIfCancellationRequested();
        return result;
    }
    private static Dictionary<MapEdge, int[]> SplitEdges(MapMesh mesh, IReadOnlyDictionary<MapEdge, float[]> fractions, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        // All proposals share one edge table and one polygon pass. Rebuilding
        // whole-mesh adjacency for every cut edge makes large knife cuts quadratic.
        var inserted = new Dictionary<MapEdge, int[]>(fractions.Count);
        foreach (var pair in fractions)
        {
            cancellation.ThrowIfCancellationRequested();
            inserted[pair.Key] = pair.Value.Select(fraction =>
                AddVertex(mesh, Vector3.Lerp(Point(mesh, pair.Key.A, cancellation: cancellation), Point(mesh, pair.Key.B, cancellation: cancellation), fraction), cancellation: cancellation)).ToArray();
        }
        for (int face = 0; face < mesh.Faces.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            int[] original = mesh.Faces[face]; var uv = mesh.FaceTexcoords[face];
            var ring = new List<int>(original.Length); var coordinates = uv == null ? null : new List<float[]>();
            for (int corner = 0; corner < original.Length; corner++)
            {
                cancellation.ThrowIfCancellationRequested();
                int from = original[corner], to = original[(corner + 1) % original.Length];
                ring.Add(from); coordinates?.Add((float[])uv![corner].Clone());
                var edge = new MapEdge(from, to);
                if (!inserted.TryGetValue(edge, out var vertices)) continue;
                for (int index = 0; index < vertices.Length; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int position = from == edge.A ? index : vertices.Length - 1 - index;
                    ring.Add(vertices[position]);
                    if (coordinates != null)
                    {
                        float fraction = from == edge.A ? fractions[edge][position] : 1 - fractions[edge][position];
                        coordinates.Add(LerpUv(uv![corner], uv[(corner + 1) % uv.Length], fraction));
                    }
                }
            }
            mesh.Faces[face] = ring.ToArray(); mesh.FaceTexcoords[face] = coordinates?.ToArray();
        }
        return inserted;
    }
    private static void CheckSource(MapMesh mesh, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mesh); MapMeshEditing.CheckOperation(mesh);
        EnsureCanEditElements(mesh);
        if (mesh.FaceMaterials.Count > mesh.Faces.Count || mesh.FaceTexcoords.Count > mesh.Faces.Count
            || mesh.Material < 0 || mesh.FaceMaterials.Any(material => material < 0))
            throw new InvalidOperationException("Mesh face channels or materials are invalid.");
        var topology = new MapMeshTopology(mesh);
        if (topology.NonManifoldEdges().Length > 0 || topology.Edges.Any(edge => topology.Uses(edge) is { Count: 2 } uses && uses[0].From == uses[1].From))
            throw new InvalidOperationException("Repair non-manifold edges and inconsistent winding before modeling.");
        foreach (int vertex in mesh.Faces.SelectMany(face => face).Distinct())
        {
            cancellation.ThrowIfCancellationRequested();
            int[] incident = topology.FacesOfVertex(vertex);
            var seen = new HashSet<int> { incident[0] }; var queue = new Queue<int>(); queue.Enqueue(incident[0]);
            while (queue.TryDequeue(out int face))
            {
                cancellation.ThrowIfCancellationRequested();
                foreach (int adjacent in topology.EdgesOfFace(face).Where(edge => edge.A == vertex || edge.B == vertex).SelectMany(topology.AdjacentFaces))
                    if (seen.Add(adjacent)) queue.Enqueue(adjacent);
            }
            if (seen.Count != incident.Length) throw new InvalidOperationException("A vertex joins disconnected face fans. Split that non-manifold vertex before modeling.");
        }
        for (int face = 0; face < mesh.FaceTexcoords.Count; face++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (mesh.FaceTexcoords[face] is { } uv && (face >= mesh.Faces.Count || uv.Length != mesh.Faces[face].Length
                || uv.Any(point => point is not { Length: 2 } || point.Any(value => !float.IsFinite(value)))))
                throw new InvalidOperationException("Mesh corner UV channels are invalid.");
        }
        cancellation.ThrowIfCancellationRequested();
    }
    private static MapMesh Finish(MapMesh source, MapMesh result, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var nonplanar = MapMeshValidator.Validate(result).Where(problem => problem.Message.Contains("not planar", StringComparison.Ordinal))
            .Select(problem => problem.Face!.Value).ToArray();
        if (nonplanar.Length > 0) MapMeshEditing.Triangulate(result, nonplanar);
        CheckSource(result, cancellation: cancellation);
        if (new MapMeshTopology(source).BoundaryEdges().Length == 0 && new MapMeshTopology(result).BoundaryEdges().Length != 0)
            throw new InvalidOperationException("The operation would open a previously closed surface.");
        cancellation.ThrowIfCancellationRequested();
        return result;
    }
    private static int AddVertex(MapMesh mesh, Vector3 point, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Finite(point) || mesh.Vertices.Count >= 65535) throw new InvalidOperationException("The proposed mesh exceeds its finite vertex budget.");
        int index = mesh.Vertices.Count; mesh.Vertices.Add(new[] { point.X, point.Y, point.Z }); return index;
    }
    private static void AddFace(MapMesh mesh, int[] ring, int material, float[][]? uv = null, MapUv? projection = null, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (mesh.Faces.Count >= 65535) throw new InvalidOperationException("The proposed mesh exceeds its face budget.");
        if (projection != null) mesh.FaceUv[mesh.Faces.Count] = projection;
        mesh.Faces.Add(ring); mesh.FaceMaterials.Add(material); mesh.FaceTexcoords.Add(uv);
    }
    private static void RemoveFace(MapMesh mesh, int face, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        mesh.Faces.RemoveAt(face); mesh.FaceMaterials.RemoveAt(face); mesh.FaceTexcoords.RemoveAt(face);
        mesh.FaceUv = mesh.FaceUv.Where(pair => pair.Key != face).ToDictionary(pair => pair.Key > face ? pair.Key - 1 : pair.Key, pair => pair.Value);
    }
    private static int Material(MapMesh mesh, int face) => face < mesh.FaceMaterials.Count ? mesh.FaceMaterials[face] : mesh.Material;
    private static float[] LerpUv(float[] a, float[] b, float amount) => new[] { a[0] + (b[0] - a[0]) * amount, a[1] + (b[1] - a[1]) * amount };
    private static Vector3 Point(MapMesh mesh, int index, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        return new(mesh.Vertices[index][0], mesh.Vertices[index][1], mesh.Vertices[index][2]);
    }
    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    private static Vector3 Normal(MapMesh mesh, int[] ring, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Vector3 normal = Vector3.Zero;
        for (int index = 0; index < ring.Length; index++) normal += Vector3.Cross(Point(mesh, ring[index], cancellation: cancellation), Point(mesh, ring[(index + 1) % ring.Length], cancellation: cancellation));
        if (normal.LengthSquared() < Epsilon * Epsilon) throw new InvalidOperationException("The proposed polygon has zero area.");
        return Vector3.Normalize(normal);
    }
    private static bool Convex(MapMesh mesh, int[] ring, CancellationToken cancellation = default)
    {
        var normal = Normal(mesh, ring, cancellation: cancellation);
        return Enumerable.Range(0, ring.Length).All(index => Vector3.Dot(Vector3.Cross(
            Point(mesh, ring[(index + 1) % ring.Length], cancellation: cancellation) - Point(mesh, ring[index], cancellation: cancellation),
            Point(mesh, ring[(index + 2) % ring.Length], cancellation: cancellation) - Point(mesh, ring[(index + 1) % ring.Length], cancellation: cancellation)), normal) >= -Epsilon);
    }
}
