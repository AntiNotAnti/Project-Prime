"""Independent native effect corner reconstruction from generated DAE/weights."""
import ast,json,xml.etree.ElementTree as ET
from collections import defaultdict
import numpy as np
def effect_records(kit,contract,frames,selected,names):
    folder=kit/'reference'/contract['model'];tree=ast.parse((folder/('import_'+contract['model']+'.py')).read_text());func=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='bone_setup');groups=defaultdict(dict);obj=group=None
    for n in func.body:
        if isinstance(n,ast.Assign) and isinstance(n.targets[0],ast.Name) and isinstance(n.value,ast.Subscript):
            if n.targets[0].id=='obj':obj=ast.literal_eval(n.value.slice)
            elif n.targets[0].id=='group':group=ast.literal_eval(n.value.slice)
        elif isinstance(n,ast.Expr) and isinstance(n.value,ast.Call) and isinstance(n.value.func,ast.Attribute) and isinstance(n.value.func.value,ast.Name) and n.value.func.value.id=='group' and n.value.func.attr=='add':
            for vi in ast.literal_eval(n.value.args[0]):groups[obj][vi]=group
    ns={'c':'http://www.collada.org/2005/11/COLLADASchema'};xml=ET.parse(folder/(contract['model']+'_pal_01.dae')).getroot();out=defaultdict(list)
    for mi,g in enumerate(xml.findall('.//c:library_geometries/c:geometry',ns)):
        material=next(m['name'] for m in contract['materials'] if mi in m['meshIds'])
        if material not in selected:continue
        mesh=g.find('c:mesh',ns);sources={}
        for s in mesh.findall('c:source',ns):
            values=list(map(float,(s.find('c:float_array',ns).text or '').split()));stride=int(s.find('c:technique_common/c:accessor',ns).attrib['stride']);sources[s.attrib['id']]=[values[i:i+stride] for i in range(0,len(values),stride)]
        posid=mesh.find('c:vertices/c:input',ns).attrib['source'][1:];fallback=next(n['name'] for n in contract['nodes'] if mi in n['meshIds']);obj='geom'+str(mi+1)+'_obj'
        for tri in mesh.findall('c:triangles',ns):
            ins={i.attrib['semantic']:(int(i.attrib['offset']),i.attrib['source'][1:]) for i in tri.findall('c:input',ns)};stride=max(v[0] for v in ins.values())+1;values=list(map(int,(tri.find('c:p',ns).text or '').split()))
            for start in range(0,len(values),stride):
                vi=values[start+ins['VERTEX'][0]];joint=groups[obj].get(vi,fallback);position=(frames[joint]@np.r_[sources[posid][vi],1])[:3];uo,us=ins['TEXCOORD'];uv=sources[us][values[start+uo]][:2];uv=[uv[0],1-uv[1]];co,cs=ins['COLOR'];color=sources[cs][values[start+co]][:3];weights=np.array([float(n==joint) for n in names]);out[material].append((position,np.array(uv),np.array(color),weights))
    return out
