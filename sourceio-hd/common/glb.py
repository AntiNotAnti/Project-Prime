"""Strict accessor/image reads shared by conversion and audit tools."""
import json, struct
from pathlib import Path

def load(path):
    data = Path(path).read_bytes()
    assert data[:4] == b'glTF' and struct.unpack_from('<I',data,4)[0] == 2
    assert struct.unpack_from('<I',data,8)[0] == len(data)
    n, kind = struct.unpack_from('<II',data,12); assert kind == 0x4e4f534a
    doc = json.loads(data[20:20+n]); size, kind = struct.unpack_from('<II',data,20+n)
    assert kind == 0x004e4942
    return doc, data[28+n:28+n+size]

def read(doc, blob, index):
    a = doc['accessors'][index]; assert 'sparse' not in a
    v = doc['bufferViews'][a['bufferView']]
    count = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
    code = {5121:'B',5123:'H',5125:'I',5126:'f'}[a['componentType']] * count
    stride = v.get('byteStride',struct.calcsize('<'+code))
    start = v.get('byteOffset',0) + a.get('byteOffset',0)
    return [struct.unpack_from('<'+code,blob,start+i*stride) for i in range(a['count'])]

def image_bytes(doc, blob, index):
    image = doc['images'][index]; assert 'uri' not in image
    v = doc['bufferViews'][image['bufferView']]; start = v.get('byteOffset',0)
    return blob[start:start+v['byteLength']]
