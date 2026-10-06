"""Lossless periodic UV atlas: original texels and UV islands, no repaint/remesh.

Run with the bundled Python/Pillow runtime before build-sourceio.py.
"""
import json,struct,math,hashlib
from pathlib import Path
from PIL import Image
import argparse
from glb import load,image_bytes,read
from source_materials import SourceMaterialLibrary
parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);parser.add_argument('--source',required=True);parser.add_argument('--output',required=True)
args=parser.parse_args();config_path=Path(args.config).resolve();config=json.loads(config_path.read_text())
ROOT=Path(args.output).resolve();ROOT.mkdir(parents=True,exist_ok=True);SOURCE=Path(args.source).resolve()
data=SOURCE.read_bytes();doc,blob=load(SOURCE)
out=ROOT/'textures';out.mkdir(exist_ok=True)
original=ROOT/'source-original';original.mkdir(exist_ok=True)
for index,image in enumerate(doc['images']):
 (original/('image-'+str(index)+'.png')).write_bytes(image_bytes(doc,blob,index))
source_materials=SourceMaterialLibrary.from_config(config,SOURCE) if config.get('sourceMaterialRoot') else None
material_images={}
def image_for(mat,channel):
 key=(mat['name'],channel)
 if key in material_images:return material_images[key]
 if channel=='material':
  result=source_materials.material(mat,image_for(mat,'source-albedo'),image_for(mat,'normal')) if source_materials else None
  material_images[key]=result;return result
 if channel=='emissive':
  es=mat.get('emissiveTexture')
  embedded=Image.open(original/('image-'+str(doc['textures'][es['index']]['source'])+'.png')).convert('RGBA') if es else None
  result=source_materials.emissive(mat,image_for(mat,'source-albedo'),embedded) if source_materials else None
  material_images[key]=result;return result
 spec=mat.get('pbrMetallicRoughness',{}).get('baseColorTexture') if channel=='albedo' else mat.get('normalTexture')
 if channel=='source-albedo':spec=mat.get('pbrMetallicRoughness',{}).get('baseColorTexture')
 if spec is None:return None
 result=Image.open(original/('image-'+str(doc['textures'][spec['index']]['source'])+'.png')).convert('RGBA')
 if channel=='albedo' and source_materials:result=source_materials.albedo(mat,result)
 material_images[key]=result;return result
def uv_values(index):
 a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']]
 assert a['type']=='VEC2' and a['componentType']==5126
 start=v.get('byteOffset',0)+a.get('byteOffset',0);stride=v.get('byteStride',8)
 return [struct.unpack_from('<2f',blob,start+i*stride) for i in range(a['count'])]
mapping=json.loads((config_path.parent/'material-map.json').read_text())
groups={}
for source,native in mapping.items():groups.setdefault(native,[]).append(source)
uvs={}
for mesh in doc['meshes']:
 if mesh['name'] not in config['sourceMeshes']:continue
 for p in mesh['primitives']:
  values=[(u,1-v) for u,v in uv_values(p['attributes']['TEXCOORD_0'])]
  compact=config.get('compactPeriodicUVs',False) and len(groups[mapping[doc['materials'][p['material']]['name']]])>1
  if compact:
   indices=[row[0] for row in read(doc,blob,p['indices'])];folded=[]
   for start in range(0,len(indices),3):
    triangle=[values[i] for i in indices[start:start+3]];shift=[math.floor(min(v[k] for v in triangle)) for k in range(2)]
    folded.extend((u-shift[0],v-shift[1]) for u,v in triangle)
   values=folded
  uvs.setdefault(p['material'],[]).extend(values)
assert set(mapping)=={doc['materials'][i]['name'] for i in uvs},'Missing or unused material mapping'
specs={}
for index,values in uvs.items():
 mat=doc['materials'][index];im=image_for(mat,'albedo');companion=image_for(mat,'normal');w,h=im.size
 if companion is not None and len(groups[mapping[mat['name']]])>1:
  w=max(w,companion.width);h=max(h,companion.height)
 low=[math.floor(min(v[i] for v in values)) for i in range(2)]
 high=[max(low[i]+1,math.ceil(max(v[i] for v in values))) for i in range(2)]
 specs[mat['name']]={'uvLow':low,'uvHigh':high,'tileSize':[w,h],
  'pixelSize':[(high[0]-low[0])*w,(high[1]-low[1])*h],'materialIndex':index,
  'compactPeriodicUVs':config.get('compactPeriodicUVs',False) and len(groups[mapping[mat['name']]])>1}
def power2(v):return 1<<(v-1).bit_length()
def atlas_tile(mat,channel,size):
 tile=image_for(mat,channel)
 if tile is None:return Image.new('RGBA',size,(0,255,0,255) if channel=='material' else (0,0,0,255) if channel=='emissive' else (128,128,255,255))
 if tile.size!=size:
  assert size[0]%tile.width==0 and size[1]%tile.height==0, 'Atlas channels require an integer lossless expansion'
  tile=tile.resize(size,Image.Resampling.NEAREST)
 return tile
pad=16;report={'paddingPixels':pad,'sourceSha256':hashlib.sha256(data).hexdigest(),'groups':{}}
for native,names in groups.items():
 channels=['albedo','normal']
 if any(image_for(doc['materials'][specs[n]['materialIndex']],'emissive') is not None for n in names):channels.append('emissive')
 if any(image_for(doc['materials'][specs[n]['materialIndex']],'material') is not None for n in names):channels.append('material')
 if len(names)==1 and config.get('reuseSingleMaterialTextures',False):
  name=names[0];spec=specs[name];mat=doc['materials'][spec['materialIndex']];tw,th=spec['tileSize']
  for channel in channels:
   tile=image_for(mat,channel) or Image.new('RGBA',(tw,th),(0,255,0,255) if channel=='material' else (0,0,0,255) if channel=='emissive' else (128,128,255,255))
   tile.save(out/(native+'-'+channel+'.png'))
  spec.update(runtimeMaterial=native,atlasSize=[tw,th],atlasUVScale=[1,1],atlasUVOffset=[0,0],directPeriodicTexture=True)
  report['groups'][native]={'size':[tw,th],'sources':names,'albedoTexelsVerifiedLossless':True,'directPeriodicTexture':True}
  continue
 required_width=max(s['pixelSize'][0]+2*pad for n,s in specs.items() if n in names)
 width=power2(required_width) if config.get('powerOfTwoAtlases',True) else required_width
 # Shelf packing retains each material's exact native texture density.
 entries=sorted(names,key=lambda n:specs[n]['pixelSize'][1],reverse=True)
 x=y=rowheight=0
 for name in entries:
  spec=specs[name];w,h=spec['pixelSize'];w+=2*pad;h+=2*pad
  if x+w>width:y+=rowheight;x=rowheight=0
  spec['offsetPixels']=[x+pad,y+pad];x+=w;rowheight=max(rowheight,h)
 height=power2(y+rowheight) if config.get('powerOfTwoAtlases',True) else y+rowheight
 albedo=Image.new('RGBA',(width,height),(30,30,30,255));normal=Image.new('RGBA',(width,height),(128,128,255,255));emissive=Image.new('RGBA',(width,height),(0,0,0,255));material=Image.new('RGBA',(width,height),(0,255,0,255))
 for name in entries:
  spec=specs[name];mat=doc['materials'][spec['materialIndex']];px,py=spec['offsetPixels'];w,h=spec['pixelSize'];tw,th=spec['tileSize']
  for channel,target in [('albedo',albedo),('normal',normal),*([('emissive',emissive)] if 'emissive' in channels else []),*([('material',material)] if 'material' in channels else [])]:
   tile=atlas_tile(mat,channel,(tw,th))
   assert tile.size==(tw,th)
   region=Image.new('RGBA',(w+2*pad,h+2*pad))
   # PNG top-left corresponds to the highest V tile. Integers wrap exactly.
   for tx in range(-1,w//tw+1):
    for ty in range(-1,h//th+1):region.paste(tile,(tx*tw+pad,ty*th+pad))
   target.paste(region,(px-pad,py-pad))
  # Verify every central repeated tile matches the source, byte for byte.
  tile=atlas_tile(mat,'albedo',(tw,th))
  for tx in range(w//tw):
   for ty in range(h//th):assert albedo.crop((px+tx*tw,py+ty*th,px+(tx+1)*tw,py+(ty+1)*th)).tobytes()==tile.tobytes()
  spec['runtimeMaterial']=native;spec['atlasSize']=[width,height]
  spec['atlasUVScale']=[tw/width,th/height]
  spec['atlasUVOffset']=[(px-spec['uvLow'][0]*tw)/width,1-(py+h+spec['uvLow'][1]*th)/height]
 albedo.save(out/(native+'-albedo.png'));normal.save(out/(native+'-normal.png'))
 if 'emissive' in channels:emissive.save(out/(native+'-emissive.png'))
 if 'material' in channels:material.save(out/(native+'-material.png'))
 report['groups'][native]={'size':[width,height],'sources':names,'albedoTexelsVerifiedLossless':True}
report['materials']=specs
(ROOT/'atlas-layout.json').write_text(json.dumps(report,indent=2)+'\n')
if source_materials:source_materials.write_audit(ROOT/'SOURCE-MATERIALS.json')
print(json.dumps(report['groups'],indent=2))
