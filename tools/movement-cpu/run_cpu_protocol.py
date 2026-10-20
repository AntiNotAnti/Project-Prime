"""Fixed Windows ETW CPU protocol candidate. Plan by default; explicit lease to run.
No global WPR stop/cancel, no background process killing, no automatic reruns.
"""
import argparse
import base64
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import time
import uuid

ROOMS = ('MP3 PROVING GROUND', 'MP1 SANCTORUS')
MODELS = ('Native', 'Momentum', 'Momentum', 'Native')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def schedule():
    result = []
    for room_index, room in enumerate(ROOMS):
        for cycle in range(2):
            for index, model in enumerate(MODELS):
                treatments = ('control', 'trace') if (cycle + index) % 2 == 0 else ('trace', 'control')
                for treatment in treatments:
                    result.append(dict(room=room, mapIndex=room_index, cycle=cycle,
                                       position=index, model=model, treatment=treatment))
    return result


def call(command, log, cwd=None, env=None, timeout=600):
    with log.open('w', encoding='utf-8') as stream:
        result = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT,
                                cwd=cwd, env=env, stdin=subprocess.DEVNULL, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f'command failed ({result.returncode}); retained {log}')


def benchmark(args, directory, room, model, phase):
    config = directory / 'user-data'
    config.mkdir()
    shutil.copy2(args.assets / 'paths.txt', config / 'paths.txt')
    env = os.environ.copy()
    for key in tuple(env):
        if key.startswith(('PRIME_', 'PROJECT_PRIME_', 'DOTNET_', 'COMPlus_')):
            env.pop(key)
    env.update(PROJECT_PRIME_USER_DATA=str(config), DOTNET_TieredCompilation='0',
               DOTNET_TieredPGO='0', DOTNET_ReadyToRun='0',
               PRIME_BENCHMARK_THREAD_CYCLES='1', PRIME_BENCHMARK_STEP_SPANS='1',
               PRIME_BENCHMARK_COMMIT=args.source, PRIME_SHOT_PROOF='shadow',
               PRIME_ACCEPTED_SHOTS='1')
    output = directory / 'benchmark.json'
    command = [str(args.dotnet), str(args.dll), '--momentum-server-performance',
               str(config), room, model, str(output), '60', phase]
    call(command, directory / 'benchmark.log', cwd=config, env=env)
    row = json.loads(output.read_text(encoding='utf-8-sig'))
    if row.get('failure') or row.get('model') != model or row.get('acceptedPerformanceEvidence') != (phase == 'timing'):
        raise RuntimeError('invalid benchmark evidence')
    if row['scenario']['steps'] != 3600 or row['scenario']['warmupSteps'] != 1200:
        raise RuntimeError('fixed sample/warmup protocol changed')
    if row['threadCycles']['startProcessor'] != row['threadCycles']['endProcessor']:
        raise RuntimeError('unpinned measurement')
    return row


def trace_arm(args, directory, arm):
    instance = 'PrimeCpu_' + uuid.uuid4().hex
    receipt = directory / 'trace-ownership.json'
    # Successful start is required before issuing any stop. No cancel fallback.
    call(['wpr', '-start', str(args.profile) + '!PrimeStepCpu', '-filemode',
          '-recordtempto', str(directory), '-instancename', instance], directory / 'start.log')
    receipt.write_text(json.dumps(dict(instance=instance, startedUnix=time.time(), owned=True)))
    try:
        row = benchmark(args, directory, arm['room'], arm['model'], 'timing')
    finally:
        call(['wpr', '-stop', str(directory / 'trace.etl'), '-skipPdbGen',
              '-instancename', instance], directory / 'stop.log')
    return row


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)]


def summarize(output, records):
    expected = schedule()
    complete = len(records) == len(expected) and all(
        all(record.get(key) == arm[key] for key in arm)
        and (output / record['directory'] / 'benchmark.json').is_file()
        and (record['treatment'] != 'trace' or (output / record['directory'] / 'cpu.json').is_file())
        for record, arm in zip(records, expected))
    if not complete:
        (output / 'summary.json').write_text(json.dumps(dict(
            status='measurement-incomplete/inconclusive', acceptanceClaim=False,
            reason='all32fixed arms and all16independent CPU audits are required'), indent=2))
        return
    maps = []
    for room in ROOMS:
        groups = {}
        for model in ('Native', 'Momentum'):
            for treatment in ('control', 'trace'):
                selected = [r for r in records if r['room'] == room and r['model'] == model and r['treatment'] == treatment]
                rows = [json.loads((output / r['directory'] / 'benchmark.json').read_text()) for r in selected]
                work = [json.dumps(r['workload'], sort_keys=True, separators=(',', ':')) for r in rows]
                groups[(model, treatment)] = dict(
                    wallP95=statistics.median(r['timing']['p95'] for r in rows),
                    loopCpuMs=statistics.median(r['timing']['processCpuMilliseconds'] for r in rows),
                    workloadHashes=[hashlib.sha256(w.encode()).hexdigest() for w in work])
                if treatment == 'trace':
                    cpu = [json.loads((output / r['directory'] / 'cpu.json').read_text()) for r in selected]
                    group = groups[(model, treatment)]
                    for label, field in [('cpuP95', 'cpuMs'), ('cpuP95Lower', 'cpuLowerMs'), ('cpuP95Upper', 'cpuUpperMs')]:
                        group[label] = statistics.median(percentile([s[field] for s in c['rows']], .95) for c in cpu)
                    group['cpuP99'] = statistics.median(c['p99CpuMs'] for c in cpu)
                    group['cpuWorst'] = max(max(s['cpuMs'] for s in c['rows']) for c in cpu)
        n, m = groups['Native', 'trace'], groups['Momentum', 'trace']
        control_ratio = groups['Momentum', 'control']['wallP95'] / groups['Native', 'control']['wallP95']
        trace_ratio = m['wallP95'] / n['wallP95']
        checks = dict(relativeWallDistortionWithin1Percent=abs(trace_ratio / control_ratio - 1) <= .01)
        for model in ('Native', 'Momentum'):
            c, t = groups[model, 'control'], groups[model, 'trace']
            checks[model + 'WallPerturbationWithin5Percent'] = abs(t['wallP95'] / c['wallP95'] - 1) <= .05
            checks[model + 'LoopCpuPerturbationWithin5Percent'] = abs(t['loopCpuMs'] / c['loopCpuMs'] - 1) <= .05
            checks[model + 'WorkloadsIdentical'] = len(set(c['workloadHashes'] + t['workloadHashes'])) == 1
        upper = m['cpuP95Upper'] / n['cpuP95Lower'] - 1
        maps.append(dict(room=room, groups={f'{k[0]}-{k[1]}': v for k, v in groups.items()},
                         controlChecks=checks, schedulerCpuP95Increase=m['cpuP95'] / n['cpuP95'] - 1,
                         numericalUpperIncrease=upper,
                         status='measurement-invalid/inconclusive' if not all(checks.values()) else
                         ('within5PercentCPU-provisional' if upper <= .05 else 'FAIL-stop-and-investigate'),
                         wallControlIncrease=control_ratio - 1))
    (output / 'summary.json').write_text(json.dumps(dict(maps=maps, acceptanceClaim=False,
        note='CPU accounting protocol result only; workload divergence and all prior failures retained'), indent=2))


def require_idle_wpr(status_text):
    if 'WPR is not recording' not in status_text:
        raise RuntimeError('existing/unknown WPR recording state; no start/stop/cancel attempted')


def binary_hashes(args):
    paths = {'tool': args.dll, 'core': args.dll.with_name('ProjectPrime.dll'),
             'runtimeConfig': args.dll.with_suffix('.runtimeconfig.json')}
    return {name: digest(path) for name, path in paths.items()}


def ambient(directory, label):
    # Numeric-only metadata; no user/process names or command lines are exported.
    script = """
$ErrorActionPreference='Stop'
$cpu=(Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor | Where-Object Name -eq '_Total').PercentProcessorTime
$processes=@(Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | Where-Object {$_.IDProcess -gt 0} | Sort-Object PercentProcessorTime -Descending | Select-Object -First 20 | ForEach-Object {@{pid=[int]$_.IDProcess;cpuPercent=[int]$_.PercentProcessorTime}})
@{utc=[DateTime]::UtcNow.ToString('o');totalCpuPercent=[int]$cpu;topProcesses=$processes} | ConvertTo-Json -Depth 4 -Compress
"""
    encoded = base64.b64encode(script.encode('utf-16le')).decode()
    result = subprocess.run(['powershell.exe', '-NoProfile', '-NonInteractive', '-EncodedCommand', encoded],
                            capture_output=True, text=True, check=True, timeout=30)
    value = json.loads(result.stdout.lstrip('\ufeff'))
    (directory / (label + '.json')).write_text(json.dumps(value, indent=2))
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('dotnet', 'dll', 'assets', 'xperf', 'profile', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source', required=True, help='genuine base SHA plus dirty overlay manifest identity')
    parser.add_argument('--execute-after-lease-release', action='store_true')
    args = parser.parse_args()
    for name in ('dotnet', 'dll', 'assets', 'xperf', 'profile', 'output'):
        setattr(args, name, getattr(args, name).resolve())
    args.output.mkdir(exist_ok=False, parents=True)
    plan = dict(schema=1, source=args.source, binaryHashes=binary_hashes(args), schedule=schedule(),
                controls=dict(maxAbsoluteWallOrLoopCpuPerturbation=.05, maxRelativeWallRatioDistortion=.01),
                threshold=.05, steps=3600, warmupSteps=1200, repeats='2 fixed ABBA cycles/map', automaticReruns=False,
                analysisPolicy='deferred until all32timing arms finish; all16audits required',
                captureDiskGuardBytes=8 * 1024**3, unusedReadyThread=False)
    (args.output / 'frozen-plan.json').write_text(json.dumps(plan, indent=2))
    if not args.execute_after_lease_release:
        print('Plan frozen only; no Windows commands executed.')
        return
    if os.name != 'nt':
        raise RuntimeError('execution requires Windows and explicit offline lease release')
    if shutil.disk_usage(args.output).free < 8 * 1024**3:
        raise RuntimeError('capture requires8GiB free; no arm started')
    call(['wpr', '-status'], args.output / 'initial-wpr-status.txt')
    require_idle_wpr((args.output / 'initial-wpr-status.txt').read_text(encoding='utf-8-sig'))
    call(['logman', 'query', '-ets'], args.output / 'initial-etw-sessions.txt')
    # An operator must review existing sessions before launch; no shared cancellation.
    records = []
    try:
        for map_index, room in enumerate(ROOMS):
            for model in ('Native', 'Momentum'):
                directory = args.output / f'verify-{map_index}-{model}'
                directory.mkdir()
                benchmark(args, directory, room, model, 'verify')
        for index, arm in enumerate(plan['schedule']):
            if index % 16 == 0:
                load = ambient(args.output, f'map-{arm["mapIndex"]}-before')
                if load['totalCpuPercent'] > 5:
                    raise RuntimeError('declared ambient precondition >5%; unopened remaining schedule deferred, all completed arms retained')
            directory = args.output / f'arm-{index:02d}-{arm["treatment"]}-{arm["model"]}'
            directory.mkdir()
            if arm['treatment'] == 'trace':
                if shutil.disk_usage(args.output).free < 8 * 1024**3:
                    raise RuntimeError('capture diskguard8GiB; unopened arms deferred')
                trace_arm(args, directory, arm)
            else:
                benchmark(args, directory, arm['room'], arm['model'], 'timing')
            records.append(dict(arm, directory=directory.name))
            (args.output / 'completed-arms.json').write_text(json.dumps(records, indent=2))
            if (index + 1) % 16 == 0:
                ambient(args.output, f'map-{arm["mapIndex"]}-after')
        if binary_hashes(args) != plan['binaryHashes']:
            raise RuntimeError('Core/tool/runtimeconfig changed during experiment')
        (args.output / 'post-binary-hashes.json').write_text(json.dumps(binary_hashes(args), indent=2))
        (args.output / 'capture-complete.json').write_text(json.dumps(dict(
            status='capture-complete-awaiting-all16offline-audits', acceptanceClaim=False,
            arms=len(records), tracedArms=16, lateAuditFailureInvalidatesEntireBatch=True), indent=2))
    except Exception as error:
        (args.output / 'failure.txt').write_text(str(error))
        raise
    finally:
        (args.output / 'post-binary-hashes.json').write_text(json.dumps(binary_hashes(args), indent=2))


if __name__ == '__main__':
    main()
