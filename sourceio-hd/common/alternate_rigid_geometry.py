"""Independent GLB/DAE geometry selection for stateful rigid alternates.

All matrices and geometry here use native/glTF Y-up coordinates. Whole Source
components remain whole unless an explicit native-cage triangle partition is
configured. Source surfaces are never clipped, remeshed or simplified.
"""
import ast, json, math, xml.etree.ElementTree as ET
from collections import defaultdict, Counter
from pathlib import Path
import numpy as np
from glb import load, read

def native_snapshot(kit):
    kit=Path(kit);contract=next(m for m in json.loads((kit/'native-reference.json').read_text())['models'] if m['part']=='alternateForm')
    frames={}
    for n in contract['nodes']:
        x,y,z=n['rotationRadians'];cx,sx=math.cos(x),math.sin(x);cy,sy=math.cos(y),math.sin(y);cz,sz=math.cos(z),math.sin(z)
        local=np.eye(4);local[:3,:3]=np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])@np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])@np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])@np.diag(n['scale']);local[:3,3]=np.array(n['position'])/contract['modelScale'];frames[n['name']]=(frames[n['parentName']] if n['parentName'] else np.eye(4))@local
    folder=kit/'reference'/contract['model'];tree=ast.parse((folder/('import_'+contract['model']+'.py')).read_text());fn=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='bone_setup');groups=defaultdict(dict);obj=group=None
    for s in fn.body:
        if isinstance(s,ast.Assign) and isinstance(s.targets[0],ast.Name) and isinstance(s.value,ast.Subscript):
            if s.targets[0].id=='obj':obj=ast.literal_eval(s.value.slice)
            elif s.targets[0].id=='group':group=ast.literal_eval(s.value.slice)
        elif isinstance(s,ast.Expr) and isinstance(s.value,ast.Call) and isinstance(s.value.func,ast.Attribute) and s.value.func.attr=='add':
            for vi in ast.literal_eval(s.value.args[0]):groups[obj][vi]=group
    ns={'c':'http://www.collada.org/2005/11/COLLADASchema'};xml=ET.parse(folder/(contract['model']+'_pal_01.dae')).getroot();triangles=[]
    for mi,g in enumerate(xml.findall('.//c:library_geometries/c:geometry',ns)):
        mesh=g.find('c:mesh',ns);sources={}
        for s in mesh.findall('c:source',ns):
            values=list(map(float,(s.find('c:float_array',ns).text or '').split()));stride=int(s.find('c:technique_common/c:accessor',ns).attrib['stride']);sources[s.attrib['id']]=np.array([values[i:i+stride] for i in range(0,len(values),stride)])
        posid=mesh.find('c:vertices/c:input',ns).attrib['source'][1:];material=next(m['name'] for m in contract['materials'] if mi in m['meshIds']);fallback=next(n['name'] for n in contract['nodes'] if mi in n['meshIds']);obj='geom'+str(mi+1)+'_obj'
        for tri in mesh.findall('c:triangles',ns):
            ins={i.attrib['semantic']:(int(i.attrib['offset']),i.attrib['source'][1:]) for i in tri.findall('c:input',ns)};stride=max(v[0] for v in ins.values())+1;values=list(map(int,tri.find('c:p',ns).text.split()))
            for start in range(0,len(values),stride*3):
                ps=[];us=[];cs=[];js=[]
                for k in range(3):
                    row=values[start+k*stride:start+(k+1)*stride];vi=row[ins['VERTEX'][0]];joint=groups[obj].get(vi,fallback);js.append(joint);ps.append((frames[joint]@np.r_[sources[posid][vi],1])[:3]);uo,uid=ins['TEXCOORD'];uv=sources[uid][row[uo]][:2];us.append([uv[0],1-uv[1]]);co,cid=ins['COLOR'];cs.append(sources[cid][row[co]][:3])
                triangles.append({'mesh':obj,'material':material,'positions':np.array(ps),'uvs':np.array(us),'colors':np.array(cs),'nodes':js})
    return contract,frames,triangles

def components(triangles):
    lookup={};points=[];parents=[];faces=[]
    def index(p):
        key=tuple(round(float(v),6) for v in p)
        if key not in lookup:lookup[key]=len(points);points.append(np.array(key));parents.append(len(parents))
        return lookup[key]
    def find(i):
        while parents[i]!=i:parents[i]=parents[parents[i]];i=parents[i]
        return i
    for tri in triangles:
        face=[index(p) for p in tri['positions']];faces.append(face)
        for i in face[1:]:parents[find(i)]=find(face[0])
    groups=defaultdict(list)
    for i,f in enumerate(faces):groups[find(f[0])].append(i)
    result=[]
    for fs in groups.values():
        ps=np.array([points[i] for i in sorted({i for fi in fs for i in faces[fi]})]);result.append({'triangles':fs,'points':ps,'center':ps.mean(0),'bounds':np.array([ps.min(0),ps.max(0)])})
    return sorted(result,key=lambda p:len(p['triangles']),reverse=True)

def select_component(pieces,spec):
    found=[p for p in pieces if len(p['triangles'])==spec['triangles'] and np.linalg.norm(p['center']-spec['centerGame'])<spec.get('centerTolerance',1e-5)]
    assert len(found)==1,('Ambiguous Source rigid component',spec,len(found));return found[0]

def closest_surfaces(points,triangles):
    """Exact closest point on every triangle: planar interior or three edges."""
    q=np.array(points);ts=np.array(triangles);a,b,c=ts[:,0],ts[:,1],ts[:,2];ab=b-a;ac=c-a;n=np.cross(ab,ac);n2=(n*n).sum(1)
    assert np.all(n2>1e-20),'Degenerate cage triangle must be excluded'
    found=[];distances=[];bary=[]
    for p in q:
        ap=p-a;plane=p-(((ap*n).sum(1)/n2)[:,None]*n);v=plane-a;d00=(ab*ab).sum(1);d01=(ab*ac).sum(1);d11=(ac*ac).sum(1);d20=(v*ab).sum(1);d21=(v*ac).sum(1);den=d00*d11-d01*d01;u=(d11*d20-d01*d21)/den;vv=(d00*d21-d01*d20)/den
        candidates=[plane];bs=[np.column_stack([1-u-vv,u,vv])];inside=(u>=0)&(vv>=0)&(u+vv<=1)
        for edge,(ia,ib) in enumerate([(0,1),(1,2),(2,0)]):
            aa=ts[:,ia];dd=ts[:,ib]-aa;dd2=(dd*dd).sum(1);t=np.clip(((p-aa)*dd).sum(1)/dd2,0,1);candidates.append(aa+t[:,None]*dd);ww=np.zeros((len(ts),3));ww[:,ia]=1-t;ww[:,ib]=t;bs.append(ww)
        dd=np.array([((p-vv)**2).sum(1) for vv in candidates]);dd[0,~inside]=np.inf;ei,ti=np.unravel_index(np.argmin(dd),dd.shape);found.append(ti);distances.append(math.sqrt(float(dd[ei,ti])));bary.append(bs[ei][ti])
    return np.array(found),np.array(distances),np.array(bary)

def source_snapshot(path,selected):
    doc,blob=load(path);objects={doc['meshes'][n['mesh']]['name']:n['name'] for n in doc['nodes'] if 'mesh'in n};out=[]
    for mesh in doc['meshes']:
        if mesh['name'] not in selected:continue
        ts=[]
        for p in mesh['primitives']:
            attrs={n:np.array(read(doc,blob,i)) for n,i in p['attributes'].items()};indices=np.array(read(doc,blob,p['indices'])).reshape(-1);mat=doc['materials'][p['material']]['name']
            for face in indices.reshape(-1,3):
                ps=attrs['POSITION'][face];ns=attrs['NORMAL'][face];uv=attrs['TEXCOORD_0'][face].copy();uv[:,1]=1-uv[:,1];ts.append({'positions':ps,'normals':ns,'sourceUvsBlender':uv,'sourceMaterial':mat,'object':objects[mesh['name']]})
        out.append({'mesh':mesh['name'],'object':objects[mesh['name']],'triangles':ts,'components':components(ts)})
    assert len(out)==len(selected),'Selected Source mesh missing';return out

def fitted_triangles(cfg,source,kit,atlas):
    contract,frames,native=native_snapshot(kit);meshes=source_snapshot(source,cfg['sourceMeshes']);fit=np.array(cfg['sourceToNativeGame']);assert fit.shape==(4,4) and np.max(abs(fit[3]-[0,0,0,1]))<1e-12;assert np.linalg.det(fit[:3,:3])>0
    singular=np.linalg.svd(fit[:3,:3],compute_uv=False);assert singular.max()-singular.min()<1e-10,'Only one baked uniform conversion scale is allowed'
    seeds={};proof=[];out=[];counts=Counter();node_counts=Counter();material_counts=Counter()
    for selector in cfg.get('componentSeeds',[]):
        m=next(m for m in meshes if m['object']==selector['object']);piece=select_component(m['components'],selector);seeds[selector['id']]=(m,piece)
    for rule in cfg['partitions']:
        m=next(m for m in meshes if m['object']==rule['object']);ts=m['triangles'];labels={};distances=[]
        if rule['method']=='node':labels={i:rule['node'] for i in range(len(ts))}
        elif rule['method']=='wholeComponentsNearestSourceSeed':
            seed_names=rule['seeds'];surfaces=[]
            for name in seed_names:
                sm,sp=seeds[name];surfaces.append([sm['triangles'][i]['positions'] for i in sp['triangles']])
            for piece in m['components']:
                direct=next((seed for seed in cfg['componentSeeds'] if seed['object']==m['object'] and len(piece['triangles'])==seed['triangles'] and np.linalg.norm(piece['center']-seed['centerGame'])<seed.get('centerTolerance',1e-5)),None)
                if direct:chosen=direct['node'];d=0.;margin=None
                else:
                    # The full component surface, not its centroid, chooses its
                    # attachment. Even small coating plates remain one piece.
                    scores=[]
                    for shell in surfaces:
                        _,ds,_=closest_surfaces(piece['points'],shell);scores.append(float(np.mean(ds)))
                    choice=int(np.argmin(scores));chosen=next(s['node'] for s in cfg['componentSeeds'] if s['id']==seed_names[choice]);d=scores[choice];margin=sorted(scores)[1]-d
                    assert margin>rule.get('minimumDistanceMargin',1e-5),('Ambiguous whole Source component attachment',piece['center'].tolist(),scores)
                for i in piece['triangles']:labels[i]=chosen
                distances.append(d);proof.append({'object':m['object'],'componentTriangles':len(piece['triangles']),'componentCenterGame':piece['center'].tolist(),'nativeNode':chosen,'meanSourceSeedSurfaceDistance':d,'alternativeMeanDistanceMargin':margin})
        elif rule['method']=='nativeCageTriangles':
            cages=[t for t in native if t['material'] in rule['nativeMaterials'] and np.linalg.norm(np.cross(t['positions'][1]-t['positions'][0],t['positions'][2]-t['positions'][0]))>1e-10];points=np.array([(fit@np.r_[t['positions'].mean(0),1])[:3] for t in ts]);ix,ds,bs=closest_surfaces(points,[t['positions'] for t in cages])
            for i,(ci,w) in enumerate(zip(ix,bs)):
                scores=defaultdict(float)
                for name,v in zip(cages[ci]['nodes'],w):scores[name]+=float(v)
                labels[i]=max(scores,key=scores.get)
            distances.extend(ds.tolist());proof.append({'object':m['object'],'method':'Closest native triangle surface at fitted Source triangle centroid; barycentric native node majority','nativeMaterials':rule['nativeMaterials'],'distanceP50P95Maximum':np.percentile(ds,[50,95,100]).tolist(),'nativeNodes':dict(Counter(labels.values()))})
        elif rule['method']=='wholeComponentsNativeCage':
            for piece in m['components']:
                fitted=(fit@np.column_stack([piece['points'],np.ones(len(piece['points']))]).T).T[:,:3];scores=[]
                for option in rule['options']:
                    cages=[t['positions'] for t in native if t['material']==option['nativeMaterial'] and ('nativeNodes' not in option or set(t['nodes'])<=set(option['nativeNodes'])) and np.linalg.norm(np.cross(t['positions'][1]-t['positions'][0],t['positions'][2]-t['positions'][0]))>1e-10];_,ds,_=closest_surfaces(fitted,cages);scores.append(float(np.mean(ds)))
                choice=int(np.argmin(scores));chosen=rule['options'][choice]['node'];margin=sorted(scores)[1]-scores[choice];assert margin>rule.get('minimumDistanceMargin',1e-5)
                for i in piece['triangles']:labels[i]=chosen
                distances.append(scores[choice]);proof.append({'object':m['object'],'componentTriangles':len(piece['triangles']),'componentCenterGame':piece['center'].tolist(),'nativeNode':chosen,'nativeCageMeanDistances':scores,'alternativeMeanDistanceMargin':margin})
        else:raise ValueError(rule['method'])
        assert len(labels)==len(ts)
        for i,t in enumerate(ts):
            node=labels[i];assert node in frames;mat=rule.get('nativeMaterial') or rule.get('nativeMaterialByNode',{}).get(node) or atlas[t['sourceMaterial']]['runtimeMaterial'];spec=atlas[t['sourceMaterial']];uv=t['sourceUvsBlender'].copy();shift=np.floor(uv.min(0)) if spec.get('compactPeriodicUVs') else np.zeros(2);uv=(uv-shift)*spec['atlasUVScale']+spec['atlasUVOffset'];uv[:,1]=1-uv[:,1]
            world=(fit@np.column_stack([t['positions'],np.ones(3)]).T).T[:,:3];normal=(np.linalg.inv(fit[:3,:3]).T@t['normals'].T).T;normal/=np.linalg.norm(normal,axis=1)[:,None];local=(np.linalg.inv(frames[node])@np.column_stack([world,np.ones(3)]).T).T[:,:3];ln=(frames[node][:3,:3].T@normal.T).T;ln/=np.linalg.norm(ln,axis=1)[:,None]
            result={'node':node,'material':mat,'sourceMaterial':t['sourceMaterial'],'object':m['object'],'positions':local,'worldPositions':world,'normals':ln,'uvs':uv,'sourceUvs':t['sourceUvsBlender'].copy(),'nativeEffect':False,'reverseWinding':node in cfg.get('reverseWindingNodes',[])}
            if result['reverseWinding']:
                for name in ['positions','worldPositions','normals','uvs','sourceUvs']:result[name]=result[name][[0,2,1]]
            out.append(result);counts[t['sourceMaterial']]+=1;node_counts[node]+=1;material_counts[mat]+=1
    assert sum(counts.values())==cfg['expectedTriangles']
    for t in native:
        if t['material'] not in cfg.get('nativeSupplementMaterials',[]):continue
        assert len(set(t['nodes']))==1,('Native effect spans nodes; retain it in renderer as an external native supplement',t['material'],t['nodes']);node=t['nodes'][0];local=(np.linalg.inv(frames[node])@np.column_stack([t['positions'],np.ones(3)]).T).T[:,:3];out.append({'node':node,'material':t['material'],'positions':local,'worldPositions':t['positions'],'uvs':t['uvs'],'colors':t['colors'],'nativeEffect':True})
    # Reconstruct both Source and native fits from independently decoded geometry.
    cage_materials=cfg['fitAudit']['nativeMaterials'];cages=[t['positions'] for t in native if t['material'] in cage_materials and np.linalg.norm(np.cross(t['positions'][1]-t['positions'][0],t['positions'][2]-t['positions'][0]))>1e-10]
    samples=np.array([t['worldPositions'].mean(0) for t in out if not t['nativeEffect'] and t['object'] in cfg['fitAudit']['sourceObjects']]);_,ds,_=closest_surfaces(samples,cages)
    forward=[]
    for yaw in [0,180]:
        candidate=fit.copy()
        if yaw!=cfg['yawDegrees']:
            candidate[:3,:3]=np.diag([-1,1,-1])@candidate[:3,:3];anchor=np.array(cfg['fitAnchorGame']);candidate[:3,3]=-candidate[:3,:3]@anchor
        sp=np.array([(candidate@np.r_[t['positions'].mean(0),1])[:3] for m in meshes if m['object'] in cfg['fitAudit']['sourceObjects'] for t in m['triangles']]);_,dd,_=closest_surfaces(sp,cages);forward.append({'yawDegrees':yaw,'surfaceDistanceP50P95Maximum':np.percentile(dd,[50,95,100]).tolist()})
    summary={'sourceTriangles':dict(counts),'nativeNodesSourceTriangles':dict(node_counts),'runtimeMaterialSourceTriangles':dict(material_counts),'sourceConversionMatrixGame':fit.tolist(),'uniformScale':float(singular.mean()),'fitNativeSurfaceDistanceP50P95Maximum':np.percentile(ds,[50,95,100]).tolist(),'fitForwardCandidates':forward,'componentAndCageProof':proof,'nativeSupplementTriangles':sum(t['nativeEffect'] for t in out),'externalNativeSupplementMaterials':cfg.get('externalNativeSupplementMaterials',[]),'externalNativeSupplementTriangles':sum(t['material'] in cfg.get('externalNativeSupplementMaterials',[]) for t in native),'sourceTopologyPreserved':True,'allSourceTrianglesAssignedToOneNativeNode':True,'runtimeAcceptanceRequired':True}
    return contract,frames,out,summary
