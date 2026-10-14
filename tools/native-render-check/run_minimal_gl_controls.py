#!/usr/bin/env python3
"""Serial immutable GL-only controls; external profiling remains opt-in."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time

from analyze_vmmap import parse as parse_vmmap
from campaign_identity import host_identity
from run_context_controls import capture, sha256
from validate_context_campaign import inspect_stacks, read_json

MODES = ('no-shader', 'compile-only', 'compile-delete', 'fresh-contexts', 'rebind-draw')
VARIANTS = ('tiny', 'canonical', 'native', 'plain', 'unlit', 'four-prewarm')


def expected_phases(mode, iterations):
    contexts = iterations if mode in ('no-shader', 'fresh-contexts') else 1
    return [f'minimal-{context:03d}-{phase}' for context in range(contexts)
            for phase in ('before-workload', 'after-workload', 'after-disposal')]


def command(args):
    cmd = [args.dotnet, str(args.binary.resolve()), '--minimal-gl', '--minimal-gl-mode', args.mode,
           '--minimal-gl-variant', args.variant, '--minimal-gl-iterations', str(args.iterations),
           '--minimal-gl-sampler-unit', str(args.sampler_unit), '-output', str(args.output.resolve() / 'minimal-gl.json')]
    for define in args.define:
        cmd.extend(['--minimal-gl-define', define])
    for binding in args.bind:
        cmd.extend(['--minimal-gl-bind', binding])
    if args.depth_vertex:
        cmd.append('--minimal-gl-depth-vertex')
    if args.fail_at:
        cmd.extend(['--minimal-gl-fail-at', args.fail_at])
    if args.capture_native_stacks:
        cmd.extend(['--minimal-gl-phase-acks', str(args.output.resolve() / 'phase-acks')])
    return cmd


def validate_minimal_campaign(manifest, root):
    """Check successful evidence closure, without native-memory acceptance."""
    root = Path(root)
    invalid, unknown, attribution = [], [], []
    try:
        if manifest.get('exitCode') != 0 or manifest.get('complete') is not True or manifest.get('error'):
            invalid.append('campaign failed or incomplete')
        if manifest.get('identityMatches') is not True:
            invalid.append('binary identity changed or comparison unavailable')
        expected = expected_phases(manifest['mode'], manifest['iterations'])
        log_path = root / 'minimal-gl.log'
        if sha256(log_path) != manifest.get('logSha256'):
            invalid.append('process log hash changed')
        if re.search(r'(?m)^(?:MINIMALGL FAIL |Unhandled exception\.)', log_path.read_text(errors='replace')):
            invalid.append('process log contains a failure')
        result_path = root / 'minimal-gl.json'
        if sha256(result_path) != manifest.get('resultSha256'):
            invalid.append('result hash changed')
        result = read_json(result_path)
        if result.get('complete') is not True or result.get('error'):
            invalid.append('GL control failed despite runner receipt')
        for key in ('harnessSha256', 'clientSha256'):
            if result.get(key, '').lower() != manifest.get('identity', {}).get(key, '').lower():
                invalid.append('control binary hash differs: ' + key)
        phases = [f"minimal-{row['context']:03d}-{row['phase']}" for row in result.get('records', []) if 'phase' in row]
        if phases != expected:
            invalid.append('missing, duplicate, mixed-context or reordered held phases')
        variants = 4 if manifest['variant'] == 'four-prewarm' else 1
        units = 1 if manifest['mode'] == 'rebind-draw' else manifest['iterations']
        programs = 0 if manifest['mode'] == 'no-shader' else units * variants
        if result.get('completedUnits') != units or result.get('compileProgramGenerations') != programs:
            invalid.append('unexpected workload/program-generation gaps')
        events = result.get('records', [])
        if len([row for row in events if row.get('operation') == 'link']) != programs:
            invalid.append('missing or extra program links')
        if len([row for row in events if row.get('operation') == 'compile']) != 2 * programs:
            invalid.append('missing or extra shader compilations')
        if len([row for row in events if row.get('operation') == 'retire']) != programs:
            invalid.append('missing or extra program retirements')
        for row in events:
            if row.get('operation') in ('compile', 'link') and row.get('status') != 1:
                invalid.append('unsuccessful compile/link receipt')
            if row.get('cleanupErrors') or (row.get('operation') == 'retire' and row.get('remainingRecognized') != 0):
                invalid.append('owned-name retirement failed')
        draws = 0 if manifest['mode'] in ('no-shader', 'compile-only') else variants * manifest['iterations']
        for row in events:
            if row.get('operation') == 'draw' and (row.get('pixels', {}).get('nonBlackPixels', 0) <= 0 or row.get('pixels', {}).get('changedPixels') == 0):
                invalid.append('vacuous or unchanged synthetic draw pixels')
        if len([row for row in events if row.get('operation') == 'use-program']) != draws:
            invalid.append('program-bind workload gap')
        if len([row for row in events if row.get('operation') == 'draw']) != draws:
            invalid.append('draw workload gap')
        captures = manifest.get('captures', [])
        if manifest.get('captureNativeStacks'):
            if [item.get('phase') for item in captures] != expected:
                invalid.append('missing, duplicate or reordered external capture phases')
            paths = set()
            for item in captures:
                if item.get('pid') != result.get('processId'):
                    invalid.append('capture process differs')
                tools = []
                for step in item.get('steps', []):
                    cmd = step.get('command', [])
                    tool = Path(cmd[0]).name if cmd else ''
                    tools.append(tool)
                    if step.get('exitCode') != 0:
                        invalid.append('failed external capture')
                    path = Path(step['path'])
                    path = path if path.is_absolute() else root / path
                    if str(path.resolve()) in paths:
                        invalid.append('capture file reused across phases')
                    paths.add(str(path.resolve()))
                    if sha256(path) != step.get('sha256'):
                        invalid.append('capture file hash changed')
                    if str(item.get('pid')) not in [str(value) for value in cmd]:
                        invalid.append('capture command targets a different process')
                    if tool == 'vmmap':
                        if '-summary' not in cmd:
                            invalid.append('VM summary command missing')
                        try:
                            vm = parse_vmmap(path.read_text(errors='replace'))
                            if not vm['supported']: unknown.append('unsupported VM-category format')
                        except ValueError:
                            unknown.append('unsupported VM-category quantity format')
                    if tool == 'malloc_history':
                        if '-allBySize' not in cmd or '-fullStacks' not in cmd or any(flag in cmd for flag in ('-allEvents', '-highWaterMark')):
                            invalid.append('malloc capture is not current outstanding full stacks')
                        raw = gzip.decompress(path.read_bytes()) if step.get('compression') == 'gzip' else path.read_bytes()
                        if hashlib.sha256(raw).hexdigest() != step.get('uncompressedSha256', step.get('sha256')):
                            invalid.append('native stdout hash changed')
                        status, issues, parsed = inspect_stacks(raw.decode(errors='replace'))
                        if status == 'INVALID': invalid.extend(issues)
                        if status == 'UNVERIFIED': unknown.extend(issues)
                        attribution.append({'phase': item['phase'], 'categories': parsed['categories'] if parsed else None})
                if Counter(tools) != Counter(['vmmap', 'malloc_history']):
                    invalid.append('incomplete capture tools')
        elif captures:
            invalid.append('captures present despite disabled capture policy')
        else:
            unknown.append('native allocation captures NOT_RUN')
        if manifest.get('sourceDirty') is not False:
            unknown.append('source tree is not a clean release receipt')
    except (OSError, ValueError, TypeError, KeyError) as error:
        invalid.append(str(error))
    return {'schema': 1, 'status': 'INVALID' if invalid else 'UNVERIFIED' if unknown else 'VALID',
            'issues': invalid, 'unverified': unknown, 'nativeAttribution': attribution,
            'boundedness': 'UNVERIFIED', 'sharedContexts': 'NOT_RUN'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--binary', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--mode', choices=MODES, default='compile-delete')
    parser.add_argument('--variant', choices=VARIANTS, default='canonical')
    parser.add_argument('--iterations', type=int, default=30)
    parser.add_argument('--define', action='append', default=[])
    parser.add_argument('--bind', action='append', default=[])
    parser.add_argument('--sampler-unit', type=int, default=0)
    parser.add_argument('--depth-vertex', action='store_true')
    parser.add_argument('--fail-at')
    parser.add_argument('--capture-native-stacks', action='store_true')
    parser.add_argument('--compress-stacks', action='store_true')
    parser.add_argument('--timeout', type=float, default=3600)
    parser.add_argument('--max-rss-mib', type=float, default=2048)
    parser.add_argument('--min-free-disk-mib', type=float, default=1024)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not 1 <= args.iterations <= 900 or not 0 <= args.sampler_unit <= 15 or args.timeout <= 0:
        parser.error('iterations 1..900, sampler unit 0..15 and positive timeout required')
    if args.max_rss_mib <= 0 or args.min_free_disk_mib <= 0:
        parser.error('positive safety guardrails required')
    if args.output.exists():
        parser.error('output directory must be fresh')
    args.output.mkdir(parents=True)
    cmd = command(args)
    client = args.binary.parent / 'ProjectPrime.dll'
    identity = {'harnessSha256': sha256(args.binary), 'clientSha256': sha256(client)}
    manifest = {'schema': 1, 'kind': 'minimal-gl-runner', 'mode': args.mode, 'variant': args.variant,
                'iterations': args.iterations, 'command': cmd, 'identity': identity, 'binary': str(args.binary.resolve()),
                'sourceSha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                'sourceDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip()),
                'host': None if args.dry_run else host_identity(args.dotnet), 'captureNativeStacks': args.capture_native_stacks,
                'compressStacks': args.compress_stacks, 'dryRun': args.dry_run,
                'guardrails': {'maxRssMiB': args.max_rss_mib, 'minFreeDiskMiB': args.min_free_disk_mib},
                'expectedNativePhases': expected_phases(args.mode, args.iterations) if args.capture_native_stacks else [],
                'captures': [], 'sharedContexts': 'NOT_RUN', 'boundedness': 'UNVERIFIED'}
    manifest_path = args.output / 'runner-manifest.json'
    def save(): manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    save()
    if args.dry_run: return 0
    env = os.environ.copy()
    if args.capture_native_stacks: env['MallocStackLogging'] = '1'
    manifest['environment'] = {key: value for key, value in env.items() if key.startswith('PROJECT_PRIME_GL_') or key.startswith('MallocStackLogging')}
    log = args.output / 'minimal-gl.log'
    started = time.monotonic(); pending = {}; observed = set()
    with log.open('w') as dest, log.open('r') as reader:
        process = subprocess.Popen(cmd, stdout=dest, stderr=subprocess.STDOUT, env=env)
        try:
            while process.poll() is None:
                for line in reader.readlines():
                    match = re.search(r'MINIMALGL phase=(\S+) context=(\d+) pid=(\d+)', line)
                    if not match: continue
                    phase, _, pid = match.groups()
                    if args.capture_native_stacks:
                        if phase in observed or phase in pending or phase not in manifest['expectedNativePhases'] or int(pid) != process.pid:
                            raise RuntimeError('Duplicate/unexpected phase or process mismatch: ' + phase)
                        pending[phase] = time.monotonic()
                for phase, marker in list(pending.items()):
                    if time.monotonic() - marker >= 3:
                        item = capture(process.pid, phase, args.output, args.compress_stacks)
                        manifest['captures'].append(item); observed.add(phase); del pending[phase]; save()
                        ack = args.output / 'phase-acks' / (phase + '.ack')
                        ack.write_text('capture complete\n')
                if time.monotonic() - started > args.timeout:
                    raise RuntimeError('Campaign timeout; partial evidence retained')
                if shutil.disk_usage(args.output).free / (1024 ** 2) < args.min_free_disk_mib:
                    raise RuntimeError('Free-disk guardrail reached; partial evidence retained')
                try:
                    rss = subprocess.check_output(['ps', '-o', 'rss=', '-p', str(process.pid)], text=True).strip()
                except subprocess.CalledProcessError:
                    if process.poll() is None: raise
                    rss = ''  # Process completed between poll and ps; final receipt decides success.
                if rss and float(rss) / 1024 > args.max_rss_mib:
                    raise RuntimeError('RSS guardrail reached; partial evidence retained')
                time.sleep(0.25)
        except Exception as error:
            manifest['error'] = str(error)
            if process.poll() is None: process.kill()
        finally:
            if process.poll() is None: process.kill()
            manifest['exitCode'] = process.wait()
            manifest['wallSeconds'] = time.monotonic() - started
            manifest['identityMatches'] = identity == {'harnessSha256': sha256(args.binary), 'clientSha256': sha256(client)}
            manifest['logSha256'] = sha256(log)
            result = args.output / 'minimal-gl.json'
            if result.is_file():
                manifest['resultSha256'] = sha256(result)
                manifest['complete'] = read_json(result).get('complete') is True
            save()
    validation = validate_minimal_campaign(manifest, args.output)
    (args.output / 'validation.json').write_text(json.dumps(validation, indent=2) + '\n')
    return 1 if validation['status'] == 'INVALID' else 0


if __name__ == '__main__':
    raise SystemExit(main())
