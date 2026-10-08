#!/usr/bin/env python3
"""Run matched, opt-in production UI diagnostics. Does not build or install anything."""
import argparse, hashlib, json, os, pathlib, shutil, statistics, subprocess, sys

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--legacy',required=True,type=pathlib.Path,help='Full-build Avalonia ProjectPrime.dll')
parser.add_argument('--native',required=True,type=pathlib.Path,help='Full-build toolkit-free ProjectPrime.dll')
parser.add_argument('--data',required=True,type=pathlib.Path,help='Isolated writable user-data fixture; never the real user-data folder')
parser.add_argument('--out',required=True,type=pathlib.Path)
parser.add_argument('--dotnet',default='dotnet')
parser.add_argument('--backend',default='metal',choices=['metal','vulkan','dx12'])
parser.add_argument('--runs',type=int,default=3)
parser.add_argument('--seconds',type=int,default=20)
args=parser.parse_args()
if not 3<=args.runs<=10 or not 5<=args.seconds<=120:parser.error('Use 3..10 runs and 5..120 sample seconds.')
args.out.mkdir(parents=True,exist_ok=True)
def asset_digest(root):
 digest=hashlib.sha256()
 for path in sorted(root.rglob('*')):
  if path.is_file():
   digest.update(path.relative_to(root).as_posix().encode());digest.update(b'\0');digest.update(path.read_bytes())
 return digest.hexdigest()
checkpoints={}
for path in [args.legacy,args.native]:
 if not path.is_file():parser.error(f'Missing full-build assembly: {path}')
 payload=path.read_bytes()
 if b'homePresentationVerified' not in payload:parser.error(f'{path} lacks verified Home presentation diagnostics; refuse to benchmark a startup/title screen')
 for guard in ['Live account authentication is disabled in UI performance diagnostics.','Live account requests are disabled in UI performance diagnostics.']:
  if guard.encode('utf-16le') not in payload:parser.error(f'{path} lacks mandatory diagnostic account guards; refuse to launch')
 library=next((path.parent/name for name in ['libProjectPrime.RmlUi.Native.dylib','ProjectPrime.RmlUi.Native.dll','libProjectPrime.RmlUi.Native.so'] if (path.parent/name).is_file()),None)
 assets=path.parent/'rmlui'
 if library is None or not assets.is_dir():parser.error(f'{path} lacks its packaged native bridge or authored asset root')
 checkpoints[path]={'assemblySha256':hashlib.sha256(payload).hexdigest(),'nativeBridgeSha256':hashlib.sha256(library.read_bytes()).hexdigest(),'assetSha256':asset_digest(assets)}
if not (args.data/'paths.txt').is_file():parser.error('Isolated data fixture must contain paths.txt for real extracted game data.')
preference_files=['paths.txt','launcher.txt','controls.txt','Savedata/settings.json']
if any(not (args.data/name).is_file() for name in preference_files):parser.error('Supply all four detached preference/path files so every process starts with identical settings.')
for path in args.data.rglob('*'):
 if path.is_file() and ('session' in path.name.lower() or 'ticket' in path.name.lower()):parser.error('Benchmark fixture must not contain authentication session or ticket files.')
reports=[]
# Alternate paths to limit temperature/cache drift. Every run is a fresh process.
for run in range(1,args.runs+1):
 for mode,binary,selection in [('Avalonia',args.legacy,'legacy'),('RmlUi',args.native,'rmlui')]:
  name=f'{mode.lower()}-{args.backend}-{run}'
  if hashlib.sha256(binary.read_bytes()).hexdigest()!=checkpoints[binary]['assemblySha256']:raise RuntimeError(f'{binary} changed after preflight; refuse mixed checkpoints')
  report=args.out/(name+'.json');log=args.out/(name+'.log')
  report.unlink(missing_ok=True)
  fixture=args.out/(name+'-data')
  if fixture.exists():raise RuntimeError(f'{fixture} already exists; choose a new output directory to keep the starting fixture clean')
  for relative in preference_files:
   target=fixture/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(args.data/relative,target)
  env=dict(os.environ,PROJECT_PRIME_USER_DATA=str(fixture.resolve()),PROJECT_PRIME_UI_PERF=str(report.resolve()),PROJECT_PRIME_UI_PERF_SECONDS=str(args.seconds),PROJECT_PRIME_UI_PERF_EXIT='1',PROJECT_PRIME_UI_PERF_SCREENSHOT=str((args.out/(name+'.png')).resolve()))
  command=[args.dotnet,str(binary.resolve()),'-launcher','-ui='+selection,'-renderer',args.backend,'-windowed']
  if mode=='Avalonia' and run==1:command.append('-rmlui')
  with log.open('w') as output:
   try:
    completed=subprocess.run(command,env=env,stdout=output,stderr=subprocess.STDOUT,timeout=args.seconds+60)
   except subprocess.TimeoutExpired:
    failure={'format':2,'complete':False,'acceptance':False,'failedSample':name,'reason':'Process timed out before a completed verified Home sample.','checkpoint':checkpoints[binary],'log':log.name,'completedSamples':[r['report'] for r in reports]}
    (args.out/'FAILED.json').write_text(json.dumps(failure,indent=2)+'\n')
    raise RuntimeError(f'{name} timed out; comparison incomplete and ineligible for acceptance; see {log}') from None
  if completed.returncode!=0 or not report.is_file():raise RuntimeError(f'{name} failed to produce successful presentation evidence; see {log}')
  value=json.loads(report.read_text())
  if value.get('format',0)<2 or value.get('workload')!='Home' or value.get('homePresentationVerified') is not True:raise RuntimeError(f'{name} did not verify its actual Home presentation; reject startup/title workload')
  if value['mode']!=mode:raise RuntimeError(f'{name} selected the wrong UI')
  if value.get('frameRateCap')!=60:raise RuntimeError(f'{name} requires a controlled 60 fps fixture')
  value['run']=run;value.update(checkpoints[binary]);value['report']=report.name;value['fixture']=fixture.name
  reports.append(value)
  print(f"{name}: first verified Home present {value['firstPresentationFromProcessStartMs']:.1f}ms; CPU {value['processCpuPercentOfOneCore']:.1f}%; p95UI {value['totalUiFrame']['p95Ms']:.3f}ms",flush=True)
legacy=[r for r in reports if r['mode']=='Avalonia'];native=[r for r in reports if r['mode']=='RmlUi']
if len({(r['width'],r['height'],r.get('frameRateCap'),r['backend']) for r in reports})!=1:raise RuntimeError('Window size, frame cap or backend differed between samples')
metrics={'coldFirstPresentationMs':lambda r:r['firstPresentationFromProcessStartMs'],'idleCpuPercentOfOneCore':lambda r:r['processCpuPercentOfOneCore'],'p95UiFrameMs':lambda r:r['totalUiFrame']['p95Ms'],'managedAllocatedBytes':lambda r:r['managedAllocatedBytes']}
comparison={}
for metric,read in metrics.items():
 old=[read(r) for r in legacy];new=[read(r) for r in native]
 comparison[metric]={'legacyMedian':statistics.median(old),'legacyRange':[min(old),max(old)],'nativeMedian':statistics.median(new),'nativeRange':[min(new),max(new)],'nativeMedianWithinMeasuredBaselineWorst':statistics.median(new)<=max(old)}
summary={'format':2,'workload':'Home','backend':args.backend,'runsPerMode':args.runs,'comparison':comparison,'samples':reports,'method':'Same hardware, extracted data, window, 60 fps fixture and backend; every alternating fresh process starts from a new clone of the same four detached preference/path files. Five second warmup after verified Home presentation, then requested idle sample. Cold boundary is process start to first verified Home frame, including the normal legacy startup reveal. IME/accessibility enabled. Explicit diagnostics disable account/presence start, authentication/HTTP, automatic updater requests, background startup maintenance and preview generation. CPU timing is UI submission cost, not GPU completion time.','criterion':'Observed baseline worst across these runs is the measured upper comparison bound; this small sample is evidence, not a cross-platform release acceptance decision.'}
(args.out/'comparison.json').write_text(json.dumps(summary,indent=2)+'\n')
print(f"Wrote {args.out/'comparison.json'}",flush=True)
