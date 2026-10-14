#!/usr/bin/env python3
"""External qualified native-AI campaign wrapper. Preparation is the default."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import sys
import time
import shutil
import re
from typing import NamedTuple

SIX = tuple(f'{name}.{extension}' for name in ('ProjectPrime', 'nettest')
            for extension in ('dll', 'deps.json', 'runtimeconfig.json'))
class Case(NamedTuple):
    players: int
    seconds: int
    port: int
    profile: str
    rtt: int
    jitter: int
    loss: int
    reorder: int
    duplicate: int


CASES = {
    'smoke2': Case(2, 90, 29250, 'poor', 250, 40, 5, 5, 2),
    'long2': Case(2, 1200, 29252, 'poor', 250, 40, 5, 5, 2),
    'long8': Case(8, 1200, 29254, 'poor', 250, 40, 5, 5, 2),
    'smoke-lan2': Case(2, 90, 29256, 'lan', 0, 0, 0, 0, 0),
    'smoke-moderate4': Case(4, 90, 29258, 'moderate', 100, 20, 1, 3, 1),
    'smoke-worst8': Case(8, 90, 29260, 'extreme', 400, 80, 5, 5, 2),
}
DISK_FLOOR = int(1.5 * 1024 ** 3)


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def save(path, value):
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def source_identity(root):
    def git(*args):
        return subprocess.check_output(['git', '-C', str(root), *args], text=True).strip()
    return {'commit': git('rev-parse', 'HEAD'), 'tree': git('rev-parse', 'HEAD^{tree}'),
            'dirty': git('status', '--porcelain')}


def frozen_hashes(build):
    for name in SIX:
        if not (build / name).is_file() or (build / name).is_symlink():
            raise ValueError(f'Physical entry/loader missing: {name}')
    return {name: digest(build / name) for name in SIX}


def sdk_identity(path, cwd=None):
    if not path.is_file() or path.is_symlink():
        raise ValueError('explicit --dotnet must name a physical SDK executable')
    version = subprocess.check_output([str(path), '--version'], text=True, cwd=cwd, timeout=20).strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.+-]+)?', version):
        raise ValueError('SDK version query did not produce a valid version')
    return {'path': str(path.resolve()), 'sha256': digest(path), 'version': version,
            'versionQuery': [str(path), '--version'], 'versionCwd': str(cwd) if cwd is not None else None}


def source_protocol(root):
    text = (root / 'src/MphRead/Mods/Network/NetProtocol.cs').read_text()
    values = re.findall(r'public const int ProtocolVersion\s*=\s*(\d+)\s*;', text)
    if len(values) != 1 or not 1 <= int(values[0]) <= 255:
        raise ValueError('source must declare one valid protocol byte')
    return int(values[0])


def inventory(build):
    return {p.name: {'bytes': p.stat().st_size,
            **({'linkTarget': str(p.resolve())} if p.is_symlink() else {'sha256': digest(p)})}
            for p in sorted(build.iterdir()) if p.is_file()}


def recordings(root):
    rows = []
    for path in sorted(root.rglob('*.ppdemo*')):
        if not path.is_file():
            continue
        with path.open('rb') as stream:
            header = stream.read(6)  # No compressed replay or checkpoint is loaded into RAM.
        rows.append({'path': str(path), 'bytes': path.stat().st_size, 'sha256': digest(path),
                     'magic': header[:4].decode('ascii', errors='replace'),
                     'format': header[4] if len(header) == 6 else None,
                     'protocol': header[5] if len(header) == 6 else None,
                     'finalName': path.name.endswith('.ppdemo')})
    return rows


def members(group):
    listing = subprocess.check_output(['ps', '-axo', 'pid=,pgid=,command='], text=True)
    return [line.strip() for line in listing.splitlines()
            if len(line.split(None, 2)) >= 2 and line.split(None, 2)[1] == str(group)]


def stop_owned(process):
    """Only the exact session/group created below; no global/name-based kill."""
    if not members(process.pid):
        return []
    for sig, wait in ((signal.SIGTERM, 10), (signal.SIGKILL, 5)):
        try:
            os.killpg(process.pid, sig)
        except ProcessLookupError:
            break
        except PermissionError:
            # A group can disappear between ps and killpg. Recheck rather than
            # declaring closure or escalating after a denied signal.
            if not members(process.pid):
                break
            raise
        deadline = time.monotonic() + wait
        while time.monotonic() < deadline and members(process.pid):
            time.sleep(.2)
        if not members(process.pid):
            break
    if process.poll() is None:
        process.wait(timeout=10)
    return members(process.pid)


def command(args, campaign):
    case = CASES[args.case]
    return [sys.executable, str(args.source_worktree / 'tools/nettest/run-net2.py'),
        '--build', str(args.build), '--paths', str(args.paths), '--source', args.source,
        '--dotnet', str(args.dotnet), '--protocol', str(args.protocol), '--out', str(campaign),
        '--players', str(case.players), '--ai-net-client', '2', '--ai-seed', '1729',
        '--powerups', '--rooms', 'MP12 SIC TRANSIT', '--seconds', str(case.seconds), '--drain-seconds', '35',
        '--profile', case.profile, '--rtt-ms', str(case.rtt), '--jitter-ms', str(case.jitter), '--loss-percent', str(case.loss),
        '--reorder-percent', str(case.reorder), '--duplicate-percent', str(case.duplicate), '--seed', '8128',
        '--port', str(args.port if args.port is not None else case.port),
        '--timed-grant-proof', 'shadow', '--timed-replay-facts', '--shot-proof', 'shadow', '--record',
        '--evidence-row-limit', '1048576']


def watched(argv, cwd, env, log, receipt, receipt_path, label, deadline_seconds):
    row = {'label': label, 'command': argv, 'cwd': str(cwd), 'log': str(log), 'startedUtc': time.time()}
    receipt.setdefault('runs', []).append(row)
    with log.open('w') as output:
        process = subprocess.Popen(argv, cwd=cwd, env=env, start_new_session=True,
            stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT)
        row.update(pid=process.pid, ownedProcessGroup=process.pid, observations=[])
        save(receipt_path, receipt)
        deadline, previous, last_checkpoint = time.monotonic() + deadline_seconds, None, 0
        try:
            while process.poll() is None:
                observed = members(process.pid)
                if observed != previous:
                    row['observations'].append({'utc': time.time(), 'members': observed})
                    previous = observed
                free = shutil.disk_usage(receipt_path.parent).free
                row['minimumObservedFreeDiskBytes'] = min(free, row.get('minimumObservedFreeDiskBytes', free))
                guard = guard_failure(time.monotonic(), deadline, free)
                if guard:
                    row['guardFailure'] = guard
                    receipt.setdefault('error', guard)
                    break
                if time.monotonic() - last_checkpoint >= 5:
                    save(receipt_path, receipt); last_checkpoint = time.monotonic()
                time.sleep(.5)
        finally:
            if process.poll() is None or members(process.pid):
                residual = members(process.pid)
                # Natural termination may leave a transient child for a moment.
                if process.poll() is not None and 'guardFailure' not in row:
                    until = time.monotonic() + 5
                    while residual and time.monotonic() < until:
                        time.sleep(.2); residual = members(process.pid)
                if residual:
                    row['residualBeforeCleanup'] = residual
                    row.setdefault('guardFailure', 'owned descendant remained after parent exit')
                    try:
                        stop_owned(process)
                    except (OSError, subprocess.SubprocessError) as error:
                        row['cleanupError'] = str(error)
            row.update(exit=process.poll(), finishedUtc=time.time(),
                       remainingOwnedGroupMembers=members(process.pid), logSha256=digest(log))
            row['ownedProcessGroupClosed'] = not row['remainingOwnedGroupMembers']
            save(receipt_path, receipt)
    return row


def guard_failure(now, deadline, free):
    if free < DISK_FLOOR:
        return 'free disk below 1.5 GiB; owned group stopped, partial data preserved'
    if now >= deadline:
        return 'owned run exceeded bounded wall-clock deadline'
    return None


def disposition(campaign, joins, guarded=False):
    if guarded or campaign.get('status') == 'FAIL' or joins.get('status') == 'FAIL':
        return 'FAIL'
    if campaign.get('status') != 'PASS' or joins.get('status') != 'PASS':
        return 'INCOMPLETE'
    return 'PASS_BOUNDED_OBSERVED_JOINS'


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-worktree', type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--build-receipt', type=Path, required=True)
    parser.add_argument('--qualification', type=Path, required=True)
    parser.add_argument('--paths', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True, help='Explicit physical qualified workspace SDK')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--case', choices=CASES, required=True)
    parser.add_argument('--port', type=int)
    parser.add_argument('--execute', action='store_true', help='Only upon explicit root dispatch after slot release')
    args = parser.parse_args(argv)
    if args.dotnet.is_symlink():
        parser.error('--dotnet must be physical, not a symlink')
    if args.port is not None and not 1024 <= args.port <= 65535:
        parser.error('--port must be between 1024 and 65535')
    for name in ('source_worktree', 'build', 'build_receipt', 'qualification', 'paths', 'out', 'dotnet'):
        setattr(args, name, getattr(args, name).resolve())
    args.out.mkdir(parents=True, exist_ok=False)
    receipt_path = args.out / 'receipt.json'
    receipt = {'schema': 1, 'status': 'PREPARING', 'executed': False, 'case': args.case,
        'scope': 'actual native AI/pickups; no seeded grants; claimed-impact and general Enforce NOT_ADJUDICATED',
        'diskFloorBytes': DISK_FLOOR, 'diskBudgetTargetBytes': 6 * 1024 ** 3,
        'semanticReplayScope': 'NOT_RUN by wrapper; inventory only, actual production consumer is a separate gate'}
    receipt['unsupportedScope'] = {'soloUdp': 'UNAVAILABLE: existing runner supports2/3/4/8 clients',
        'controlled400msPumpStall': 'NOT_RUN: no application fixture option in this runner',
        'rateCapacityExhaustion': 'NOT_RUN: requires separate authentic fixture'}
    try:
        before = source_identity(args.source_worktree)
        built = json.loads(args.build_receipt.read_text()); qualified = json.loads(args.qualification.read_text())
        frozen = frozen_hashes(args.build); actual = built.get('actualLoadedClient', {})
        args.protocol = source_protocol(args.source_worktree)
        if (before['commit'] != args.source or before['dirty']
            or built.get('sourceBefore') != before or built.get('sourceAfter') != before
            or qualified.get('sourceBefore') != before or qualified.get('sourceAfter') != before
            or not str(built.get('status', '')).startswith('PASS')
            or not str(qualified.get('status', '')).startswith('PASS')
            or built.get('physicalHashesBefore') != frozen or built.get('physicalHashesAfter') != frozen
            or actual.get('sha256') != frozen['ProjectPrime.dll'] or not actual.get('mvid')
            or not actual.get('sourceQualifiedPhysicalClient') or actual.get('protocol') != args.protocol
            or qualified.get('actualLoadedClient', {}).get('protocol') != args.protocol
            or built.get('sourceConstants', {}).get('protocol') != args.protocol):
            raise ValueError('clean source/tree/qualified physical runtime identity mismatch')
        runner = args.source_worktree / 'tools/nettest/run-net2.py'
        auditor = args.source_worktree / 'tools/nettest/net04_join_evidence.py'
        if not args.paths.is_file() or not runner.is_file() or not auditor.is_file():
            raise ValueError('workspace SDK, asset paths, runner or strict joined auditor absent')
        env = {k: v for k, v in os.environ.items() if not k.startswith(('PRIME_', 'MPHREAD_', 'PROJECT_PRIME_'))}
        campaign = args.out / 'campaign'
        receipt.update(sourceBefore=before, physicalHashesBefore=frozen, runtimeInventoryBefore=inventory(args.build),
            qualifiedLoadedClient=actual, qualification={'path': str(args.qualification), 'sha256': digest(args.qualification)},
            buildReceipt={'path': str(args.build_receipt), 'sha256': digest(args.build_receipt)},
            paths={'path': str(args.paths), 'sha256': digest(args.paths)},
            sdk=sdk_identity(args.dotnet, args.source_worktree), protocol=args.protocol,
            runnerSha256=digest(runner), auditorSha256=digest(auditor),
            recordingsBefore=recordings(args.out), plannedCommand=command(args, campaign),
            startFreeDiskBytes=shutil.disk_usage(args.out).free)
        if not args.execute:
            receipt['status'] = 'PREPARED_NOT_RUN'; return 0
        if receipt['startFreeDiskBytes'] < DISK_FLOOR:
            raise ValueError('initial disk below1.5GiB floor')
        port = args.port if args.port is not None else CASES[args.case].port
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            probe.bind(('127.0.0.1', port))
        receipt.update(executed=True, status='RUNNING')
        run = watched(receipt['plannedCommand'], args.source_worktree, env, args.out / 'campaign-runner.log',
                      receipt, receipt_path, 'campaign', CASES[args.case].seconds + 35 + 240)
        for name in ('summary', 'ai-summary', 'manifest'):
            path = campaign / f'{name}.json'
            receipt[name] = json.loads(path.read_text()) if path.exists() else {'status': 'MISSING'}
        receipt['actualCommandsAndEnvironments'] = receipt['manifest'].get('commands')
        receipt['actualProcesses'] = receipt['summary'].get('processes')
        joined_path = args.out / 'strict-joined-evidence.json'
        # Even a failed campaign is audited when reports survived. No retry rewrites it.
        if (campaign / 'server.json').is_file():
            joined = watched([sys.executable, str(auditor), '--campaign', str(campaign), '--out', str(joined_path)],
                args.source_worktree, env, args.out / 'strict-joined-evidence.log', receipt, receipt_path,
                'strict-auditor', 240)
            receipt['joinedAuditRun'] = joined
        receipt['joinedEvidence'] = json.loads(joined_path.read_text()) if joined_path.exists() else {'status': 'INCOMPLETE', 'reason': 'strict audit report missing'}
        receipt['status'] = disposition(receipt['summary'], receipt['joinedEvidence'],
            guarded=any(row.get('guardFailure') or not row['ownedProcessGroupClosed'] for row in receipt.get('runs', [])))
        failures = [row['guardFailure'] for row in receipt.get('runs', []) if row.get('guardFailure')]
        if failures:
            receipt['error'] = failures[0]
        if (run['exit'] != 0 and receipt['summary'].get('status') != 'INCOMPLETE'):
            receipt['status'] = 'FAIL'
        audit_run = receipt.get('joinedAuditRun')
        if audit_run and audit_run['exit'] != {'PASS': 0, 'FAIL': 1, 'INCOMPLETE': 2}.get(receipt['joinedEvidence'].get('status')):
            receipt['status'] = 'FAIL'
        return 0 if receipt['status'].startswith('PASS') else 1 if receipt['status'] == 'FAIL' else 2
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        receipt.update(status='FAIL_PRECONDITION_OR_PARTIAL')
        if 'error' in receipt:
            receipt['secondaryError'] = str(error)
        else:
            receipt['error'] = str(error)
        return 1
    except (KeyboardInterrupt, SystemExit) as error:
        receipt.update(status='FAIL_INTERRUPTED_PARTIAL_PRESERVED', error=str(error)); return 1
    finally:
        if 'sourceBefore' in receipt:
            try:
                receipt.update(sourceAfter=source_identity(args.source_worktree), physicalHashesAfter=frozen_hashes(args.build),
                    runtimeInventoryAfter=inventory(args.build), recordingsAfter=recordings(args.out))
                receipt['sourceUnchanged'] = receipt['sourceAfter'] == receipt['sourceBefore']
                receipt['runtimeUnchanged'] = (receipt['physicalHashesAfter'] == receipt['physicalHashesBefore']
                    and receipt['runtimeInventoryAfter'] == receipt['runtimeInventoryBefore'])
                receipt['sdkAfter'] = sdk_identity(args.dotnet, args.source_worktree)
                receipt['sdkUnchanged'] = receipt['sdkAfter'] == receipt['sdk']
                if not receipt['sourceUnchanged'] or not receipt['runtimeUnchanged'] or not receipt['sdkUnchanged']:
                    receipt['status'] = 'FAIL_IDENTITY_CHANGED'
            except (OSError, ValueError, subprocess.SubprocessError) as error:
                receipt.update(status='FAIL_IDENTITY_UNREADABLE', identityError=str(error))
        save(receipt_path, receipt)
        if receipt['status'].startswith('FAIL'):
            return 1


if __name__ == '__main__':
    def interrupted(_number, _frame):
        raise KeyboardInterrupt('wrapper interrupted; owned process cleanup required')
    signal.signal(signal.SIGTERM, interrupted)
    raise SystemExit(main())
