"""Re-decode raw Source/native cages and prove exported corner/skin contracts."""
import argparse,json,math
from collections import defaultdict,Counter
from pathlib import Path
import numpy as np
from glb import load,read
from alternate_cage_geometry import fitted_triangles
from alternate_cage_provenance import verify,digest
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--source',required=True);p.add_argument('--output',required=True);a=p.parse_args();cfg=json.loads(Path(a.config).read_text());root=Path(a.output);source=Path(a.source);model=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb';provenance=json.loads((root/'prebuild-provenance.json').read_text());verify(provenance);assert provenance['configurationSha256']==digest(a.config) and provenance['sourceGlbSha256']==digest(source)==cfg['sourceSha256']
for name,h in json.loads((root/'generated-reference-hashes.json').read_text()).items():assert digest(root/name)==h
atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];contract,frames,expected,proof=fitted_triangles(cfg,source,root/'kit',atlas);doc,blob=load(model);entry=json.loads((root/'kit/starter/characters.json').read_text())['models'][0];assert entry['skinning']=='weighted4' and entry['part']=='alternateForm';skin=doc['skins'][0];names=[doc['nodes'][i]['name'] for i in skin['joints']];assert set(names)==set(entry['boneMap'])==set(frames) and len(names)<=32
bind_error=max(np.max(np.abs(frames[n]@np.array(v).reshape(4,4).T-np.eye(4))) for n,v in zip(names,read(doc,blob,skin['inverseBindMatrices'])));assert bind_error<1e-5
parents={c:i for i,n in enumerate(doc['nodes']) for c in n.get('children',[])};world={}
def frame(i):
    if i in world:return world[i]
    n=doc['nodes'][i];assert 'matrix'in n or n.get('name')=='Armature';local=np.array(n.get('matrix',np.eye(4).T.flatten())).reshape(4,4).T;world[i]=(frame(parents[i]) if i in parents else np.eye(4))@local;return world[i]
rest_error=max(np.max(abs(frame(i)-frames[doc['nodes'][i]['name']])) for i in skin['joints']);assert rest_error<1e-5
for node in doc['nodes']:
    if 'mesh'in node:assert not node.get('matrix') and node.get('translation',[0,0,0])==[0,0,0] and node.get('rotation',[0,0,0,1])==[0,0,0,1] and node.get('scale',[1,1,1])==[1,1,1]
groups=defaultdict(list)
for t in expected:groups[t['material']].append(dict(t,skinWeights=np.array([[w.get(n,0) for n in names] for w in t['weights']])))
maximum_pos=maximum_uv=maximum_norm=maximum_weight=maximum_sum=0.;checked=blended=0;counts=Counter();seen=set();weightcohorts={};cohort_mismatch=0
for mesh in doc['meshes']:
    for pr in mesh['primitives']:
        mat=doc['materials'][pr['material']];pair=mat['name'];assert pair in groups;seen.add(pair);ts=groups[pair];points=np.array(read(doc,blob,pr['attributes']['POSITION']));uv=np.array(read(doc,blob,pr['attributes']['TEXCOORD_0']));normal=np.array(read(doc,blob,pr['attributes']['NORMAL']));joints=np.array(read(doc,blob,pr['attributes']['JOINTS_0']));weights=np.array(read(doc,blob,pr['attributes']['WEIGHTS_0']));faces=np.array(read(doc,blob,pr['indices'])).reshape(-1,3);assert len(faces)==len(ts);cells=defaultdict(list)
        assert 'baseColorTexture'in mat.get('pbrMetallicRoughness',{}) and 'normalTexture'in mat and mat.get('alphaMode','OPAQUE')=='OPAQUE'
        for ti,t in enumerate(ts):cells[tuple(math.floor(float(v)/3e-5) for v in t['positions'].mean(0))].append(ti)
        consumed=set()
        for face in faces:
            dense=np.zeros((3,len(names)))
            for k,vi in enumerate(face):
                assert 1<=sum(weights[vi]>1e-6)<=4;maximum_sum=max(maximum_sum,float(abs(weights[vi].sum()-1)));assert maximum_sum<2e-6
                for j,w in zip(joints[vi],weights[vi]):dense[k,int(j)]+=w
                key=(pair,*np.round(points[vi],6));tupleweight=dense[k]
                if key in weightcohorts:cohort_mismatch=max(cohort_mismatch,float(np.max(abs(weightcohorts[key]-tupleweight))))
                else:weightcohorts[key]=tupleweight.copy()
            cell=tuple(math.floor(float(v)/3e-5) for v in points[face].mean(0));near=[]
            for x in [-1,0,1]:
                for y in [-1,0,1]:
                    for z in [-1,0,1]:near.extend(i for i in cells.get((cell[0]+x,cell[1]+y,cell[2]+z),[]) if i not in consumed)
            scores=[]
            for ti in near:
                t=ts[ti]
                for shift in range(3):
                    order=np.roll(np.arange(3),shift);pe=float(np.max(np.linalg.norm(points[face]-t['positions'][order],axis=1)));ue=float(np.max(abs(uv[face]-t['uvs'][order])));we=float(np.max(abs(dense-t['skinWeights'][order])));ne=float(np.max(np.linalg.norm(normal[face]-t['normals'][order],axis=1)));scores.append((pe,ue,we,ne,ti,order))
            valid=[s for s in scores if s[0]<1e-5 and s[1]<2e-6 and s[2]<2e-6];assert valid,('Source geometry/UV/continuous skin/winding changed',pair)
            pe,ue,we,ne,ti,order=min(valid,key=lambda s:s[3]);consumed.add(ti);t=ts[ti];maximum_pos=max(maximum_pos,pe);maximum_uv=max(maximum_uv,ue);maximum_weight=max(maximum_weight,we);length=np.linalg.norm(normal[face],axis=1);assert np.max(abs(length-1))<1e-4;angle=float(np.degrees(np.arccos(np.clip(np.sum(normal[face]/length[:,None]*t['normals'][order],axis=1),-1,1))).max());maximum_norm=max(maximum_norm,angle);counts[t['sourceMaterial']]+=1;checked+=3;blended+=sum(sum(w>1e-6 for w in weights[vi])>1 for vi in face)
        assert len(consumed)==len(ts)
assert seen==set(groups) and sum(counts.values())==cfg['expectedTriangles'] and maximum_norm<1.1 and cohort_mismatch<2e-6
for mat in doc['materials']:
    if mat['name'] in cfg.get('teamRecolorMaterials',[]):assert set(mat['extras']['projectPrimeRecolors'])=={'4','5'}
assert all(v['albedoTexelsVerifiedLossless'] for v in json.loads((root/'atlas-layout.json').read_text())['groups'].values());assert json.loads((root/'source-alpha.json').read_text())['pass'];assert json.loads((root/'raw-source-normal-restoration.json').read_text())['pass'];report=json.loads((root/'retarget-report.json').read_text());assert report['sourceConversionMatrixGame']==proof['sourceConversionMatrixGame'] and report['nativeInfluenceCornerHistogram']==proof['nativeInfluenceCornerHistogram']
out={'pass':True,'scope':'Static original Source topology/UV/normal/continuous cage-skin/native bind audit; native attack seam and Source resemblance capture acceptance required','hunter':cfg['hunter'],'part':'alternateForm','skinning':'weighted4','sourceSha256':digest(source),'configSha256':digest(a.config),'shippingGlbSha256':digest(model),'generatedExporterSha256':digest(root/'kit/prepare-altform-weighted4.py'),'sourceTriangles':sum(counts.values()),'totalTriangles':sum(counts.values()),'nativeSupplementTriangles':0,'nativeSupplementMaterials':[],'sourceMaterialTriangles':dict(counts),'nativeOnlyJoints':True,'nativeJointCount':len(names),'maximumInverseBindError':float(bind_error),'maximumNativeRestError':float(rest_error),'maximumSourcePositionError':maximum_pos,'maximumRuntimeUvError':maximum_uv,'maximumSourceNormalAngleDegrees':maximum_norm,'maximumContinuousWeightError':maximum_weight,'maximumWeightSumError':maximum_sum,'maximumCoincidentRuntimeWeightError':cohort_mismatch,'triangleCornersChecked':checked,'blendedIndexedCorners':int(blended),'maxInfluences':4,'sourceTopologyAndSplitNormalsPreserved':True,'sourceAtlasTexelsLossless':True,'conditionalBladeMaterial':'ALT_Rock_01_tga','conditionalBladeNativeRestAlpha':0,'componentAndNativeFitAudit':proof,'prebuildInputsUnchanged':True,'prebuildProvenance':provenance};verify(provenance);(root/'audit.json').write_text(json.dumps(out,indent=2)+'\n');print('ALTERNATE_CAGE_STATIC_AUDIT_PASS',cfg['hunter'],out['sourceTriangles'],out['blendedIndexedCorners'])
