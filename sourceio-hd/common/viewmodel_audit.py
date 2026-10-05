"""Independent authoring/native-frame/GLB rigid position, UV and normal audit."""
import bpy,sys,json,argparse,hashlib,math
from pathlib import Path
from collections import Counter
from mathutils import Vector
from mathutils.kdtree import KDTree
sys.path.insert(0,str(Path(__file__).resolve().parent));from viewmodel_native import read_native,TO_BLENDER;from glb import load,read
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);p.add_argument('--kit',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);cp=Path(a.config);cfg=json.loads(cp.read_text());root=Path(a.output);kit=Path(a.kit);model,animate,native=read_native(kit);frames=animate();T=TO_BLENDER;Ti=T.inverted();bpy.ops.wm.open_mainfile(filepath=str(root/(cfg['hunter']+'_SourceIO_FirstPerson_Rigid.blend')));doc,blob=load(root/'starter'/cfg['hunter'].lower()/'viewmodel.glb');assert not doc.get('skins');audit=json.loads((root/'audit.json').read_text());manifest=json.loads((root/'starter/characters.json').read_text())['models'][0];assert all(n==v and n in frames for n,v in manifest['boneMap'].items());maximum=0.;normal_angle=0.;tris=0;source_tris=0;source_area=0.
for node in doc['nodes']:
 if 'mesh' not in node:continue
 name=node['name'];assert not any(k in node for k in ['translation','rotation','scale','matrix']);assert name in manifest['boneMap'];obj=bpy.data.objects.get('Source_Weapon_'+name)
 for pr in doc['meshes'][node['mesh']]['primitives']:
  indices=[v[0] for v in read(doc,blob,pr['indices'])];tris+=len(indices)//3;mat=doc['materials'][pr['material']]
  if not obj or 'baseColorTexture' not in mat.get('pbrMetallicRoughness',{}) or mat['name'] not in audit['sourceMaterialNames']:continue
  assert mat.get('doubleSided') and 'normalTexture' in mat
  mesh=obj.data;mesh.calc_loop_triangles();records=[]
  for tri in mesh.loop_triangles:
   if mesh.materials[mesh.polygons[tri.polygon_index].material_index].name!=mat['name']:continue
   for vi,li in zip(tri.vertices,tri.loops):
    uv=mesh.uv_layers.active.data[li].uv;records.append((Ti@mesh.vertices[vi].co,Vector((uv.x,1-uv.y)),(Ti.to_3x3()@mesh.corner_normals[li].vector).normalized()))
  tree=KDTree(len(records))
  for i,r in enumerate(records):tree.insert(r[0],i)
  tree.balance();positions=read(doc,blob,pr['attributes']['POSITION']);uvs=read(doc,blob,pr['attributes']['TEXCOORD_0']);normals=read(doc,blob,pr['attributes']['NORMAL']);source_tris+=len(indices)//3
  for i,(pos,uv,normal) in enumerate(zip(positions,uvs,normals)):
   world=frames[name]@Vector(pos);wn=(frames[name].to_3x3().inverted().transposed()@Vector(normal)).normalized();near=tree.find_range(world,1e-5);candidates=[records[ix] for pt,ix,d in near if (records[ix][1]-Vector(uv)).length<2e-6];assert candidates,('Missing original Source corner',cfg['hunter'],name,world,uv)
   assert abs(Vector(normal).length-1)<1e-5
   closest=min(candidates,key=lambda r:(r[2]-wn).length);maximum=max(maximum,(closest[0]-world).length);normal_angle=max(normal_angle,math.degrees(closest[2].angle(wn)))
  for start in range(0,len(indices),3):
   aa,bb,cc=[frames[name]@Vector(positions[i]) for i in indices[start:start+3]];source_area+=(bb-aa).cross(cc-aa).length/2
assert tris==audit['totalTriangles'];assert source_tris==audit['sourceCutTriangles'];assert abs(source_area-audit['sourceClosedSurfaceArea'])<max(1e-7,source_area*1e-5);assert maximum<1e-5 and normal_angle<1.
assert (root/'prepare-viewmodel-rigid.py').read_bytes()==(kit/'prepare-viewmodel-rigid.py').read_bytes()
native_colors=0
for node in doc['nodes']:
 if 'mesh' not in node:continue
 for pr in doc['meshes'][node['mesh']]['primitives']:
  mat=doc['materials'][pr['material']]['name']
  if 'COLOR_0' not in pr['attributes']:continue
  records=[]
  for s in native:
   if s['node']!=node['name'] or s['material']!=mat:continue
   for face,uvs,colors in zip(s['faces'],s['uvs'],s['colors']):
    records.extend((s['vertices'][i],Vector((uv[0],1-uv[1])),Vector(color)) for i,uv,color in zip(face,uvs,colors))
  assert records,('Unknown native color surface',node['name'],mat)
  tree=KDTree(len(records))
  for i,r in enumerate(records):tree.insert(r[0],i)
  tree.balance()
  for pos,uv,color in zip(read(doc,blob,pr['attributes']['POSITION']),read(doc,blob,pr['attributes']['TEXCOORD_0']),read(doc,blob,pr['attributes']['COLOR_0'])):
   candidates=[records[ix] for point,ix,d in tree.find_range(Vector(pos),1e-5) if (records[ix][1]-Vector(uv)).length<1e-5]
   assert candidates and min((r[2]-Vector(color)).length for r in candidates)<1e-6,'Native effect artwork changed'
   native_colors+=1
assert native_colors==audit['nativeEffectColorCorners']
audit['nativeEffectColorAudit']={'pass':True,'nativeCornerColorsPreserved':native_colors}
audit['independentAudit']={'pass':True,'maximumNativeReconstructedPositionError':maximum,'maximumSourceNormalAngleDegrees':normal_angle,'exportedSourceSurfaceArea':source_area,'totalTriangles':tris,'sourceTriangles':source_tris,'nativeOnlyRigidNodes':True,'embeddedSurfaceAndNormalMaps':True,'twoSidedMaterials':True,'noRuntimeSourceArmature':True};(root/'audit.json').write_text(json.dumps(audit,indent=2)+'\n');print('INDEPENDENT VIEWMODEL AUDIT PASS',cfg['hunter'],audit['independentAudit'])
