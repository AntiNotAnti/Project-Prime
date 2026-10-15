using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace ProjectPrime.Studio.Map;

/// <summary>
/// Asset-free, one-request process boundary for the native editor. Geometry stays in
/// Prime coordinates and all modeling is performed by the canonical editor library.
/// No file, installation lease, UI toolkit or game asset initialization is needed.
/// </summary>
public static class RadiantMeshWorker
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private const int MaxElements = 65_535;
    private const int MaxCorners = 1_048_576;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 64,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Run()
    {
        // Use the stream directly so incidental library diagnostics can never corrupt
        // the protocol. The complete reply is serialized before writing any bytes.
        using Stream output = Console.OpenStandardOutput();
        Console.SetOut(TextWriter.Null);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        Reply reply;
        try
        {
            byte[] input = ReadRequest(Console.OpenStandardInput(), lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token).GetAwaiter().GetResult();
            reply = Task.Run(() => Execute(input), lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            string error = ex is OperationCanceledException or TimeoutException
                ? "Mesh worker timed out or was cancelled." : ex.Message;
            reply = new() { Success = false, Error = error.Length > 2048 ? error[..2048] : error };
        }
        finally { Console.CancelKeyPress -= cancel; }
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(reply, Json);
            if (bytes.Length > MaxBytes)
            {
                reply = new() { Success = false, Error = "Mesh result exceeds the 32 MiB response budget." };
                bytes = JsonSerializer.SerializeToUtf8Bytes(reply, Json);
            }
            output.WriteAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            output.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
        { return 1; } // Parent closed its pipe, stopped reading, or cancelled.
        return reply.Success ? 0 : 1;
    }

    private static async Task<byte[]> ReadRequest(Stream input, CancellationToken cancellation)
    {
        using (input)
        using (var buffer = new MemoryStream())
        {
            byte[] chunk = new byte[16 * 1024];
            int length;
            while ((length = await input.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + length > MaxBytes) throw new InvalidDataException("Mesh request exceeds 32 MiB.");
                buffer.Write(chunk, 0, length);
            }
            if (buffer.Length == 0) throw new InvalidDataException("Mesh request is empty.");
            return buffer.ToArray();
        }
    }

    private static Reply Execute(byte[] input)
    {
        using JsonDocument document = JsonDocument.Parse(input, new() { MaxDepth = 64 });
        CheckJson(document.RootElement);
        var request = document.RootElement.Deserialize<Request>(Json) ?? throw new InvalidDataException("Mesh request is missing.");
        if (request.Schema != 1 || request.Payload is not { Schema: 1, Units: 16 })
            throw new InvalidDataException("Mesh request and payload require schema 1 and units 16.");
        Payload payload = request.Payload;
        JsonElement rawPayload = document.RootElement.GetProperty("payload");
        if (System.Text.Encoding.UTF8.GetByteCount(rawPayload.GetRawText()) > PrimeMeshInterchange.MaximumDecodedBytes)
            throw new InvalidDataException("Canonical mesh payload exceeds 16 MiB.");
        RequireMeshIdentity(rawPayload.GetProperty("mesh"));
        MapMesh mesh = payload.Mesh as MapMesh ?? throw new InvalidDataException("Payload must contain a canonical mesh.");
        if (payload.Materials is not { Length: > 0 and <= 65_535 }) throw new InvalidDataException("Supply 1–65,535 material definitions.");
        foreach (JsonElement material in payload.Materials) CheckMaterial(material);
        string operation = Normalize(request.Operation);
        bool analyze = operation is "analyzemesh" or "meshissues";
        bool snap = operation == "snaptosurface";
        bool repair = operation is "weld" or "weldnearbyvertices" or "mergebydistance";
        CheckMesh(mesh, payload.Materials.Length, repair || analyze);
        string mode = request.Mode ?? request.Selection?.Mode
            ?? (request.Selection is { Faces.Length: 0, Edges.Length: > 0 } ? "Edge"
                : request.Selection is { Faces.Length: 0, Vertices.Length: > 0 } ? "Vertex" : "Face");
        mode = mode.ToLowerInvariant() switch
        {
            "face" => "Face", "edge" => "Edge", "vertex" => "Vertex", "object" => "Object",
            _ => throw new InvalidDataException("Selection mode must be Face, Edge, Vertex or Object.")
        };
        MapSubSelection selection = ReadSelection(request.Selection ?? new(), mesh);
        int[] faces = selection.Faces.Order().ToArray();
        int[] vertices = mode == "Vertex" ? selection.OrderedVertices : selection.VertexIndices(mesh, mode);
        MapEdge[] edges = selection.Edges.OrderBy(e => e.A).ThenBy(e => e.B).ToArray();
        float amount = request.Amount;
        if (!float.IsFinite(amount) || MathF.Abs(amount) > 1e7f) throw new InvalidDataException("Amount must be finite and within the modeling coordinate budget.");
        if (request.Segments is < 1 or > 64) throw new InvalidDataException("Segments must be between 1 and 64.");
        if (request.Cursor is not null) CheckVector(request.Cursor, 3, "Cursor");
        if (request.Material is int materialIndex && (uint)materialIndex >= payload.Materials.Length)
            throw new InvalidDataException("Material index is outside the material table.");
        int maximumContexts = snap ? 4096 : 64;
        if (request.Meshes is { } contexts && contexts.Length > maximumContexts)
            throw new InvalidDataException($"At most {maximumContexts} context meshes are supported for this operation.");
        var others = (request.Meshes ?? []).Select(value => value as MapMesh ?? throw new InvalidDataException("Context geometry must be a mesh.")).ToArray();
        if (document.RootElement.TryGetProperty("meshes", out var rawMeshes))
            foreach (var raw in rawMeshes.EnumerateArray()) RequireMeshIdentity(raw);
        foreach (MapMesh other in others) CheckMesh(other, payload.Materials.Length, false);
        if (others.Select(m => m.Id).Append(mesh.Id).Distinct().Count() != others.Length + 1)
            throw new InvalidDataException("Context meshes must have distinct identities.");
        int contextElements = snap ? 262_144 : MaxElements;
        if (others.Sum(m => (long)m.Vertices.Count) + mesh.Vertices.Count > contextElements
            || others.Sum(m => (long)m.Faces.Count) + mesh.Faces.Count > contextElements)
            throw new InvalidDataException("Context exceeds the mesh vertex/face budget.");

        if (analyze)
        {
            // Diagnostics are read-only, including locked meshes and invalid
            // surfaces that pass the bounded structural/channel checks above.
            // Preserve the caller's selection order and payload provenance.
            return new()
            {
                Success = true, Payload = rawPayload.Clone(),
                Selection = document.RootElement.TryGetProperty("selection", out JsonElement rawSelection) ? rawSelection.Clone() : new Selection(),
                Issues = MapMeshValidator.Validate(mesh).Select(problem => new Issue
                {
                    Name = problem.Cleanup ? "Cleanup" : "Geometry", Severity = problem.Severity.ToString(), Message = problem.Message,
                    Face = problem.Face, Vertex = problem.Vertex,
                    Edge = problem.Edge is { } edge ? [edge.A, edge.B] : null,
                    Vertices = problem.Vertex is int vertex ? [vertex] : problem.Edge is { } issueEdge ? [issueEdge.A, issueEdge.B]
                        : problem.Face is int face ? mesh.Faces[face].Distinct().ToArray() : null,
                    Cleanup = problem.Cleanup
                }).ToArray()
            };
        }

        bool selectionOnly = operation is "selectlinked" or "connected" or "connectedfaces" or "selectconnectedfaces" or "selectfaceswithsamematerial" or "grow" or "shrink" or "selectboundary" or "boundary" or "selectall" or "selectallfaces" or "selectnone" or "edgeloop";
        if (!selectionOnly && mesh.Locked) throw new InvalidOperationException("Unlock the mesh before editing it.");
        var additional = new List<MapMesh>();
        bool removeSource = false, keepSelection = selectionOnly;
        Guid identity = mesh.Id;
        int[] RequireFaces()
        {
            if (faces.Length == 0) throw new InvalidOperationException("Select faces first.");
            return faces;
        }
        void RequireVertices(int count = 1)
        { if (vertices.Length < count) throw new InvalidOperationException($"Select at least {count} vertices first."); }
        MapEdge OneEdge()
        { if (edges.Length != 1) throw new InvalidOperationException("Select one edge first."); return edges[0]; }
        Vector3 Cursor() => request.Cursor is { } cursor ? new(cursor[0], cursor[1], cursor[2]) : throw new InvalidOperationException("Cursor position is required.");
        void Preflight(long addedVertices, long addedFaces)
        {
            if (mesh.Vertices.Count + addedVertices > MaxElements || mesh.Faces.Count + addedFaces > MaxElements)
                throw new InvalidOperationException("Operation would exceed the 65,535 vertex/face limit.");
        }
        long selectedCorners = faces.Sum(f => (long)mesh.Faces[f].Length);
        switch (operation)
        {
            case "selectlinked": case "connected": case "connectedfaces": selection.Linked(mesh, mode); break;
            case "selectconnectedfaces": mode = "Face"; selection.Linked(mesh, mode); break;
            case "selectfaceswithsamematerial":
                int selectedMaterial = FaceMaterial(mesh, RequireFaces()[0]); mode = "Face"; selection.Faces.Clear();
                selection.Faces.UnionWith(Enumerable.Range(0, mesh.Faces.Count).Where(f => FaceMaterial(mesh, f) == selectedMaterial)); break;
            case "grow": case "shrink": selection.Resize(mesh, mode, operation == "grow"); break;
            case "selectboundary": case "boundary": selection.Edges.UnionWith(new MapMeshTopology(mesh).BoundaryEdges()); mode = "Edge"; break;
            case "edgeloop": selection.Edges.UnionWith(new MapMeshTopology(mesh).EdgeLoop(OneEdge())); mode = "Edge"; break;
            case "selectall": selection.SelectAll(mesh, mode); break;
            case "selectallfaces": mode = "Face"; selection.SelectAll(mesh, mode); break;
            case "selectnone": selection.Clear(); selection.Bind(mesh.Id); break;
            case "extrude": case "extruderegion":
                Preflight(selectedCorners, selectedCorners); MapMeshEditing.ExtrudeRegion(mesh, RequireFaces(), amount); break;
            case "extrudeface": case "extrudeindividual":
                Preflight(selectedCorners, selectedCorners); foreach (int f in RequireFaces()) MapMeshEditing.ExtrudeFace(mesh, f, amount); break;
            case "inset": case "insetregion":
                Preflight(selectedCorners, selectedCorners); MapMeshEditing.InsetRegion(mesh, RequireFaces(), amount); break;
            case "insetface":
                Preflight(selectedCorners, selectedCorners); foreach (int f in RequireFaces()) MapMeshEditing.InsetFace(mesh, f, amount); break;
            case "bevel": case "beveledge": case "bevelface":
                if (operation == "beveledge" || operation == "bevel" && mode == "Edge")
                { Preflight(4L + 2L * request.Segments, request.Segments); MapMeshEditing.BevelEdge(mesh, OneEdge(), amount, request.Segments, request.Material); }
                else
                { Preflight(selectedCorners * 2, selectedCorners * 2); foreach (int f in RequireFaces()) MapMeshEditing.BevelFace(mesh, f, amount, request.Distance ?? amount * .25f); }
                break;
            case "splitedge":
                if (edges.Length == 0) throw new InvalidOperationException("Select edges first.");
                Preflight((long)edges.Length * request.Segments, 0);
                foreach (var edge in edges) MapMeshEditing.SplitEdge(mesh, edge, amount, request.Segments); break;
            case "dissolveedge":
                if (edges.Length == 0) throw new InvalidOperationException("Select edges first.");
                foreach (var edge in edges) MapMeshEditing.DissolveEdge(mesh, edge); break;
            case "collapseedge": case "collapsetoa": case "collapsetob": case "collapsetocursor":
                MapMeshEditing.CollapseEdge(mesh, OneEdge(), operation == "collapsetoa" ? "First" : operation == "collapsetob" ? "Last" : operation == "collapsetocursor" ? "Cursor" : "Center",
                    operation == "collapsetocursor" ? MapMeshEditing.WorldToLocal(mesh, Cursor()) : null); break;
            case "slideedge":
                MapEdge slideEdge = OneEdge(); MapMeshEditing.SlideEdge(mesh, slideEdge, request.Face ?? new MapMeshTopology(mesh).AdjacentFaces(slideEdge).First(), amount); break;
            case "mergecenter": case "mergefirst": case "mergelast": case "mergetocursor":
                RequireVertices(2); MapMeshEditing.MergeVertices(mesh, vertices, operation == "mergefirst" ? "First" : operation == "mergelast" ? "Last" : operation == "mergetocursor" ? "Cursor" : "Center",
                    operation == "mergetocursor" ? MapMeshEditing.WorldToLocal(mesh, Cursor()) : null); break;
            case "mergebydistance": RequireVertices(2); MapMeshEditing.MergeByDistance(mesh, vertices, amount); break;
            case "weld": case "weldnearbyvertices": MapMeshEditing.Weld(mesh, amount); break;
            case "connectvertices":
                if (vertices.Length != 2) throw new InvalidOperationException("Select two vertices.");
                Preflight(0, 1); MapMeshEditing.ConnectVertices(mesh, vertices[0], vertices[1]); break;
            case "ripvertex":
                if (vertices.Length != 1) throw new InvalidOperationException("Select one vertex.");
                Preflight(1, 0); MapMeshEditing.RipVertex(mesh, vertices[0], request.Face ?? new MapMeshTopology(mesh).FacesOfVertex(vertices[0]).First()); break;
            case "slidevertex":
                if (vertices.Length != 2) throw new InvalidOperationException("Select the vertex and its neighboring target.");
                MapMeshEditing.SlideVertex(mesh, vertices[0], vertices[1], amount); break;
            case "flattenx": case "flatteny": case "flattenz": MapMeshEditing.Flatten(mesh, vertices, "xyz".IndexOf(operation[^1])); break;
            case "movevertex":
                RequireVertices(); float[] delta = request.Delta ?? throw new InvalidOperationException("Vertex delta is required."); CheckVector(delta, 3, "Delta");
                foreach (int vertex in vertices) MapMeshEditing.MoveVertex(mesh, vertex, new(delta[0], delta[1], delta[2])); break;
            case "snaptosurface":
                RequireVertices(); if (others.Length == 0) throw new InvalidOperationException("Supply context meshes to snap to a surface.");
                var surfaces = others.Where(m => !m.Hidden).SelectMany(m => GeometryCompiler.Compile(m, TexScale(m.Material, payload.Materials))
                    .Select(f => new MapViewportFace(m.Id, f.Points.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray(), f.Shade, f.Material, m.Solid))).ToArray();
                foreach (int vertex in vertices)
                {
                    var contact = MapLayoutCommands.NearestSurface(MapMeshEditing.VertexWorld(mesh, vertex), surfaces)
                        ?? throw new InvalidOperationException("No nearby collision surface was found.");
                    MapMeshEditing.SetVertexWorld(mesh, vertex, contact.Point);
                }
                break;
            case "duplicate": case "duplicatefaces":
                Preflight(selectedCorners, faces.Length); mesh = MapMeshEditing.Join([mesh, MapMeshEditing.DuplicateFaces(mesh, RequireFaces())], TexScale(mesh.Material, payload.Materials)); break;
            case "duplicatetoobject": additional.Add(MapMeshEditing.DuplicateFaces(mesh, RequireFaces())); break;
            case "separate":
                additional.Add(MapMeshEditing.Separate(mesh, RequireFaces())); removeSource = mesh.Faces.Count == 0; break;
            case "join": case "joinmeshes":
                if (others.Length == 0) throw new InvalidOperationException("Supply another mesh to join.");
                mesh = MapMeshEditing.Join(new[] { mesh }.Concat(others), TexScale(mesh.Material, payload.Materials)); break;
            case "fill":
                RequireVertices();
                var fillTopology = new MapMeshTopology(mesh);
                var fillEdges = (edges.Length > 0 ? edges : fillTopology.BoundaryEdges().Where(e => vertices.Contains(e.A) && vertices.Contains(e.B))).ToHashSet();
                long fillFaces = fillTopology.OpenBoundaries().Where(loop => Enumerable.Range(0, loop.Length)
                    .All(i => fillEdges.Contains(new(loop[i], loop[(i + 1) % loop.Length])))).Sum(loop => (long)loop.Length - 2);
                Preflight(0, fillFaces); MapMeshEditing.Fill(mesh, fillEdges); break;
            case "triangulate":
                Preflight(0, selectedCorners - faces.Length * 3L); MapMeshEditing.Triangulate(mesh, RequireFaces()); break;
            case "subdivide": case "subdivideface":
                Preflight(faces.Length, selectedCorners - faces.Length);
                foreach (int f in RequireFaces().OrderDescending()) MapMeshEditing.SubdivideFace(mesh, f); break;
            case "dissolvetriangles":
                foreach (var edge in new MapMeshTopology(mesh).Edges.ToArray())
                {
                    int[] adjacent = new MapMeshTopology(mesh).AdjacentFaces(edge);
                    if (adjacent.Length == 2 && adjacent.All(f => mesh.Faces[f].Length == 3))
                        try { MapMeshEditing.DissolveEdge(mesh, edge); } catch (InvalidOperationException) { /* Ineligible triangle pairs retain their seam, as in RunModeling. */ }
                }
                break;
            case "flipnormals": case "flipface": foreach (int f in RequireFaces()) MapMeshEditing.FlipFace(mesh, f); break;
            case "recalculatewinding": MapMeshEditing.RecalculateWinding(mesh); break;
            case "cleanunusedvertices": case "cleanup": MapMeshEditing.Compact(mesh); break;
            case "delete": case "deleteface":
                int[] removed = operation == "deleteface" || mode == "Face" ? RequireFaces()
                    : Enumerable.Range(0, mesh.Faces.Count).Where(f => mode == "Vertex" ? mesh.Faces[f].Any(vertices.Contains)
                        : new MapMeshTopology(mesh).EdgesOfFace(f).Any(edges.Contains)).ToArray();
                if (removed.Length == 0) throw new InvalidOperationException("Select elements to delete first.");
                foreach (int f in removed.OrderDescending()) MapMeshEditing.DeleteFace(mesh, f);
                removeSource = mesh.Faces.Count == 0; break;
            case "uvtransform": case "transformuv":
                UvEdit uv = request.Uv ?? new(); CheckVector(uv.Scale, 2, "UV scale"); CheckVector(uv.Offset, 2, "UV offset");
                foreach (int f in RequireFaces()) MapMeshEditing.TransformUv(mesh, [f], new(uv.Scale[0], uv.Scale[1]), new(uv.Offset[0], uv.Offset[1]), uv.Rotation, TexScale(FaceMaterial(mesh, f), payload.Materials));
                keepSelection = true; break;
            case "uvfit": case "fituv":
                foreach (int f in RequireFaces())
                {
                    JsonElement material = payload.Materials[FaceMaterial(mesh, f)];
                    FitUv(mesh, f, request, material, TexScale(FaceMaterial(mesh, f), payload.Materials));
                }
                keepSelection = true; break;
            case "uvdensity": case "matchtexeldensity":
                foreach (int f in RequireFaces()) MapMeshEditing.MatchTexelDensity(mesh, [f], request.TexelDensity ?? amount, TexScale(FaceMaterial(mesh, f), payload.Materials));
                keepSelection = true; break;
            case "uvreset": case "resetuv":
                foreach (int f in RequireFaces()) if (f < mesh.FaceTexcoords.Count) mesh.FaceTexcoords[f] = null;
                keepSelection = true; break;
            case "uvsetprojection":
                UvEdit projection = request.Uv ?? throw new InvalidOperationException("UV projection is required.");
                CheckVector(projection.Scale, 2, "UV scale"); CheckVector(projection.Offset, 2, "UV offset");
                if (!float.IsFinite(projection.Rotation)) throw new InvalidDataException("UV rotation must be finite.");
                foreach (int f in RequireFaces())
                {
                    if (f < mesh.FaceTexcoords.Count) mesh.FaceTexcoords[f] = null;
                    mesh.FaceUv[f] = new() { Scale = (float[])projection.Scale.Clone(), Offset = (float[])projection.Offset.Clone(), Rotation = projection.Rotation };
                }
                keepSelection = true; break;
            case "uvsettexdef":
                foreach (int f in RequireFaces()) SetTexdef(mesh, f, request, TexScale(FaceMaterial(mesh, f), payload.Materials));
                keepSelection = true; break;
            case "assignmaterial":
                MapMeshEditing.AssignMaterial(mesh, RequireFaces(), request.Material ?? throw new InvalidOperationException("Material index is required.")); keepSelection = true; break;
            default: throw new InvalidOperationException("Unknown mesh modeling operation: " + request.Operation);
        }

        mesh.Id = identity;
        if (!removeSource) CheckMesh(mesh, payload.Materials.Length, false);
        foreach (MapMesh added in additional) CheckMesh(added, payload.Materials.Length, false);
        if (!keepSelection || removeSource) { selection.Clear(); selection.Bind(mesh.Id); }
        else selection.Validate(mesh);
        // Interchange provenance (originalGeometry/originalMeshSha256 and future
        // root metadata) is opaque to modeling and must round-trip byte semantics.
        payload.Mesh = removeSource ? null : mesh;
        if (JsonSerializer.SerializeToUtf8Bytes(payload, Json).Length > PrimeMeshInterchange.MaximumDecodedBytes)
            throw new InvalidOperationException("Canonical mesh result exceeds the 16 MiB primitive budget.");
        return new()
        {
            Success = true,
            Payload = payload,
            Selection = new Selection() { Faces = selection.Faces.Order().ToArray(), Vertices = selection.OrderedVertices,
                Edges = selection.Edges.OrderBy(e => e.A).ThenBy(e => e.B).Select(e => new[] { e.A, e.B }).ToArray(), Mode = mode },
            AdditionalMeshes = additional.Count == 0 ? null : additional.Cast<MapGeometry>().ToArray(), RemoveSource = removeSource ? true : null
        };
    }

    private static string Normalize(string operation) => new((operation ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static void CheckJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!keys.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name); CheckJson(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) CheckJson(child);
        else if (element.ValueKind == JsonValueKind.Number && (!element.TryGetDouble(out double value) || !double.IsFinite(value)))
            throw new InvalidDataException("JSON numbers must be finite.");
    }
    private static void RequireMeshIdentity(JsonElement mesh)
    {
        if (mesh.ValueKind != JsonValueKind.Object || !mesh.TryGetProperty("id", out var id) || !id.TryGetGuid(out Guid guid) || guid == Guid.Empty
            || !mesh.TryGetProperty("vertices", out _) || !mesh.TryGetProperty("faces", out _))
            throw new InvalidDataException("Canonical mesh requires a nonempty id, vertices and faces.");
    }
    private static void CheckVector(float[]? vector, int length, string label)
    { if (vector is null || vector.Length != length || vector.Any(p => !float.IsFinite(p) || MathF.Abs(p) > 1e9f)) throw new InvalidDataException(label + " has an invalid shape or value."); }
    private static void CheckUv(MapUv? uv)
    {
        if (uv is null) throw new InvalidDataException("UV projection is missing.");
        CheckVector(uv.Scale, 2, "UV scale"); CheckVector(uv.Offset, 2, "UV offset");
        if (uv.Scale.Any(s => s <= 0) || !float.IsFinite(uv.Rotation)) throw new InvalidDataException("UV projection needs positive scale and finite rotation.");
    }
    private static void CheckMaterial(JsonElement material)
    {
        if (material.ValueKind != JsonValueKind.Object || !material.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || name.GetString() is not { Length: > 0 and <= 128 } label || string.IsNullOrWhiteSpace(label) || label.Any(char.IsControl))
            throw new InvalidDataException("Material requires a bounded name.");
        if (!material.TryGetProperty("texScale", out _) || !material.TryGetProperty("width", out _) || !material.TryGetProperty("height", out _))
            throw new InvalidDataException("Material requires texScale, width and height.");
        _ = TexScale(0, [material]); _ = Dimension(material, "width"); _ = Dimension(material, "height");
    }
    private static float Dimension(JsonElement material, string property)
    {
        int value = material.TryGetProperty(property, out var number) ? number.GetInt32() : 64;
        if (value is <= 0 or > 16_384) throw new InvalidDataException("Material texture dimensions must be integers between 1 and 16,384.");
        return value;
    }
    private static float TexScale(int material, JsonElement[] materials)
    {
        float scale = materials[material].TryGetProperty("texScale", out var number) ? number.GetSingle() : 16;
        if (!float.IsFinite(scale) || scale is <= 0 or > 1e6f) throw new InvalidDataException("Material texScale must be positive and bounded.");
        return scale;
    }
    private static int FaceMaterial(MapMesh mesh, int face) => face < mesh.FaceMaterials.Count ? mesh.FaceMaterials[face] : mesh.Material;
    private static void SetTexdef(MapMesh mesh, int face, Request request, float texScale)
    {
        if (request.HShift is null && request.VShift is null && request.HScale is null && request.VScale is null && request.Rotation is null
            && request.HShiftDelta is null && request.VShiftDelta is null && request.HScaleDelta is null && request.VScaleDelta is null && request.RotationDelta is null)
            throw new InvalidOperationException("Supply a texture projection component to change.");
        MapUv old = mesh.FaceUv.GetValueOrDefault(face) ?? mesh.Uv;
        float Scale(float original, float? absolute, float? delta)
        {
            if (absolute is null && delta is null) return original;
            float oldQuake = 16 / (texScale * original), quake = (absolute ?? oldQuake) + (delta ?? 0);
            if (delta is not null && MathF.Abs(quake) <= 1e-5f) quake = -oldQuake;
            return 16 / (texScale * quake);
        }
        float u = Scale(old.Scale[0], request.HScale, request.HScaleDelta);
        float v = Scale(old.Scale[1], request.VScale, request.VScaleDelta);
        float angle = (request.Rotation ?? old.Rotation) + (request.RotationDelta ?? 0);
        var offset = new Vector2((request.HShift ?? old.Offset[0]) + (request.HShiftDelta ?? 0), (request.VShift ?? old.Offset[1]) + (request.VShiftDelta ?? 0));
        if (!float.IsFinite(u) || !float.IsFinite(v) || !float.IsFinite(angle) || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y)
            || MathF.Abs(u) < 1e-9f || MathF.Abs(v) < 1e-9f) throw new InvalidDataException("Texture projection requires finite values and nonzero scales.");
        // Bake with the canonical projection/compiler, then replace projection
        // components as one affine map so authored corner seams remain intact.
        MapMeshEditing.TransformUv(mesh, [face], Vector2.One, Vector2.Zero, 0, texScale);
        var matrix = Matrix3x2.CreateTranslation(-old.Offset[0], -old.Offset[1])
            * Matrix3x2.CreateRotation(-old.Rotation * MathF.PI / 180)
            * Matrix3x2.CreateScale(u / old.Scale[0], v / old.Scale[1])
            * Matrix3x2.CreateRotation(angle * MathF.PI / 180) * Matrix3x2.CreateTranslation(offset);
        foreach (float[] corner in mesh.FaceTexcoords[face]!)
        { Vector2 point = Vector2.Transform(new(corner[0], corner[1]), matrix); corner[0] = point.X; corner[1] = point.Y; }
        mesh.FaceUv[face] = new() { Scale = [MathF.Abs(u), MathF.Abs(v)], Offset = [offset.X, offset.Y], Rotation = angle };
    }
    private static void FitUv(MapMesh mesh, int face, Request request, JsonElement material, float texScale)
    {
        float horizontal = request.Horizontal ?? 1, vertical = request.Vertical ?? 1;
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical) || MathF.Abs(horizontal) > 1024 || MathF.Abs(vertical) > 1024
            || horizontal <= 0 && vertical <= 0) throw new InvalidDataException("UV fit needs a positive bounded repeat count for at least one dimension.");
        MapMeshEditing.TransformUv(mesh, [face], Vector2.One, Vector2.Zero, 0, texScale);
        float[][] source = mesh.FaceTexcoords[face]!;
        float oldWidth = source.Max(p => p[0]) - source.Min(p => p[0]), oldHeight = source.Max(p => p[1]) - source.Min(p => p[1]);
        float width = request.Width ?? Dimension(material, "width") * horizontal;
        float height = request.Height ?? Dimension(material, "height") * vertical;
        if (horizontal <= 0) width = request.OnlyDimension ? Math.Max(1e-6f, oldWidth) : height / Math.Max(1e-6f, oldHeight) * oldWidth;
        if (vertical <= 0) height = request.OnlyDimension ? Math.Max(1e-6f, oldHeight) : width / Math.Max(1e-6f, oldWidth) * oldHeight;
        if (!float.IsFinite(width) || !float.IsFinite(height) || width is <= 0 or > 1e9f || height is <= 0 or > 1e9f)
            throw new InvalidDataException("UV fit dimensions must be positive and bounded.");
        MapMeshEditing.FitUv(mesh, [face], texScale, width, height);
        if (request.OnlyDimension)
            for (int corner = 0; corner < source.Length; corner++)
            {
                if (horizontal <= 0) mesh.FaceTexcoords[face]![corner][0] = source[corner][0];
                if (vertical <= 0) mesh.FaceTexcoords[face]![corner][1] = source[corner][1];
            }
    }
    private static void CheckMesh(MapMesh mesh, int materials, bool repair)
    {
        if (mesh is null || mesh.Id == Guid.Empty || mesh.Vertices is null || mesh.Faces is null || mesh.FaceMaterials is null || mesh.FaceTexcoords is null || mesh.FaceUv is null)
            throw new InvalidDataException("Canonical mesh channels or identity are missing.");
        if (mesh.Vertices.Count is < 3 or > MaxElements || mesh.Faces.Count is < 1 or > MaxElements || mesh.Faces.Sum(f => (long)(f?.Length ?? 0)) > MaxCorners)
            throw new InvalidDataException("Mesh exceeds the surface, vertex, face or corner budget.");
        foreach (float[] point in mesh.Vertices) CheckVector(point, 3, "Vertex");
        foreach (int[] face in mesh.Faces)
            if (face is not { Length: >= 3 and <= 32 } || face.Any(i => (uint)i >= mesh.Vertices.Count)) throw new InvalidDataException("Face has invalid corners or vertex indices.");
        if ((uint)mesh.Material >= materials || mesh.FaceMaterials.Count > mesh.Faces.Count || mesh.FaceMaterials.Any(m => (uint)m >= materials))
            throw new InvalidDataException("Mesh material channel is invalid.");
        if (mesh.FaceTexcoords.Count > mesh.Faces.Count) throw new InvalidDataException("Mesh corner UV channel exceeds the face count.");
        for (int f = 0; f < mesh.FaceTexcoords.Count; f++)
            if (mesh.FaceTexcoords[f] is { } uv)
            { if (uv.Length != mesh.Faces[f].Length) throw new InvalidDataException("Corner UV count differs from face corners."); foreach (float[] corner in uv) CheckVector(corner, 2, "Corner UV"); }
        if (mesh.Transform is null) throw new InvalidDataException("Mesh transform is missing.");
        CheckVector(mesh.Transform.Position, 3, "Position"); CheckVector(mesh.Transform.Scale, 3, "Scale"); CheckVector(mesh.Transform.Rotation, 4, "Rotation");
        if (mesh.Transform.Scale.Any(s => s <= 0) || Math.Abs(mesh.Transform.Rotation.Sum(q => (double)q * q) - 1) > .001)
            throw new InvalidDataException("Mesh needs an invertible scale and unit rotation quaternion.");
        if (!float.IsFinite(mesh.Shade) || mesh.Slipperiness is < 0 or > 3 || mesh.Label is null || mesh.Layer is null || mesh.Terrain is null || mesh.QuakeFaces is not null)
            throw new InvalidDataException("Mesh authoring metadata is invalid.");
        CheckUv(mesh.Uv);
        foreach (var entry in mesh.FaceUv)
        { if ((uint)entry.Key >= mesh.Faces.Count) throw new InvalidDataException("Face UV index is outside the mesh."); CheckUv(entry.Value); }
        // Warnings (including legacy nonplanarity and open boundaries) remain editable.
        // Errors are never silently cleaned by an unrelated command.
        if (!repair) MapMeshEditing.CheckOperation(mesh);
    }
    private static MapSubSelection ReadSelection(Selection selected, MapMesh mesh)
    {
        if (selected.Faces is null || selected.Vertices is null || selected.Edges is null || selected.Faces.Length > MaxElements || selected.Vertices.Length > MaxElements || selected.Edges.Length > MaxCorners)
            throw new InvalidDataException("Selection exceeds the element budget or has missing channels.");
        if (selected.Faces.Any(f => (uint)f >= mesh.Faces.Count) || selected.Vertices.Any(v => (uint)v >= mesh.Vertices.Count))
            throw new InvalidDataException("Selection index is outside the mesh.");
        var available = new MapMeshTopology(mesh).Edges.ToHashSet();
        var result = new MapSubSelection(); result.Bind(mesh.Id);
        result.Faces.UnionWith(selected.Faces);
        foreach (int vertex in selected.Vertices) { result.Vertices.Add(vertex); result.ActiveVertex = vertex; }
        foreach (int[] edge in selected.Edges)
        {
            if (edge is not { Length: 2 } || edge[0] == edge[1] || !available.Contains(new(edge[0], edge[1]))) throw new InvalidDataException("Selection contains a missing or invalid edge.");
            result.Edges.Add(new(edge[0], edge[1]));
        }
        result.Validate(mesh); return result;
    }

    private sealed class Request
    {
        public Request() { }
        public int Schema { get; set; }
        public string Operation { get; set; } = "";
        public Payload? Payload { get; set; }
        public Selection? Selection { get; set; }
        public string? Mode { get; set; }
        public float Amount { get; set; } = .25f;
        public int Segments { get; set; } = 1;
        public float[]? Cursor { get; set; }
        public float[]? Delta { get; set; }
        public float? Distance { get; set; }
        public int? Material { get; set; }
        public int? Face { get; set; }
        public float? TexelDensity { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }
        public float? Horizontal { get; set; }
        public float? Vertical { get; set; }
        public bool OnlyDimension { get; set; }
        public float? HShift { get; set; }
        public float? VShift { get; set; }
        public float? HScale { get; set; }
        public float? VScale { get; set; }
        public float? Rotation { get; set; }
        public float? HShiftDelta { get; set; }
        public float? VShiftDelta { get; set; }
        public float? HScaleDelta { get; set; }
        public float? VScaleDelta { get; set; }
        public float? RotationDelta { get; set; }
        public UvEdit? Uv { get; set; }
        public MapGeometry[]? Meshes { get; set; }
    }
    private sealed class Payload
    {
        public Payload() { }
        public int Schema { get; set; }
        public int Units { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public MapGeometry? Mesh { get; set; }
        public JsonElement[] Materials { get; set; } = [];
        [JsonExtensionData] public Dictionary<string, JsonElement>? Metadata { get; set; }
    }
    private sealed class Selection
    {
        public Selection() { }
        public int[] Faces { get; set; } = [];
        public int[] Vertices { get; set; } = [];
        public int[][] Edges { get; set; } = [];
        public string? Mode { get; set; }
    }
    private sealed class UvEdit
    {
        public UvEdit() { }
        public float[] Scale { get; set; } = [1, 1];
        public float[] Offset { get; set; } = [0, 0];
        public float Rotation { get; set; }
    }
    private sealed class Reply
    {
        public int Schema { get; set; } = 1;
        public bool Success { get; set; }
        public object? Payload { get; set; }
        public object? Selection { get; set; }
        public MapGeometry[]? AdditionalMeshes { get; set; }
        public bool? RemoveSource { get; set; }
        public string? Error { get; set; }
        public Issue[]? Issues { get; set; }
    }
    private sealed class Issue
    {
        public string Name { get; set; } = "Geometry";
        public string Severity { get; set; } = "Info";
        public string Message { get; set; } = "";
        public int? Face { get; set; }
        public int? Vertex { get; set; }
        public int[]? Edge { get; set; }
        public int[]? Vertices { get; set; }
        public bool Cleanup { get; set; }
    }
}
