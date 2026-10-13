"""Explicit 60-cycle diagnostic stress with bounded audio/GC/GL drain; never FPS evidence."""
import argparse,hashlib,json,os,pathlib,statistics,subprocess,time
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--dotnet',default='dotnet');p.add_argument('--harness',type=pathlib.Path,default=ROOT/'tools/native-render-check/bin/Release/net10.0/native-render-check.dll')
 p.add_argument('--output',type=pathlib.Path,default=ROOT/'artifacts/opengl-modernization');p.add_argument('--prefix',default='completion-memory-60')
 p.add_argument('--report',type=pathlib.Path,default=ROOT/'docs/performance/opengl-completion-memory.json')
 p.add_argument('--cycles',type=int,default=60);p.add_argument('--frames',type=int,default=120);p.add_argument('--user-data');p.add_argument('--summarize-only',action='store_true')
 a=p.parse_args()
 if not a.prefix or any(c in a.prefix for c in '/\\') or a.prefix in ('.','..'):p.error('prefix must be filename component')
 if not 1<=a.cycles<=200 or not 30<=a.frames<=600:p.error('cycles1..200 and frames30..600 required')
 out=a.output.resolve();rp=out/(a.prefix+'.json');lp=out/(a.prefix+'.log');ip=out/(a.prefix+'.invocation.json')
 if a.report.exists():raise RuntimeError('Refusing report overwrite: '+str(a.report))
 if not a.summarize_only:
  if any(x.exists() for x in (rp,lp,ip)):raise RuntimeError('Refusing raw evidence overwrite')
  out.mkdir(parents=True,exist_ok=True);h=a.harness.absolute()
  if h.is_symlink():raise RuntimeError('Materialize harness beside intended client')
  client=h.with_name('ProjectPrime.dll');binary={'harness':sha(h),'client':sha(client)}
  env=dict(os.environ)
  if a.user_data:env['PROJECT_PRIME_USER_DATA']=a.user_data
  for key in ('PROJECT_PRIME_GL_PROFILE','PROJECT_PRIME_GL_GPU_TIMER','PROJECT_PRIME_GL_FORCE_DEPTH_REPLAY','PROJECT_PRIME_GL_FORCE_STATE_REQUESTS'):env.pop(key,None)
  env['PROJECT_PRIME_GL_FORCE_LEGACY']='0'
  for flag in ('ROOM_IDENTITY','MESH_OBJECT','SHADOW_PREP','SHADOW_GROUPS','UNLIT_SHADER'):env['PROJECT_PRIME_GL_'+flag]='1'
  command=[a.dotnet,str(h),'-room','MP3 PROVING GROUND','-players','8','-size','1920x1080','-hz','-1','-seconds','8','-shadows','High',
           '-resourcestress','-resourcecycles',str(a.cycles),'-resourceframes',str(a.frames),'-resource-drain','-output',str(rp)]
  inv=dict(schema=1,command=command,binarySha256=binary,requestedCycles=a.cycles,requestedFrames=a.frames,
           requestedFlags={k:v for k,v in env.items() if k.startswith('PROJECT_PRIME_GL_')},purpose='Lifecycle/memory diagnosis with final drain; no FPS interpretation')
  ip.write_text(json.dumps(inv,indent=2)+'\n');started=time.monotonic()
  with lp.open('w') as f:subprocess.run(command,env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
  j=json.loads(rp.read_text())
  if binary!={'harness':sha(h),'client':sha(client)}:raise RuntimeError('Binary changed during diagnostic')
  inv.update(actualAssemblyMvid=j['assemblyMvid'],elapsedWallSeconds=time.monotonic()-started,rawResultSha256=sha(rp),rawLogSha256=sha(lp))
  ip.write_text(json.dumps(inv,indent=2)+'\n')
 j=json.loads(rp.read_text());inv=json.loads(ip.read_text())
 if inv['rawResultSha256']!=sha(rp) or inv['rawLogSha256']!=sha(lp) or inv['actualAssemblyMvid']!=j['assemblyMvid']:raise RuntimeError('Raw identity/hash mismatch')
 if not j.get('complete') or j['cyclesCompleted']!=inv['requestedCycles'] or not j.get('resourceDrainRequested'):raise RuntimeError('Incomplete requested diagnostic')
 records=j['records']
 if len(records)!=2*j['cyclesCompleted'] or len(j['studioReloads'])!=j['cyclesCompleted']:raise RuntimeError('Missing arms/Studio sessions')
 if any(r['simulationFrame']!=inv['requestedFrames'] for r in records):raise RuntimeError('Capture step mismatch')
 if any(v!=0 for r in records for k,v in r['cleanup'].items() if k.endswith('After')):raise RuntimeError('Owned resources retained')
 if any(r['candidate'] and r['stage'] and not r['injectionHit'] for r in records):raise RuntimeError('Requested failure did not hit')
 def mean(block,key):
  values=[r.get('memory',{}).get(key) for r in block];values=[v for v in values if v is not None]
  return statistics.mean(values) if values else None
 blocks=[dict(firstArm=i,lastArm=min(i+9,len(records)-1),arms=len(records[i:i+10]),
  means={k:mean(records[i:i+10],k) for k in ('workingSetBytes','managedLiveBytes','gcCommittedBytes','gcHeapSizeBytes','gcFragmentedBytes','processThreadCount')}) for i in range(0,len(records),10)]
 report=dict(schema=1,assemblyMvid=j['assemblyMvid'],cycles=j['cyclesCompleted'],resourceAssertionsPassed=True,
  nativeMemoryBoundedness='UNVERIFIED: inspect block trends and final drain; owned-object release does not establish process/native memory boundedness',
  blocks=blocks,drainSamples=j['drainSamples'],drainError=j.get('drainError'),audioShutdown=j['audioShutdown'],
  rawResult=str(rp),rawResultSha256=sha(rp),rawLog=str(lp),rawLogSha256=sha(lp),invocation=inv,
  limits='Live managed heap differs from GC committed/fragmented memory and RSS. Private memory may be unavailable on Mac. Audio waits, GC/finalizers and GL.Finish are diagnostic-only and excluded from FPS evidence.')
 a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(report,indent=2)+'\n');print('memory report',a.report)
if __name__=='__main__':main()
