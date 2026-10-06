"""Spire Source shell fitted once, with continuous native-cage vertex weights.

Native cage corners supply at most three bones. Shared Source coordinates within
one bodygroup get the same tuple, across UV/normal and triangle-corner seams.
Whole shell components choose one native side before vertex projection.
"""
from collections import Counter,defaultdict
import numpy as np
from alternate_rigid_geometry import native_snapshot,source_snapshot,closest_surfaces

def fitted_triangles(cfg,source,kit,atlas):
    contract,frames,native=native_snapshot(kit);meshes=source_snapshot(source,cfg['sourceMeshes']);fit=np.array(cfg['sourceToNativeGame'],float);singular=np.linalg.svd(fit[:3,:3],compute_uv=False)
    assert np.linalg.det(fit[:3,:3])>0 and singular.max()-singular.min()<1e-10
    assert cfg['hunter']=='Spire' and cfg['skinning']=='weighted4'
    rules={r['object']:r for r in cfg['cagePartitions']};out=[];proof=[];counts=Counter();material_counts=Counter();hist=Counter();allweights={};shared=Counter()
    for m in meshes:
        rule=rules[m['object']];ts=m['triangles'];assign={};materials={};cache={}
        if rule['method']=='node':
            for i,t in enumerate(ts):
                assign[i]=[{rule['node']:1.}]*3;materials[i]=rule['nativeMaterial']
        else:
            for piece in m['components']:
                points=(fit@np.column_stack([piece['points'],np.ones(len(piece['points']))]).T).T[:,:3];options=[]
                for option in rule['options']:
                    cages=[t for t in native if t['material']==option['nativeMaterial'] and set(t['nodes'])<=set(option['nativeNodes']) and np.linalg.norm(np.cross(t['positions'][1]-t['positions'][0],t['positions'][2]-t['positions'][0]))>1e-10]
                    assert cages;ix,ds,bs=closest_surfaces(points,[t['positions'] for t in cages]);options.append((float(ds.mean()),option,cages))
                options.sort(key=lambda o:o[0]);score,option,cages=options[0];margin=options[1][0]-score;assert margin>rule.get('minimumDistanceMargin',1e-5),('Ambiguous Source component side',m['object'],piece['center'].tolist(),[o[0] for o in options])
                unique={tuple(float(v) for v in p):p for i in piece['triangles'] for p in ts[i]['positions']};keys=list(unique);world=(fit@np.column_stack([list(unique.values()),np.ones(len(keys))]).T).T[:,:3]
                if rule['method']=='wholeComponentNode':weights=[{option['node']:1.} for k in keys];ds=np.zeros(len(keys))
                else:
                    assert rule['method']=='nativeCageVertices';ix,ds,bs=closest_surfaces(world,[t['positions'] for t in cages]);weights=[]
                    for ci,bary in zip(ix,bs):
                        merged=defaultdict(float)
                        for n,w in zip(cages[ci]['nodes'],bary):
                            if w>cfg.get('weightEpsilon',1e-6):merged[n]+=float(w)
                        total=sum(merged.values());assert total>0;weights.append({n:w/total for n,w in sorted(merged.items())});assert 1<=len(weights[-1])<=4
                for k,ws in zip(keys,weights):
                    if k in cache:assert cache[k]==ws,('Coincident shell spans different native side',m['object'],k)
                    cache[k]=ws
                for i in piece['triangles']:
                    assign[i]=[cache[tuple(float(v) for v in p)] for p in ts[i]['positions']];materials[i]=rule.get('nativeMaterial',option['nativeMaterial'])
                proof.append({'object':m['object'],'componentTriangles':len(piece['triangles']),'componentCenterGame':piece['center'].tolist(),'nativeCageMaterial':option['nativeMaterial'],'allowedNativeNodes':option['nativeNodes'],'componentMeanSideDistance':score,'alternativeSideDistanceMargin':margin,'vertexSurfaceDistanceP50P95Maximum':np.percentile(ds,[50,95,100]).tolist(),'method':rule['method']})
        assert len(assign)==len(ts)
        for i,t in enumerate(ts):
            spec=atlas[t['sourceMaterial']];uv=t['sourceUvsBlender'].copy();shift=np.floor(uv.min(0)) if spec.get('compactPeriodicUVs') else np.zeros(2);uv=(uv-shift)*spec['atlasUVScale']+spec['atlasUVOffset'];uv[:,1]=1-uv[:,1]
            world=(fit@np.column_stack([t['positions'],np.ones(3)]).T).T[:,:3];normal=(np.linalg.inv(fit[:3,:3]).T@t['normals'].T).T;normal/=np.linalg.norm(normal,axis=1)[:,None]
            for p,ws in zip(t['positions'],assign[i]):
                k=(m['object'],*tuple(float(v) for v in p));hist[len(ws)]+=1;shared[k]+=1
                if k in allweights:assert allweights[k]==ws,'Shared Source coordinate changed native skin tuple'
                allweights[k]=ws
            out.append({'material':materials[i],'sourceMaterial':t['sourceMaterial'],'object':m['object'],'positions':world,'normals':normal,'uvs':uv,'sourceUvs':t['sourceUvsBlender'].copy(),'weights':assign[i]});counts[t['sourceMaterial']]+=1;material_counts[materials[i]]+=1
    assert len(out)==cfg['expectedTriangles'];assert set(rules)=={m['object'] for m in meshes}
    summary={'sourceTriangles':dict(counts),'runtimeMaterialSourceTriangles':dict(material_counts),'sourceConversionMatrixGame':fit.tolist(),'uniformScale':float(singular.mean()),'cageComponentProof':proof,'nativeInfluenceCornerHistogram':{str(k):v for k,v in hist.items()},'sharedSourceCoordinateCohorts':sum(v>1 for v in shared.values()),'sharedSourceCoordinateWeightMismatches':0,'sourceTopologyPreserved':True,'singleConversionFitForEveryNativeInfluence':True,'maxNativeInfluences':max(hist),'runtimeAcceptanceRequired':True}
    return contract,frames,out,summary
