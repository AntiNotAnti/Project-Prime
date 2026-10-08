#!/usr/bin/env python3
"""Run matched, opt-in production UI diagnostics. Does not build or install anything."""
import argparse, hashlib, json, os, pathlib, statistics, subprocess, sys

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
for path in [args.legacy,args.native]:
 if not path.is_file():parser.error(f'Missing full-build assembly: {path}')
 payload=path.read_bytes()
 if b'homePresentationVerified' not in payload:parser.error(f'{path} lacks verified Home presentation diagnostics; refuse to benchmark a startup/title screen')
 for guard in ['Live account authentication is disabled in UI performance diagnostics.','Live account requests are disabled in UI performance diagnostics.']:
  if guard.encode('utf-16le') not in payload:parser.error(f'{path} lacks mandatory diagnostic account guards; refuse to launch')
if not (args.data/'paths.txt').is_file():parser.error('Isolated data fixture must contain paths.txt for real extracted game data.')
for path in args.data.rglob('*'):
 if path.is_file() and ('session' in path.name.lower() or 'ticket' in path.name.lower()):parser.error('Benchmark fixture must not contain authentication session or ticket files.')
reports=[]
# Alternate paths to limit temperature/cache drift. Every run is a fresh process.
for run in range(1,args.runs+1):
 for mode,binary,selection in [('Avalonia',args.legacy,'legacy'),('RmlUi',args.native,'rmlui')]:
  name=f'{mode.lower()}-{args.backend}-{run}'
  report=args.out/(name+'.json');log=args.out/(name+'.log')
  report.unlink(missing_ok=True)
  env=dict(os.environ,PROJECT_PRIME_USER_DATA=str(args.data.resolve()),PROJECT_PRIME_UI_PERF=str(report.resolve()),PROJECT_PRIME_UI_PERF_SECONDS=str(args.seconds),PROJECT_PRIME_UI_PERF_EXIT='1',PROJECT_PRIME_UI_PERF_SCREENSHOT=str((args.out/(name+'.png')).resolve()))
  command=[args.dotnet,str(binary.resolve()),'-launcher','-ui='+selection,'-renderer',args.backend,'-windowed']
  if mode=='Avalonia' and run==1:command.append('-rmlui')
  with log.open('w') as output:
   completed=subprocess.run(command,env=env,stdout=output,stderr=subprocess.STDOUT,timeout=args.seconds+60)
  if completed.returncode!=0 or not report.is_file():raise RuntimeError(f'{name} failed to produce successful presentation evidence; see {log}')
  value=json.loads(report.read_text())
  if value.get('format',0)<2 or value.get('workload')!='Home' or value.get('homePresentationVerified') is not True:raise RuntimeError(f'{name} did not verify its actual Home presentation; reject startup/title workload')
  if value['mode']!=mode:raise RuntimeError(f'{name} selected the wrong UI')
  if value.get('frameRateCap')!=60:raise RuntimeError(f'{name} requires a controlled 60 fps fixture')
  value['run']=run;value['assemblySha256']=hashlib.sha256(binary.read_bytes()).hexdigest();value['report']=report.name
  reports.append(value)
  print(f"{name}: first verified Home present {value['firstPresentationFromProcessStartMs']:.1f}ms; CPU {value['processCpuPercentOfOneCore']:.1f}%; p95UI {value['totalUiFrame']['p95Ms']:.3f}ms",flush=True)
legacy=[r for r in reports if r['mode']=='Avalonia'];native=[r for r in reports if r['mode']=='RmlUi']
if len({(r['width'],r['height'],r.get('frameRateCap'),r['backend']) for r in reports})!=1:raise RuntimeError('Window size, frame cap or backend differed between samples')
metrics={'coldFirstPresentationMs':lambda r:r['firstPresentationFromProcessStartMs'],'idleCpuPercentOfOneCore':lambda r:r['processCpuPercentOfOneCore'],'p95UiFrameMs':lambda r:r['totalUiFrame']['p95Ms'],'managedAllocatedBytes':lambda r:r['managedAllocatedBytes']}
comparison={}
for metric,read in metrics.items():
 old=[read(r) for r in legacy];new=[read(r) for r in native]
 comparison[metric]={'legacyMedian':statistics.median(old),'legacyRange':[min(old),max(old)],'nativeMedian':statistics.median(new),'nativeRange':[min(new),max(new)],'nativeMedianWithinMeasuredBaselineWorst':statistics.median(new)<=max(old)}
summary={'format':2,'workload':'Home','backend':args.backend,'runsPerMode':args.runs,'comparison':comparison,'samples':reports,'method':'Same hardware, extracted data, window, 60 fps fixture and backend; alternating fresh processes with 5 second warmup after verified Home presentation, then requested idle sample. Cold boundary is process start to first verified Home frame, including the normal legacy startup reveal. IME/accessibility enabled. Explicit diagnostics disable account/presence start, authentication/HTTP, automatic updater requests, background startup maintenance and preview generation. CPU timing is UI submission cost, not GPU completion time.','criterion':'Observed baseline worst across these runs is the measured upper comparison bound; this small sample is evidence, not a cross-platform release acceptance decision.'}
(args.out/'comparison.json').write_text(json.dumps(summary,indent=2)+'\n')
print(f"Wrote {args.out/'comparison.json'}",flush=True)
