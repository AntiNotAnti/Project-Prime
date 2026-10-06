"""Append Source specular/roughness maps after the unchanged generated exporter."""
import hashlib, json, struct
from pathlib import Path
from glb import load, image_bytes

def add_material_maps(root, model):
    root, model = Path(root), Path(model)
    doc, binary = load(model)
    blob = bytearray(binary)
    report = {}
    for mat in doc['materials']:
        path = root/'textures'/(mat['name']+'-material.png')
        if not path.exists():
            continue
        pbr = mat.get('pbrMetallicRoughness', {})
        if 'baseColorTexture' not in pbr:
            continue
        assert 'metallicRoughnessTexture' not in pbr, 'Material map already attached'
        payload = path.read_bytes()
        while len(blob) % 4: blob.append(0)
        view = len(doc['bufferViews'])
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': len(payload)})
        blob.extend(payload)
        image = len(doc['images'])
        doc['images'].append({'bufferView': view, 'mimeType': 'image/png', 'name': mat['name']+'-source-phong'})
        texture = len(doc['textures'])
        base = doc['textures'][pbr['baseColorTexture']['index']]
        doc['textures'].append(dict(base, source=image))
        pbr.update(metallicRoughnessTexture={'index': texture, 'texCoord': 0}, metallicFactor=1., roughnessFactor=1.)
        mat.setdefault('extras', {})['sourcePhongApproximation'] = True
        assert image_bytes(doc, blob, image) == payload
        report[mat['name']] = {'imageSha256': hashlib.sha256(payload).hexdigest(),
                               'encoding': 'glTF G=roughness, B=bounded Source specular strength; runtime converts B to specular R'}
    if not report:
        return
    while len(blob) % 4: blob.append(0)
    doc['buffers'][0]['byteLength'] = len(blob)
    encoded = json.dumps(doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    model.write_bytes(struct.pack('<4sII', b'glTF', 2, 28+len(encoded)+len(blob))+
                      struct.pack('<II', len(encoded), 0x4e4f534a)+encoded+
                      struct.pack('<II', len(blob), 0x004e4942)+blob)
    assert bytes(blob[:len(binary)]) == binary, 'Generated mesh and artwork bytes changed'
    (root/'source-specular-maps.json').write_text(json.dumps({'generatedBinaryPreserved': True, 'materials': report}, indent=2)+'\n')
