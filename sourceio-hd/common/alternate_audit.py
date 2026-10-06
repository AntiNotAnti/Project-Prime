"""Independent Source corner, skin, UV, normal and native-bind alternate audit.

Decode the original Source GLB again rather than trusting authoring mesh counts.
Audit only indexed records; transparency may share full attribute accessors.
"""
import argparse,json,hashlib,math
from pathlib import Path
from collections import defaultdict,Counter
from itertools import product
import numpy as np
from glb import load,read,image_bytes
from alternate_components import component_keys
from alternate_effects import effect_records
from alternate_provenance import verify
class CornerGrid:
    """Exact radius lookup using 27 neighboring cells, without SciPy."""
    def __init__(self,points,cell=1e-5):
        self.points=points;self.cell=cell;self.cells=defaultdict(list)
        for i,p in enumerate(points):self.cells[self.key(p)].append(i)
    def key(self,p):return tuple(math.floor(float(v)/self.cell) for v in p)
    def query_ball_point(self,p,radius):
        assert radius<=self.cell
        key=self.key(p);result=[]
        for delta in product([-1,0,1],repeat=3):
            for i in self.cells.get(tuple(k+d for k,d in zip(key,delta)),[]):
                if sum((float(a)-float(b))**2 for a,b in zip(self.points[i],p))<=radius*radius:result.append(i)
        return result
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--source',required=True);p.add_argument('--output',required=True);a=p.parse_args()
cp=Path(a.config);cfg=json.loads(cp.read_text());root=Path(a.output);source=Path(a.source);model=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb'
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
provenance=json.loads((root/'prebuild-provenance.json').read_text());verify(provenance)
assert provenance['configurationSha256']==sha(cp) and provenance['sourceGlbSha256']==sha(source)
assert sha(source)==cfg['sourceSha256']
for name,h in json.loads((root/'generated-reference-hashes.json').read_text()).items():assert sha(root/name)==h,('Changed generated reference/exporter',name)
report=json.loads((root/'retarget-report.json').read_text());atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];sd,sb=load(source);doc,blob=load(model)
entry=json.loads((root/'kit/starter/characters.json').read_text())['models'][0];assert entry['part']=='alternateForm' and entry['skinning']=='weighted4'
T=np.array([[1,0,0,0],[0,0,-1,0],[0,1,0,0],[0,0,0,1]],float);Ti=T.T
frames={n:np.array(v['bakedSourceToNative']) for n,v in report['bindFitting'].items()};source_names=[sd['nodes'][i]['name'] for i in sd['skins'][0]['joints']]
skin=doc['skins'][0];names=[doc['nodes'][i]['name'] for i in skin['joints']];assert set(names)==set(entry['boneMap']) and len(names)<=32
native=next(m for m in json.loads((root/'kit/native-reference.json').read_text())['models'] if m['part']=='alternateForm');native_world={}
for n in native['nodes']:
    x,y,z=n['rotationRadians'];cx,sx=math.cos(x),math.sin(x);cy,sy=math.cos(y),math.sin(y);cz,sz=math.cos(z),math.sin(z)
    rot=np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])@np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])@np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])
    local=np.eye(4);local[:3,:3]=rot@np.diag(n['scale']);local[:3,3]=np.array(n['position'])/native['modelScale'];native_world[n['name']]=(native_world[n['parentName']] if n['parentName'] else np.eye(4))@local
bind_error=max(np.max(np.abs(native_world[name]@np.array(v).reshape(4,4).T-np.eye(4))) for name,v in zip(names,read(doc,blob,skin['inverseBindMatrices'])))
assert bind_error<1e-5,('Inverse bind mismatch',bind_error)
node_parents={c:i for i,n in enumerate(doc['nodes']) for c in n.get('children',[])};node_frames={}
def node_frame(i):
    if i in node_frames:return node_frames[i]
    n=doc['nodes'][i];assert 'matrix' in n or n.get('name')=='Armature'
    local=np.array(n.get('matrix',np.eye(4).T.flatten())).reshape(4,4).T
    node_frames[i]=(node_frame(node_parents[i]) if i in node_parents else np.eye(4))@local;return node_frames[i]
default_error=max(np.max(np.abs(node_frame(i)-native_world[doc['nodes'][i]['name']])) for i in skin['joints']);assert default_error<1e-5
objects={sd['meshes'][n['mesh']]['name']:n['name'] for n in sd['nodes'] if 'mesh' in n};expected=defaultdict(list);expected_triangles=Counter();source_triangles=Counter();source_weights=Counter()
for sm in sd['meshes']:
    if sm['name'] not in cfg['sourceMeshes']:continue
    obj=objects[sm['name']]
    selections={}
    for oi,override in enumerate(cfg.get('geometryOverrides',[])):
        if override.get('object',obj)==obj and 'component' in override:
            points=[];faces=[]
            for pp in sm['primitives']:
                offset=len(points);points+=read(sd,sb,pp['attributes']['POSITION']);ii=[x[0] for x in read(sd,sb,pp['indices'])];faces.extend(tuple(offset+i for i in ii[start:start+3]) for start in range(0,len(ii),3))
            spec=dict(override['component']);spec['center']=spec.pop('centerGame');selections[oi]=component_keys(points,faces,spec)
    for pr in sm['primitives']:
        mat=sd['materials'][pr['material']]['name'];spec=atlas[mat];runtime=spec['runtimeMaterial'];override=next((v for v in cfg.get('materialOverrides',[]) if v.get('object',obj)==obj and v['sourceMaterial']==mat),None)
        if override:runtime=override['nativeMaterial']
        positions=read(sd,sb,pr['attributes']['POSITION']);normals=read(sd,sb,pr['attributes']['NORMAL']);uvs=read(sd,sb,pr['attributes']['TEXCOORD_0']);joints=read(sd,sb,pr['attributes']['JOINTS_0']);weights=read(sd,sb,pr['attributes']['WEIGHTS_0']);indices=[v[0] for v in read(sd,sb,pr['indices'])]
        for start in range(0,len(indices),3):
            face=indices[start:start+3];triangle_uv=np.array([[uvs[i][0],1-uvs[i][1]] for i in face]);shift=np.floor(triangle_uv.min(0)) if spec.get('compactPeriodicUVs') else np.zeros(2);mapped_uv=(triangle_uv-shift)*spec['atlasUVScale']+spec['atlasUVOffset'];mapped_uv[:,1]=1-mapped_uv[:,1]
            source_triangles[mat]+=1;expected_triangles[runtime]+=1
            for vi,uv in zip(face,mapped_uv):
                target_bone=next((v['nativeBone'] for oi,v in enumerate(cfg.get('geometryOverrides',[])) if v.get('object',obj)==obj and v.get('sourceMaterial',mat)==mat and (oi not in selections or tuple(round(c,6) for c in positions[vi]) in selections[oi])),None)
                merged=defaultdict(float)
                for j,w in zip(joints[vi],weights[vi]):
                    if w>1e-6:merged[cfg['boneMap'][source_names[j]]]+=w
                if target_bone:merged={target_bone:sum(merged.values())}
                total=sum(merged.values());merged={n:w/total for n,w in merged.items()};assert 1<=len(merged)<=4
                native_weights=np.array([merged.get(n,0) for n in names]);source_weights[len(merged)]+=1
                point=T@np.r_[positions[vi],1];transform=sum(frames[n]*w for n,w in merged.items());out=Ti@transform@point
                normal=np.linalg.inv(transform[:3,:3]).T@T[:3,:3]@np.array(normals[vi]);normal=Ti[:3,:3]@normal
                if np.linalg.norm(normal)<1e-8:
                    aa,bb,cc=[np.array(positions[i]) for i in face];normal=np.cross(bb-aa,cc-aa)
                    if np.linalg.norm(normal)<1e-8:normal=np.array([0,1,0.])
                normal/=np.linalg.norm(normal);expected[runtime].append((out[:3],uv,normal,native_weights))
assert sum(source_triangles.values())==cfg['expectedTriangles'] and dict(source_triangles)==report['sourceMaterialTriangles']
trees={m:CornerGrid([v[0] for v in values]) for m,values in expected.items()};actual_triangles=Counter();maximum_position=maximum_uv=maximum_weight=maximum_normal=maximum_sum=0.;checked=0;blended=0;native_effect_triangles=0
effects=effect_records(root/'kit',native,native_world,cfg.get('nativeSupplementMaterials',[]),names);effect_trees={m:CornerGrid([v[0] for v in values]) for m,values in effects.items()};effect_checked=0;effect_position=effect_uv=effect_color=effect_weight=0.
for mesh in doc['meshes']:
    for pr in mesh['primitives']:
        mat=doc['materials'][pr['material']];name=mat['name'];positions=read(doc,blob,pr['attributes']['POSITION']);uvs=read(doc,blob,pr['attributes']['TEXCOORD_0']);normals=read(doc,blob,pr['attributes']['NORMAL']);joints=read(doc,blob,pr['attributes']['JOINTS_0']);weights=read(doc,blob,pr['attributes']['WEIGHTS_0']);indices=[v[0] for v in read(doc,blob,pr['indices'])]
        if name not in expected:
            assert name in cfg.get('nativeSupplementMaterials',[]),('Unexpected material',name)
            assert 'baseColorTexture' not in mat.get('pbrMetallicRoughness',{}),'Native effect texture binding overridden'
            native_effect_triangles+=len(indices)//3
            colors=read(doc,blob,pr['attributes']['COLOR_0']) if 'COLOR_0' in pr['attributes'] else [(1,1,1)]*len(positions)
            for i in sorted(set(indices)):
                ws=np.zeros(len(names))
                for j,w in zip(joints[i],weights[i]):ws[j]+=w
                candidates=[effects[name][k] for k in effect_trees[name].query_ball_point(positions[i],1e-5) if np.max(np.abs(effects[name][k][1]-uvs[i]))<2e-6 and np.max(np.abs(effects[name][k][3]-ws))<2e-6]
                assert candidates,('Native effect geometry/UV/weights changed',name,i,positions[i],uvs[i])
                best=min(candidates,key=lambda r:np.linalg.norm(r[2]-colors[i][:3]));effect_position=max(effect_position,np.linalg.norm(best[0]-positions[i]));effect_uv=max(effect_uv,np.max(np.abs(best[1]-uvs[i])));effect_color=max(effect_color,np.max(np.abs(best[2]-colors[i][:3])));effect_weight=max(effect_weight,np.max(np.abs(best[3]-ws)));effect_checked+=1
            continue
        actual_triangles[name]+=len(indices)//3;assert 'baseColorTexture' in mat['pbrMetallicRoughness'] and 'normalTexture' in mat
        for i in sorted(set(indices)):
            pos=np.array(positions[i]);uv=np.array(uvs[i]);normal=np.array(normals[i]);ws=np.zeros(len(names))
            assert all(math.isfinite(w) and w>=0 for w in weights[i]);assert 1<=sum(w>1e-6 for w in weights[i])<=4
            for j,w in zip(joints[i],weights[i]):ws[j]+=w
            maximum_sum=max(maximum_sum,abs(sum(ws)-1));blended+=sum(ws>1e-6)>1
            near=trees[name].query_ball_point(pos,1e-5);candidates=[expected[name][k] for k in near if np.max(np.abs(expected[name][k][1]-uv))<2e-6 and np.max(np.abs(expected[name][k][3]-ws))<2e-6]
            assert candidates,('Exported vertex has no original Source position/UV/native weight',name,i,pos.tolist(),uv.tolist(),ws.tolist())
            best=min(candidates,key=lambda r:np.linalg.norm(r[2]-normal));pe=np.linalg.norm(best[0]-pos);ue=np.max(np.abs(best[1]-uv));we=np.max(np.abs(best[3]-ws));ne=math.degrees(math.acos(float(np.clip(np.dot(best[2],normal/np.linalg.norm(normal)),-1,1))))
            maximum_position=max(maximum_position,pe);maximum_uv=max(maximum_uv,ue);maximum_weight=max(maximum_weight,we);maximum_normal=max(maximum_normal,ne);checked+=1
            assert abs(np.linalg.norm(normal)-1)<.001
assert actual_triangles==expected_triangles,('Source topology/material inventory changed',actual_triangles,expected_triangles)
assert maximum_normal<1.1 and maximum_sum<1e-5
assert native_effect_triangles==report['nativeSupplementTriangles']
assert native_effect_triangles*3==sum(len(v) for v in effects.values()) and effect_color<1e-6
alpha=json.loads((root/'source-alpha.json').read_text());assert alpha['pass']
for name,detail in alpha['materials'].items():assert detail['triangles']==source_triangles[name]
for mat in doc['materials']:
    if mat['name'] in cfg.get('teamRecolorMaterials',[]) and 'baseColorTexture' in mat.get('pbrMetallicRoughness',{}):assert set(mat['extras']['projectPrimeRecolors'])=={'4','5'}
atlas_report=json.loads((root/'atlas-layout.json').read_text());assert all(v['albedoTexelsVerifiedLossless'] for v in atlas_report['groups'].values())
normal_receipt=json.loads((root/'raw-source-normal-restoration.json').read_text());assert normal_receipt['pass'] and normal_receipt['sourceTriangles']==cfg['expectedTriangles'] and normal_receipt['authoredSourceNormalCornersRestored']==cfg['expectedTriangles']*3
out={'pass':True,'scope':'Static Source geometry/material/native-contract audit; runtime acceptance and capture review pending','hunter':cfg['hunter'],'part':'alternateForm','sourceSha256':sha(source),'configSha256':sha(cp),'shippingGlbSha256':sha(model),'generatedExporterSha256':sha(root/'kit/prepare-altform-weighted4.py'),'sourceTriangles':sum(actual_triangles.values()),'totalTriangles':sum(actual_triangles.values())+native_effect_triangles,'nativeSupplementTriangles':native_effect_triangles,'indexedExportedSourceRecords':checked,'blendedIndexedSourceRecords':blended,'nativeJoints':names,'inverseBindMaximumError':float(bind_error),'defaultJointNativeBindMaximumError':float(default_error),'maximumSourcePositionError':float(maximum_position),'maximumRuntimeUvError':float(maximum_uv),'maximumCollapsedSourceWeightError':float(maximum_weight),'maximumWeightSumError':float(maximum_sum),'maximumSourceNormalAngleDegrees':float(maximum_normal),'sourceMaterialTriangles':dict(source_triangles),'runtimeMaterialTriangles':dict(actual_triangles),'nativeOnlyNormalizedWeighted4':True,'sourceTopologyAndSplitNormalsPreserved':True,'sourceAtlasTexelsLossless':True,'sourceTransparentSurfacesPreserved':alpha['materials'],'inheritedNativeBindScalePreserved':True,'teamRecolorsRequired':bool(cfg.get('teamRecolorMaterials')),'nativeEffectsRetained':cfg.get('nativeSupplementMaterials',[]),'remeshed':False,'subdivided':False}
out['blendedIndexedSourceRecords']=int(blended)
out['rawSourceNormalRestoration']=normal_receipt
out['nativeEffectAudit']={'pass':True,'indexedRecordsChecked':effect_checked,'maximumPositionError':float(effect_position),'maximumUvError':float(effect_uv),'maximumColorError':float(effect_color),'maximumNativeWeightError':float(effect_weight),'originalNativeTextureBindingsRetained':True}
verify(provenance);out['prebuildProvenance']=provenance;out['prebuildInputsUnchanged']=True
(root/'audit.json').write_text(json.dumps(out,indent=2)+'\n');print('ALTERNATE_STATIC_AUDIT_PASS',cfg['hunter'],out['sourceTriangles'],out['totalTriangles'],out['maximumSourceNormalAngleDegrees'])
