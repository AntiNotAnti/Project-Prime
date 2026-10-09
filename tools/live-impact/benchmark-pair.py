#!/usr/bin/env python3
"""Alternate frozen before/after server workloads; retain every sample and command."""
import argparse, hashlib, json, os, platform, statistics, subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--before',type=Path,required=True);p.add_argument('--after',type=Path,required=True)
p.add_argument('--before-commit',required=True);p.add_argument('--after-commit',required=True)
p.add_argument('--assets',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
p.add_argument('--dotnet',default='dotnet');p.add_argument('--seconds',type=int,default=30);p.add_argument('--repeats',type=int,default=3)
p.add_argument('--after-arg',action='append',default=[],help='Additional after-arm argument, e.g. --after-arg=--impact-server-scratch')
a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
if any(out.iterdir()):p.error('output must be empty')
if a.repeats<1 or a.seconds<1:p.error('positive repeats and duration required')
binaries={'before':a.before.resolve(),'after':a.after.resolve()};rows=[]
for players in (2,4,8):
 for repeat in range(a.repeats):
  for label in (('before','after') if repeat%2==0 else ('after','before')):
   stem=f'{label}-{players}-{repeat+1}';path=out/(stem+'.json')
   command=[a.dotnet,str(binaries[label]),'--server-performance',str(a.assets.resolve()),'MP1 SANCTORUS',str(players),str(path),str(a.seconds)]
   if label=='after': command += a.after_arg
   env=dict(os.environ,PRIME_BENCHMARK_COMMIT=getattr(a,label+'_commit'))
   with (out/(stem+'.log')).open('w') as log:r=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT)
   rows.append(dict(label=label,players=players,repeat=repeat+1,command=command,exitCode=r.returncode,report=path.name,log=stem+'.log'))
   print(stem,r.returncode,flush=True)
comparison=[]
for players in (2,4,8):
 groups={label:[json.loads((out/r['report']).read_text()) for r in rows if r['label']==label and r['players']==players and r['exitCode']==0] for label in binaries}
 if any(len(v)!=a.repeats for v in groups.values()):continue
 med={label:{key:statistics.median(d[key] for d in group) for key in ('p99','allocationsPerStep','mean')} for label,group in groups.items()}
 comparison.append(dict(players=players,medianOfRuns=med,p99ChangePercent=100*(med['after']['p99']/med['before']['p99']-1),allocationChangeBytesPerStep=med['after']['allocationsPerStep']-med['before']['allocationsPerStep']))
manifest=dict(schema=1,host=platform.platform(),binaries={label:dict(path=str(path),assemblySha256=hashlib.sha256(path.with_name('ProjectPrime.dll').read_bytes()).hexdigest(),commit=getattr(a,label+'_commit')) for label,path in binaries.items()},runs=rows,comparison=comparison,
 afterArguments=a.after_arg, tieredCompilation=os.environ.get('DOTNET_TieredCompilation'),
 limitations=['Synthetic-intent server frames; no live socket peers or rendered effects. Does not establish enabled eight-player transport/presentation acceptance.',
 'Process-local microbenchmark with alternating runs; median of run p99s is not a pooled p99 or statistical confidence interval.',
 'Historical baseline runner may have no p99.9 field; absence is not zero. Feature arguments are recorded explicitly.'])
(out/'comparison.json').write_text(json.dumps(manifest,indent=2)+'\n')
raise SystemExit(any(r['exitCode']!=0 for r in rows))
