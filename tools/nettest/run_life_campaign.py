#!/usr/bin/env python3
"""Serialize LIFE acceptance with immutable inputs and owned POSIX process groups."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import struct
import subprocess
import sys
import time
import zlib


def sha(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def clean_environment(original, userdata):
    removed = sorted(key for key in original if key.upper().startswith(('PRIME', 'MPH', 'PROJECT')))
    environment = {key: value for key, value in original.items() if key not in removed}
    environment['PROJECT_PRIME_USER_DATA'] = str(userdata)
    return environment, removed


def group_members(pgid):
    # Include zombies in evidence, but they own no executable/native resources.
    result = subprocess.run(['ps', '-axo', 'pid=,pgid=,stat='], capture_output=True, text=True, check=True, timeout=10)
    members = []
    for line in result.stdout.splitlines():
        values = line.split()
        if len(values) == 3 and int(values[1]) == pgid:
            members.append({'pid': int(values[0]), 'state': values[2]})
    return members


def live_members(members):
    return [member for member in members if not member['state'].startswith('Z')]


def close_group(process):
    before = group_members(process.pid)
    signals = []
    for number, grace in [(signal.SIGTERM, 2), (signal.SIGKILL, 5)]:
        if not live_members(group_members(process.pid)):
            break
        try:
            os.killpg(process.pid, number)
            signals.append(number.name)
        except ProcessLookupError:
            break
        deadline = time.monotonic() + grace
        while time.monotonic() < deadline:
            process.poll()  # Reap the group leader while checking descendants.
            if not live_members(group_members(process.pid)):
                break
            time.sleep(.05)
    process.wait(timeout=5)
    after = group_members(process.pid)
    return {'membersBeforeClose': before, 'signals': signals, 'membersAfterClose': after,
            'closed': not live_members(after)}


def run_owned(command, cwd, environment, log_path, timeout):
    if os.name != 'posix':
        raise RuntimeError('NOT_RUN: process-group closure is qualified only on POSIX hosts')
    started = time.monotonic()
    with Path(log_path).open('w') as log:
        process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        receipt = {'pid': process.pid, 'pgid': process.pid, 'timedOut': False}
        class Interrupted(Exception):
            pass
        def interrupt(number, frame):
            raise Interrupted(signal.Signals(number).name)
        previous_handlers = {number: signal.getsignal(number) for number in (signal.SIGINT, signal.SIGTERM)}
        for number in previous_handlers:
            signal.signal(number, interrupt)
        try:
            receipt['exitCode'] = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            receipt.update(exitCode=-1, timedOut=True, error='timeout; owned group terminated; evidence preserved')
        except Interrupted as error:
            receipt.update(exitCode=-1, interrupted=str(error), error='executor interrupted; owned group terminated; evidence preserved')
        finally:
            try:
                receipt['ownership'] = close_group(process)
            finally:
                for number, handler in previous_handlers.items():
                    signal.signal(number, handler)
    receipt['wallSeconds'] = time.monotonic() - started
    receipt['logSha256'] = sha(log_path)
    receipt['status'] = 'PASS' if receipt['exitCode'] == 0 and receipt['ownership']['closed'] and not live_members(receipt['ownership']['membersBeforeClose']) else 'FAIL'
    return receipt


def png_receipt(path):
    data = Path(path).read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('invalid PNG signature: ' + Path(path).name)
    at, compressed, header = 8, bytearray(), None
    while at < len(data):
        size = struct.unpack('>I', data[at:at + 4])[0]
        kind, payload = data[at + 4:at + 8], data[at + 8:at + 8 + size]
        if len(payload) != size or at + size + 12 > len(data):
            raise ValueError('truncated PNG')
        checksum = struct.unpack('>I', data[at + size + 8:at + size + 12])[0]
        if checksum != zlib.crc32(kind + payload) & 0xffffffff:
            raise ValueError('PNG chunk checksum mismatch')
        if kind == b'IHDR':
            header = struct.unpack('>IIBBBBB', payload)
        elif kind == b'IDAT':
            compressed.extend(payload)
        elif kind == b'IEND':
            break
        at += size + 12
    if header is None:
        raise ValueError('missing PNG header')
    width, height, bits, color, compression, filtering, interlace = header
    if not 0 < width <= 8192 or not 0 < height <= 8192 or bits != 8 or color not in (2, 6) or compression or filtering or interlace:
        raise ValueError('unsupported or invalid capture PNG')
    channels = 3 if color == 2 else 4
    stride = width * channels
    raw = zlib.decompress(compressed)
    if len(raw) != height * (stride + 1):
        raise ValueError('PNG scanline length mismatch')
    prior, nonblack = bytearray(stride), False
    for row in range(height):
        start = row * (stride + 1)
        mode, decoded = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        for index in range(stride):
            left = decoded[index - channels] if index >= channels else 0
            above = prior[index]
            corner = prior[index - channels] if index >= channels else 0
            if mode == 1:
                predictor = left
            elif mode == 2:
                predictor = above
            elif mode == 3:
                predictor = (left + above) // 2
            elif mode == 4:
                estimate = left + above - corner
                distances = [abs(estimate - value) for value in (left, above, corner)]
                predictor = (left, above, corner)[distances.index(min(distances))]
            elif mode == 0:
                predictor = 0
            else:
                raise ValueError('invalid PNG scanline filter')
            decoded[index] = (decoded[index] + predictor) & 255
        nonblack |= any(value for index, value in enumerate(decoded) if index % channels < 3)
        prior = decoded
    if not nonblack:
        raise ValueError('all-black capture: ' + Path(path).name)
    return {'name': Path(path).name, 'width': width, 'height': height, 'bytes': len(data), 'sha256': sha(path), 'nonblack': nonblack}


def shell_receipt(repository, images, text):
    source = (repository / 'src/MphRead/Mods/Launcher/Gui/Shell.cs').read_text()
    standard = source.split('private static Action<RenderWindow>[] StandardScript', 1)[1].split('private static Action<RenderWindow>[]? _lifecycleTail', 1)[0]
    expected = set(re.findall(r'Shot\(w,\s*"([^"]+)"', standard))
    if not expected or 'shell-bot-rematch' not in expected or 'shell-bot-next-map' not in expected:
        raise ValueError('Current full standard shell script is missing rematch/rotation captures')
    for cycle in (1, 2):
        expected.update(f'lifecycle-{cycle}-{stage}' for stage in ('spectator', 'rejoin', 'forge', 'replay', 'replay-seek', 'return'))
    markers = re.findall(r'\[shelllifecycle\] PASS cycle=(\d+) spectator/rejoin/Forge/replay/seek/return', text)
    assets_ready = re.search(r'game files ready=true', text, re.IGNORECASE) is not None
    failures = re.findall(r'\[shellshot\].*(?:nothing .*matched|found nothing to press|missing|could not be read|no game files|started nothing|failed|invalid:)', text, re.IGNORECASE)
    if markers != ['1', '2'] or not assets_ready or failures:
        raise ValueError(f'Incomplete full shell script: cycles={markers}, assetsReady={assets_ready}, failures={failures}')
    captures = []
    for name in sorted(expected):
        image = images / (name + '.png')
        capture = png_receipt(image)
        evidence = image.with_suffix('.png.evidence.json')
        metadata = json.loads(evidence.read_text())
        if metadata.get('actualBackend') != 'OpenGL' or metadata.get('width') != capture['width'] or metadata.get('height') != capture['height']:
            raise ValueError('capture backend/dimension identity mismatch: ' + name)
        capture['evidenceSha256'] = sha(evidence)
        captures.append(capture)
    return {'assetsReady': assets_ready, 'completedCycles': markers, 'fullStandardScript': True,
            'missingSteps': 0, 'expectedCaptures': len(expected), 'captures': captures}


def respawn_receipt(text):
    summaries = re.findall(r'RESPAWNRENDER PASS room=.* cycles=(\d+)/(\d+).*', text)
    if summaries != [('9', '9')]:
        raise ValueError('Missing exact nine-cycle native respawn PASS summary')
    line = next(line for line in text.splitlines() if 'RESPAWNRENDER PASS room=' in line)
    values = dict(re.findall(r'(\w+)=(\d+)\b', line))
    for key in ('invariants', 'gl', 'rendererGl', 'failures'):
        if values.get(key) != '0':
            raise ValueError('Respawn regression count: ' + key)
    if values.get('controlComparisons') != '14' or int(values.get('checked', '0')) == 0 or int(values.get('deaths', '0')) < 9 or values.get('deaths') != values.get('respawns'):
        raise ValueError('Respawn positive denominator missing')
    coverage = dict(re.findall(r'RESPAWNRENDER coverage ([\w-]+)=(\d+)', text))
    families = ('opponent', 'self', 'environment', 'beam-source', 'rapid', 'disruption', 'whiteout', 'scoreboard', 'combined')
    if any(int(coverage.get(family, '0')) < 1 for family in families):
        raise ValueError('Native respawn case coverage missing')
    return {'summary': line, 'coverage': coverage}


def qualification_receipt(data, source, client_hash):
    actual = data.get('actualLoadedClient', {})
    before, after = data.get('sourceBefore', {}), data.get('sourceAfter', {})
    if (not str(data.get('status', '')).startswith('PASS') or before.get('commit') != source['sha']
            or before.get('dirty') or after != before or source['status']
            or actual.get('sourceQualifiedPhysicalClient') is not True
            or actual.get('sha256', '').lower() != client_hash.lower() or not actual.get('mvid')):
        raise ValueError('Source/client qualification receipt is missing, failed, dirty or from a different head/binary')
    return actual


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[2])
    for option in ('nettest', 'client', 'native', 'music', 'filesystem', 'replay', 'paths-template', 'qualification-receipt', 'output'):
        parser.add_argument('--' + option, type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--counts', type=int, nargs='+', default=[10, 100, 500])
    parser.add_argument('--native-cycles', type=int, default=10)
    parser.add_argument('--operations', nargs='+', choices=['asset', 'native', 'replica', 'export', 'music', 'shell', 'respawn'], default=['asset', 'native', 'replica', 'export', 'music', 'shell'])
    parser.add_argument('--timeout', type=int, default=3600)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if any(n < 1 or n > 500 for n in args.counts) or not 1 <= args.native_cycles <= 200 or args.timeout <= 0:
        parser.error('counts must be 1..500, native cycles 1..200 and timeout positive')
    if os.name != 'posix' and not args.dry_run:
        parser.error('NOT_RUN: executable group ownership is qualified only on POSIX hosts')
    repository, output = args.repository.resolve(), args.output.resolve()
    if output.exists() and any(output.iterdir()):
        parser.error('output must be fresh; preserve all prior failures')
    paths = {key: getattr(args, key).resolve() for key in ('nettest', 'client', 'native', 'music', 'replay', 'paths_template', 'qualification_receipt')}
    if paths['paths_template'].name != 'paths.txt':
        parser.error('--paths-template must identify an existing paths.txt')
    for tool in ('nettest', 'native', 'music'):
        paths[tool + 'Client'] = paths[tool].with_name('ProjectPrime.dll')
    for tool in ('nettest', 'client', 'native', 'music'):
        paths[tool + 'Deps'] = paths[tool].with_suffix('.deps.json')
        paths[tool + 'RuntimeConfig'] = paths[tool].with_suffix('.runtimeconfig.json')
    paths['runner'] = Path(__file__).resolve()
    helper_directory = repository / 'tools/native-render-check'
    paths['identityHelper'] = helper_directory / 'campaign_identity.py'
    paths['nativeRunner'] = helper_directory / 'run_resource_arms.py'
    for path in paths.values():
        if not path.is_file():
            parser.error('required input file unavailable: ' + str(path))
    identities = {key: sha(path) for key, path in paths.items()}
    if len({identities[key] for key in ('client', 'nativeClient', 'nettestClient', 'musicClient')}) != 1:
        parser.error('tools must reference the same qualified integrated client DLL')
    sys.path.insert(0, str(helper_directory))
    from campaign_identity import asset_identity, isolate_user_data, host_identity
    output.mkdir(parents=True, exist_ok=True)
    userdata, asset_before = isolate_user_data(paths['paths_template'].parent, output)
    if Path(asset_before['roots'].get('AMHE1', '')).resolve() != args.filesystem.resolve():
        parser.error('AMHE1 paths template must identify the supplied filesystem')
    paths['isolatedPaths'] = userdata / 'paths.txt'
    identities['isolatedPaths'] = sha(paths['isolatedPaths'])
    environment, removed = clean_environment(os.environ, userdata)
    def source_identity():
        def git(*command):
            return subprocess.check_output(['git', '-C', str(repository), *command], text=True).strip()
        return {'sha': git('rev-parse', 'HEAD'), 'status': git('status', '--porcelain'), 'diffSha256': hashlib.sha256(subprocess.check_output(['git', '-C', str(repository), 'diff', 'HEAD', '--'])).hexdigest()}
    source_before = source_identity()
    qualification = qualification_receipt(json.loads(paths['qualification_receipt'].read_text()), source_before, identities['client'])
    manifest = {'schema': 2, 'source': source_before, 'host': host_identity(args.dotnet), 'identities': identities,
                'paths': {key: str(path) for key, path in paths.items()}, 'assetIdentity': asset_before,
                'removedEnvironmentKeys': removed, 'explicitEnvironment': {'PROJECT_PRIME_USER_DATA': str(userdata)},
                'qualification': qualification, 'dryRun': args.dry_run, 'runs': [],
                'scope': 'Serial exact-build LIFE acceptance; platforms and UI/editor playtest not actually exercised remain NOT_RUN.'}
    def changed_inputs():
        changed = [key for key, path in paths.items() if not path.is_file() or sha(path) != identities[key]]
        if source_identity() != source_before:
            changed.append('source')
        return changed
    def save():
        (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    commands = []
    if 'asset' in args.operations:
        for count in args.counts:
            commands.append((f'asset-{count}', [args.dotnet, str(paths['nettest']), '--asset-lifecycle', str(args.filesystem.resolve()), str(count), str(paths['replay']), str(output / f'asset-{count}.json')], args.timeout))
    if 'native' in args.operations:
        commands.append(('native-arms', [sys.executable, str(paths['nativeRunner']), '--dotnet', args.dotnet, '--binary', str(paths['native']), '--output', str(output / 'native-arms'), '--arms', 'combined', 'map-gl', 'studio', 'audio', '--cycles', str(args.native_cycles), '--frames', '120', '--quiescent-samples', '6', '--quiescent-ms', '1000', '--', '-resource-audio-wait'], args.timeout))
    if 'replica' in args.operations:
        commands.append(('replica', [args.dotnet, str(paths['client']), '-replayreplicacheck', str(paths['replay']), '-shots', str(output / 'replica-pictures')], args.timeout))
    if 'export' in args.operations:
        commands.append(('export', [args.dotnet, str(paths['client']), '-replayexportcheck', str(paths['replay']), '-output', str(output / 'export')], args.timeout))
    if 'music' in args.operations:
        commands.append(('music', [args.dotnet, str(paths['music']), '--native', str(args.filesystem.resolve())], args.timeout))
    if 'shell' in args.operations:
        commands.append(('shell', [args.dotnet, str(paths['client']), '-noupdate', '-debuglog', '-shellshot', str(output / 'shell-pictures'), '-shelllifecycle', str(paths['replay'])], args.timeout))
    if 'respawn' in args.operations:
        commands.append(('respawn', [args.dotnet, str(paths['client']), '-noupdate', '-respawnrendercheck', 'MP3 PROVING GROUND', '-cycles', '9', '-timeout', '300'], min(args.timeout, 360)))
    manifest['runs'] = [{'name': name, 'command': command, 'status': 'NOT_RUN'} for name, command, timeout in commands]
    save()
    for (name, command, timeout), record in zip(commands, manifest['runs']):
        save()
        if args.dry_run:
            print(json.dumps(command))
            continue
        changed = changed_inputs()
        if changed:
            record.update(status='FAIL', error='immutable input changed before workload', changedInputs=changed)
            save()
            return 1
        log_path = output / (name + '.log')
        try:
            record.update(run_owned(command, repository, environment, log_path, timeout))
            changed = changed_inputs()
            if changed:
                record.update(status='FAIL', error='immutable input changed after workload', changedInputs=changed)
            if record['status'] == 'PASS':
                if name.startswith('asset-'):
                    result_path = output / (name + '.json')
                    receipt = json.loads(result_path.read_text())
                    count = int(name.split('-')[1])
                    valid = receipt.get('status') == 'PASS' and receipt.get('completedLoads') == count and receipt.get('simultaneousWorlds') == 3 and receipt.get('liveWorlds') == 0 and receipt.get('liveSimultaneous') == 0 and receipt.get('assemblySha256') == identities['client'] and receipt.get('assemblyMvid', '').lower() == qualification['mvid'].lower()
                    record.update(resultSha256=sha(result_path), positiveReceiptValid=valid, assemblyMvid=receipt.get('assemblyMvid'))
                    if not valid:
                        raise ValueError('Asset fixture positive receipt or loaded DLL identity mismatch')
                elif name == 'shell':
                    text = log_path.read_text()
                    debug_logs = list(userdata.rglob(f'*-{record["pid"]}.log'))
                    for debug_log in debug_logs:
                        text += '\n' + debug_log.read_text(errors='replace')
                    record['debugLogs'] = [{'name': path.name, 'sha256': sha(path)} for path in debug_logs]
                    record['acceptance'] = shell_receipt(repository, output / 'shell-pictures', text)
                elif name == 'respawn':
                    record['acceptance'] = respawn_receipt(log_path.read_text())
        except Exception as error:
            record.update(status='FAIL', error=type(error).__name__ + ': ' + str(error))
        save()
        print(f"{name}: {record['status']} wall={record.get('wallSeconds', 0):.1f}s", flush=True)
        if record['status'] != 'PASS':
            return 1
    if not args.dry_run:
        manifest['assetIdentityAfter'] = asset_identity(paths['isolatedPaths'])
        manifest['inputsUnchangedAtCompletion'] = not changed_inputs() and manifest['assetIdentityAfter'] == asset_before
        save()
        if not manifest['inputsUnchangedAtCompletion']:
            return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
