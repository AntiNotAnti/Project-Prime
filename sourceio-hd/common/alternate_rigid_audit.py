"""Decode original GLB/native DAE again and audit every exported rigid triangle."""
import argparse,json,math
from collections import defaultdict,Counter
from pathlib import Path
import numpy as np
from glb import load,read
from alternate_rigid_geometry import fitted_triangles
from alternate_rigid_provenance import verify,digest
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--source',required=True);p.add_argument('--output',required=True);a=p.parse_args();cfg=json.loads(Path(a.config).read_text());root=Path(a.output);source=Path(a.source);model=root/'kit/starter'/cfg['hunter'].lower()/'altform.glb';provenance=json.loads((root/'prebuild-provenance.json').read_text());verify(provenance);assert provenance['configurationSha256']==digest(a.config) and provenance['sourceGlbSha256']==digest(source)==cfg['sourceSha256']
for name,h in json.loads((root/'generated-reference-hashes.json').read_text()).items():assert digest(root/name)==h
atlas=json.loads((root/'atlas-layout.json').read_text())['materials'];contract,frames,expected,fitproof=fitted_triangles(cfg,source,root/'kit',atlas);doc,blob=load(model);assert not doc.get('skins'),'Runtime Source skeleton is not allowed';entry=json.loads((root/'kit/starter/characters.json').read_text())['models'][0];assert entry['skinning']=='rigidNodes' and entry['part']=='alternateForm';assert entry.get('nativeSupplementMaterials',[])==cfg.get('externalNativeSupplementMaterials',[])
groups=defaultdict(list)
for t in expected:groups[t['node'],t['material']].append(t)
assert set(entry['boneMap'])=={n for n,m in groups} and all(n==v and n in frames for n,v in entry['boneMap'].items())
actual=Counter();maximum_pos=maximum_uv=maximum_norm=maximum_color=0.;checked=0;effect_triangles=0;seen=set()
for node in doc['nodes']:
    if 'mesh' not in node:continue
    assert node['name'] in entry['boneMap'] and not node.get('matrix') and node.get('translation',[0,0,0])==[0,0,0] and node.get('rotation',[0,0,0,1])==[0,0,0,1] and node.get('scale',[1,1,1])==[1,1,1]
    for pr in doc['meshes'][node['mesh']]['primitives']:
        mat=doc['materials'][pr['material']];pair=node['name'],mat['name'];seen.add(pair);ts=groups[pair];points=np.array(read(doc,blob,pr['attributes']['POSITION']));uv=np.array(read(doc,blob,pr['attributes']['TEXCOORD_0']));normal=np.array(read(doc,blob,pr['attributes']['NORMAL']));colors=np.array(read(doc,blob,pr['attributes']['COLOR_0'])) if 'COLOR_0'in pr['attributes'] else np.ones((len(points),3));faces=np.array(read(doc,blob,pr['indices'])).reshape(-1,3);assert len(faces)==len(ts);cells=defaultdict(list)
        for ti,t in enumerate(ts):cells[tuple(math.floor(float(v)/3e-5) for v in t['positions'].mean(0))].append(ti)
        consumed=set()
        for face in faces:
            cell=tuple(math.floor(float(v)/3e-5) for v in points[face].mean(0));near=[]
            for x in [-1,0,1]:
                for y in [-1,0,1]:
                    for z in [-1,0,1]:near.extend(i for i in cells.get((cell[0]+x,cell[1]+y,cell[2]+z),[]) if i not in consumed)
            scores=[]
            for ti in near:
                t=ts[ti]
                # A cyclic corner order preserves winding; reverse order is not
                # silently accepted, including the explicit native Ball adaptation.
                for shift in range(3):
                    order=np.roll(np.arange(3),shift);pe=np.max(np.linalg.norm(points[face]-t['positions'][order],axis=1));ue=np.max(np.abs(uv[face]-t['uvs'][order]));scores.append((pe+ue,ti,order,pe,ue))
            assert scores,('No original Source/native effect triangle',pair,cell)
            score,ti,order,pe,ue=min(scores,key=lambda s:s[0]);assert pe<1e-5 and ue<2e-6,('Source surface/UV/winding changed',pair,pe,ue);consumed.add(ti);t=ts[ti];maximum_pos=max(maximum_pos,float(pe));maximum_uv=max(maximum_uv,float(ue))
            if t['nativeEffect']:
                assert 'baseColorTexture' not in mat.get('pbrMetallicRoughness',{}),'Native billboard artwork binding overridden';ce=np.max(np.abs(colors[face]-t['colors'][order]));maximum_color=max(maximum_color,float(ce));effect_triangles+=1
            else:
                assert 'baseColorTexture' in mat.get('pbrMetallicRoughness',{}) and 'normalTexture' in mat
                normals=normal[face];length=np.linalg.norm(normals,axis=1);assert np.max(abs(length-1))<1e-4;dots=np.sum(normals/length[:,None]*t['normals'][order],axis=1);ne=float(np.degrees(np.arccos(np.clip(dots,-1,1))).max());maximum_norm=max(maximum_norm,ne);actual[t['sourceMaterial']]+=1
            checked+=3
        assert len(consumed)==len(ts)
assert seen==set(groups) and sum(actual.values())==cfg['expectedTriangles'];assert maximum_norm<1.1 and maximum_color<1e-6
for mat in doc['materials']:
    if mat['name'] in cfg.get('teamRecolorMaterials',[]) and 'baseColorTexture' in mat.get('pbrMetallicRoughness',{}):assert set(mat['extras']['projectPrimeRecolors'])=={'4','5'}
assert all(v['albedoTexelsVerifiedLossless'] for v in json.loads((root/'atlas-layout.json').read_text())['groups'].values());assert json.loads((root/'source-alpha.json').read_text())['pass'];assert json.loads((root/'rigid-attribute-restoration.json').read_text())['pass'];report=json.loads((root/'retarget-report.json').read_text());assert report['sourceConversionMatrixGame']==fitproof['sourceConversionMatrixGame'];assert report['nativeNodesSourceTriangles']==fitproof['nativeNodesSourceTriangles']
out={'pass':True,'scope':'Static Source geometry/material/corner-normal and exact native rigid-node/effect audit; runtime state, seam/clipping and resemblance capture acceptance required','hunter':cfg['hunter'],'part':'alternateForm','skinning':'rigidNodes','sourceSha256':digest(source),'configSha256':digest(a.config),'shippingGlbSha256':digest(model),'generatedExporterSha256':digest(root/'kit/prepare-altform-rigid.py'),'sourceTriangles':sum(actual.values()),'totalTriangles':sum(actual.values())+effect_triangles,'nativeSupplementTriangles':effect_triangles,'nativeOnlyRigidNodes':True,'noRuntimeSourceArmature':True,'maximumSourceLocalPositionError':maximum_pos,'maximumRuntimeUvError':maximum_uv,'maximumSourceNormalAngleDegrees':maximum_norm,'maximumNativeEffectColorError':maximum_color,'triangleCornersChecked':checked,'sourceMaterialTriangles':dict(actual),'sourceTopologyAndSplitNormalsPreserved':True,'sourceAtlasTexelsLossless':True,'nativeEffectsRetained':cfg.get('nativeSupplementMaterials',[]),'nativeSupplementMaterials':cfg.get('externalNativeSupplementMaterials',[]),'externalNativeSupplementTriangles':fitproof['externalNativeSupplementTriangles'],'nativeBillboardNodes':{n['name']:n['billboard']for n in contract['nodes']if n['name'] in entry['boneMap'] and n['billboard']!='None'},'componentAndNativeFitAudit':fitproof,'prebuildInputsUnchanged':True,'prebuildProvenance':provenance};verify(provenance);(root/'audit.json').write_text(json.dumps(out,indent=2)+'\n');print('ALTERNATE_RIGID_STATIC_AUDIT_PASS',cfg['hunter'],out['sourceTriangles'],out['totalTriangles'])
