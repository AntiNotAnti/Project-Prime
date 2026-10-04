# Project Prime Weighted4 character exporter
#
# Load/refine the generated native Blender reference first. The mesh may use
# smooth weights, but every non-zero armature influence must target one of the
# expected native MPH nodes and each vertex may use at most four influences.

import bpy
from pathlib import Path

ASSET_LABEL = "Samus/ViewModel"
EXPECTED_BONES = set(["Main","centerGlow","cp","decals","g1","g2","g5","glowBlue","glowGreen","glowOrange","glowRed","glowYellow","housing","noz1","noz2","noz3","noz4","ring1","ring2","ring3"])
EXPECTED_MATERIALS = set(["GlowBlue","GlowGreen","GlowOrange","GlowRed","GlowYellow","lambert10","lambert11"])
RELATIVE_OUTPUT = "starter/samus/viewmodel_weighted4.glb"
WEIGHT_EPSILON = 1.0e-6
MATRIX_EPSILON = 1.0e-5

def fail(message):
    raise RuntimeError("[Project Prime Weighted4 export] " + message)

def script_root():
    value = globals().get("__file__")
    if value:
        return Path(value).resolve().parent
    if bpy.data.filepath:
        return Path(bpy.data.filepath).resolve().parent
    return Path.cwd()

def armature_for(obj):
    for modifier in obj.modifiers:
        if modifier.type == 'ARMATURE' and modifier.object is not None:
            return modifier.object
    if obj.parent is not None and obj.parent.type == 'ARMATURE':
        return obj.parent
    return None

def matrix_is_identity(matrix):
    for row in range(4):
        for col in range(4):
            expected = 1.0 if row == col else 0.0
            if abs(matrix[row][col] - expected) > MATRIX_EPSILON:
                return False
    return True

def validate_materials(obj):
    for slot in obj.material_slots:
        if slot.material is None:
            continue
        if slot.material.name not in EXPECTED_MATERIALS:
            fail(f"{obj.name}: material '{slot.material.name}' is not native for "
                 f"{ASSET_LABEL}. Rename it to one of {sorted(EXPECTED_MATERIALS)}")
    if not obj.material_slots:
        fail(f"{obj.name}: no material slots were found.")

def validate_weights(obj, armature):
    armature_bones = {bone.name for bone in armature.data.bones}
    group_names = {group.index: group.name for group in obj.vertex_groups}
    used = set()
    for vertex in obj.data.vertices:
        influences = []
        for assignment in vertex.groups:
            if assignment.weight <= WEIGHT_EPSILON:
                continue
            name = group_names.get(assignment.group)
            if name is None or name not in armature_bones:
                continue
            if name not in EXPECTED_BONES:
                fail(f"{obj.name}: vertex {vertex.index} uses unmapped bone '{name}'.")
            influences.append((name, assignment.weight))
            used.add(name)
        if not influences:
            fail(f"{obj.name}: vertex {vertex.index} has no native bone influence.")
        if len(influences) > 4:
            influences.sort(key=lambda value: value[1], reverse=True)
            fail(f"{obj.name}: vertex {vertex.index} has {len(influences)} influences; "
                 f"Weighted4 allows four. Strongest: {influences[:6]}")
        total = sum(weight for _, weight in influences)
        if total <= WEIGHT_EPSILON:
            fail(f"{obj.name}: vertex {vertex.index} has zero total weight.")
    return used

def build():
    sources = []
    armature = None
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH':
            continue
        candidate = armature_for(obj)
        if candidate is None:
            continue
        group_names = {group.name for group in obj.vertex_groups}
        if not group_names.intersection(EXPECTED_BONES):
            continue
        if armature is None:
            armature = candidate
        elif armature != candidate:
            fail("All Weighted4 mesh objects must use the same Armature.")
        sources.append(obj)

    if armature is None or not sources:
        fail("No skinned mesh using the expected native armature groups was found.")

    bone_names = {bone.name for bone in armature.data.bones}
    missing = sorted(EXPECTED_BONES - bone_names)
    if missing:
        fail(f"Armature is missing expected native bones: {missing}")

    used_bones = set()
    for source in sources:
        # The runtime deliberately rejects a transform on the glTF mesh node;
        # the skin/inverse-bind matrices own deformation instead.
        relative = armature.matrix_world.inverted() @ source.matrix_world
        if not matrix_is_identity(relative):
            fail(f"{source.name}: mesh transform is not identity relative to Armature. "
                 "Apply/alignment transforms before export.")
        validate_materials(source)
        used_bones.update(validate_weights(source, armature))

    if len(used_bones) > 32:
        fail(f"Skin uses {len(used_bones)} joints; Project Prime supports 32.")

    previous_deform = {bone.name: bone.use_deform for bone in armature.data.bones}
    previous_selection = {obj.name: obj.select_get() for obj in bpy.context.scene.objects}
    previous_active = bpy.context.view_layer.objects.active

    try:
        # Blender's glTF exporter can limit skin joints to deform bones. Keep
        # the original data intact by restoring the flags immediately after.
        for bone in armature.data.bones:
            bone.use_deform = bone.name in EXPECTED_BONES

        bpy.ops.object.select_all(action='DESELECT')
        armature.select_set(True)
        for source in sources:
            source.select_set(True)
        bpy.context.view_layer.objects.active = armature

        output_path = (script_root() / RELATIVE_OUTPUT).resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)

        kwargs = dict(
            filepath=str(output_path),
            export_format='GLB',
            use_selection=True,
            export_animations=False,
            export_materials='EXPORT',
            export_yup=True
        )
        properties = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
        if 'export_skins' in properties:
            kwargs['export_skins'] = True
        if 'export_def_bones' in properties:
            kwargs['export_def_bones'] = True
        if 'export_morph' in properties:
            kwargs['export_morph'] = False
        bpy.ops.export_scene.gltf(**kwargs)
        print(f"[Project Prime Weighted4 export] {ASSET_LABEL}: "
              f"{len(sources)} mesh object(s), {len(used_bones)} used joints")
        print(f"[Project Prime Weighted4 export] wrote {output_path}")
        return str(output_path)
    finally:
        for bone in armature.data.bones:
            if bone.name in previous_deform:
                bone.use_deform = previous_deform[bone.name]
        bpy.ops.object.select_all(action='DESELECT')
        for obj in bpy.context.scene.objects:
            if previous_selection.get(obj.name, False):
                obj.select_set(True)
        if previous_active is not None and previous_active.name in bpy.context.scene.objects:
            bpy.context.view_layer.objects.active = previous_active

if __name__ == "__main__":
    build()