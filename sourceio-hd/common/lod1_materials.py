"""Retain the accepted LOD0 material contract on a simplified native LOD1.

Mesh names identify the source primitive, including separate Weavel alpha
layers with the same native material name. Image payloads and texture indices
are copied exactly; only explicit native LOD1 material names change.
"""
import copy, hashlib, json, struct
from pathlib import Path
from glb import load, read, image_bytes

def preserve_materials(source, target, primitive_sources, names, output):
    old, old_blob = load(source)
    doc, binary = load(target)
    original_geometry = {i: read(doc, binary, i) for i in range(len(doc['accessors']))}
    image_views = {i['bufferView'] for i in doc.get('images', [])}
    blob = bytearray(); views = []; mapping = {}
    for i, view in enumerate(doc['bufferViews']):
        if i in image_views: continue
        while len(blob) % 4: blob.append(0)
        start = view.get('byteOffset', 0)
        replacement = dict(view, byteOffset=len(blob), buffer=0)
        mapping[i] = len(views); views.append(replacement)
        blob.extend(binary[start:start + view['byteLength']])
    for accessor in doc['accessors']:
        accessor['bufferView'] = mapping[accessor['bufferView']]
    doc['bufferViews'] = views
    doc['images'] = copy.deepcopy(old.get('images', []))
    for i, image in enumerate(doc['images']):
        payload = image_bytes(old, old_blob, i)
        while len(blob) % 4: blob.append(0)
        image['bufferView'] = len(views)
        views.append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(payload)})
        blob.extend(payload)
    for key in ['textures', 'samplers', 'materials']:
        doc[key] = copy.deepcopy(old.get(key, []))
    for material in doc['materials']:
        material['name'] = names.get(material['name'], material['name'])
    for mesh in doc['meshes']:
        index = primitive_sources[mesh['name']]
        assert len(mesh['primitives']) == 1
        mesh['primitives'][0]['material'] = index
    for key in ['extensionsUsed', 'extensionsRequired']:
        combined = sorted(set(doc.get(key, [])) | set(old.get(key, [])))
        if combined: doc[key] = combined
    while len(blob) % 4: blob.append(0)
    doc['buffers'] = [{'byteLength': len(blob)}]
    encoded = json.dumps(doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    Path(target).write_bytes(struct.pack('<4sII', b'glTF', 2, 28 + len(encoded) + len(blob)) +
        struct.pack('<II', len(encoded), 0x4e4f534a) + encoded + struct.pack('<II', len(blob), 0x004e4942) + blob)
    final, final_blob = load(target)
    assert all(read(final, final_blob, i) == rows for i, rows in original_geometry.items())
    assert all(image_bytes(final, final_blob, i) == image_bytes(old, old_blob, i) for i in range(len(old['images'])))
    assert final['textures'] == old.get('textures', []) and final['samplers'] == old.get('samplers', [])
    result = {'pass': True, 'geometryAndBindAccessorBytesPreserved': True,
        'materialNames': names, 'materials': len(final['materials']),
        'images': [{'name': i.get('name'), 'sha256': hashlib.sha256(image_bytes(final, final_blob, k)).hexdigest()}
                   for k, i in enumerate(final['images'])],
        'separateSourceAlphaPrimitivesPreserved': True, 'recolorsAndCompanionsPreserved': True}
    Path(output).write_text(json.dumps(result, indent=2) + '\n')

