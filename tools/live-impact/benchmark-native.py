#!/usr/bin/env python3
"""Alternating same-binary UDP controls; inspect workload counts before comparing.

This measures whole-match server frames, including startup, with diagnostics on
in both arms. Random match spawns mean equal configuration is not identical work.
Run exclusively: concurrent builds or other campaigns invalidate timing claims.
"""
import argparse
import json
import os
from pathlib import Path
import statistics
import subprocess

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--runtime', required=True)
p.add_argument('--data', required=True)
p.add_argument('--mapdir', required=True)
p.add_argument('--map', default='TEST ARENA')
p.add_argument('--output', type=Path, required=True)
p.add_argument('--repeats', type=int, default=3)
p.add_argument('--seconds', type=int, default=30)
p.add_argument('--dotnet', default=str(Path.home()/'.dotnet/dotnet'))
a = p.parse_args()
if a.repeats < 1 or a.seconds < 10: p.error('positive repeats and at least ten seconds required')
if a.output.exists() and any(a.output.iterdir()): p.error('output must be new or empty')
a.output.mkdir(parents=True, exist_ok=True)
repo = Path(__file__).resolve().parents[2]
env = dict(os.environ, DOTNET_TieredCompilation='0')
rows = []
for repeat in range(a.repeats):
    for live in ((False, True) if repeat % 2 == 0 else (True, False)):
        name = f'{repeat}-'+('on' if live else 'off')
        output = a.output/name
        command = ['python3', str(repo/'tools/hitrig/run-networking-slices.py'),
            '--runtime', a.runtime, '--data', a.data, '--map', a.map, '--mapdir', a.mapdir,
            '--output', str(output), '--seconds', str(a.seconds), '--dotnet', a.dotnet,
            '--owner-grace', '8',
            '--players', '8', '--modes', 'shockcoil-all', '--profiles', 'rtt250-loss2',
            '--jitter-ms', '40', '--impacts', '--fixture-loadout', '--require-combat', '--claim-mode', 'shadow']
        if not live: command.append('--no-live')
        with (a.output/(name+'.log')).open('w') as log:
            result = subprocess.run(command, cwd=repo, env=env, stdout=log, stderr=subprocess.STDOUT)
        arm = output/'shockcoil-all-rtt250-loss2'
        row = dict(repeat=repeat, live=live, exit=result.returncode, command=command)
        metric = arm/'server-impacts.json.server.json'
        if metric.exists(): row['server'] = json.loads(metric.read_text())
        summary = output/'summary.json'
        if summary.exists(): row['workload'] = json.loads(summary.read_text())[0].get('authority_facts_by_weapon')
        rows.append(row)
        (a.output/'results.json').write_text(json.dumps(rows, indent=2)+'\n')
        print(name, result.returncode, row.get('server',{}).get('p99Ms'), flush=True)
comparison = dict(scope=__doc__, tieredCompilation=False, arms=rows)
comparison['medians'] = {str(live): {metric: statistics.median(row['server'][metric]
    for row in rows if row['live']==live and 'server' in row)
    for metric in ('p50Ms','p95Ms','p99Ms','p999Ms','allocatedBytesPerFrame')}
    for live in (False,True) if any(row['live']==live and 'server' in row for row in rows)}
(a.output/'comparison.json').write_text(json.dumps(comparison,indent=2)+'\n')
raise SystemExit(any(row['exit'] or 'server' not in row for row in rows))
