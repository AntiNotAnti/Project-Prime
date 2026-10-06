"""Original Source selection, native muzzle and independent fit reconstruction."""
from pathlib import Path
from collections import Counter
import math,json
import numpy as np
from glb import load,read
from alternate_effects import effect_records
T=np.array([[1,0,0,0],[0,0,-1,0],[0,1,0,0],[0,0,0,1]],float)
def native_frames(contract):
    world={}
    for n in contract['nodes']:
        x,y,z=n['rotationRadians'];cx,sx=math.cos(x),math.sin(x);cy,sy=math.cos(y),math.sin(y);cz,sz=math.cos(z),math.sin(z);local=np.eye(4);local[:3,:3]=np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])@np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])@np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])@np.diag(n['scale']);local[:3,3]=np.array(n['position'])/contract['modelScale'];world[n['name']]=(world[n['parentName']] if n['parentName'] else np.eye(4))@local
    return world

def source_frames(doc):
    parents={c:i for i,n in enumerate(doc['nodes']) for c in n.get('children',[])};world={}
    def frame(i):
        if i in world:return world[i]
        n=doc['nodes'][i]
        if 'matrix'in n:local=np.array(n['matrix']).reshape(4,4).T
        else:
            x,y,z,w=n.get('rotation',[0,0,0,1]);local=np.eye(4);local[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])@np.diag(n.get('scale',[1,1,1]));local[:3,3]=n.get('translation',[0,0,0])
        world[i]=(frame(parents[i]) if i in parents else np.eye(4))@local;return world[i]
    return {doc['nodes'][i]['name']:frame(i) for i in doc['skins'][0]['joints']}

def rotation(a,b):
    a=a/np.linalg.norm(a);b=b/np.linalg.norm(b);v=np.cross(a,b);c=float(a@b);s=np.linalg.norm(v)
    if s<1e-12:
        if c>0:return np.eye(3)
        axis=np.cross(a,[1,0,0] if abs(a[0])<.9 else [0,1,0]);axis/=np.linalg.norm(axis);return 2*np.outer(axis,axis)-np.eye(3)
    k=np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]]);return np.eye(3)+k+k@k*((1-c)/(s*s))

def reconstruct(cfg,source,kit,receipt=None,report=None):
    kit=Path(kit);sd,sb=load(source);contract=next(m for m in json.loads((kit/'native-reference.json').read_text())['models'] if m['part']=='halfturret');native=native_frames(contract);names=list(native);sourceworld=source_frames(sd);removed={};counts=Counter();points=[];allpoints=[]
    for mesh in sd['meshes']:
        if mesh['name'] not in cfg['sourceMeshes']:continue
        for pr in mesh['primitives']:
            mat=sd['materials'][pr['material']]['name'];ps=np.array(read(sd,sb,pr['attributes']['POSITION']));ii=np.array(read(sd,sb,pr['indices'])).reshape(-1);allpoints.extend(ps)
            if mat in cfg.get('excludedSourceMaterials',[]):removed[mat]={'triangles':len(ii)//3,'boundsGame':[ps[ii].min(0).tolist(),ps[ii].max(0).tolist()]}
            else:counts[mat]+=len(ii)//3;points.extend(ps[ii])
    assert {m:v['triangles'] for m,v in removed.items()}==cfg['excludedSourceMaterialTriangles'];assert sum(counts.values())==cfg['expectedTriangles']
    if receipt:
        assert receipt['excluded']==removed and receipt['retainedMaterialTriangles']==dict(counts) and receipt['originalBinaryPayloadUnchanged']
    unique=np.unique(np.array(points),axis=0);proof=cfg['muzzleSourceGeometryProof'];front=unique[unique[:,2]>unique[:,2].max()-proof['frontBandGameZ']];muzzle=front.mean(0);assert len(front)==proof['frontUniquePositions'];assert np.max(abs(muzzle-proof['sourcePointGame']))<1e-7;assert np.max(abs((T@np.r_[muzzle,1])[:3]-cfg['muzzle']['sourcePoint']))<1e-7
    records=effect_records(kit,contract,native,cfg['nativeFitMaterials'],names);nativepoints=np.array([r[0] for rs in records.values() for r in rs]);tip=np.array([r[0] for rs in records.values() for r in rs if r[3][names.index('TurretTip')]==1]);assert len(tip)==18;nativemuzzle=tip.mean(0);assert np.max(abs(nativemuzzle-proof['nativeTipMeanGame']))<1e-7;assert np.max(abs((native['TurretTip']@np.r_[cfg['muzzle']['nativeLocalPoint'],1])[:3]-nativemuzzle))<1e-7
    heads={n:w[:3,3] for n,w in native.items()};sourceheads={n:w[:3,3] for n,w in sourceworld.items()};pairerrors={}
    for target,label in [('Thighs','Thigh'),('Calf','Calf'),('Foot','Foot')]:
        paired=(sourceheads['ValveBiped.Bip01_L_'+label]+sourceheads['ValveBiped.Bip01_R_'+label])/2;configured=(T.T@np.r_[cfg['fit'][target]['sourcePoint'],1])[:3];error=float(np.max(abs(paired-configured)));assert error<1e-7;pairerrors[target]=error
    allpoints=np.array(allpoints);scale=(nativepoints[:,1].max()-nativepoints[:,1].min())/(allpoints[:,1].max()-allpoints[:,1].min());yaw=math.radians(cfg['yawDegrees']);c,s=math.cos(yaw),math.sin(yaw);globalfit=np.eye(4);globalfit[:3,:3]=np.array([[c,0,s],[0,1,0],[-s,0,c]])*scale;globalfit[:3,3]=heads[cfg['rootNative']]-globalfit[:3,:3]@sourceheads[cfg['rootSource']];fits={};anchors={}
    for name,spec in cfg['fit'].items():
        anchor=(T.T@np.r_[spec['sourcePoint'],1])[:3] if 'sourcePoint'in spec else sourceheads[spec['anchor']]
        if spec.get('inheritFitFrom'):fits[name]=fits[spec['inheritFitFrom']].copy();continue
        if spec.get('preserveSourceShape'):fits[name]=globalfit.copy();continue
        local=np.eye(3);endpoint=None
        if spec.get('muzzle'):endpoint=muzzle;target=nativemuzzle
        elif 'endSourcePoint'in spec:endpoint=(T.T@np.r_[spec['endSourcePoint'],1])[:3];target=heads[spec['endNative']]
        if endpoint is not None:
            d=globalfit[:3,:3]@(endpoint-anchor);v=target-heads[name];u=d/np.linalg.norm(d);stretch=np.eye(3)+(np.linalg.norm(v)/np.linalg.norm(d)-1)*np.outer(u,u);radial=np.eye(3)*spec.get('radialScale',1.)+(1-spec.get('radialScale',1.))*np.outer(u,u);local=rotation(d,v)@stretch@radial
        if 'orientFromSourcePoint'in spec:prior=(T.T@np.r_[spec['orientFromSourcePoint'],1])[:3];local=rotation(globalfit[:3,:3]@(anchor-prior),heads[name]-heads[spec['orientNative']])
        f=np.eye(4);f[:3,:3]=local;f[:3,3]=heads[name]-local@(globalfit@np.r_[anchor,1])[:3];fits[name]=f@globalfit;anchors[name]=float(np.linalg.norm((fits[name]@np.r_[anchor,1])[:3]-heads[name]));assert anchors[name]<1e-8
    muzzleerror=float(np.linalg.norm((fits['TurretTip']@np.r_[muzzle,1])[:3]-nativemuzzle));assert muzzleerror<1e-8;fittedfront=(fits['TurretTip']@np.column_stack([front,np.ones(len(front))]).T).T[:,:3];sourcewidth=fittedfront.max(0)-fittedfront.min(0);nativewidth=tip.max(0)-tip.min(0);frameerror=None
    if report:
        frameerror=max(float(np.max(abs(T.T@np.array(report['bindFitting'][n]['bakedSourceToNative'])@T-f))) for n,f in fits.items());assert frameerror<1e-5,('Independent turret fit differs',frameerror)
    return {'pass':True,'sourceSelectionOriginalGlb':{'excluded':removed,'retainedMaterialTriangles':dict(counts)},'sourceMuzzleFrontUniqueVertices':len(front),'sourceMuzzleGame':muzzle.tolist(),'nativeTipIndexedReferenceCorners':len(tip),'nativeMuzzleGame':nativemuzzle.tolist(),'muzzleErrorGame':muzzleerror,'pairedSourceBoneAnchorMaximumErrors':pairerrors,'independentUniformHeightScale':float(scale),'independentGameFitMatrices':{n:f.tolist() for n,f in fits.items()},'maximumReportedFitMatrixError':frameerror,'nativeAnchorErrors':anchors,'radialFitIsExplicitBakedConstraint':cfg['fit']['TurretMid'].get('radialScale',1.),'fittedSourceLandmarkBandWidthHeightGame':sourcewidth[:2].tolist(),'nativeVisualTipWidthHeightGame':nativewidth[:2].tolist(),'frontBandIsMuzzleLandmarkNotWholeCannonSilhouette':True,'runtimeProjectileEffectAlignmentStillRequired':True}
