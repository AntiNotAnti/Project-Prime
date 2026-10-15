#!/usr/bin/env python3
"""Exercise the actual Studio child-process protocol without game assets or a UI.

Usage: python3 tools/radiant-mesh-worker-check/check.py path/to/ProjectPrimeStudio.dll
       python3 tools/radiant-mesh-worker-check/check.py path/to/ProjectPrimeStudio
The caller builds Studio first; this check never starts a competing build.
"""
import copy
import json
import math
import pathlib
import subprocess
import sys
import time
import uuid

studio = pathlib.Path(sys.argv[1]).resolve()
command = (["dotnet", str(studio)] if studio.suffix == ".dll" else [str(studio)]) + ["--radiant-mesh-worker"]
checks = 0


def check(value, label):
    global checks
    if not value:
        raise AssertionError(label)
    checks += 1
    print("PASS", label)


def invoke(request, success=True):
    encoded = request if isinstance(request, bytes) else json.dumps(request, allow_nan=False).encode("utf-8")
    completed = subprocess.run(command, input=encoded, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=35)
    try:
        result = json.loads(completed.stdout)
    except Exception as error:
        raise AssertionError(f"Worker did not emit exactly one JSON reply: {completed.stdout[:500]!r}; stderr={completed.stderr[:500]!r}") from error
    check(result.get("schema") == 1 and result.get("success") is success and (completed.returncode == 0) is success,
          "child process returns matching schema, status and exit code")
    if not success:
        check(isinstance(result.get("error"), str) and bool(result["error"]) and "payload" not in result,
              "failure exposes an error without a partially mutated payload")
    return result


def quad():
    return {
        "kind": "mesh", "id": str(uuid.uuid4()), "label": "Worker fixture", "material": 0, "solid": False,
        "terrain": "Metal", "shade": .75, "layer": "Fixture", "damaging": True,
        "collisionOnly": True, "ignorePlayers": True, "reflectBeams": True, "slipperiness": 3,
        "transform": {"position": [3, 4, 5], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]},
        "vertices": [[0, 0, 0], [2, 0, 0], [2, 2, 0], [0, 2, 0]], "faces": [[0, 1, 2, 3]],
        "faceMaterials": [0], "faceTexcoords": [[[0, 0], [64, 0], [64, 64], [0, 64]]],
        "uv": {"scale": [2, 3], "offset": [4, 5], "rotation": 15},
        "faceUv": {"0": {"scale": [1, 2], "offset": [3, 4], "rotation": 30}},
    }


def request(operation, mesh=None, **kwargs):
    base = {
        "schema": 1, "operation": operation,
        "payload": {"schema": 1, "units": 16, "mesh": mesh or quad(),
                    "materials": [{"name": "fixture", "texScale": 16, "width": 128, "height": 64},
                                  {"name": "second", "texScale": 8, "width": 64, "height": 32}]},
        "selection": {"faces": [0], "vertices": [], "edges": []}, "amount": .25, "segments": 1,
    }
    base.update(kwargs)
    return base


def cube():
    mesh = quad()
    mesh.update(vertices=[[-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1],
                          [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1]],
                faces=[[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [3, 7, 6, 2], [0, 4, 7, 3], [1, 2, 6, 5]],
                faceUv={}, faceTexcoords=[], faceMaterials=[], uv={"scale": [1, 1], "offset": [0, 0], "rotation": 0}, solid=True)
    return mesh


original = request("extrudeRegion")
original["payload"]["originalGeometry"] = {"kind": "box", "id": original["payload"]["mesh"]["id"], "future": [3, "opaque"]}
original["payload"]["originalMeshSha256"] = "0" * 64
original_text = json.dumps(original, sort_keys=True)
extruded = invoke(original)
mesh = extruded["payload"]["mesh"]
check(len(mesh["vertices"]) == 8 and len(mesh["faces"]) == 5 and mesh["vertices"][4][2] == .25,
      "region extrusion uses canonical geometry and compaction")
check(mesh["id"] == original["payload"]["mesh"]["id"] and mesh["layer"] == "Fixture" and mesh["ignorePlayers"] and mesh["collisionOnly"],
      "geometry edit preserves identity and authored collision metadata")
check(mesh["faceTexcoords"][0] == original["payload"]["mesh"]["faceTexcoords"][0], "extrusion preserves cap corner UVs")
check(extruded["payload"]["originalGeometry"] == original["payload"]["originalGeometry"]
      and extruded["payload"]["originalMeshSha256"] == original["payload"]["originalMeshSha256"], "worker preserves opaque interchange provenance")
check(extruded["selection"]["faces"] == [] and json.dumps(original, sort_keys=True) == original_text,
      "geometry selection is cleared and caller request remains immutable")

inset = invoke(request("Inset face"))["payload"]["mesh"]
check(len(inset["vertices"]) == 8 and len(inset["faces"]) == 5 and inset["faceTexcoords"][0][0] == [8, 8],
      "face inset interpolates canonical corner UVs")
beveled = invoke(request("Bevel", cube(), mode="Edge", amount=.125, segments=3,
                         selection={"faces": [], "vertices": [], "edges": [[0, 1]]}))["payload"]["mesh"]
check(len(beveled["vertices"]) == 14 and len(beveled["faces"]) == 9,
      "segmented bevel reuses canonical manifold endpoint and strip construction")
collapsed = invoke(request("Collapse edge", cube(), mode="Edge",
                           selection={"faces": [], "vertices": [], "edges": [[0, 1]]}))["payload"]["mesh"]
check(len(collapsed["vertices"]) == 7 and len(collapsed["faces"]) == 6, "edge collapse removes and compacts the canonical vertex ring")
merged = invoke(request("Merge first", cube(), mode="Vertex",
                        selection={"faces": [], "vertices": [1, 0], "edges": []}))["payload"]["mesh"]
check(merged["vertices"][0] == [1, -1, -1], "merge-first preserves the caller's ordered vertex selection")
connected_vertices = invoke(request("Connect vertices", mode="Vertex",
                                    selection={"faces": [], "vertices": [0, 2], "edges": []}))["payload"]["mesh"]
check(len(connected_vertices["faces"]) == 2 and all(len(f) == 3 for f in connected_vertices["faces"]),
      "vertex connect splits a polygon and preserves aligned corner channels")
ripped = invoke(request("Rip vertex", cube(), mode="Vertex", face=0,
                        selection={"faces": [], "vertices": [0], "edges": []}))["payload"]["mesh"]
check(len(ripped["vertices"]) == 9 and ripped["faces"][0][0] == 8, "vertex rip duplicates only the selected face corner")

# Both sides of a shared edge carry intentionally different corner UV coordinates.
seam = quad()
seam.update(vertices=[[0, 0, 0], [2, 0, 0], [2, 2, 0], [0, 2, 0], [4, 0, 0], [4, 2, 0]],
            faces=[[0, 1, 2, 3], [1, 4, 5, 2]], faceMaterials=[0, 1], faceUv={},
            faceTexcoords=[[[0, 0], [64, 0], [64, 64], [0, 64]], [[100, 0], [164, 0], [164, 64], [100, 64]]])
split = invoke(request("splitEdge", seam, mode="Edge", amount=.5, selection={"faces": [], "vertices": [], "edges": [[1, 2]]}))["payload"]["mesh"]
check(len(split["vertices"]) == 7 and [len(f) for f in split["faces"]] == [5, 5], "shared edge split inserts one shared geometric vertex")
check(split["faceTexcoords"][0][2] == [64, 32] and split["faceTexcoords"][1][-1] == [100, 32],
      "shared edge split preserves distinct corner UV seams and per-face materials")

triangulated = invoke(request("triangulate"))["payload"]["mesh"]
check(len(triangulated["faces"]) == 2 and all(len(f) == 3 for f in triangulated["faces"])
      and all(len(uv) == 3 for uv in triangulated["faceTexcoords"]), "ear clipping keeps triangle corner UV channels aligned")
dissolved = invoke(request("Dissolve triangles", triangulated))["payload"]["mesh"]
check(len(dissolved["faces"]) == 1 and len(dissolved["faces"][0]) == 4, "canonical triangle dissolve restores the polygon")

connected = invoke(request("Select linked", seam))["selection"]
check(connected["faces"] == [0, 1], "connected selection follows canonical topology")
boundary = invoke(request("Select boundary", seam))["selection"]
check(boundary["mode"] == "Edge" and len(boundary["edges"]) == 6 and [1, 2] not in boundary["edges"], "boundary selection excludes the shared interior edge")
grown = invoke(request("Grow", seam))["selection"]
check(grown["faces"] == [0, 1], "face selection growth includes the adjacent face")
shrunk = invoke(request("Shrink", seam))["selection"]
check(shrunk["faces"] == [], "face selection shrink removes the exposed selected face")

transformed = invoke(request("uvTransform", uv={"scale": [2, .5], "offset": [4, 8], "rotation": 90}))
uv = transformed["payload"]["mesh"]["faceTexcoords"][0]
check(math.isclose(uv[1][0], 4, abs_tol=1e-4) and math.isclose(uv[1][1], 136, abs_tol=1e-4)
      and transformed["selection"]["faces"] == [0], "UV transform uses texels and retains valid selection")
fitted = invoke(request("uvFit"))["payload"]["mesh"]["faceTexcoords"][0]
check(max(p[0] for p in fitted) == 128 and max(p[1] for p in fitted) == 64, "UV fit uses material texture dimensions")
fit_width = invoke(request("uvFit", horizontal=1, vertical=0, onlyDimension=False))["payload"]["mesh"]["faceTexcoords"][0]
check(max(p[0] for p in fit_width) == 128 and max(p[1] for p in fit_width) == 128,
      "native Fit Width preserves texture aspect density across both dimensions")
fit_width_only = invoke(request("uvFit", horizontal=1, vertical=0, onlyDimension=True))["payload"]["mesh"]["faceTexcoords"][0]
check(max(p[0] for p in fit_width_only) == 128 and max(p[1] for p in fit_width_only) == 64,
      "native Fit Width Only preserves the unrequested dimension")
texdef_mesh = quad()
texdef_mesh["faceUv"] = {}
texdef_mesh["uv"] = {"scale": [1, 1], "offset": [0, 0], "rotation": 0}
texdef = invoke(request("uvSetTexdef", texdef_mesh, hScale=-1, hShift=7, rotation=90))["payload"]["mesh"]
check(math.isclose(texdef["faceTexcoords"][0][1][0], 7, abs_tol=1e-4)
      and math.isclose(texdef["faceTexcoords"][0][1][1], -64, abs_tol=1e-4)
      and texdef["faceUv"]["0"]["scale"] == [1, 1], "absolute native texdef retains explicit corners and supports signed UV flips")
texdef_delta = invoke(request("uvSetTexdef", texdef_mesh, hScaleDelta=-1, hShiftDelta=5, vShiftDelta=10, rotationDelta=90))["payload"]["mesh"]
check(math.isclose(texdef_delta["faceTexcoords"][0][1][0], 5, abs_tol=1e-4)
      and math.isclose(texdef_delta["faceTexcoords"][0][1][1], -54, abs_tol=1e-4),
      "relative texdef commands preserve corners and mirror when Quake scale crosses zero")
reset_input = request("uvReset", seam)
reset = invoke(reset_input)["payload"]["mesh"]
check(reset["faceTexcoords"][0] is None and reset["faceTexcoords"][1] == seam["faceTexcoords"][1]
      and reset["uv"] == seam["uv"], "UV reset only clears selected explicit UVs")
assigned = invoke(request("assignMaterial", material=1))["payload"]["mesh"]
check(assigned["faceMaterials"] == [1], "material assignment uses the canonical per-face channel")
same_material = invoke(request("Select faces with same material", seam))["selection"]
check(same_material["faces"] == [0], "same-material selection respects distinct per-face material bindings")
all_faces = invoke(request("Select all faces", seam))["selection"]
check(all_faces["faces"] == [0, 1] and all_faces["mode"] == "Face", "native all-faces selection label uses face mode")

duplicated_input = request("Duplicate to object")
duplicated = invoke(duplicated_input)
check(duplicated["payload"]["mesh"]["id"] == duplicated_input["payload"]["mesh"]["id"]
      and len(duplicated["additionalMeshes"]) == 1
      and duplicated["additionalMeshes"][0]["id"] != duplicated_input["payload"]["mesh"]["id"], "duplicate to object mints only the new object identity")
separated = invoke(request("Separate"))
check(separated["removeSource"] and separated["payload"]["mesh"] is None and len(separated["additionalMeshes"]) == 1,
      "separating every face explicitly removes the empty source")
joined = invoke(request("Join meshes", meshes=[quad()]))["payload"]["mesh"]
check(len(joined["faces"]) == 2 and len(joined["vertices"]) == 8 and joined["transform"]["position"] == [0, 0, 0],
      "join bakes world transforms using the canonical join algorithm")
deleted = invoke(request("Delete"))
check(deleted["removeSource"] and deleted["payload"]["mesh"] is None, "deleting the last face returns explicit source removal")

nonplanar = quad()
nonplanar["vertices"][2][2] = .125
invoke(request("Select boundary", nonplanar))
check(True, "legacy nonplanarity warnings do not prohibit selection edits")

analyze_mesh = copy.deepcopy(nonplanar)
analyze_mesh["solid"] = True
analyze_mesh["locked"] = True
analyze_mesh["vertices"].append([9, 9, 9])
analyze_selection = {"faces": [0], "vertices": [3, 0], "edges": [[1, 0]], "mode": "Vertex"}
analyze_request = request("analyzeMesh", analyze_mesh, selection=analyze_selection)
analyzed = invoke(analyze_request)
check(analyzed["payload"] == analyze_request["payload"] and analyzed["selection"] == analyze_selection,
      "analysis preserves the entire payload and ordered selection exactly, including locked geometry")
check(any(issue["severity"] == "Warning" and issue.get("face") == 0 and issue.get("vertices") == [0, 1, 2, 3]
          and "not planar" in issue["message"] for issue in analyzed["issues"])
      and any("open boundary" in issue["message"] for issue in analyzed["issues"])
      and any(issue["name"] == "Cleanup" and issue["cleanup"] for issue in analyzed["issues"]),
      "analysis exposes canonical nonplanarity, open-boundary and cleanup diagnostics with selection locations")
degenerate = quad()
degenerate["vertices"] = [[0, 0, 0], [1, 0, 0], [2, 0, 0], [3, 0, 0]]
analyzed_error = invoke(request("analyzeMesh", degenerate))
check(any(issue["severity"] == "Error" and issue.get("face") == 0 and "zero area" in issue["message"]
          for issue in analyzed_error["issues"]), "read-only analysis reports canonical surface errors without attempting a repair")
inconsistent = copy.deepcopy(seam)
inconsistent["faces"][1].reverse()
inconsistent["faceTexcoords"][1].reverse()
edge_issues = invoke(request("analyzeMesh", inconsistent))["issues"]
check(any(issue["severity"] == "Warning" and issue.get("edge") == [1, 2] and issue.get("vertices") == [1, 2]
          for issue in edge_issues), "analysis exposes exact shared-edge endpoints for native repair navigation")
clean_issues = invoke(request("analyzeMesh", cube()))["issues"]
check(clean_issues == [], "analysis returns an explicit empty issue list for a clean closed mesh")

# A real scene can exceed both the old 64-object and 65,535 aggregate vertex
# limits while each source mesh stays legal. Context meshes are ephemeral.
snap_context = []
for context_index in range(65):
    context_mesh = quad()
    context_mesh["solid"] = True
    context_mesh["transform"]["position"][2] = 6 + context_index
    context_mesh["vertices"].extend([[100 + i, 100, 100] for i in range(1024)])
    snap_context.append(context_mesh)
snap_request = request("Snap to surface", meshes=snap_context, mode="Vertex",
                       selection={"faces": [], "vertices": [0], "edges": []})
snapped = invoke(snap_request)
check(snapped["payload"]["mesh"]["vertices"][0] == [0, 0, 1] and "additionalMeshes" not in snapped
      and len(snap_request["meshes"]) == 65, "surface snap accepts a bounded full-scene context without creating context objects")
join_over_budget = copy.deepcopy(snap_request)
join_over_budget["operation"] = "Join meshes"
invoke(join_over_budget, False)

for malformed in (b"", b"{}{}", b'{"schema":1,"schema":1}', b'{"schema":1,"amount":NaN}', b'\xff'):
    invoke(malformed, False)
invalid_requests = []
invalid_requests.append(request("notAnOperation"))
invalid_requests.append(request("Extrude region", selection={"faces": [999], "vertices": [], "edges": []}))
invalid_requests.append(request("Split edge", mode="Edge", selection={"faces": [], "vertices": [], "edges": [[0, 2]]}))
invalid_requests.append(request("Extrude region", segments=65))
invalid_requests.append(request("Assign material", material=99))
invalid_requests.append(request("uvSetTexdef", hScale=0))
invalid_requests.append(request("uvSetProjection", uv={"scale": [-1, 1], "offset": [0, 0], "rotation": 0}))
bad_uv = request("uvFit")
bad_uv["payload"]["mesh"]["faceTexcoords"][0].pop()
invalid_requests.append(bad_uv)
missing_id = request("Triangulate")
del missing_id["payload"]["mesh"]["id"]
invalid_requests.append(missing_id)
locked = request("Extrude region")
locked["payload"]["mesh"]["locked"] = True
invalid_requests.append(locked)
invalid_scale = request("uvFit")
invalid_scale["payload"]["mesh"]["transform"]["scale"][0] = 0
invalid_requests.append(invalid_scale)
invalid_geometry = request("uvFit")
invalid_geometry["payload"]["mesh"]["vertices"][2] = [1, 0, 0]
invalid_geometry["payload"]["mesh"]["vertices"][3] = [0, 0, 0]
invalid_requests.append(invalid_geometry)
for malformed in invalid_requests:
    invoke(malformed, False)
invoke(b" " * (32 * 1024 * 1024 + 1), False)

# Keep stdin open without EOF: the process must report its own bounded read
# deadline rather than depending on the native client's outer kill timeout.
started = time.monotonic()
waiting = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
try:
    waiting.wait(timeout=15)
    timed_out = json.loads(waiting.stdout.read())
    check(waiting.returncode != 0 and timed_out["success"] is False and "timed out" in timed_out["error"]
          and time.monotonic() - started < 15, "worker exits with JSON error when the parent never closes stdin")
finally:
    if waiting.poll() is None:
        waiting.kill()
        waiting.wait(timeout=5)
    waiting.stdin.close()
    waiting.stdout.close()
    waiting.stderr.close()

closed_started = time.monotonic()
closed = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
try:
    closed.stdout.close()
    closed.stdin.write(json.dumps(request("Triangulate")).encode("utf-8"))
    closed.stdin.close()
    closed.wait(timeout=5)
    # Console's standard stream suppresses EPIPE on some platforms. Either exit
    # code is valid after the consumer disappears; bounded termination matters.
    check(time.monotonic() - closed_started < 5, "worker exits promptly when the parent closes the result pipe")
finally:
    if closed.poll() is None:
        closed.kill()
        closed.wait(timeout=5)
    closed.stderr.close()

print(f"PASS {checks} Radiant mesh worker checks")
