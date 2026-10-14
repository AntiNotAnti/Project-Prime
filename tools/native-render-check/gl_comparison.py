#!/usr/bin/env python3
"""Serial same-binary PALETTE_PREP controls and portable evidence verification.

Importing never starts GL. Native execution requires --execute; otherwise run
prints the complete schedule. Performance, capture and profiling remain separate.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import sys

METRICS = ('frameIntervalP50Ms', 'frameIntervalP95Ms', 'frameIntervalP99Ms')
FIXTURE_KEYS = ('room', 'cap', 'shadows', 'textureQuality', 'lighting', 'resolutionScale',
                'vendor', 'renderer', 'glVersion', 'effectiveCap', 'vsync', 'framebuffer', 'renderSize')
CLEANUP_ZERO = ('ownedTexturesAfter', 'framebufferAfter', 'ownedProgramsAfter',
                'recognizedProgramNamesAfter', 'pendingDeletedProgramCountAfter', 'currentProgramAfter',
                'depthSnapshotBytesAfter', 'proceduralBytesAfter', 'retainedListsAfter',
                'combinedBytesAfter', 'retainedBytesAfter', 'glyphCountAfter', 'glyphBytesAfter')


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def write(path, data):
    Path(path).write_text(json.dumps(data, indent=2, allow_nan=False) + '\n')


def physical_binary(path):
    path = Path(path).absolute()
    if any(part.is_symlink() for part in (path, *path.parents)):
        raise ValueError('materialize the harness and client in a physical directory')
    client = path.with_name('ProjectPrime.dll')
    if client.is_symlink():
        raise ValueError('materialize the client beside the harness')
    return path, {'clientSha256': sha256(client), 'harnessSha256': sha256(path)}


def relative_file(root, name):
    root = Path(root).resolve()
    path = (root / name).resolve()
    if not path.is_relative_to(root):
        raise ValueError('evidence path escapes bundle: ' + name)
    return path


def require_result(data, record, expected_mvid):
    if record.get('exitCode') != 0 or data.get('complete') is not True:
        raise ValueError('failed/incomplete native result')
    if data.get('assemblyMvid', '').lower() != expected_mvid.lower():
        raise ValueError('loaded client MVID differs from declared binary')
    cleanup = data.get('cleanup', {})
    if any(cleanup.get(key) != 0 for key in CLEANUP_ZERO):
        raise ValueError('missing/nonzero owned-name retirement')
    if cleanup.get('programOwnerFieldsReleased') is not True or cleanup.get('shadowReleased') is not True:
        raise ValueError('incomplete scene cleanup')
    detail = cleanup.get('detailedRetirement', {})
    if not detail or any(value != 0 for key, value in detail.items() if key.endswith('After')):
        raise ValueError('detailed owned-name retirement is incomplete')


def validate(bundle, root):
    """Verify raw result/log digests and recompute descriptive per-run statistics."""
    if bundle.get('schema') != 1 or bundle.get('complete') is not True:
        raise ValueError('incomplete evidence bundle')
    if bundle.get('fixtureIdentityMatches') is not True:
        raise ValueError('asset fixture changed or was not checked')
    if bundle.get('sanitization'):
        from sanitize_gl_evidence import verify
        verify(root, manifest=bundle)
    binary = bundle['binary']
    for key in ('clientSha256', 'harnessSha256'):
        if re.fullmatch('[0-9a-fA-F]{64}', binary.get(key, '')) is None:
            raise ValueError('missing binary hash: ' + key)
    mvid = binary['expectedLoadedClientMvid']
    if not mvid:
        raise ValueError('missing expected loaded client MVID')
    for entry in bundle.get('files', []):
        if sha256(relative_file(root, entry['file'])) != entry['sha256']:
            raise ValueError('changed evidence file: ' + entry['file'])
    samples = {0: [], 1: []}
    fixtures = {}
    seen = set()
    seen_paths = set()
    for record in bundle['records']:
        identity = (record['category'], record['index'])
        if identity in seen:
            raise ValueError('duplicate run identity')
        seen.add(identity)
        if record['arm'] not in (0, 1):
            raise ValueError('invalid comparison arm')
        for kind in ('result', 'log'):
            path_key = str(relative_file(root, record[kind]))
            if path_key in seen_paths:
                raise ValueError('run reuses another raw result/log path')
            seen_paths.add(path_key)
            if sha256(relative_file(root, record[kind])) != record[kind + 'Sha256']:
                raise ValueError('changed ' + kind + ': ' + record[kind])
        data = json.loads(relative_file(root, record['result']).read_text())
        log = relative_file(root, record['log']).read_text()
        require_result(data, record, mvid)
        if 'NATIVERENDER FAIL' in log or 'Unhandled exception.' in log:
            raise ValueError('native log reports failure')
        status = record.get('status', 'VALID')
        if status == 'EXCLUDED':
            if not record.get('reason'):
                raise ValueError('excluded run lacks a preserved reason')
            continue
        if status != 'VALID':
            raise ValueError('unknown run disposition')
        category = record['category']
        key = (category, record.get('fixture', 'default'))
        fixture = {field: data[field] for field in FIXTURE_KEYS}
        if key in fixtures and fixtures[key] != fixture:
            raise ValueError('fixture differs between matched arms')
        fixtures[key] = fixture
        if category == 'unprofiled':
            if record.get('environment', {}).get('PROJECT_PRIME_GL_PALETTE_PREP') != str(record['arm']):
                raise ValueError('comparison arm differs from declared launch environment')
            if data.get('captures') or data.get('captureKillFeedLifetimeStabilized') or '[glpasses-json]' in log:
                raise ValueError('capture/profile work contaminates performance result')
            if log.count('NATIVERENDER measurement-start') != 1:
                raise ValueError('missing/duplicate simulation warmup marker')
            if data.get('droppedSimulationSteps') != 0 or data.get('stalls') != 0:
                raise ValueError('simulation dropped steps or stalled')
            if not 59 <= data.get('simulationHz', 0) <= 61:
                raise ValueError('simulation cadence outside diagnostic bound')
            values = [data.get(metric) for metric in METRICS]
            if any(isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0 for value in values):
                raise ValueError('invalid frame percentiles')
            if values != sorted(values) or data.get('samples', 0) < 1000:
                raise ValueError('unordered percentiles or insufficient samples')
            samples[record['arm']].append(data)
        elif category == 'parity':
            if [item['simulationFrame'] for item in data.get('captures', [])] != [240, 540, 840, 1140]:
                raise ValueError('parity capture simulation frames differ')
        elif category != 'profile':
            raise ValueError('unknown evidence category')
    medians, arrays = {}, {}
    if any(samples.values()):
        if min(len(samples[arm]) for arm in (0, 1)) < 5 or len(samples[0]) != len(samples[1]):
            raise ValueError('need at least five accepted matched runs per arm')
        for arm in (0, 1):
            arrays[str(arm)] = {metric: [row[metric] for row in samples[arm]] for metric in METRICS}
            medians[str(arm)] = {metric: statistics.median(values) for metric, values in arrays[str(arm)].items()}
    change = {metric: (medians['1'][metric] / medians['0'][metric] - 1) * 100 for metric in METRICS} if medians else {}
    return {'schema': 1, 'status': 'VALID', 'acceptedSamplesPerArm': {str(arm): len(samples[arm]) for arm in (0, 1)},
            'sampleArrays': arrays, 'medianPerRun': medians, 'candidatePercentChange': change,
            'performanceDisposition': 'REJECTED' if any(change.get(metric, 0) > 0 for metric in METRICS[1:]) else 'REQUIRES_REMAINING_GATES',
            'defaultPolicy': 'PALETTE_PREP_OFF', 'gpuTiming': 'UNVERIFIED',
            'scope': 'Receipt integrity and descriptive timing only; recorded parity requires original capture files for a new pixel comparison.'}


def schedule(kind):
    if kind == 'regression':
        return [(index, arm, 'eight-high', 8, 'High', '1920x1080', 'Samus') for index, arm in enumerate((0, 1))]
    if kind == 'unprofiled':
        return [(index, arm, 'eight-high', 8, 'High', '1920x1080', 'Samus')
                for index, arm in enumerate((0, 1, 1, 0, 0, 1, 1, 0, 0, 1))]
    if kind == 'profile':
        return [(index, arm, 'eight-high', 8, 'High', '1920x1080', 'Samus') for index, arm in enumerate((1, 0))]
    return [(index, arm, fixture, players, shadows, size, hunter)
            for index, (fixture, players, shadows, size, hunter, arm) in enumerate(
                [(fixture, players, shadows, size, hunter, arm)
                 for fixture, players, shadows, size, hunter, arms in (
                     ('eight-high', 8, 'High', '1920x1080', 'Samus', (0, 1)),
                     ('solo-off', 1, 'Off', '1280x720', 'Kanden', (1, 0)),
                     ('one-bot-high', 2, 'High', '1920x1080', 'Spire', (0, 1)))
                 for arm in arms])]


def compare_captures(manifest, root):
    from PIL import Image
    comparisons = []
    groups = {}
    for record in manifest['records']:
        groups.setdefault(record['fixture'], {})[record['arm']] = record
    expected_base = {f'frame-{index}-{kind}' for index in range(4)
                     for kind in ('hud.png', 'world.png', 'world-depth-float32.bin')}
    expected_base.update(('frame-0-world-repeat.png', 'frame-0-world-depth-repeat-float32.bin'))
    for fixture, arms in groups.items():
        if set(arms) != {0, 1}:
            raise ValueError('missing parity arm')
        folders = [root / (Path(arms[arm]['result']).stem + '-shots') for arm in (0, 1)]
        expected = expected_base | ({'shadow-depth-float32.bin'} if fixture != 'solo-off' else set())
        if any({path.name for path in folder.iterdir()} != expected for folder in folders):
            raise ValueError('missing/extra parity captures')
        result = json.loads((root / arms[0]['result']).read_text())
        width, height = result['renderSize']
        for name in sorted(expected):
            paths = [folder / name for folder in folders]
            if name.endswith('.png'):
                with Image.open(paths[0]) as off, Image.open(paths[1]) as on:
                    passed = off.size == on.size == (width, height) and off.convert('RGBA').tobytes() == on.convert('RGBA').tobytes()
                kind = 'exactRGBA'
            else:
                expected_bytes = width * height * 4
                if name == 'shadow-depth-float32.bin':
                    expected_bytes = result['captures'][0]['shadow']['size'] ** 2 * 4
                passed = paths[0].stat().st_size == paths[1].stat().st_size == expected_bytes and paths[0].read_bytes() == paths[1].read_bytes()
                kind = 'exactFloat32DepthBits'
            comparisons.append({'fixture': fixture, 'file': name, 'kind': kind, 'passed': passed,
                                'referenceSha256': sha256(paths[0]), 'candidateSha256': sha256(paths[1])})
            for path in paths:
                manifest['files'].append({'file': path.relative_to(root).as_posix(), 'sha256': sha256(path)})
    receipt = {'schema': 1, 'purpose': 'capture-only, excluded from timing', 'comparisons': comparisons,
               'passed': bool(comparisons) and all(item['passed'] for item in comparisons)}
    write(root / 'parity.json', receipt)
    if not receipt['passed']:
        raise ValueError('exact native parity differs; all mismatch captures retained')


def run(args):
    binary, hashes = physical_binary(args.binary)
    source = Path(args.source_root).resolve()
    git = lambda *cmd: subprocess.check_output(['git', '-C', str(source), *cmd], text=True).strip()
    manifest = {'schema': 1, 'complete': False, 'source': git('rev-parse', 'HEAD'),
                'sourceDirty': bool(git('status', '--porcelain')), 'binary': dict(hashes, expectedLoadedClientMvid=args.client_mvid),
                'fixtureIdentityMatches': False, 'records': [], 'files': [], 'kind': args.kind,
                'host': {'os': platform.platform(), 'architecture': platform.machine()},
                'idleMachine': args.idle_note, 'purpose': 'same-binary serial GL control; not a device rollout certificate'}
    env = {key: value for key, value in os.environ.items()
           if not key.startswith(('PROJECT_PRIME_GL_', 'MallocStackLogging'))}
    if args.execute:
        if platform.system() not in ('Darwin', 'Windows'):
            raise ValueError('current harness supports only macOS/Windows; other devices remain NOT_RUN')
        if manifest['sourceDirty']:
            raise ValueError('native campaign requires a clean source receipt')
        if not args.idle_note:
            raise ValueError('--idle-note must record held builds/tests/game and host conditions')
        if args.output.exists():
            raise ValueError('use a fresh output directory')
        sys.path.insert(0, str(source / 'tools/native-render-check'))
        from campaign_identity import isolate_user_data, asset_identity
        args.output.mkdir(parents=True)
        user_data, assets = isolate_user_data(args.user_data, args.output)
        manifest['fixtureSha256'] = assets['sha256']
        env['PROJECT_PRIME_USER_DATA'] = str(user_data)
    else:
        user_data = args.output / 'user-data'
        env['PROJECT_PRIME_USER_DATA'] = str(user_data)
    for index, arm, fixture, players, shadows, size, hunter in schedule(args.kind):
        name = f'{index:02d}-{fixture}-palette-{arm}'
        record = {'category': 'parity' if args.kind == 'regression' else args.kind, 'index': index, 'arm': arm, 'fixture': fixture,
                  'result': name + '.json', 'log': name + '.log', 'exitCode': None,
                  'environment': {'PROJECT_PRIME_GL_PALETTE_PREP': str(arm)}}
        command = [args.dotnet, str(binary), '-room', 'MP3 PROVING GROUND', '-players', str(players),
                   '-seconds', '25' if args.kind == 'profile' else '24' if args.kind in ('parity', 'regression') else '12',
                   '-hz', '-1', '-size', size, '-shadows', shadows, '-hunter', hunter,
                   '-output', str(args.output / record['result'])]
        if args.kind in ('parity', 'regression'): command += ['-shots', str(args.output / (name + '-shots')), '-stablehud']
        if args.kind == 'profile': command += ['-glprofile']
        record['command'] = command
        manifest['records'].append(record)
        if not args.execute:
            continue
        write(args.output / 'manifest.json', manifest)
        if physical_binary(binary)[1] != hashes:
            raise ValueError('binary changed between arms')
        record['startedUtc'] = datetime.now(timezone.utc).isoformat()
        with (args.output / record['log']).open('w') as log:
            try:
                record['exitCode'] = subprocess.run(command, env=dict(env, **record['environment']),
                                                   stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout).returncode
            except subprocess.TimeoutExpired:
                record['exitCode'], record['error'] = -1, 'timeout; retained incomplete receipt'
        record['finishedUtc'] = datetime.now(timezone.utc).isoformat()
        record['logSha256'] = sha256(args.output / record['log'])
        if (args.output / record['result']).exists():
            record['resultSha256'] = sha256(args.output / record['result'])
        write(args.output / 'manifest.json', manifest)
        data = json.loads((args.output / record['result']).read_text())
        require_result(data, record, args.client_mvid)
        if physical_binary(binary)[1] != hashes:
            raise ValueError('binary changed during arm')
        print(name + ' COMPLETE', flush=True)
    if not args.execute:
        print(json.dumps(dict(manifest, dryRun=True), indent=2))
        return 0
    manifest['fixtureIdentityMatches'] = asset_identity(user_data / 'paths.txt')['sha256'] == manifest['fixtureSha256']
    if args.kind in ('parity', 'regression'):
        compare_captures(manifest, args.output)
    manifest['complete'] = True
    write(args.output / 'manifest.json', manifest)
    try:
        report = validate(manifest, args.output)
    except (ValueError, KeyError, OSError, TypeError) as error:
        manifest.update(complete=False, validationStatus='INVALID', error=str(error))
        write(args.output / 'manifest.json', manifest)
        raise
    write(args.output / 'validation.json', report)
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    check = commands.add_parser('validate')
    check.add_argument('manifest', type=Path)
    check.add_argument('--output', type=Path)
    native = commands.add_parser('run')
    native.add_argument('--binary', type=Path, required=True)
    native.add_argument('--client-mvid', required=True)
    native.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[2])
    native.add_argument('--user-data', type=Path, required=True, help='Only paths.txt is copied to fresh isolated settings')
    native.add_argument('--output', type=Path, required=True)
    native.add_argument('--kind', choices=('unprofiled', 'parity', 'profile', 'regression'), required=True)
    native.add_argument('--dotnet', default='dotnet')
    native.add_argument('--idle-note', default='')
    native.add_argument('--timeout', type=int, default=120)
    native.add_argument('--execute', action='store_true')
    args = parser.parse_args()
    try:
        if args.command == 'run':
            args.output = args.output.absolute()
            if args.timeout <= 0: parser.error('positive timeout required')
            return run(args)
        report = validate(json.loads(args.manifest.read_text()), args.manifest.parent)
        if args.output:
            if args.output.exists(): raise ValueError('refusing validation receipt overwrite')
            write(args.output, report)
        print(json.dumps(report, indent=2))
        return 0
    except (ValueError, KeyError, OSError, TypeError) as error:
        print('GL EVIDENCE INVALID: ' + str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
