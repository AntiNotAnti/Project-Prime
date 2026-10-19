#!/usr/bin/env python3
"""Reproduce CW-00 checks, retaining each exit status and complete local log."""
import argparse
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, default=ROOT / 'artifacts/custom-weapons/CW-00')
a = p.parse_args()
a.output = a.output.resolve()
a.output.mkdir(parents=True, exist_ok=True)
user_data = a.output / 'user-data'
user_data.mkdir(exist_ok=True)
run_user_data = Path(tempfile.mkdtemp(prefix='run-', dir=user_data))
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1')
main_project = 'src/MphRead/MphRead.csproj'
nettest_project = 'tools/nettest/nettest.csproj'
# Explicit restores and --no-restore builds make both configurations reproducible.
# A failed prerequisite must never let an old binary produce a false PASS.
checks = [
    ('restore-debug', ['restore', main_project, '-p:Configuration=Debug'], []),
    ('build-debug', ['build', main_project, '-c', 'Debug', '--no-restore', '-p:UseSharedCompilation=false'], ['restore-debug']),
    ('restore-release', ['restore', main_project, '-p:Configuration=Release'], []),
    ('build-release', ['build', main_project, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false'], ['restore-release']),
    ('restore-nettest', ['restore', nettest_project, '-p:Configuration=Release'], []),
    ('build-nettest', ['build', nettest_project, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false'], ['restore-nettest', 'build-release']),
]
for name in ['architecture', 'lobby', 'weapon-policy', 'shot-source-journal', 'shot-inputs',
             'shot-lifecycle', 'timed-damage-profiles', 'timed-shot-damage', 'protocol58-replay']:
    checks.append((name, ['tools/nettest/bin/Release/net10.0/nettest.dll', '--' + name], ['build-nettest']))
for name in ['community-v3-check', 'community-dependency-check', 'protocol-generator-check', 'match-events-check']:
    project = 'tools/' + name
    restore, build = 'restore-' + name, 'build-' + name
    checks.append((restore, ['restore', project, '-p:Configuration=Release'], []))
    checks.append((build, ['build', project, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false'], ([restore, 'build-release'] if name.startswith('community-') else [restore])))
    checks.append((name, ['run', '--project', project, '-c', 'Release', '--no-build', '--no-restore'], [build]))
for name in ['replayformatcheck', 'cosmeticscheck', 'frametimingcheck']:
    checks.append((name, ['src/MphRead/bin/Release/net10.0/ProjectPrime.dll', '-' + name], ['build-release']))
results = []
completed = {}
for name, arguments, dependencies in checks:
    command = [a.dotnet, *arguments]
    started = time.monotonic()
    check_user_data = run_user_data / name
    check_user_data.mkdir()
    check_env = dict(env, PROJECT_PRIME_USER_DATA=str(check_user_data))
    blocked_by = [dependency for dependency in dependencies if completed[dependency] != 'PASS']
    reason = None
    with (a.output / (name + '.log')).open('w') as log:
        log.write('cwd: ' + str(ROOT) + '\ncommand: ' + shlex.join(command) + '\n')
        log.write('PROJECT_PRIME_USER_DATA=' + str(check_user_data) + '\n')
        if blocked_by:
            reason = 'Prerequisite did not pass: ' + ', '.join(blocked_by)
            log.write('BLOCKED: ' + reason + '\n')
            result, code = 'BLOCKED', None
        else:
            log.flush()
            try:
                run = subprocess.run(command, cwd=ROOT, env=check_env, stdout=log,
                                     stderr=subprocess.STDOUT, timeout=900)
                result = 'PASS' if run.returncode == 0 else 'FAIL'
                code = run.returncode
            except (subprocess.TimeoutExpired, OSError) as ex:
                reason = str(ex)
                log.write('BLOCKED: ' + reason + '\n')
                result, code = 'BLOCKED', None
    # Reclassify only concrete setup failures. Assertion/compile failures remain
    # FAIL; a prerequisite/network/SDK failure is not a completed behavioral test.
    if result == 'FAIL' and arguments[0] in ('restore', 'build'):
        output = (a.output / (name + '.log')).read_text(errors='replace')
        if re.search(r'error (?:NU1301|NETSDK1045|NETSDK1147)\b', output):
            result = 'BLOCKED'
            reason = 'NuGet source, required SDK, or workload unavailable; inspect complete log.'
    completed[name] = result
    results.append(dict(name=name, command=command, result=result, exit_code=code,
                        seconds=round(time.monotonic() - started, 3), log=name + '.log',
                        cwd=str(ROOT), user_data=str(check_user_data),
                        dependencies=dependencies, blocked_by=blocked_by, reason=reason))
    (a.output / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    print(f'{name}: {result} ({results[-1]["seconds"]}s)', flush=True)
raise SystemExit(0 if all(r['result'] == 'PASS' for r in results) else 1)
