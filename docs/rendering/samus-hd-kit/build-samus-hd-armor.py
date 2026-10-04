"""Add a first-pass, native-rigged Samus armor surface to the Weighted4 source."""
import bpy
import bmesh
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUTPUT_BLEND = ROOT / "Samus_HD_Work.blend"
EXPECTED = {
    "Pelvis", "L_hip", "L_knee", "L_ankle", "R_hip", "R_knee", "R_ankle",
    "Spine_1", "Spine_2", "Head_1", "L_shoulder", "L_elbow", "L_wrist",
    "L_varias2_SDK", "R_shoulder", "R_elbow", "R_varias2_SDK",
}


def mesh_surface(source):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    vertices = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    faces = [list(p.vertices) for p in mesh.polygons]
    if not faces or any(len(face) != 3 for face in faces):
        evaluated.to_mesh_clear()
        raise RuntimeError(f"{source.name} must be triangulated for surface UV projection")
    uv_layer = mesh.uv_layers.active
    face_uvs = []
    for poly in mesh.polygons:
        face_uvs.append([Vector(uv_layer.data[i].uv) for i in poly.loop_indices])
    bvh = BVHTree.FromPolygons(vertices, faces, all_triangles=True)
    evaluated.to_mesh_clear()
    return vertices, faces, face_uvs, bvh


def barycentric(p, a, b, c):
    v0 = b - a
    v1 = c - a
    v2 = p - a
    d00 = v0.dot(v0)
    d01 = v0.dot(v1)
    d11 = v1.dot(v1)
    d20 = v2.dot(v0)
    d21 = v2.dot(v1)
    denom = d00 * d11 - d01 * d01
    if abs(denom) < 1.0e-12:
        return (1.0, 0.0, 0.0)
    v = (d11 * d20 - d01 * d21) / denom
    w = (d00 * d21 - d01 * d20) / denom
    u = 1.0 - v - w
    vals = [max(0.0, u), max(0.0, v), max(0.0, w)]
    total = sum(vals)
    return tuple(x / total for x in vals)


def project_uv(point, source_info):
    verts, faces, face_uvs, bvh = source_info
    hit = bvh.find_nearest(point)
    if hit is None or hit[2] is None:
        return (0.5, 0.5)
    nearest, _normal, face_index, _distance = hit
    indices = faces[face_index]
    weights = barycentric(nearest, verts[indices[0]], verts[indices[1]], verts[indices[2]])
    uv = sum((face_uvs[face_index][i] * weights[i] for i in range(3)), Vector((0.0, 0.0)))
    return (float(uv.x), float(uv.y))


def plate(center, width, height, depth, bone, profile, bevel=0.008):
    """Extruded, tapered armor silhouette with a small rounded edge bevel."""
    center = Vector(center)
    bm = bmesh.new()
    front_y = center.y - depth * 0.5
    back_y = center.y + depth * 0.5
    front = [bm.verts.new((center.x + x * width * 0.5, front_y,
                           center.z + z * height * 0.5)) for x, z in profile]
    back = [bm.verts.new((center.x + x * width * 0.5, back_y,
                          center.z + z * height * 0.5)) for x, z in profile]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for index in range(len(profile)):
        nxt = (index + 1) % len(profile)
        bm.faces.new((front[index], back[index], back[nxt], front[nxt]))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, depth * 0.30),
                    segments=6, profile=0.5, clamp_overlap=True)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.verts.index_update()
    points = [vertex.co.copy() for vertex in bm.verts]
    faces = [tuple(vertex.index for vertex in face.verts) for face in bm.faces]
    # Keep the broad plate faces planar; smooth only the beveled perimeter.
    smooth_flags = [abs(face.normal.y) < 0.985 for face in bm.faces]
    bm.free()
    return {"points": points, "faces": faces, "smooth_flags": smooth_flags, "bone": bone}


def torus(axis, center, major, minor, bone, major_segments=48, minor_segments=12,
          ellipse_y=1.0):
    center = Vector(center)
    points = []
    faces = []
    for i in range(major_segments):
        theta = 2.0 * math.pi * i / major_segments
        ct, st = math.cos(theta), math.sin(theta)
        for j in range(minor_segments):
            phi = 2.0 * math.pi * j / minor_segments
            cp, sp = math.cos(phi), math.sin(phi)
            if axis == "X":
                p = Vector((minor * sp, (major + minor * cp) * ct,
                            (major + minor * cp) * st))
            elif axis == "Z":
                p = Vector(((major + minor * cp) * ct,
                            ellipse_y * (major + minor * cp) * st,
                            minor * sp))
            else:
                raise ValueError(axis)
            points.append(center + p)
    for i in range(major_segments):
        ni = (i + 1) % major_segments
        for j in range(minor_segments):
            nj = (j + 1) % minor_segments
            a = i * minor_segments + j
            b = ni * minor_segments + j
            c = ni * minor_segments + nj
            d = i * minor_segments + nj
            faces.append((a, b, c, d))
    return {"points": points, "faces": faces, "bone": bone}


def build_object(name, material_name, features, source, armature):
    source_info = mesh_surface(source)
    points, faces, uv_values, point_bones, smooth_flags = [], [], [], [], []
    for feature in features:
        offset = len(points)
        bone_name = feature["bone"]
        if bone_name not in EXPECTED:
            raise RuntimeError(f"Unknown native bone: {bone_name}")
        pose_bone = armature.pose.bones.get(bone_name)
        rest_bone = armature.data.bones.get(bone_name)
        if pose_bone is None or rest_bone is None:
            raise RuntimeError(f"Missing native bone: {bone_name}")
        pose_to_rest = (armature.matrix_world @ rest_bone.matrix_local @
                        pose_bone.matrix.inverted() @ armature.matrix_world.inverted())
        for point in feature["points"]:
            points.append(pose_to_rest @ point)
            uv_values.append(project_uv(point, source_info))
            point_bones.append(bone_name)
        faces.extend([tuple(offset + index for index in face) for face in feature["faces"]])
        smooth_flags.extend(feature.get("smooth_flags", [True] * len(feature["faces"])))

    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(points, [], faces)
    mesh.update()
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for poly_index, poly in enumerate(mesh.polygons):
        poly.use_smooth = smooth_flags[poly_index]
        for loop_index in poly.loop_indices:
            vertex_index = mesh.loops[loop_index].vertex_index
            uv_layer.data[loop_index].uv = uv_values[vertex_index]
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = armature
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    material = bpy.data.materials.get(material_name)
    if material is None:
        raise RuntimeError(f"Native material missing: {material_name}")
    obj.data.materials.append(material)
    bone_groups = {}
    for index, bone_name in enumerate(point_bones):
        bone_groups.setdefault(bone_name, []).append(index)
    for bone_name, indices in bone_groups.items():
        group = obj.vertex_groups.new(name=bone_name)
        group.add(indices, 1.0, "REPLACE")
    modifier = obj.modifiers.new("Native Samus Armature", "ARMATURE")
    modifier.object = armature
    modifier.use_vertex_groups = True
    return obj


scene = bpy.context.scene
armature = bpy.data.objects.get("Armature")
if armature is None or armature.type != "ARMATURE":
    raise RuntimeError("Native Samus Armature is missing")

# Remove outputs from a previous run so the script is safe to re-run.
for obj in list(scene.objects):
    if (obj.name.startswith("HD_Armor_Surface") or
            obj.name.startswith("HD_Cannon_Detail") or
            obj.name.startswith("HD_Visor")):
        bpy.data.objects.remove(obj, do_unlink=True)
for mesh in list(bpy.data.meshes):
    if mesh.users == 0 and mesh.name.startswith("HD_"):
        bpy.data.meshes.remove(mesh)

pauldron = [(-0.95, -0.16), (-0.78, -0.62), (-0.30, -0.92),
            (0.35, -0.88), (0.78, -0.58), (1.00, -0.08),
            (0.84, 0.43), (0.42, 0.84), (-0.18, 0.98),
            (-0.72, 0.70), (-1.00, 0.22)]
chest = [(-0.70, -0.88), (0.0, -1.0), (0.70, -0.88),
         (0.93, -0.20), (1.0, 0.45), (0.67, 1.0),
         (-0.67, 1.0), (-1.0, 0.45), (-0.93, -0.20)]
inset = [(-0.82, -0.10), (-0.62, -0.70), (0.0, -1.0),
         (0.62, -0.70), (0.82, -0.10), (0.60, 0.70),
         (0.0, 1.0), (-0.60, 0.70)]
segment = [(-0.78, -0.45), (-0.55, -1.0), (0.55, -1.0),
           (0.78, -0.45), (0.78, 0.45), (0.55, 1.0),
           (-0.55, 1.0), (-0.78, 0.45)]
thigh = [(-0.65, -0.82), (0.0, -1.0), (0.65, -0.82),
         (0.90, -0.35), (0.80, 0.55), (0.55, 1.0),
         (-0.55, 1.0), (-0.80, 0.55), (-0.90, -0.35)]
kneepad = [(-0.70, -0.70), (0.0, -1.0), (0.70, -0.70),
           (1.0, 0.10), (0.68, 0.85), (0.0, 1.0),
           (-0.68, 0.85), (-1.0, 0.10)]
shin = [(-0.72, -0.80), (0.0, -1.0), (0.72, -0.80),
        (0.84, -0.25), (0.68, 0.70), (0.42, 1.0),
        (-0.42, 1.0), (-0.68, 0.70), (-0.84, -0.25)]
boot = [(-0.82, -0.30), (-0.62, -0.90), (0.62, -0.90),
        (0.82, -0.30), (0.72, 0.62), (0.40, 1.0),
        (-0.40, 1.0), (-0.72, 0.62)]

body_features = [
    plate((0.0, -0.137, 1.702), 0.190, 0.105, 0.022, "Head_1", inset, 0.007),
    plate((-0.337, -0.055, 1.535), 0.285, 0.155, 0.035, "L_shoulder", pauldron, 0.009),
    plate((0.337, -0.055, 1.535), 0.285, 0.155, 0.035, "R_shoulder", pauldron, 0.009),
    plate((0.0, -0.139, 1.426), 0.325, 0.172, 0.026, "Spine_2", chest, 0.008),
    plate((-0.090, -0.153, 1.461), 0.102, 0.060, 0.016, "Spine_2", inset, 0.005),
    plate((0.090, -0.153, 1.461), 0.102, 0.060, 0.016, "Spine_2", inset, 0.005),
    plate((0.0, -0.143, 1.334), 0.190, 0.050, 0.018, "Spine_1", segment, 0.005),
    plate((0.0, -0.138, 1.285), 0.176, 0.044, 0.018, "Pelvis", segment, 0.005),
    plate((0.0, -0.132, 1.112), 0.210, 0.080, 0.022, "Pelvis", thigh, 0.006),
    plate((-0.105, -0.130, 0.855), 0.116, 0.214, 0.022, "L_hip", thigh, 0.006),
    plate((0.105, -0.130, 0.855), 0.116, 0.214, 0.022, "R_hip", thigh, 0.006),
    plate((-0.105, -0.145, 0.642), 0.118, 0.078, 0.020, "L_knee", kneepad, 0.006),
    plate((0.105, -0.145, 0.642), 0.118, 0.078, 0.020, "R_knee", kneepad, 0.006),
    plate((-0.105, -0.137, 0.393), 0.092, 0.206, 0.020, "L_knee", shin, 0.006),
    plate((0.105, -0.137, 0.393), 0.092, 0.206, 0.020, "R_knee", shin, 0.006),
    plate((-0.105, -0.116, 0.105), 0.112, 0.071, 0.022, "L_ankle", boot, 0.006),
    plate((0.105, -0.116, 0.105), 0.112, 0.071, 0.022, "R_ankle", boot, 0.006),
    plate((-0.716, -0.079, 1.505), 0.142, 0.074, 0.022, "L_elbow", segment, 0.006),
]
body_features.extend([
    torus("Z", (0.0, 0.020, 1.147), 0.132, 0.009, "Pelvis", ellipse_y=0.78),
    torus("X", (-0.682, 0.037, 1.509), 0.073, 0.008, "L_elbow"),
    torus("X", (-0.894, 0.037, 1.505), 0.060, 0.008, "L_wrist"),
])

gun_features = [
    torus("X", (0.708, 0.020, 1.510), 0.080, 0.009, "R_elbow"),
    torus("X", (0.874, 0.020, 1.510), 0.080, 0.009, "R_elbow"),
    torus("X", (1.035, 0.020, 1.510), 0.080, 0.009, "R_elbow"),
]

armor = build_object("HD_Armor_Surface", "body", body_features,
                     bpy.data.objects["geom3_obj"], armature)
cannon = build_object("HD_Cannon_Detail", "Gun", gun_features,
                      bpy.data.objects["geom1_obj"], armature)
visor_feature = plate((0.0, -0.155, 1.655), 0.112, 0.040, 0.012,
                      "Head_1", inset, 0.004)
visor = build_object("HD_Visor", "body_full_bright", [visor_feature],
                     bpy.data.objects["geom4_obj"], armature)

triangles = 0
for obj in scene.objects:
    if obj.type == "MESH":
        triangles += sum(len(p.vertices) - 2 for p in obj.data.polygons)
print(f"[Samus HD armor] added {len(body_features)} body/limb details and "
      f"{len(gun_features)} cannon bands; scene total {triangles:,} triangles")
print(f"[Samus HD armor] added objects: {armor.name}, {cannon.name}, {visor.name}")
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND))
print(f"[Samus HD armor] saved {OUTPUT_BLEND}")
