"""Preserve full native bind transforms, including inherited native bone scale.

Blender edit bones cannot store scale. The generated exporter still validates the
native rig and Weighted4 weights unchanged; this corrects its serialized bind
contract against the full reference pose captured before the edit-bone bake.
Mesh positions, indices, weights, UVs and embedded images remain byte-identical.
"""
import json, math, struct, hashlib
from mathutils import Matrix
from glb import load,read

def correct_native_binds(path,native_bind,root):
 doc,binary=load(path);blob=bytearray(binary);convert=Matrix.Rotation(-math.pi/2,4,'X')
 parents={child:i for i,node in enumerate(doc['nodes']) for child in node.get('children',[])}
 skin=doc['skins'][0];before=read(doc,binary,skin['inverseBindMatrices']);maximum=0.
 accessor=doc['accessors'][skin['inverseBindMatrices']];view=doc['bufferViews'][accessor['bufferView']]
 start=view.get('byteOffset',0)+accessor.get('byteOffset',0);stride=view.get('byteStride',64)
 changed_ranges=[]
 for k,i in enumerate(skin['joints']):
  inverse=(convert@native_bind[doc['nodes'][i]['name']]).inverted()
  values=[inverse[r][c] for c in range(4) for r in range(4)]
  maximum=max(maximum,max(abs(a-b) for a,b in zip(before[k],values)))
  struct.pack_into('<16f',blob,start+k*stride,*values);changed_ranges.append((start+k*stride,start+k*stride+64))
 # Exporter emits only needed rig bones, with skipped ancestors already folded
 # into each local transform. Derive locals from the actual exported hierarchy.
 for i,node in enumerate(doc['nodes']):
  name=node.get('name')
  if name not in native_bind:continue
  world=convert@native_bind[name];parent=parents.get(i)
  if parent is not None and doc['nodes'][parent].get('name') in native_bind:
   local=(convert@native_bind[doc['nodes'][parent]['name']]).inverted()@world
  else:
   assert parent is None or doc['nodes'][parent].get('name')=='Armature'
   local=world
  for key in ['translation','rotation','scale']:node.pop(key,None)
  node['matrix']=[local[r][c] for c in range(4) for r in range(4)]
 immutable=bytearray(blob)
 for lo,hi in changed_ranges:immutable[lo:hi]=binary[lo:hi]
 assert bytes(immutable)==binary,'Bind correction modified mesh/image payload'
 encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
 path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)
 report={'nativeBindScale':[list(m.to_scale()) for m in native_bind.values()],'maximumGeneratedInverseBindCorrection':maximum,'meshWeightsUvsAndImagesUnchanged':True,'generatedExporterUnchanged':True,'correctedJoints':len(skin['joints'])}
 (root/'native-bind-correction.json').write_text(json.dumps(report,indent=2)+'\n')
