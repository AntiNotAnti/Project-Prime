using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++; Console.WriteLine("MODELING PASS " + description);
}
string Bytes(MapMesh mesh) => JsonSerializer.Serialize(mesh);
Vector3 Point(MapMesh mesh, int index) => new(mesh.Vertices[index][0], mesh.Vertices[index][1], mesh.Vertices[index][2]);
double Volume(MapMesh mesh) => mesh.Faces.Sum(face => Enumerable.Range(1, face.Length - 2)
    .Sum(corner => (double)Vector3.Dot(Point(mesh, face[0]), Vector3.Cross(Point(mesh, face[corner]), Point(mesh, face[corner + 1]))) / 6));
double Area(MapMesh mesh) => mesh.Faces.Sum(face => Enumerable.Range(0, face.Length)
    .Select(corner => Vector3.Cross(Point(mesh, face[corner]), Point(mesh, face[(corner + 1) % face.Length])))
    .Aggregate(Vector3.Zero, (a, b) => a + b).Length() / 2d);
bool Near(double a, double b) => Math.Abs(a - b) < 1e-4;
MapMesh Cube(float minX = -1, float maxX = 1, float minZ = -1, float maxZ = 1)
{
    var mesh = new MapMesh { Label = "Topology fixture", Vertices = new()
    {
        new[] { minX, -1f, minZ }, new[] { maxX, -1f, minZ }, new[] { maxX, 1f, minZ }, new[] { minX, 1f, minZ },
        new[] { minX, -1f, maxZ }, new[] { maxX, -1f, maxZ }, new[] { maxX, 1f, maxZ }, new[] { minX, 1f, maxZ }
    }, Faces = new() { new[] { 0,3,2,1 }, new[] { 4,5,6,7 }, new[] { 0,1,5,4 }, new[] { 1,2,6,5 }, new[] { 3,7,6,2 }, new[] { 0,4,7,3 } } };
    mesh.FaceMaterials = Enumerable.Range(0, 6).ToList();
    mesh.FaceTexcoords = mesh.Faces.Select(_ => (float[][]?)new[] { new[] { 0f,0 }, new[] { 64f,0 }, new[] { 64f,64 }, new[] { 0f,64 } }).ToList();
    return mesh;
}
void RemoveFace(MapMesh mesh, int face)
{ mesh.Faces.RemoveAt(face); mesh.FaceMaterials.RemoveAt(face); mesh.FaceTexcoords.RemoveAt(face); }
void Closed(MapMesh mesh, string description)
{
    Check(!MapMeshValidator.Validate(mesh).Any(problem => problem.Severity == MapDiagnosticSeverity.Error), description + " has valid polygon geometry");
    var topology = new MapMeshTopology(mesh);
    Check(topology.Edges.All(edge => topology.Uses(edge).Count == 2 && topology.Uses(edge)[0].From != topology.Uses(edge)[1].From), description + " remains a closed consistently wound manifold");
    Check(mesh.FaceTexcoords.Count == mesh.Faces.Count && mesh.FaceTexcoords.Select((uv, face) => uv == null || uv.Length == mesh.Faces[face].Length).All(value => value), description + " preserves corner UV channel alignment");
    Check(GeometryCompiler.Compile(mesh, 16).Count == mesh.Faces.Count, description + " is accepted by the actual canonical geometry compiler");
}
void Reject(MapMesh source, Func<MapMesh> operation, string description)
{
    string before = Bytes(source); bool rejected = false;
    try { operation(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { rejected = true; }
    Check(rejected && Bytes(source) == before, description + " rejects without changing any source byte");
}

var cube = Cube(); string original = Bytes(cube);
Closed(cube, "source cube");
var cut = MapModelingEnhancements.LoopCut(cube, new(0, 1));
Closed(cut, "quad ring cut");
Check(cut.Vertices.Count == 12 && cut.Faces.Count == 10 && cut.Faces.All(face => face.Length == 4), "loop cut creates one shared midpoint per ring edge and two quads per strip face");
Check(Near(Area(cut), Area(cube)) && Near(Volume(cut), Volume(cube)), "loop cut preserves surface area and enclosed volume");
Check(Bytes(cube) == original, "successful loop cut leaves the serialized source unchanged");
var multiple = MapModelingEnhancements.LoopCut(cube, new(0, 1), cuts: 3);
Closed(multiple, "three ring cuts");
Check(multiple.Vertices.Count == 20 && multiple.Faces.Count == 18 && Near(Volume(multiple), 8), "multiple ring cuts share three vertices per edge without cracks");
var offsetCut = MapModelingEnhancements.LoopCut(cube, new(0, 1), .25f);
Check(offsetCut.Vertices.Skip(8).All(point => Near(point[0], -.5)), "offset loop cut follows a consistent orientation around the ring");
var nonquad = MapMeshEditing.Clone(cube); MapMeshEditing.Triangulate(nonquad, new[] { 0 });
Reject(nonquad, () => MapModelingEnhancements.LoopCut(nonquad, new(0, 1)), "ring cut at a triangulated pole");
Reject(cube, () => MapModelingEnhancements.LoopCut(cube, new(0, 1), 0), "endpoint ring cut");

var knife = MapModelingEnhancements.KnifeCut(cube, Vector3.UnitX, 0);
Closed(knife, "world-plane knife");
Check(knife.Vertices.Count == 12 && knife.Faces.Count == 10, "knife inserts four shared plane intersections across adjacent polygons");
Check(Near(Area(knife), 24) && Near(Volume(knife), 8), "knife retains both sides with exact source area and volume");
Check(knife.FaceTexcoords.Where(uv => uv != null).SelectMany(uv => uv!).Any(uv => Near(uv[0], 32) || Near(uv[1], 32)), "knife interpolates existing corner UVs at shared edge intersections");
var transformed = Cube(); transformed.Transform.Position = new[] { 10f, 2, 3 }; transformed.Transform.Scale = new[] { 2f, 1, 1 };
var worldKnife = MapModelingEnhancements.KnifeCut(transformed, Vector3.UnitX, 10);
Check(worldKnife.Vertices.Skip(8).All(point => Near(point[0], 0)), "knife plane uses world coordinates on translated scaled source geometry");
Reject(cube, () => MapModelingEnhancements.KnifeCut(cube, Vector3.UnitX, 4), "knife missing the mesh");
Reject(cube, () => MapModelingEnhancements.KnifeCut(cube, Vector3.Zero, 0), "zero normal knife");
var concave = new MapMesh { Vertices = new(), Faces = new() };
Vector2[] outline = { new(-3,-3),new(3,-3),new(3,3),new(1,3),new(1,-1),new(-1,-1),new(-1,3),new(-3,3) };
foreach (float z in new[] { -1f, 1f }) foreach (var point in outline) concave.Vertices.Add(new[] { point.X, point.Y, z });
concave.Faces.Add(Enumerable.Range(0, 8).Reverse().ToArray()); concave.Faces.Add(Enumerable.Range(8, 8).ToArray());
for (int index = 0; index < 8; index++) concave.Faces.Add(new[] { index, (index + 1) % 8, (index + 1) % 8 + 8, index + 8 });
var concaveCut = MapModelingEnhancements.KnifeCut(concave, Vector3.UnitY, 0);
Closed(concaveCut, "concave U-prism knife");
Check(Near(Area(concaveCut), Area(concave)) && Near(Volume(concaveCut), Volume(concave)), "concave knife triangulates disconnected polygon crossings without changing geometry");

var lower = Cube(minZ: -3, maxZ: -1); RemoveFace(lower, 1);
var upper = Cube(minZ: 1, maxZ: 3); RemoveFace(upper, 0);
var ends = MapMeshEditing.Join(new[] { lower, upper });
var bridged = MapModelingEnhancements.Bridge(ends, new MapMeshTopology(ends).BoundaryEdges(), segments: 3);
Closed(bridged, "three-segment boundary bridge");
Check(Near(Volume(bridged), 24) && bridged.Vertices.Count == 24, "bridge adds the actual gap volume and shared intermediate rings");
Reject(ends, () => MapModelingEnhancements.Bridge(ends, new MapMeshTopology(ends).BoundaryEdges().Skip(1)), "partial boundary bridge");
var mismatched = MapMeshEditing.Clone(ends); MapMeshEditing.SplitEdge(mismatched, new MapMeshTopology(mismatched).BoundaryEdges()[0]);
Reject(mismatched, () => MapModelingEnhancements.Bridge(mismatched, new MapMeshTopology(mismatched).BoundaryEdges()), "unequal boundary bridge");

var hole = Cube(); foreach (var edge in new MapMeshTopology(hole).EdgesOfFace(1).ToArray()) MapMeshEditing.SplitEdge(hole, edge);
RemoveFace(hole, 1);
var filled = MapModelingEnhancements.GridFill(hole, new MapMeshTopology(hole).BoundaryEdges(), columns: 2);
Closed(filled, "two-by-two Coons grid cap");
Check(filled.Vertices.Count == 13 && filled.Faces.Count == 9 && Near(Volume(filled), 8), "grid fill creates four quads and one shared interior vertex");
Check(filled.Vertices.Any(point => Near(point[0], 0) && Near(point[1], 0) && Near(point[2], 1)), "Coons grid interior interpolates the original boundary");
Reject(hole, () => MapModelingEnhancements.GridFill(hole, new MapMeshTopology(hole).BoundaryEdges(), columns: 4), "impossible grid dimensions");
var warpedHole = MapMeshEditing.Clone(hole); warpedHole.Vertices[4][2] += .2f;
Reject(warpedHole, () => MapModelingEnhancements.GridFill(warpedHole, new MapMeshTopology(warpedHole).BoundaryEdges(), 2), "nonplanar grid boundary");

var soft = MapModelingEnhancements.ProportionalMove(cube, new[] { 0 }, new(0, .1f, 0), 3, MapProportionalFalloff.Linear);
Closed(soft, "proportional deformation");
Check(Near(soft.Vertices[0][1], -.9) && Near(soft.Vertices[1][1], -1 + .1 / 3), "proportional radius applies full selection and measured linear neighbor falloff");
Check(soft.Vertices[6].SequenceEqual(cube.Vertices[6]), "proportional radius leaves distant vertices unchanged");
var islands = MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapArrayModifier(2, new(4, 0, 0)) });
var connected = MapModelingEnhancements.ProportionalMove(islands, new[] { 1 }, new(0, .1f, 0), 10, connectedOnly: true);
var spatial = MapModelingEnhancements.ProportionalMove(islands, new[] { 1 }, new(0, .1f, 0), 10, connectedOnly: false);
Check(connected.Vertices[8].SequenceEqual(islands.Vertices[8]) && !spatial.Vertices[8].SequenceEqual(islands.Vertices[8]), "connected falloff isolates detached islands while spatial falloff can include them");
Reject(cube, () => MapModelingEnhancements.ProportionalMove(cube, new[] { 99 }, Vector3.One, 2), "missing proportional selection");
Reject(cube, () => MapModelingEnhancements.ProportionalMove(cube, new[] { 0 }, Vector3.One, float.NaN), "nonfinite proportional radius");
var pinched = MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapArrayModifier(2, new(2,2,2)) });
for (int face = 6; face < pinched.Faces.Count; face++) pinched.Faces[face] = pinched.Faces[face].Select(vertex => vertex == 8 ? 6 : vertex).ToArray();
MapMeshEditing.Compact(pinched);
Reject(pinched, () => MapModelingEnhancements.ProportionalMove(pinched, new[] { 6 }, new(0,.1f,0), 2), "non-manifold vertex connecting disconnected closed fans");

var half = Cube(minX: 0); string halfBytes = Bytes(half);
MapModelModifier[] modifiers = { new MapMirrorModifier(0), new MapArrayModifier(3, new(0, 5, 0)) };
var evaluated = MapModelingEnhancements.EvaluateModifiers(half, modifiers);
Closed(evaluated, "mirror and array stack");
Check(Near(Volume(evaluated), 24) && new MapMeshTopology(evaluated).FaceIslands().Length == 3, "mirror removes internal plane caps, welds seams and arrays three closed islands");
Check(Bytes(half) == halfBytes && modifiers.Length == 2, "modifier evaluation preserves serialized source and unbaked stack");
var disabled = MapModelingEnhancements.EvaluateModifiers(half, new MapModelModifier[] { new MapMirrorModifier(0, IsEnabled: false) });
Check(Bytes(disabled) == halfBytes, "disabled modifier returns the unchanged detached source");
Reject(cube, () => MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapMirrorModifier(0) }), "mirror plane crossing source");
Reject(cube, () => MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapArrayModifier(2, Vector3.Zero) }), "coincident array copies");
Reject(cube, () => MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapArrayModifier(128, new(4,0,0)), new MapArrayModifier(128, new(0,4,0)) }), "modifier vertex budget overflow");

var inset = MapModelingEnhancements.InsetRegionWidth(cube, new[] { 1 }, .25f);
Closed(inset, "constant-width face inset");
Check(inset.Vertices.Skip(8).All(point => Near(Math.Abs(point[0]), .75) && Near(Math.Abs(point[1]), .75)), "constant-width inset offsets all polygon edges by the requested distance");
Check(Near(Area(inset), 24) && Near(Volume(inset), 8), "planar inset preserves total surface geometry and volume");
var scaledInsetSource = Cube(); scaledInsetSource.Transform.Scale = new[] { 2f, 1, 1 };
var scaledInset = MapModelingEnhancements.InsetRegionWidth(scaledInsetSource, new[] { 1 }, .25f);
Check(scaledInset.Vertices.Skip(8).All(point => Near(Math.Abs(point[0]), .875) && Near(Math.Abs(point[1]), .75)), "inset uses a constant world width under nonuniform object scale");
var regionInset = MapModelingEnhancements.InsetRegionWidth(filled, Enumerable.Range(5, 4), .25f);
Closed(regionInset, "multi-face region inset");
Check(Near(Volume(regionInset), 8), "interior vertices relax with the inset boundary without cracks");
Reject(cube, () => MapModelingEnhancements.InsetRegionWidth(cube, new[] { 1 }, 1.1f), "collapsed constant-width inset");
Reject(concave, () => MapModelingEnhancements.InsetRegionWidth(concave, new[] { 1 }, .1f), "concave offset region");

var beveled = MapModelingEnhancements.BevelEdge(cube, new(0, 1), .2f, segments: 3);
Closed(beveled, "segmented manifold edge bevel");
Check(Near(Volume(beveled), 7.96), "bevel removes the expected triangular prism instead of overlapping endpoint caps");
var octahedron = new MapMesh
{
    Vertices = new() { new[] { 0f,0,1 },new[] { 0f,0,-1 },new[] { 1f,0,0 },new[] { 0f,1,0 },new[] { -1f,0,0 },new[] { 0f,-1,0 } },
    Faces = new() { new[] { 0,2,3 },new[] { 0,3,4 },new[] { 0,4,5 },new[] { 0,5,2 },
        new[] { 1,3,2 },new[] { 1,4,3 },new[] { 1,5,4 },new[] { 1,2,5 } }
};
var generalBevel = MapModelingEnhancements.BevelEdge(octahedron, new(0, 2), .1f, segments: 3);
Closed(generalBevel, "four-face endpoint fan bevel");
Check(Volume(generalBevel) > 0 && Volume(generalBevel) < Volume(octahedron), "general bevel clips and caps nontrivalent endpoints with positive remaining volume");
Reject(cube, () => MapModelingEnhancements.BevelEdge(cube, new(0, 6), .1f), "nonexistent bevel edge");
Reject(cube, () => MapModelingEnhancements.BevelEdge(cube, new(0, 1), 2), "oversized bevel");

var persistent = MapModelingEnhancements.WithModifierStack(half, modifiers);
var roundTrip = JsonSerializer.Deserialize<MapMesh>(Bytes(persistent))!;
Check(roundTrip.ModifierSource is { Modifiers.Count: 2 } && roundTrip.ModifierSource.Source.ModifierSource == null,
    "canonical JSON stores one bounded original source and a typed editable modifier stack");
Check(roundTrip.ModifierSource!.Modifiers[1] is MapArrayModifier { Offset.Y: 5 }, "modifier vectors survive canonical JSON as exact XYZ coordinates");
var toggled = MapModelingEnhancements.WithModifierStack(roundTrip, new MapModelModifier[] { modifiers[0] with { Enabled = false }, modifiers[1] });
Check(Near(Volume(toggled), 12) && toggled.ModifierSource!.Source.Vertices.Count == 8,
    "toggling reevaluates original source rather than applying another modifier generation");
var repeated = MapModelingEnhancements.WithModifierStack(persistent, modifiers);
Check(Bytes(repeated) == Bytes(persistent), "repeated modifier edits cannot grow a provenance chain");
var movedStack = MapMeshEditing.Clone(persistent);
movedStack.Transform.Position = new[] { 17f, 4, -9 }; movedStack.Transform.Scale = new[] { 2f, 3, 4 }; movedStack.Label = "Moved stack";
var reevaluatedMoved = MapModelingEnhancements.WithModifierStack(movedStack, modifiers);
Check(reevaluatedMoved.Transform.Position.SequenceEqual(movedStack.Transform.Position)
    && reevaluatedMoved.ModifierSource!.Source.Transform.Scale.SequenceEqual(movedStack.Transform.Scale)
    && reevaluatedMoved.Id == movedStack.Id && reevaluatedMoved.Label == "Moved stack",
    "modifier edits preserve current resolved object identity, label and world transform in original provenance");
var firstOrder = MapModelingEnhancements.WithModifierStack(half, new MapModelModifier[] { new MapMirrorModifier(0), new MapArrayModifier(2, new(4,0,0)) });
var secondOrder = MapModelingEnhancements.WithModifierStack(half, new MapModelModifier[] { new MapArrayModifier(2, new(4,0,0)), new MapMirrorModifier(0) });
Check(new MapMeshTopology(firstOrder).FaceIslands().Length == 2 && new MapMeshTopology(secondOrder).FaceIslands().Length == 3,
    "reordering modifiers changes actual topology according to evaluation order");
var baked = MapModelingEnhancements.BakeModifierStack(persistent);
Check(baked.ModifierSource == null && Near(Volume(baked), Volume(persistent)) && baked.Id == persistent.Id,
    "explicit bake removes authoring provenance while preserving evaluated runtime geometry and identity");
Reject(persistent, () => MapModelingEnhancements.KnifeCut(persistent, Vector3.UnitX, .5f), "raw topology edit on unbaked modifier provenance");
var snapshot = new MapDocument(new(new MapDefinition { Geometry = new() { persistent } })).CaptureBuildSnapshot().CreateDefinition();
var snapshotMesh = (MapMesh)snapshot.Geometry[0];
Check(snapshotMesh.ModifierSource is { Modifiers.Count: 2 } && Bytes(snapshotMesh) == Bytes(persistent),
    "detached canonical build snapshots preserve evaluated geometry and exact self-contained modifier provenance");
var stackedDocument = new MapDocument(new(new MapDefinition
{ Geometry = new() { MapMeshEditing.Clone(persistent) }, Materials = new() { new(), new() } }));
string stackedBefore = stackedDocument.Project.Definition.Serialize(); var stackedState = stackedDocument.CurrentStateId;
bool paintRejected = false;
try { stackedDocument.PaintFaces(persistent.Id, new[] { 0 }, 1); }
catch (InvalidOperationException) { paintRejected = true; }
Check(paintRejected && stackedDocument.CurrentStateId == stackedState
    && stackedDocument.Project.Definition.Serialize() == stackedBefore,
    "canonical face paint rejects unbaked modifier geometry before changing source bytes or history");

var definition = new MapDefinition { Geometry = new() { Cube() } };
var document = new MapDocument(new(definition), Path.Combine(Path.GetTempPath(), "modeling-fixture.json"));
string documentBefore = document.Project.Definition.Serialize(); var stateBefore = document.CurrentStateId;
document.Edit("Transactional loop cut", value => value.Geometry[0] = MapModelingEnhancements.LoopCut((MapMesh)value.Geometry[0], new(0,1)), MapChangeDomain.Geometry);
string documentAfter = document.Project.Definition.Serialize();
Check(document.CurrentStateId != stateBefore && document.IsDirty, "validated proposal commits as one real document history command");
document.History.Undo(); Check(document.Project.Definition.Serialize() == documentBefore && !document.IsDirty, "one undo restores exact geometry channels and saved state");
document.History.Redo(); Check(document.Project.Definition.Serialize() == documentAfter, "redo restores exact validated topology");
var failedState = document.CurrentStateId; bool failed = false;
try { document.Edit("Invalid knife", value => value.Geometry[0] = MapModelingEnhancements.KnifeCut((MapMesh)value.Geometry[0], Vector3.Zero, 0), MapChangeDomain.Geometry); }
catch (ArgumentException) { failed = true; }
Check(failed && document.CurrentStateId == failedState && document.Project.Definition.Serialize() == documentAfter, "rejected proposal leaves live document and history untouched");

var workload = MapModelingEnhancements.EvaluateModifiers(cube, new MapModelModifier[] { new MapArrayModifier(128, new(4,0,0)) });
long allocation = GC.GetAllocatedBytesForCurrentThread(); var stopwatch = Stopwatch.StartNew();
var workloadCut = MapModelingEnhancements.KnifeCut(workload, Vector3.UnitY, 0);
stopwatch.Stop(); allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
Closed(workloadCut, "128-island knife workload");
Check(workloadCut.Vertices.Count == 1536 && Near(Volume(workloadCut), 1024), "larger detached workload keeps exact shared topology and all enclosed volume");
Console.WriteLine($"MODELING MEASURE sourceVertices={workload.Vertices.Count} resultVertices={workloadCut.Vertices.Count} resultFaces={workloadCut.Faces.Count} cpuMs={stopwatch.Elapsed.TotalMilliseconds:0.###} allocatedBytes={allocation}");
Check(allocation < 64 * 1024 * 1024, "bulk knife edge splitting stays below a measured 64 MiB allocation budget for the 1024-vertex fixture");
Console.WriteLine($"Studio modeling checks passed: {checks}.");
