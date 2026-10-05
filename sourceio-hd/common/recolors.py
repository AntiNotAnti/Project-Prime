"""Add native team color variants; leave exported geometry and base artwork intact."""
import argparse,json,struct,hashlib,io
from pathlib import Path
from PIL import Image,ImageChops
from glb import load,image_bytes

def add_recolors(config,root):
 selected=config.get('teamRecolorMaterials',[])
 if not selected:return
 path=root/config.get('recolorPackDirectory','kit/starter')/config.get('recolorModelRelative',config['hunter'].lower()+'/biped_weighted4.glb');doc,binary=load(path);blob=bytearray(binary);report={}
 for material in doc['materials']:
  if material['name'] not in selected:continue
  # Native ammo surfaces can share the shell's material identity without
  # carrying its Source atlas. Their native team binding remains authoritative.
  if 'baseColorTexture' not in material.get('pbrMetallicRoughness',{}):continue
  base=material['pbrMetallicRoughness']['baseColorTexture']['index'];texture=doc['textures'][base];im=Image.open(io.BytesIO(image_bytes(doc,binary,texture['source']))).convert('RGBA');variants={}
  for suit,color in [(4,(1.,19/31,0.)),(5,(0.,1.,0.))]:
   rgb=im.convert('RGB');r,g,b=rgb.split()
   maximum=ImageChops.lighter(ImageChops.lighter(r,g),b);minimum=ImageChops.darker(ImageChops.darker(r,g),b)
   mask=ImageChops.subtract(maximum,minimum).point([round(min(1.,max(0.,(v/255-.035)/.16))*255) for v in range(256)])
   detail=rgb.convert('L',matrix=(.2126,.7152,.0722,0))
   tinted=Image.merge('RGB',tuple(detail.point([round(min(1.,c*(.2+.95*v/255))*255) for v in range(256)]) for c in color))
   target=Image.composite(tinted,rgb,mask).convert('RGBA');target.putalpha(im.getchannel('A'))
   out=io.BytesIO();target.save(out,format='PNG');payload=out.getvalue()
   while len(blob)%4:blob.append(0)
   view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(payload)});blob.extend(payload)
   image=len(doc['images']);doc['images'].append({'bufferView':view,'mimeType':'image/png','name':material['name']+'-team-'+str(suit)})
   tex=len(doc['textures']);doc['textures'].append(dict(texture,source=image));variants[str(suit)]={'index':tex,'texCoord':0}
  material.setdefault('extras',{})['projectPrimeRecolors']=variants
  report[material['name']]={'suits':[4,5],'baseImageSha256':hashlib.sha256(image_bytes(doc,binary,texture['source'])).hexdigest(),'size':list(im.size)}
 assert set(report)==set(selected),'Unknown team recolor material'
 while len(blob)%4:blob.append(0)
 doc['buffers'][0]['byteLength']=len(blob);encoded=json.dumps(doc,separators=(',',':')).encode();encoded+=b' '*((-len(encoded))%4)
 path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(blob))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(blob),0x004e4942)+blob)
 assert bytes(blob[:len(binary)])==binary, 'Exported geometry/base image bytes changed'
 (root/'team-recolors.json').write_text(json.dumps({'generatedGeometryAndBaseImagesUnchanged':True,'materials':report,'method':'Chroma mask and source luminance retain neutral metal/detail; native orange/green team colors. Base textures and geometry unmodified.'},indent=2)+'\n')
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);a=p.parse_args();add_recolors(json.loads(Path(a.config).read_text()),Path(a.output).resolve())
