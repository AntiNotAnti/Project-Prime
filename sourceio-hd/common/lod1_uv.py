"""Classify accepted atlas triangles by Source material and periodic fold.

Collapse may interpolate UV layers only within one affine mapping. Matching
position/skin alone can otherwise join different compact-period folds.
"""
from glb import read

def classify(doc, blob, primitive, atlas):
    name = doc['materials'][primitive['material']]['name']
    uv = read(doc, blob, primitive['attributes']['TEXCOORD_0'])
    original = read(doc, blob, primitive['attributes']['TEXCOORD_1'])
    indices = [r[0] for r in read(doc, blob, primitive['indices'])]
    labels = []
    for start in range(0, len(indices), 3):
        triangle = indices[start:start+3]; candidates = []
        for source_name, spec in atlas['materials'].items():
            if spec['runtimeMaterial'] != name: continue
            q = [[(uv[i][0]-spec['atlasUVOffset'][0])/spec['atlasUVScale'][0],
                  (1-uv[i][1]-spec['atlasUVOffset'][1])/spec['atlasUVScale'][1]] for i in triangle]
            old = [[original[i][0], 1-original[i][1]] for i in triangle]
            if not spec.get('directPeriodicTexture') and any(
                v[k] < spec['uvLow'][k]-1e-5 or v[k] > spec['uvHigh'][k]+1e-5 for v in q for k in range(2)):
                continue
            shift = [round(old[0][k]-q[0][k]) if spec.get('compactPeriodicUVs') else 0 for k in range(2)]
            if max(abs(old[v][k]-q[v][k]-shift[k]) for v in range(3) for k in range(2)) < 1e-5:
                candidates.append((source_name,*shift))
        assert len(candidates) == 1, ('Ambiguous accepted atlas triangle', name, start//3, candidates)
        labels.append(candidates[0])
    return labels

