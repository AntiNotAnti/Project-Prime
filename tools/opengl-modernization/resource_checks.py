"""Explicitly scheduled resource stress or short quality/fill proxy captures."""
import argparse,hashlib,json,os,pathlib,subprocess,time,datetime
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--group',choices=['stress','same','shadow','fill','texture','cross'],required=True)
 p.add_argument('--dotnet',default='dotnet');p.add_argument('--harness',type=pathlib.Path,default=ROOT/'tools/native-render-check/bin/Release/net10.0/native-render-check.dll')
 p.add_argument('--output',type=pathlib.Path,default=ROOT/'artifacts/opengl-modernization');p.add_argument('--prefix',default='resource-check')
 p.add_argument('--previous-harness',type=pathlib.Path,help='Required for cross-build pair')
 p.add_argument('--cycles',type=int,default=30);p.add_argument('--frames',type=int,default=120);p.add_argument('--seconds',type=int,default=8)
 p.add_argument('--failure-stages',default='retained-promotion-after-vbo,retained-vao-after-create,retained-combined-after-vbo,depth-snapshot-after-framebuffer,shadow-after-framebuffer')
 p.add_argument('--limit',type=int);p.add_argument('--start',type=int,default=0);p.add_argument('--user-data')
 a=p.parse_args()
 if not a.prefix or any(c in a.prefix for c in '/\\') or a.prefix in ('.','..'):p.error('prefix must be filename component')
 if not 1<=a.cycles<=200 or not 30<=a.frames<=600 or not 8<=a.seconds<=120:p.error('invalid cycle/frame/seconds bounds')
 if a.group=='stress':plan=[('stress',['-resourcestress','-resourcecycles',str(a.cycles),'-resourceframes',str(a.frames),'-failure-stages',a.failure_stages,'-shadows','High'],True,'current')]
 elif a.group=='same':plan=[(arm+'-'+rep,['-shadows','Off'],enabled,'current') for arm,rep,enabled in [('baseline','a',False),('candidate','a',True),('candidate','b',True),('baseline','b',False)]]
 elif a.group=='shadow':plan=[(q.lower()+'-'+arm+'-'+rep,['-shadows',q],enabled,'current') for q in ['Low','High','Ultra'] for arm,rep,enabled in [('baseline','a',False),('candidate','a',True),('candidate','b',True),('baseline','b',False)]]
 elif a.group=='fill':plan=[('scale-'+str(v)+'-'+arm,['-shadows','Off','-render-scale',str(v),'-texturequality','High'],enabled,'current') for v in [100,75,50] for arm,enabled in [('baseline',False),('candidate',True)]]
 elif a.group=='texture':plan=[('texture-'+v.lower()+'-'+arm,['-shadows','Off','-render-scale','100','-texturequality',v],enabled,'current') for v in ['High','Low'] for arm,enabled in [('baseline',False),('candidate',True)]]
 else:
  if a.previous_harness is None:p.error('cross requires --previous-harness')
  plan=[(arm+'-'+rep,['-shadows','Off'],enabled,build) for arm,rep,enabled,build in [('baseline','a',False,'previous'),('candidate','a',True,'current'),('candidate','b',True,'current'),('baseline','b',False,'previous')]]
 if a.start<0 or a.start>=len(plan) or a.limit is not None and a.limit<=0:p.error('invalid start/limit')
 selected=plan[a.start:][:a.limit]
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);h=a.harness.absolute()
 if h.is_symlink():p.error('materialize harness beside intended client')
 for label,extra,enabled,build in selected:
  h=(a.previous_harness if build=='previous' else a.harness).absolute()
  if h.is_symlink():p.error('materialize harness beside intended client')
  binary={'harness':sha(h),'client':sha(h.with_name('ProjectPrime.dll'))}
  identity_path=out/(a.prefix+'-'+build+'-identity.json');identity=json.loads(identity_path.read_text()) if identity_path.exists() else None
  if identity and identity['binarySha256']!=binary:raise RuntimeError('Binary changed across captures')
  name=a.prefix+'-'+a.group+'-'+label;rp=out/(name+'.json');lp=out/(name+'.log');ip=out/(name+'.invocation.json')
  if any(v.exists() for v in (rp,lp,ip)):raise RuntimeError('Refusing overwrite '+name)
  command=[a.dotnet,str(h),'-room','MP3 PROVING GROUND','-players','8','-seconds',str(a.seconds),'-hz','-1','-size','1920x1080','-output',str(rp)]+extra
  if a.group not in ('stress','same','cross'):command+=['-glprofile']
  env=dict(os.environ)
  if a.user_data:env['PROJECT_PRIME_USER_DATA']=a.user_data
  for k in ('PROJECT_PRIME_GL_PROFILE','PROJECT_PRIME_GL_GPU_TIMER','PROJECT_PRIME_GL_FORCE_DEPTH_REPLAY','PROJECT_PRIME_GL_FORCE_STATE_REQUESTS'):env.pop(k,None)
  for flag in ('ROOM_IDENTITY','MESH_OBJECT','SHADOW_PREP','UNLIT_SHADER','SHADOW_GROUPS'):env['PROJECT_PRIME_GL_'+flag]=str(int(enabled))
  inv=dict(schema=1,group=a.group,build=build,arm='candidate' if enabled else 'baseline',command=command,binarySha256=binary,expectedAssemblyMvid=identity['assemblyMvid'] if identity else None,requestedFlags={k:v for k,v in env.items() if k.startswith('PROJECT_PRIME_GL_')},profiled=a.group not in ('stress','same','cross'),purpose='Lifecycle evidence' if a.group=='stress' else 'Unprofiled FPS control' if a.group in ('same','cross') else 'Profiled CPU/frame response proxy; instrumentation affects FPS; does not prove GPU bandwidth or true overdraw')
  inv['startedUtc']=datetime.datetime.now(datetime.timezone.utc).isoformat();started=time.monotonic()
  ip.write_text(json.dumps(inv,indent=2)+'\n')
  with lp.open('w') as f:subprocess.run(command,env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
  inv['completedUtc']=datetime.datetime.now(datetime.timezone.utc).isoformat();inv['wallSeconds']=time.monotonic()-started
  j=json.loads(rp.read_text());mvid=j.get('assemblyMvid')
  if not j.get('complete') or not mvid or identity and identity['assemblyMvid']!=mvid:raise RuntimeError('Incomplete/wrong identity')
  if binary!={'harness':sha(h),'client':sha(h.with_name('ProjectPrime.dll'))}:raise RuntimeError('Binary changed during capture')
  if a.group!='stress' and any(v!=0 for k,v in j['cleanup'].items() if k.endswith('After')):raise RuntimeError('Cleanup failed')
  if not identity:
   identity=dict(schema=1,assemblyMvid=mvid,binarySha256=binary);identity_path.write_text(json.dumps(identity,indent=2)+'\n')
  inv.update(actualAssemblyMvid=mvid,rawResultSha256=sha(rp),rawLogSha256=sha(lp));ip.write_text(json.dumps(inv,indent=2)+'\n')
  print(name,mvid,flush=True)
if __name__=='__main__':main()
