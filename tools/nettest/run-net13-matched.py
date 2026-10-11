#!/usr/bin/env python3
"""Serial matched synthetic ServerSim benchmark. Preparation is the default; no builds.

Each invocation uses a fresh output directory; --execute prepares and runs it.
The operator must keep other tests/campaigns/builds idle through --execute. This
is controlled Mac synthetic simulation evidence, not a production hosting gate.
"""
import argparse, hashlib, json, math, os, platform, re, shutil, subprocess, sys, time
from pathlib import Path

MATRIX = [(players, pair, order) for players in (2, 4, 8)
          for pair, order in enumerate((('baseline', 'candidate'), ('candidate', 'baseline'), ('baseline', 'candidate')), 1)]
WARMUP, STEPS, SECONDS = 300, 3600, 60
# Derived catalog keys include staged binary paths; preserve as output, not seeded settings.
PROFILE_EXCLUDED = {'map-catalog-v2.json', 'logs', 'telemetry', '_demos', 'native-render-result.json', 'thumbnails.log'}
WORKLOAD_FIELDS = ('shotsFired', 'damageEvents', 'damagePoints', 'claimsReceived', 'claimsApplied', 'claimsDuplicate', 'claimsRefused')

def sha(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''): value.update(block)
    return value.hexdigest()

def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')

def files(directory):
    return {str(p.relative_to(directory)): sha(p) for p in sorted(Path(directory).rglob('*')) if p.is_file()}

def private_copy(source, dest):
    # APFS clone shares unchanged blocks, but remains a separate mutable inode.
    if platform.system() == 'Darwin':
        subprocess.run(['/bin/cp', '-cp', str(source), str(dest)], check=True)
        strategy = 'APFS clone /bin/cp -cp'
    else:
        shutil.copy2(source, dest)
        strategy = 'shutil.copy2'
    source_stat, dest_stat = source.stat(), dest.stat()
    digest = sha(source)
    if sha(dest) != digest or (source_stat.st_dev, source_stat.st_ino) == (dest_stat.st_dev, dest_stat.st_ino):
        raise ValueError('private managed copy integrity/inode failure: ' + str(dest))
    return {'source': str(source), 'destination': str(dest), 'sha256': digest,
            'distinctInodes': True, 'strategy': strategy}

def copy_bin(engine, runner, target):
    target.mkdir()
    copies = []
    for source in sorted(engine.rglob('*')):
        rel = source.relative_to(engine)
        if rel.parts[0].startswith('nettest'): continue
        dest = target / rel
        if source.is_dir(): dest.mkdir(exist_ok=True); continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        if source.suffix in ('.dll', '.pdb', '.json'): copies.append(private_copy(source, dest))
        else: os.link(source, dest)
    for name in ('nettest.dll', 'nettest.pdb', 'nettest.deps.json', 'nettest.runtimeconfig.json'):
        source = runner / name
        if not source.is_file(): raise ValueError('missing shared runner ' + str(source))
        copies.append(private_copy(source, target / name))
    return copies

def seed_profile(source, dest):
    dest.mkdir()
    for entry in sorted(source.iterdir()):
        if entry.name in PROFILE_EXCLUDED or entry.name.startswith('netlog-'): continue
        if entry.is_dir(): shutil.copytree(entry, dest / entry.name)
        elif entry.is_file(): shutil.copy2(entry, dest / entry.name)
    if not (dest / 'paths.txt').is_file(): raise ValueError('profile lacks paths.txt')

def active_processes():
    text = subprocess.check_output(['ps', '-axo', 'pid=,command='], text=True)
    found = []
    for line in text.splitlines():
        fields = line.strip().split(None, 1)
        if len(fields) != 2 or int(fields[0]) == os.getpid(): continue
        command = fields[1]
        if re.search(r'(?:^|/)dotnet\s+(?:build\b|[^\n]*\b(?:nettest|native-render-check|ProjectPrime)\.dll\b)', command):
            found.append({'pid': int(fields[0]), 'command': command})
    return found

def environment(commit, profile):
    env = {key: value for key, value in os.environ.items()
           if not key.startswith(('PRIME_', 'MPHREAD_', 'PROJECT_PRIME_'))}
    explicit = {'PRIME_BENCHMARK_COMMIT': commit, 'PROJECT_PRIME_USER_DATA': str(profile), 'ALSOFT_DRIVERS': 'null'}
    env.update(explicit)
    return env, explicit

def validate(result, players, commit):
    errors = []
    def check(ok, field):
        if not ok: errors.append(field)
    check(result.get('commit') == commit, 'commit')
    scenario = result.get('scenario', {})
    for field, expected in [('players', players), ('room', 'MP1 SANCTORUS'), ('steps', STEPS), ('warmupSteps', WARMUP)]:
        check(scenario.get(field) == expected, 'scenario.' + field)
    check(result.get('failures') == 0, 'failures')
    samples = result.get('stepMilliseconds', [])
    check(result.get('sampleCount') == STEPS and len(samples) == STEPS, 'sampleCount/stepMilliseconds')
    check(all(isinstance(value, (int, float)) and math.isfinite(value) and value >= 0 for value in samples), 'finite samples')
    if len(samples) == STEPS and not errors:
        ordered = sorted(samples)
        for field, fraction in [('p95', .95), ('p99', .99), ('p999', .999)]:
            expected = ordered[min(STEPS - 1, int(STEPS * fraction))]
            actual = result.get(field)
            check(isinstance(actual, (int, float)) and math.isfinite(actual) and abs(actual - expected) <= 1e-9, field + '.rawParity')
    for field in ('p95', 'p99', 'p999', 'gcPauseMilliseconds', 'allocations', 'allocationsPerStep', 'gen0', 'gen1', 'gen2'):
        value = result.get(field)
        check(isinstance(value, (int, float)) and math.isfinite(value) and value >= 0, field)
    check(result.get('p99', 0) > 0, 'p99.nonzero')
    rng = result.get('rng', {})
    for stage in ('loaded', 'initial', 'afterWarmup', 'end'):
        check(set(rng.get(stage, {})) == {'rng1', 'rng2'} and all(isinstance(v, int) and 0 <= v <= 0xffffffff for v in rng.get(stage, {}).values()), 'rng.' + stage)
    workload = result.get('workload', {})
    for field in WORKLOAD_FIELDS:
        check(isinstance(workload.get(field), int) and workload[field] >= 0, 'workload.' + field)
    check(isinstance(workload.get('shotsFired'), int) and workload['shotsFired'] > 0, 'workload.shotsFired.nonzero')
    population = result.get('playerWorkload', {})
    check(population.get('requested') == players, 'playerWorkload.requested')
    check(population.get('afterWarmupActive') == players and population.get('afterWarmupAlive', 0) > 0
          and population.get('endActive') == players, 'playerWorkload.active')
    for stage in ('beforeWarmup', 'afterWarmup', 'end'):
        rows = population.get(stage, [])
        check(len(rows) == players and [r.get('slot') for r in rows] == list(range(players)), 'playerWorkload.' + stage + '.slots')
        check(all(r.get('hunter') == slot % 7 for slot, r in enumerate(rows)), 'playerWorkload.' + stage + '.hunters')
    return errors

def pair_comparison(b, c):
    mismatches = []
    for field in ('scenario', 'runtime', 'environment', 'protocol', 'workload', 'playerWorkload'):
        if b.get(field) != c.get(field): mismatches.append(field)
    for stage in ('loaded', 'initial', 'afterWarmup', 'end'):
        if b.get('rng', {}).get(stage) != c.get('rng', {}).get(stage): mismatches.append('rng.' + stage)
    change = (c['p99'] / b['p99'] - 1) * 100 if not mismatches and b['p99'] > 0 else None
    return {'workloadEquality': not mismatches, 'mismatchingFields': mismatches,
            'baseline': {key: b.get(key) for key in ('p95', 'p99', 'p999', 'gcPauseMilliseconds', 'allocations', 'allocationsPerStep', 'gen0', 'gen1', 'gen2')},
            'candidate': {key: c.get(key) for key in ('p95', 'p99', 'p999', 'gcPauseMilliseconds', 'allocations', 'allocationsPerStep', 'gen0', 'gen1', 'gen2')},
            'pairedP99PercentChange': change, 'pairedP99Above5Percent': c['p99'] > b['p99'] * 1.05 if change is not None else None}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-bin', type=Path, required=True)
    parser.add_argument('--candidate-bin', type=Path, required=True)
    parser.add_argument('--runner-bin', type=Path, required=True)
    parser.add_argument('--baseline-source', required=True)
    parser.add_argument('--candidate-source', required=True)
    parser.add_argument('--runner-source', required=True)
    parser.add_argument('--baseline-sha', required=True)
    parser.add_argument('--candidate-sha', required=True)
    parser.add_argument('--runner-sha', required=True)
    parser.add_argument('--profile', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, default=Path('/Users/jarrett/.cache/project-prime/dotnet-10.0.401/dotnet'))
    parser.add_argument('--execute', action='store_true', help='Run prepared matrix serially; keep all other tests/builds/campaigns idle')
    parser.add_argument('--timeout-seconds', type=int, default=180)
    args = parser.parse_args()
    for name in ('baseline_bin', 'candidate_bin', 'runner_bin', 'profile', 'out', 'dotnet'):
        setattr(args, name, getattr(args, name).resolve())
    for field in ('baseline_source', 'candidate_source', 'runner_source'):
        if not re.fullmatch('[0-9a-f]{40}', getattr(args, field)): raise ValueError(field + ' must be exact 40-character source SHA')
    for path, expected in [(args.baseline_bin / 'ProjectPrime.dll', args.baseline_sha),
                           (args.candidate_bin / 'ProjectPrime.dll', args.candidate_sha),
                           (args.runner_bin / 'nettest.dll', args.runner_sha)]:
        if sha(path) != expected: raise ValueError('input SHA mismatch: ' + str(path))
    args.out.mkdir(parents=True, exist_ok=False)
    builds = {}
    private_copies = {}
    for label in ('baseline', 'candidate'):
        target = args.out / (label + '-bin'); private_copies[label] = copy_bin(getattr(args, label + '_bin'), args.runner_bin, target)
        builds[label] = target
    seed = args.out / 'profile-seed'; seed_profile(args.profile, seed); seed_hashes = files(seed)
    sdk = subprocess.check_output([str(args.dotnet), '--version'], text=True).strip()
    if sdk != '10.0.401': raise ValueError('SDK differs from pinned 10.0.401: ' + sdk)
    manifest = {'schema': 1, 'privateManagedCopies': private_copies,
                'copyStrategy': 'Darwin APFS /bin/cp -cp private verified distinct inodes; other platforms copy2; native hardlinks unchanged', 'driverSha256': sha(__file__), 'settings': {'players': [2, 4, 8], 'pairsPerPlayers': 3,
                'orders': ['B/C', 'C/B', 'B/C'], 'warmupSteps': WARMUP, 'measuredSteps': STEPS, 'simulationSeconds': SECONDS,
                'room': 'MP1 SANCTORUS', 'telemetry': 'explicit -notelemetry both; legacy ProductionTelemetry disabled; PerformanceEvidence env absent'},
                'source': {'baseline': args.baseline_source, 'candidate': args.candidate_source, 'runner': args.runner_source},
                'sdk': sdk, 'runtimes': subprocess.check_output([str(args.dotnet), '--list-runtimes'], text=True),
                'host': {'platform': platform.platform(), 'architecture': platform.machine(), 'machine': platform.node(), 'processors': os.cpu_count()},
                'inputs': {'baselineEngineSha256': args.baseline_sha, 'candidateEngineSha256': args.candidate_sha, 'runnerSha256': args.runner_sha},
                'binBefore': {label: files(path) for label, path in builds.items()}, 'seedProfileHashes': seed_hashes,
                'commands': [], 'status': 'PREPARED', 'scope': 'Controlled synthetic asset-backed ServerSim; no transport/hosting-hardware claim; no pooled confidence estimates'}
    for players, pair, order in MATRIX:
        for label in order:
            run = args.out / f'{players}players-pair{pair}-{label}'; run.mkdir(); profile = run / 'profile'; seed_profile(seed, profile)
            output = run / 'result.json'; commit = getattr(args, label + '_source')
            _, explicit = environment(commit, profile)
            cmd = [str(args.dotnet), str(builds[label] / 'nettest.dll'), '--server-performance', str(profile),
                   'MP1 SANCTORUS', str(players), str(output), str(SECONDS), '-notelemetry']
            manifest['commands'].append({'label': label, 'players': players, 'pair': pair, 'order': list(order), 'command': cmd,
                   'cwd': str(builds[label]), 'environment': explicit, 'profileBefore': files(profile), 'result': str(output),
                   'log': str(run / 'stdout.log'), 'runDirectory': str(run)})
    write(args.out / 'manifest.json', manifest)
    if not args.execute:
        print('PREPARED ONLY: no benchmark process executed. ' + str(args.out / 'manifest.json')); return 0
    results = {}
    def binary_hashes(label):
        return {name: sha(builds[label] / name) for name in ('ProjectPrime.dll', 'nettest.dll')}
    def finish_invalid(status):
        manifest['status'] = status
        manifest['binAfter'] = {label: files(path) for label, path in builds.items()}
        write(args.out / 'manifest.json', manifest)
        return 1
    for row in manifest['commands']:
        busy = active_processes()
        if busy:
            manifest['concurrentProcesses'] = busy
            return finish_invalid('BLOCKED_CONCURRENT_TEST')
        row['binaryBeforeHashes'] = binary_hashes(row['label'])
        row['started'] = time.time(); env, _ = environment(getattr(args, row['label'] + '_source'), Path(row['environment']['PROJECT_PRIME_USER_DATA']))
        with Path(row['log']).open('w') as log:
            try: row['exit'] = subprocess.call(row['command'], cwd=row['cwd'], env=env, stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout_seconds)
            except subprocess.TimeoutExpired: row['exit'] = 'TIMEOUT'
        row['finished'] = time.time(); row['logSha256'] = sha(row['log']); row['profileAfter'] = files(Path(row['environment']['PROJECT_PRIME_USER_DATA']))
        row['seededProfileChanges'] = [key for key, value in row['profileBefore'].items() if row['profileAfter'].get(key) != value]
        row['binaryAfterHashes'] = binary_hashes(row['label'])
        row['validationErrors'] = []
        expected = {'ProjectPrime.dll': getattr(args, row['label'] + '_sha'), 'nettest.dll': args.runner_sha}
        if row['binaryBeforeHashes'] != expected or row['binaryAfterHashes'] != expected:
            row['validationErrors'].append('binaryHashDrift')
        if row['exit'] != 0: row['validationErrors'].append('processExit')
        if row['seededProfileChanges']: row['validationErrors'].append('seededProfileDrift')
        if Path(row['result']).is_file():
            row['resultSha256'] = sha(row['result'])
            try:
                value = json.loads(Path(row['result']).read_text())
                row['validationErrors'] += validate(value, row['players'], getattr(args, row['label'] + '_source'))
                results[(row['players'], row['pair'], row['label'])] = value
            except (ValueError, TypeError, KeyError, AttributeError):
                row['validationErrors'].append('malformedResult')
        else: row['validationErrors'].append('missingResult')
        write(args.out / 'manifest.json', manifest)
        print(json.dumps({'players': row['players'], 'pair': row['pair'], 'label': row['label'], 'exit': row['exit'], 'errors': row['validationErrors']}), flush=True)
        if row['validationErrors']:
            return finish_invalid('INVALID_FIRST_RESULT')
        bkey = (row['players'], row['pair'], 'baseline'); ckey = (row['players'], row['pair'], 'candidate')
        if bkey in results and ckey in results:
            comparison = pair_comparison(results[bkey], results[ckey])
            row['completedPairComparison'] = comparison
            write(args.out / 'manifest.json', manifest)
            print(json.dumps({'players': row['players'], 'pair': row['pair'], **comparison}), flush=True)
            if not comparison['workloadEquality']:
                return finish_invalid('INVALID_PAIRED_WORKLOAD')
    pairs = []
    for players, pair, order in MATRIX:
        comparison = pair_comparison(results[(players, pair, 'baseline')], results[(players, pair, 'candidate')])
        pairs.append({'players': players, 'pair': pair, 'order': list(order), **comparison})
    manifest['binAfter'] = {label: files(path) for label, path in builds.items()}
    unchanged = manifest['binAfter'] == manifest['binBefore']
    summary = {'pairs': pairs, 'binHashesUnchanged': unchanged, 'noPooledConfidenceEstimate': True, 'scope': manifest['scope']}
    if not unchanged or any(not pair['workloadEquality'] for pair in pairs): summary['status'] = 'INVALID'
    elif any(pair['pairedP99Above5Percent'] for pair in pairs): summary['status'] = 'PAIRED_P99_REGRESSION_FLAG'
    else: summary['status'] = 'NO_PAIRED_P99_REGRESSION_OVER_5_PERCENT_OBSERVED'
    manifest['status'] = summary['status']; write(args.out / 'manifest.json', manifest); write(args.out / 'comparison.json', summary)
    print(json.dumps(summary, indent=2)); return 1 if summary['status'] in ('INVALID', 'PAIRED_P99_REGRESSION_FLAG') else 0

if __name__ == '__main__':
    sys.exit(main())
