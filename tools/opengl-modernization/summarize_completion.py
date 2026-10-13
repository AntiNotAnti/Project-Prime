"""Summarize completed native completion captures; never launches native work."""
import argparse,hashlib,json,math,pathlib,statistics
ROOT=pathlib.Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def finite(v):
 if isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) or v<0:raise ValueError('Invalid finite nonnegative metric')
 return v
def med(values):
 values=[finite(v) for v in values if v is not None]
 return statistics.median(values) if values else None
def read(rp):
 j=json.loads(rp.read_text());ip=rp.with_suffix('.invocation.json');lp=rp.with_suffix('.log');inv=json.loads(ip.read_text())
 if not j.get('complete'):raise ValueError('Incomplete native result')
 mvid=j['assemblyMvid']
 if inv.get('actualAssemblyMvid')!=mvid or inv.get('expectedAssemblyMvid') not in (None,mvid):raise ValueError('MVID mismatch')
 if inv.get('rawResultSha256')!=sha(rp) or inv.get('rawLogSha256')!=sha(lp):raise ValueError('Raw evidence modified')
 flags=inv['requestedFlags'];candidate=inv.get('arm')=='candidate' if 'arm' in inv else '-candidate-' in rp.stem
 for f in ('ROOM_IDENTITY','MESH_OBJECT','SHADOW_PREP','UNLIT_SHADER','SHADOW_GROUPS'):
  if str(flags.get('PROJECT_PRIME_GL_'+f,flags.get(f)))!=str(int(candidate)):raise ValueError('Completion flag mismatch '+f)
 command=inv['command']
 def arg(key,default=None):return command[command.index(key)+1] if key in command else default
 if j.get('room')!=arg('-room') or j.get('cap')!=int(arg('-hz')):raise ValueError('Actual room/cap differs from invocation')
 size=[int(x) for x in arg('-size').split('x')]
 if j.get('framebuffer')!=size:raise ValueError('Actual framebuffer differs from invocation')
 if j.get('shadows')!=arg('-shadows','Off'):raise ValueError('Actual shadow quality differs')
 if '-render-scale' in command and j.get('resolutionScale')!=int(arg('-render-scale')):raise ValueError('Actual render scale differs')
 if '-texturequality' in command and j.get('textureQuality')!=arg('-texturequality'):raise ValueError('Actual texture quality differs')
 if '-lighting' in command and j.get('lighting')!=(arg('-lighting').lower()=='true'):raise ValueError('Actual lighting differs')
 cleanup=j['cleanup']
 if not cleanup.get('shadowReleased') or any(v!=0 for k,v in cleanup.items() if k.endswith('After')):raise ValueError('Nonzero resources after cleanup')
 metrics={k:finite(j[k]) for k in ('meanCompletedFrameFps','frameIntervalP50Ms','frameIntervalP95Ms','frameIntervalP99Ms','simulationHz','droppedSimulationSteps','stalls')}
 group=inv['group'];visual=group=='visual'
 if not visual and (j.get('captures') or j.get('captureKillFeedLifetimeStabilized')):raise ValueError('Readback/stable HUD in performance evidence')
 if not visual and (j['droppedSimulationSteps'] or j['stalls']):raise ValueError('Steady performance drops/stalls')
 config={k:j.get(k) for k in ('room','cap','effectiveCap','shadows','framebuffer','renderSize','textureQuality','resolutionScale','lighting','renderer','vendor','glVersion','vsync')}
 record=dict(name=rp.stem,group=group,arm='candidate' if candidate else 'baseline',build=inv.get('build','current'),assemblyMvid=mvid,
  configuration=config,unavailableMetadata=[k for k,v in config.items() if v is None],harness=metrics,cleanup=cleanup,invocation=inv,rawResult=str(rp),rawResultSha256=sha(rp),rawLog=str(lp),rawLogSha256=sha(lp),rawInvocation=str(ip),rawInvocationSha256=sha(ip))
 profiled=inv.get('profiled',group in ('profile','shadow','fill','texture'))
 if profiled:
  raw=lp.read_text();before,after=raw.split('NATIVERENDER measurement-start',1)
  frames=[json.loads(l.split('[glframe-json]',1)[1]) for l in after.splitlines() if '[glframe-json]' in l][2:]
  ids={f['window'] for f in frames}
  passes=[json.loads(l.split('[glpasses-json]',1)[1]) for l in raw.splitlines() if '[glpasses-json]' in l]
  passes=[p for p in passes if p['window'] in ids]
  if any(p.get('schema')!=1 or p.get('samples')!=240 for p in passes):raise ValueError('Invalid pass window schema')
  if not frames or len(ids)!=len(frames) or len(passes)!=len(frames) or len({p['window'] for p in passes})!=len(frames):raise ValueError('Missing/duplicate profile alignment')
  if any(f['schema']!=1 or f['samples']!=240 or f['context']['assemblyMvid']!=mvid for f in frames):raise ValueError('Invalid profile schema/identity')
  if len({json.dumps(f['context'],sort_keys=True) for f in frames})!=1:raise ValueError('Profile configuration changed')
  if any(f['droppedSimulationSteps'] or f['stalls'] for f in frames):raise ValueError('Retained profile drops/stalls')
  record['profile']=dict(retainedWindows=len(frames),warmupWindowsAfterMeasurementMarker=2,sufficientFourWindows=len(frames)>=4,
   context=frames[0]['context'],cpuP99Ms={k:med([p['cpu'][k]['p99Ms'] for p in passes]) for k in passes[0]['cpu']},
   counters={k:med([p['counters'][k] for p in passes]) for k in passes[0]['counters']},
   frame={k:med([f[k] for f in frames]) for k in ('frameIntervalP95Ms','frameIntervalP99Ms','renderCpuP99Ms','simulationHz')},gpuStatus='UNVERIFIED')
 return record,j

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--prefix',default='completion-final');p.add_argument('--artifacts',type=pathlib.Path,default=ROOT/'artifacts/opengl-modernization');p.add_argument('--output',type=pathlib.Path,default=ROOT/'docs/performance/opengl-completion-results.json');p.add_argument('--visual-case-prefix',action='append',default=[],help='Explicit case=prefix replacement control; preserve original comparison separately');p.add_argument('--visual-control-reason',default='');a=p.parse_args()
 if not a.prefix or any(c in a.prefix for c in '/\\') or a.prefix in ('.','..'):p.error('prefix must be filename component')
 overrides={}
 for entry in a.visual_case_prefix:
  case,sep,prefix=entry.partition('=')
  if not sep or case not in ('solo','two','shadow','bomb','impact','combat','unlit','shadow-ultra') or not prefix or any(c in prefix for c in '/\\') or prefix in ('.','..') or case in overrides:p.error('invalid/duplicate visual case prefix')
  overrides[case]=prefix
 if overrides and not a.visual_control_reason:p.error('replacement controls require an explicit reason')
 directory=a.artifacts.resolve();runs=[];errors=[];comparisons=[];payload={};separate_lifecycle=[]
 paths=set(directory.glob(a.prefix+'-*.json'))
 for case,prefix in overrides.items():
  paths.update(directory/(prefix+'-visual-'+case+'-'+arm+'-a.json') for arm in ('baseline','candidate'))
 for rp in sorted(paths):
  if rp.name.endswith(('.invocation.json','-identity.json')):continue
  if not rp.with_suffix('.invocation.json').exists():continue
  try:
   inv=json.loads(rp.with_suffix('.invocation.json').read_text())
   if inv.get('purpose')=='Lifecycle/memory diagnosis with final drain; no FPS interpretation':
    j=json.loads(rp.read_text())
    if not j.get('complete') or not j.get('resourceDrainRequested') or j.get('cyclesCompleted')!=inv.get('requestedCycles') or inv.get('actualAssemblyMvid')!=j.get('assemblyMvid') or inv.get('rawResultSha256')!=sha(rp) or inv.get('rawLogSha256')!=sha(rp.with_suffix('.log')):raise ValueError('Invalid separate lifecycle evidence')
    separate_lifecycle.append(dict(name=rp.stem,rawResult=str(rp),rawResultSha256=sha(rp),reason='Separate lifecycle diagnostic; excluded from FPS and visual-control aggregation'))
    continue
   r,j=read(rp);runs.append(r);payload[rp.stem]=j
  except (ValueError,KeyError,OSError,TypeError) as e:errors.append(dict(name=rp.stem,error=str(e)))
 identities={}
 for r in runs:
  key=r['build'];identity=(r['assemblyMvid'],json.dumps(r['invocation']['binarySha256'],sort_keys=True))
  if key in identities and identities[key]!=identity:errors.append(dict(name=r['name'],error='Mixed binary identity within build'))
  identities[key]=identity
 for group in ('same','cross'):
  matching=[r for r in runs if r['group']==group]
  common={k for r in matching for k in r['configuration']}
  if group=='cross':common={k for k in common if all(r['configuration'].get(k) is not None for r in matching)}
  if len({json.dumps({k:r['configuration'][k] for k in common},sort_keys=True) for r in matching})>1:errors.append(dict(name=group,error='Matched available configurations differ'))
 visuals=[r for r in runs if r['group']=='visual'];cases=('solo','two','shadow','bomb','impact','combat','unlit','shadow-ultra');pending=[]
 if visuals:
  from PIL import Image
  for case in cases:
   names=[overrides.get(case,a.prefix)+'-visual-'+case+'-'+arm+'-a' for arm in ('baseline','candidate')]
   if any(n not in payload for n in names):pending.append('visual:'+case);continue
   x,y=[directory/n for n in names];j,k=[payload[n] for n in names]
   def capture_shapes(result):return [{key:c.get(key) for key in ('index','simulationFrame','camera','width','height')} for c in result['captures']]
   expected_count=2 if case=='combat' else 1
   if len(j['captures'])!=expected_count or len(k['captures'])!=expected_count or capture_shapes(j)!=capture_shapes(k):errors.append(dict(name=case,error='Capture shape/index mismatch'));continue
   def files(d):return {f.relative_to(d) for f in d.rglob('*') if f.is_file() and (f.suffix.lower()=='.png' or f.suffix.lower()=='.bin' and 'depth' in f.name.lower())}
   xf,yf=files(x),files(y)
   if not xf or xf!=yf:errors.append(dict(name=case,error='Missing/extra visual files'));continue
   for f in sorted(xf):
    if f.suffix.lower()=='.png':
     with Image.open(x/f) as im:xx=im.convert('RGBA');xs=xx.size;xb=xx.tobytes()
     with Image.open(y/f) as im:yy=im.convert('RGBA');ys=yy.size;yb=yy.tobytes()
     eq=xs==ys and xb==yb;fmt='decoded-RGBA8'
    else:eq=(x/f).read_bytes()==(y/f).read_bytes();fmt='float32-depth-bytes'
    comparisons.append(dict(case=case,file=str(f),format=fmt,identical=eq,referenceSha256=sha(x/f),candidateSha256=sha(y/f)))
    if not eq:errors.append(dict(name=case+'/'+str(f),error='Visual mismatch'))
 else:pending.append('visual:all')
 for group,count in (('same',4),('cross',4),('shadow',12),('fill',6),('texture',4)):
  actual=sum(r['group']==group for r in runs)
  if actual<count:pending.append(group+':'+str(actual)+'/'+str(count))
 report=dict(schema=1,prefix=a.prefix,complete=not pending and not errors,passedPresentValidation=bool(runs) and not errors,
  pending=pending,errors=errors,runs=runs,separateLifecycle=separate_lifecycle,visualControlOverrides=overrides,visualControlReason=a.visual_control_reason,comparisons=comparisons,comparisonCount=len(comparisons),identicalComparisons=sum(c['identical'] for c in comparisons),
  limits='Mac native evidence only unless capture metadata says otherwise. Profiled FPS includes instrumentation. Resolution and texture sweeps are CPU/frame-response proxies, not direct bandwidth or true overdraw measurements. Cross-build comparison is intentionally mixed-MVID. GPU timing unverified. Missing groups cannot pass.')
 a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,indent=2)+'\n');print('runs',len(runs),'comparisons',len(comparisons),'pending',pending,'errors',errors)
 return bool(errors)
if __name__=='__main__':raise SystemExit(main())
