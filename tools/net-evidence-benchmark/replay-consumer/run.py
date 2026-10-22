#!/usr/bin/env python3
"""Compile the preserved friend entry, then consume original replays in the real scene.
No game build, decoder replacement, timing claim, or full-plan acceptance claim.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
PRESERVED_PROGRAM_SHA = '06563c4aa9a61c6ba7395880e533ac06fe34994b2eca0398cf4e044d6d1c4179'
HARD_CAP = 1_000_000  # Unchanged Program.cs loop, not an EOF marker.
class SourceSetupError(ValueError):
    pass


SIX = ('ProjectPrime.dll', 'ProjectPrime.deps.json', 'ProjectPrime.runtimeconfig.json',
       'nettest.dll', 'nettest.deps.json', 'nettest.runtimeconfig.json')
ASSET_KEYS = ('AMFE0', 'AMFP0', 'A76E0', 'AMHE0', 'AMHE1', 'AMHP0', 'AMHP1', 'AMHJ0', 'AMHJ1', 'AMHK0')


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def source_identity(repo):
    return {key: subprocess.check_output(['git', *args], cwd=repo, text=True).strip()
            for key, args in [('commit', ['rev-parse', 'HEAD']), ('tree', ['rev-parse', 'HEAD^{tree}']),
                              ('dirty', ['status', '--porcelain'])]}


def validate_qualification(q, head, physical):
    if not q.get('status', '').startswith('PASS'):
        raise ValueError('Qualification is not passing')
    before = q['sourceBefore']
    if before['commit'] != head or before != q['sourceAfter'] or before['dirty']:
        raise ValueError('Require exact clean, unchanged qualified source')
    if not q.get('allOwnedGroupsClosed'):
        raise ValueError('Qualification owned groups are not closed')
    if physical != q['physicalHashesAfter']:
        raise ValueError('All six physical runtime hashes must match qualification')
    client, fixture = q['actualLoadedClient'], q['actualLoadedNettest']
    if not client.get('sourceQualifiedPhysicalClient') or not fixture.get('sourceQualifiedPhysicalFixture'):
        raise ValueError('Qualification must bind actual full client and fixture')
    if client['sha256'] != physical['ProjectPrime.dll'] or fixture['sha256'] != physical['nettest.dll']:
        raise ValueError('Qualified assembly hashes disagree')
    for mvid in (client['mvid'], fixture['mvid'], q['nettestMvid']):
        uuid.UUID(mvid)
    if fixture['mvid'].lower() != q['nettestMvid'].lower():
        raise ValueError('Qualified full fixture MVID mismatch')
    return before


def parse_scene(raw, client_sha, client_mvid, entry_sha, max_frames):
    # Each marker is emitted exactly once by the preserved Program.cs scene path.
    clients = re.findall(r'^IDENTITY ClientPath=(.+) SHA256=([0-9a-f]{64}) MVID=([0-9a-f-]{36})$', raw, re.M)
    entries = re.findall(r'^IDENTITY EntrySHA256=([0-9a-f]{64}) MVID=([0-9a-f-]{36})$', raw, re.M)
    if len(clients) != 1 or clients[0][1:] != (client_sha, client_mvid.lower()):
        raise SourceSetupError('Loaded client SHA/MVID mismatch or missing')
    if len(entries) != 1 or entries[0][0] != entry_sha:
        raise SourceSetupError('Loaded thin entry identity mismatch or missing')
    uuid.UUID(entries[0][1])
    if raw.count('SESSION_BEFORE active=False authority=False') != 1:
        raise ValueError('Scene consumer entered with live session/authority')
    if 'SCENE_EXCEPTION' in raw:
        raise ValueError('Actual scene reconstruction/step exception')
    finals = re.findall(r'^SCENE_FINAL frames=(\d+) sceneEvents=(\d+) certs=(\d+) scopes=(\d+) current=(\d+) result=(\w+) error=(.*?) warning=(.*?) pendingBombs=(\d+) activeNetSession=(\w+) authority=(\w+)$', raw, re.M)
    if len(finals) != 1:
        raise ValueError('Missing or duplicate actual scene final marker')
    f = finals[0]
    frames = int(f[0])
    if not 0 < frames < HARD_CAP or frames > max_frames:
        raise ValueError('Frame bound reached/exceeded; success result is not EOF proof')
    if f[5] != 'Success' or f[6] or int(f[8]) != 0 or f[9:] != ('False', 'False'):
        raise ValueError('Scene final failed success/Bomb0/no-live-session/no-authority contract')
    return {'frames': frames, 'sceneEvents': int(f[1]), 'certificateFactsObserved': int(f[2]),
            'scopeFactsObserved': int(f[3]), 'currentFrame': int(f[4]), 'warning': f[7],
            'pendingBombs': 0, 'eof': 'Natural scene.Step false before preserved hard cap',
            'loadedClient': {'path': clients[0][0], 'sha256': client_sha, 'mvid': client_mvid},
            'loadedThinEntry': {'sha256': entries[0][0], 'mvid': entries[0][1]}}


def paths_profile(template, profile):
    lines = Path(template).read_text().splitlines()
    if not lines or '=' in lines[0]:
        raise ValueError('Paths template requires its version line')
    roots = {}
    for line in lines[1:]:
        key, sep, value = line.partition('=')
        if not sep or key not in (*ASSET_KEYS, 'Export') or key in roots:
            raise ValueError('Unknown/duplicate Paths key; credentials/settings are not copied')
        if key != 'Export':
            if value and (not Path(value).is_absolute() or not Path(value).is_dir()):
                raise ValueError('Asset roots must be existing absolute directories')
            roots[key] = value
    if not any(roots.values()):
        raise ValueError('No actual asset root in Paths template')
    sanitized = '\n'.join([lines[0], *(f'{k}={roots.get(k, "")}' for k in ASSET_KEYS),
                           f'Export={profile / "exports"}']) + '\n'
    return sanitized, roots


def asset_manifest(roots):
    # Stream bytes; do not duplicate assets or read large replays into memory.
    return {key: {str(p.relative_to(root)): sha(p) for p in sorted(Path(root).rglob('*')) if p.is_file()}
            for key, root in roots.items() if root}


def replay_header(path):
    with Path(path).open('rb') as f:
        header = f.read(6)
    if len(header) != 6 or header[:4] not in (b'PPDM', b'FPDM') or header[4] not in range(1, 7):
        raise ValueError('Replay header is not a current/legacy supported input')
    return {'magic': header[:4].decode('ascii'), 'format': header[4], 'protocol': header[5]}


def group_members(pid):
    rows = subprocess.check_output(['ps', '-axo', 'pid=,ppid=,pgid=,stat=,command='], text=True).splitlines()
    return [r.strip() for r in rows if len(r.split()) >= 4 and r.split()[2] == str(pid)]


def owned_run(command, cwd, env, log, timeout):
    result = {'command': list(map(str, command)), 'cwd': str(cwd), 'startedUtc': utc()}
    with log.open('x') as stream:
        proc = subprocess.Popen(command, cwd=cwd, env=env, stdout=stream,
                                stderr=subprocess.STDOUT, start_new_session=True)
        result['pid'] = result['pgid'] = proc.pid
        result['initialOwnedGroup'] = group_members(proc.pid)
        try:
            result['exit'] = proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            result['timeout'] = True
            os.killpg(proc.pid, signal.SIGTERM)  # This still-live process owns our new session.
            try:
                result['exit'] = proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(proc.pid, signal.SIGKILL)
                result['exit'] = proc.wait(timeout=5)
    # Record any descendants. Never signal a recycled PID after its leader is reaped.
    end = time.monotonic() + 5
    while group_members(proc.pid) and time.monotonic() < end:
        time.sleep(.1)
    result.update(ownedSurvivors=group_members(proc.pid), finishedUtc=utc(), logSha256=sha(log))
    return result


def project(source, runtime, target_framework):
    root = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(root, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': target_framework, 'Nullable': 'enable',
                       'AssemblyName': 'nettest', 'EnableDefaultCompileItems': 'false', 'UseAppHost': 'false',
                       'UseSharedCompilation': 'false', 'EnableNETAnalyzers': 'false', 'Deterministic': 'true'}.items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(root, 'ItemGroup')
    ET.SubElement(items, 'Compile', Include=str(source))
    # All managed refs use the qualified runtime resolver, excluding its full fixture.
    for path in sorted(runtime.glob('*.dll')):
        if path.name == 'nettest.dll':
            continue
        ref = ET.SubElement(items, 'Reference', Include=path.stem)
        ET.SubElement(ref, 'HintPath').text = str(path)
        ET.SubElement(ref, 'Private').text = 'true'
    return ET.ElementTree(root)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('repo', 'runtime', 'sdk', 'qualification', 'paths', 'out'):
        p.add_argument('--' + name, required=True, type=lambda s: Path(s).resolve())
    p.add_argument('--head', required=True)
    p.add_argument('--replay', required=True, nargs='+', type=lambda s: Path(s).resolve())
    p.add_argument('--max-frames', type=int, default=120_000)
    p.add_argument('--timeout', type=float, default=1800)
    args = p.parse_args(argv)
    if not 1 <= len(args.replay) <= 9 or not 0 < args.max_frames < HARD_CAP or args.timeout < 1:
        p.error('Require 1..9 inputs, max-frames 1..999999 and positive timeout')
    if args.out.exists() or args.out == args.repo or args.repo in args.out.parents or args.out == HERE.parents[2] or HERE.parents[2] in args.out.parents or args.out == args.runtime or args.runtime in args.out.parents:
        p.error('Require a fresh output outside source/tool checkouts')
    args.out.mkdir(parents=True)
    receipt = {'schema': 1, 'status': 'SOURCE_SETUP_IN_PROGRESS', 'fullPlanStatus': 'NOT_ACCEPTED',
               'maxFramesAcceptanceBound': args.max_frames, 'preservedHelperHardStepBound': HARD_CAP, 'perProcessTimeoutSeconds': args.timeout,
               'startedUtc': utc(), 'command': ['python3', str(Path(__file__).resolve()), *(argv or __import__('sys').argv[1:])],
               'steps': [], 'scope': 'Actual original-file PassiveReplayScene.Step; no rendered fidelity, live authority, performance or award-count claim'}
    def save():
        (args.out / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    stage = 'SOURCE_SETUP_FAIL'
    save()
    try:
        tool_repo = HERE.parents[2]
        receipt['toolSourceBefore'] = source_identity(tool_repo)
        if receipt['toolSourceBefore']['dirty']:
            raise ValueError('Committed clean tool source required')
        receipt['sdkSha256'] = sha(args.sdk)
        if sha(HERE / 'Program.cs') != PRESERVED_PROGRAM_SHA:
            raise ValueError('Historical Program.cs changed; preserved scene-loop proof must be reviewed')
        receipt['toolHashesBefore'] = {str(f.relative_to(HERE)): sha(f) for f in HERE.glob('*') if f.is_file()}
        receipt['sourceBefore'] = source_identity(args.repo)
        physical = {}
        for name in SIX:
            f = args.runtime / name
            if f.is_symlink() or not f.is_file():
                raise ValueError('Qualified six entries must be physical files: ' + name)
            physical[name] = sha(f)
        q = json.loads(args.qualification.read_text())
        expected_source = validate_qualification(q, args.head, physical)
        if receipt['sourceBefore'] != expected_source:
            raise ValueError('Frozen checkout differs from qualification source')
        receipt.update(physicalHashesBefore=physical, qualifiedClient=q['actualLoadedClient'],
                       qualifiedFullFixture=q['actualLoadedNettest'], qualificationSha256=sha(args.qualification),
                       fullFixtureMvidProvenance='Actual loaded full fixture in supplied qualification, hash rebound here; not executed by this thin entry')
        step_source = args.repo / 'src/MphRead/Mods/Network/PassiveReplayScene.cs'
        step = step_source.read_text().split('public bool Step()', 1)[1].split('internal void StepLive', 1)[0]
        normalized_step = re.sub(r'\s+', '', step)
        if normalized_step != ('{ObjectDisposedException.ThrowIf(_disposed,this);if(Session.AtEnd)returnfalse;'
                               'Session.PumpFrame();if(Session.LastResult!=ReplayOpenResult.Success)thrownewInvalidDataException(Session.LastError);'
                               'Scene.StepReplica();HasStepped=true;if(!Session.IsWarming)Session.Transport.AfterFrame();returntrue;}'):
            raise ValueError('Qualified Step EOF contract changed; review rather than infer EOF')
        receipt['qualifiedSceneStepSourceSha256'] = sha(step_source)
        sanitized, roots = paths_profile(args.paths, args.out / 'profile-unused')
        if any(Path(root) == args.out or Path(root) in args.out.parents for root in roots.values() if root):
            raise ValueError('Output may not mutate an asset root')
        receipt['pathsTemplateSha256'] = sha(args.paths)
        receipt['assetsBefore'] = asset_manifest(roots)
        receipt['replaysBefore'] = [{'path': str(f), 'sha256': sha(f), 'bytes': f.stat().st_size, **replay_header(f)} for f in args.replay]
        receipt['managedReferenceHashesBefore'] = {f.name: sha(f) for f in args.runtime.glob('*.dll')}
        config = json.loads((args.runtime / 'nettest.runtimeconfig.json').read_text())
        framework = config['runtimeOptions']['tfm']
        buildsource = args.out / 'source'
        buildsource.mkdir()
        shutil.copyfile(HERE / 'Program.cs', buildsource / 'Program.cs')
        receipt['preservedProgramSha256'] = sha(buildsource / 'Program.cs')
        project(buildsource / 'Program.cs', args.runtime, framework).write(buildsource / 'consumer.csproj', encoding='utf-8', xml_declaration=True)
        receipt['generatedProjectSha256'] = sha(buildsource / 'consumer.csproj')
        env = {k: v for k, v in os.environ.items() if not k.startswith(('PRIME_', 'PROJECT_PRIME_', 'MPHREAD_', 'DOTNET_', 'COMPlus_'))}
        env.update(DOTNET_ROOT=str(args.sdk.parent), DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
        thin = args.out / 'thin-runtime'
        save()
        build = owned_run([str(args.sdk), 'build', str(buildsource / 'consumer.csproj'), '-c', 'Release', '-o', str(thin), '-m:1', '-p:UseSharedCompilation=false', '-nodeReuse:false'], args.out, env, args.out / 'build.log', args.timeout)
        receipt['steps'].append({'name': 'build', **build})
        save()
        if build['exit'] or build['ownedSurvivors'] or build.get('timeout'):
            raise ValueError('Thin build failed or owned group remains open')
        if (thin / 'ProjectPrime.dll').is_symlink() or sha(thin / 'ProjectPrime.dll') != physical['ProjectPrime.dll']:
            raise ValueError('Thin runtime must contain the identical physical qualified engine')
        entry_sha = sha(thin / 'nettest.dll')
        if entry_sha == physical['nettest.dll']:
            raise ValueError('Thin entry aliases full fixture')
        # Preserve generated deps/config; link only missing resolver/native payloads.
        for f in args.runtime.iterdir():
            if not (thin / f.name).exists():
                (thin / f.name).symlink_to(f.resolve(), target_is_directory=f.is_dir())
        receipt['thinPhysicalHashesBefore'] = {f.name: sha(f) for f in thin.glob('*') if f.is_file() and not f.is_symlink()}
        stage = 'SEMANTIC_EOF_FAIL'
        receipt['status'] = 'SEMANTIC_CONSUMPTION_IN_PROGRESS'
        save()
        for index, replay in enumerate(args.replay):
            profile = args.out / f'profile-{index:02d}'
            profile.mkdir()
            sanitized, _ = paths_profile(args.paths, profile)
            (profile / 'paths.txt').write_text(sanitized)
            profile_paths_before = sha(profile / 'paths.txt')
            runenv = dict(env, PROJECT_PRIME_USER_DATA=str(profile), ALSOFT_DRIVERS='null')
            log = args.out / f'replay-{index:02d}.log'
            run = owned_run([str(args.sdk), str(thin / 'nettest.dll'), str(replay), '--scene'], profile, runenv, log, args.timeout)
            row = {'name': f'replay-{index:02d}', 'input': str(replay), 'pathsSha256Before': profile_paths_before, 'pathsSha256After': sha(profile / 'paths.txt'), **run}
            receipt['steps'].append(row)
            save()
            if run['exit'] or run['ownedSurvivors'] or run.get('timeout'):
                if not re.search(r'^SCENE_(PROGRESS|FINAL|EXCEPTION)', log.read_text(errors='replace'), re.M):
                    stage = 'SOURCE_SETUP_FAIL'
                raise ValueError('Scene process failed/timed out or owned group remains open')
            try:
                row['scene'] = parse_scene(log.read_text(errors='replace'), physical['ProjectPrime.dll'], q['actualLoadedClient']['mvid'], entry_sha, args.max_frames)
            except SourceSetupError:
                stage = 'SOURCE_SETUP_FAIL'
                raise
            except ValueError:
                if 'SCENE_FINAL' not in log.read_text(errors='replace') and 'SCENE_EXCEPTION' not in log.read_text(errors='replace'):
                    stage = 'SOURCE_SETUP_FAIL'  # Loader/Paths/bootstrap failed before the scene contract.
                raise
            if row['scene']['loadedThinEntry']['mvid'].lower() == q['nettestMvid'].lower():
                raise ValueError('Thin MVID aliases full fixture')
            save()
        receipt.update(status='PASS_FULL_FILE_SCENE_CONSUMPTION', thinEntrySha256=entry_sha)
    except Exception as ex:
        receipt.update(status=stage, originalFailureStage=stage, error=f'{type(ex).__name__}: {ex}')
    finally:
        # These comparisons also run on semantic/setup failure; raw evidence stays intact.
        try:
            receipt['sourceAfter'] = source_identity(args.repo)
            receipt['physicalHashesAfter'] = {n: sha(args.runtime / n) for n in SIX}
            receipt['replaysAfter'] = [{'path': str(f), 'sha256': sha(f)} for f in args.replay]
            if 'assetsBefore' in receipt:
                receipt['assetsAfter'] = asset_manifest(roots)
            receipt['toolSourceAfter'] = source_identity(tool_repo)
            receipt['pathsTemplateSha256After'] = sha(args.paths)
            receipt['qualificationSha256After'] = sha(args.qualification)
            receipt['sdkSha256After'] = sha(args.sdk)
            receipt['managedReferenceHashesAfter'] = {f.name: sha(f) for f in args.runtime.glob('*.dll')}
            if 'thinPhysicalHashesBefore' in receipt:
                receipt['thinPhysicalHashesAfter'] = {f.name: sha(f) for f in thin.glob('*') if f.is_file() and not f.is_symlink()}
            receipt['toolHashesAfter'] = {str(f.relative_to(HERE)): sha(f) for f in HERE.glob('*') if f.is_file()}
            receipt['inputsUnchanged'] = (receipt.get('sdkSha256') == receipt['sdkSha256After'] and receipt.get('qualificationSha256') == receipt['qualificationSha256After'] and all(row['pathsSha256Before'] == row['pathsSha256After'] for row in receipt['steps'] if 'pathsSha256Before' in row) and receipt.get('toolSourceBefore') == receipt['toolSourceAfter'] and receipt.get('pathsTemplateSha256') == receipt['pathsTemplateSha256After'] and receipt.get('managedReferenceHashesBefore') == receipt['managedReferenceHashesAfter'] and receipt.get('thinPhysicalHashesBefore') == receipt.get('thinPhysicalHashesAfter') and receipt.get('sourceBefore') == receipt['sourceAfter'] and receipt.get('physicalHashesBefore') == receipt['physicalHashesAfter'] and receipt.get('assetsBefore') == receipt.get('assetsAfter') and receipt.get('toolHashesBefore') == receipt['toolHashesAfter'] and all(a['sha256'] == b['sha256'] for a, b in zip(receipt.get('replaysBefore', []), receipt['replaysAfter'])))
            receipt['allOwnedGroupsClosed'] = all(not s['ownedSurvivors'] for s in receipt['steps'])
            if receipt['status'].startswith('PASS') and not (receipt['inputsUnchanged'] and receipt['allOwnedGroupsClosed']):
                receipt.update(status='PROVENANCE_FAIL', error='Source/runtime/assets/replays/tool changed or owned groups remain open')
        except Exception as ex:
            receipt.update(status='PROVENANCE_FAIL', finalizationError=str(ex))
        receipt['finishedUtc'] = utc()
        save()
    print(receipt['status'], flush=True)
    return 0 if receipt['status'].startswith('PASS') else 1


if __name__ == '__main__':
    raise SystemExit(main())
