"""Exact native first-person idle frames and local reference surfaces."""
import ast,json,math,xml.etree.ElementTree as ET
from mathutils import Matrix,Vector,Euler
TO_BLENDER=Matrix.Rotation(math.pi/2,4,'X')
def read_native(kit):
 model=next(m for m in json.loads((kit/'native-reference.json').read_text())['models'] if m['part']=='viewModel');folder=kit/'reference'/model['model']
 tree=ast.parse((folder/('import_'+model['model']+'.py')).read_text());assignment=next(a for a in ast.walk(tree) if isinstance(a,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='node_anims' for t in a.targets));animations=ast.literal_eval(assignment.value)
 idle_path=kit/'viewmodel-native-idle.json'
 engine_idle={n['name']:Matrix(n['matrix']) for n in json.loads(idle_path.read_text())['nodes']} if idle_path.exists() else None
 def frames(animation=3,age=0):
  if engine_idle is not None and animation==3 and age==0:return {name:m.copy() for name,m in engine_idle.items()}
  result={}
  for n in model['nodes']:
   values=animations[animation][n['name']][min(age,len(animations[animation][n['name']])-1)] if n['name'] in animations[animation] else n['scale']+n['rotationRadians']+n['position']
   local=Matrix.Translation(Vector([v/s for v,s in zip(values[6:9],model['modelScale'])]))@Euler(values[3:6],'XYZ').to_matrix().to_4x4()@Matrix.Diagonal(Vector(values[:3]+[1]))
   result[n['name']]=result[n['parentName']]@local if n['parentName'] else local
  return result
 ns={'c':'http://www.collada.org/2005/11/COLLADASchema'};dae=next(folder.glob(model['model']+'_img_01.dae'));xml=ET.parse(dae).getroot();by_mesh={i:n['name'] for n in model['nodes'] for i in n['meshIds']};meshes=[]
 for i,g in enumerate(xml.findall('.//c:library_geometries/c:geometry',ns)):
  mesh=g.find('c:mesh',ns);sources={}
  for s in mesh.findall('c:source',ns):
   values=list(map(float,(s.find('c:float_array',ns).text or '').split()));stride=int(s.find('c:technique_common/c:accessor',ns).attrib['stride']);sources[s.attrib['id']]=[values[k:k+stride] for k in range(0,len(values),stride)]
  vertex=mesh.find('c:vertices/c:input',ns).attrib['source'][1:];positions=[Vector(v[:3]) for v in sources[vertex]]
  for tri in mesh.findall('c:triangles',ns):
   inputs={v.attrib['semantic']:(int(v.attrib['offset']),v.attrib['source'][1:]) for v in tri.findall('c:input',ns)};stride=max(v[0] for v in inputs.values())+1;values=list(map(int,(tri.find('c:p',ns).text or '').split()));vo=inputs['VERTEX'][0];uo,us=inputs['TEXCOORD'];faces=[];uvs=[];colors=[]
   for start in range(0,len(values),stride*3):
    faces.append(tuple(values[start+c*stride+vo] for c in range(3)));uvs.append([sources[us][values[start+c*stride+uo]][:2] if sources[us] else [0.,0.] for c in range(3)])
    co,cs=inputs['COLOR'];colors.append([sources[cs][values[start+c*stride+co]][:3] for c in range(3)])
   if not faces:continue
   mat=tri.attrib['material'].removeprefix('#').removesuffix('-material');meshes.append({'name':g.attrib['name'],'node':by_mesh[i],'vertices':positions,'faces':faces,'uvs':uvs,'colors':colors,'material':mat})
 return model,frames,meshes
