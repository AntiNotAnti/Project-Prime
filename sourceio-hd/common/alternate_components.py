"""Stable whole-piece selectors for Source alternates, independent of UV splits."""
from collections import defaultdict

def component_keys(points,faces,spec):
    lookup={};positions=[];local=[];parents=[]
    for p in points:
        key=tuple(round(float(x),6) for x in p)
        if key not in lookup:lookup[key]=len(positions);positions.append(key);parents.append(len(parents))
        local.append(lookup[key])
    def find(i):
        while parents[i]!=i:parents[i]=parents[parents[i]];i=parents[i]
        return i
    welded=[]
    for f in faces:
        face=[local[i] for i in f];welded.append(face)
        for v in face[1:]:parents[find(v)]=find(face[0])
    pieces=defaultdict(list)
    for i,f in enumerate(welded):pieces[find(f[0])].append(i)
    candidates=[]
    for fs in pieces.values():
        if len(fs)!=spec['triangles']:continue
        vs={v for i in fs for v in welded[i]};center=[sum(positions[v][k] for v in vs)/len(vs) for k in range(3)]
        delta=sum((a-b)**2 for a,b in zip(center,spec['center']))**.5
        if delta<spec.get('centerTolerance',1e-5):candidates.append({positions[v] for v in vs})
    assert len(candidates)==1,('Source rigid-piece selector is ambiguous or missing',spec,len(candidates))
    return candidates[0]
