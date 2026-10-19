"""Explicit native comparator contract for run-net2; no process controller here.

The upstream harness owns simulation, shots and rendering. This adapter only
validates/stages its declared package and translates existing command options.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

NCX_FIXTURES = {'ncx-static': ('NCX EMULATED STATIC', 'wells:powerbeam'),
                'ncx-lateral': ('NCX EMULATED LATERAL', 'lanes:powerbeam'),
                'ncx-jump': ('NCX EMULATED JUMP', 'wells:powerbeam')}


def qualified_fixture(args):
    if args.scenario not in NCX_FIXTURES: return None
    if not args.fixture_maps or not args.fixture_maps.is_dir():
        raise ValueError('NCX emulated fixtures require explicit --fixture-maps')
    room, _ = NCX_FIXTURES[args.scenario]
    if args.rooms != room or args.players not in (2, 3):
        raise ValueError('NCX emulated fixture requires its declared room and two participants plus optional observer')
    contract_path = Path(__file__).parent / 'fixtures/ncx-emulated-contract.json'
    contract = json.loads(contract_path.read_text())
    expected = {row['map']: row['sha256'] for row in contract['fixtures'].values()}
    actual = {p.relative_to(args.fixture_maps).as_posix(): digest(p)
              for p in args.fixture_maps.rglob('*.json') if p.is_file()}
    if actual != expected: raise ValueError('NCX emulated map definitions differ from the hashed fixture contract')
    return dict(contractSha256=digest(contract_path), definitions=expected, room=room,
                scope='Emulated open-box both-fire stock PowerBeam input fixture; not historical glass wells',
                roles=contract['roles'], matching='Workload declaration only; actual motion/inputs/visibility still require measurement')


def digest(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def validate_options(args):
    unsupported = []
    for name in ('drain_seconds', 'shot_timeline', 'shot_visibility', 'presentation_hz', 'confirmed_impacts', 'server_performance',
                 'server_operator', 'movement_validation_shadow', 'bomb_visibility',
                 'bomb_triangle_fixture', 'soak', 'powerups', 'server_bot',
                 'pacing_lobby_client', 'timed_replay_facts', 'observer_delay'):
        if getattr(args, name, False): unsupported.append('--' + name.replace('_', '-'))
    if args.ai_net_client is not None: unsupported.append('--ai-net-client')
    if args.timed_grant_proof != 'off': unsupported.append('--timed-grant-proof')
    if args.evidence_row_limit is not None: unsupported.append('--evidence-row-limit')
    if args.shot_proof != 'shadow': unsupported.append('--shot-proof')
    if any(hasattr(args, name) for name in ('turnover_client_slot', 'spectate_at', 'rejoin_at', 'form_hunter')):
        unsupported.append('Prime turnover/form options')
    if unsupported:
        raise ValueError('Fruity native has no qualified equivalent for ' + ', '.join(unsupported))
    if args.players not in (2, 3): raise ValueError('Fruity control currently qualifies two clients plus optional observer')
    if args.protocol != 34: raise ValueError('Pinned Fruity native package requires explicit --protocol 34')
    if args.scenario not in ('tour', 'duel', 'powerbeam', 'missile', 'shockcoil', *NCX_FIXTURES):
        raise ValueError('Scenario has no qualified Fruity native route')
    if args.fruity_hitrig and args.scenario != 'tour':
        raise ValueError('--fruity-hitrig is an explicit replacement for the tour only')
    if args.fruity_hitrig and not args.fixture_maps:
        raise ValueError('Cell fixtures require an explicit --fixture-maps directory')
    if not args.comparator_manifest: raise ValueError('--engine fruity-native requires --comparator-manifest')
    if not args.build.is_dir() or args.build.suffix != '.app':
        raise ValueError('Fruity native --build must be its declared macOS .app directory')
    qualified_fixture(args)


def qualify_package(build, manifest_path, source):
    manifest = json.loads(manifest_path.read_text())
    if manifest.get('status') != 'PASS' or manifest.get('exit') != 0:
        raise ValueError('Comparator requires a successful package-qualified native smoke')
    commit, adapter = manifest['sourceCommit'], manifest['adapterPatchSha256']
    if source != commit + '+profile-adapter:' + adapter:
        raise ValueError('--source must name the pinned commit and declared profile-adapter hash')
    binary = build / 'Contents/MacOS/FruityPrime'
    if digest(binary) != manifest['binarySha256']:
        raise ValueError('Comparator binary differs from qualified smoke package')
    patch = manifest_path.parent / 'fruity-private-profile.patch'
    if digest(patch) != adapter: raise ValueError('Comparator profile adapter patch changed')
    libraries = manifest.get('loadedNonSystemLibraries', [])
    if not libraries: raise ValueError('Comparator runtime library inventory is unavailable')
    for library in libraries:
        if digest(library['resolvedPath']) != library['sha256']:
            raise ValueError('Comparator runtime library changed: ' + library['resolvedPath'])
    return manifest


def stage_package(build, stage):
    destination = stage / 'FruityPrime.app'
    shutil.copytree(build, destination, symlinks=True)
    return destination / 'Contents/MacOS/FruityPrime'


def stage_fixture_maps(binary, definitions):
    # Pinned C++ Metadata initializes before command-line -mapdir is applied.
    # Seed its ordinary resource directory inside this run's private app copy
    # so eager registration and subsequent generation read the same recipes.
    destination = binary.parent.parent / 'Resources/maps'
    shutil.copytree(definitions, destination)
    return destination


def environment(profile, inherited=None):
    inherited = os.environ if inherited is None else inherited
    env = {key: value for key, value in inherited.items()
           if not key.startswith(('PRIME_', 'MPHREAD_', 'PROJECT_PRIME_', 'FRUITY_', 'DYLD_'))}
    env.update(FRUITY_NCX_USER_DATA=str(profile.resolve()), ALSOFT_DRIVERS='null')
    return env


def command(args, binary, maps, role, rotation=None, slot=0, faults=(), hitlog=None, shots=None):
    argv = [str(binary), '-mapdir', str(maps), '-noupdate', '-noautoupdate']
    if role == 'server':
        argv += ['-server', '-port', str(args.port), '-players', str(args.players),
                 '-nomaster', '-rotation', str(rotation), '-debuglog']
    else:
        argv += ['-netcheck', '127.0.0.1', '-port', str(args.port), '-name', role,
                 '-hunter', 'Samus', '-seconds', str(args.seconds), '-size', '480x270', *faults]
        if not args.rendered: argv.append('-headless')
        if args.record: argv.append('-recorddemo')
        rig = 'observe' if slot == 2 else args.fruity_hitrig or (
            NCX_FIXTURES[args.scenario][1] if args.scenario in NCX_FIXTURES
            else args.scenario if args.scenario != 'tour' else None)
        if rig: argv += ['-hitrig', rig]
        if shots is not None: argv += ['-shots', str(shots)]
    if hitlog is not None: argv += ['-hitlog', str(hitlog)]
    return argv


def observe_client(text):
    slots = [int(value) for value in re.findall(r'\[netcheck\].* joined slot (\d+), loading ', text)]
    snapshots = [int(value) for value in re.findall(r'packets: snapshots sent=\d+ received=(\d+)', text)]
    # Keep upstream verdict exactly; an unexercised feature tour may fail even
    # when UDP joining and snapshot transfer worked.
    verdicts = re.findall(r'^\s*RESULT: (PASS|FAIL)\b', text, flags=re.MULTILINE)
    return dict(joinedSlots=slots, snapshotsReceived=max(snapshots, default=0),
                nativeResult=verdicts[-1] if verdicts else 'UNAVAILABLE')


def scene_started(text):
    # DedicatedServer emits this only after synchronous StartSimulation and
    # ServerSim::Start have loaded the room and completed Scene::OnLoad.
    return '[server] this server runs the match itself' in text


def summarize(records, texts, listening, runner_error=None):
    peers = []
    for record in records:
        if record['name'] == 'server': continue
        row = observe_client(texts.get(record['name'], ''))
        row.update(name=record['name'], exit=record['exit'], timedOut=record['timedOut'])
        peers.append(row)
    expected = next((r.get('expectedClients') for r in records if r['name'] == 'server'), None)
    transport = bool(listening and expected is not None and len(peers) == expected
                     and all(p['joinedSlots'] and p['snapshotsReceived'] > 0 for p in peers))
    harness = bool(transport and not runner_error and all(p['exit'] == 0 and not p['timedOut']
                    and p['nativeResult'] == 'PASS' for p in peers))
    failures = ([] if not runner_error else [runner_error])
    if not transport: failures.append('Missing native client join/snapshot control evidence')
    if any(p['exit'] not in (0, None) or p['nativeResult'] == 'FAIL' for p in peers):
        failures.append('Native harness reported a nonzero exit or FAIL; raw logs retained')
    return dict(schema=1, engine='fruity-native', status='FAIL' if failures else 'INCOMPLETE',
                udpControlStatus='PASS' if transport else 'FAIL', nativeHarnessStatus='PASS' if harness else 'FAIL',
                peers=peers, failures=failures, incomplete=['Matched Prime fixtures, independent oracle, actual-view pixel evidence and A/A variance are not certified'],
                capabilities=dict(transport='native UDP; upstream join/snapshot observations',
                    drain='UNAVAILABLE: upstream has no Prime frontier/quiescence contract; termination is cleanup only',
                    shotTimeline='UNAVAILABLE', actualPixels='UNAVAILABLE: screenshots alone are not exact-shot exclusion evidence',
                    legalHitOracle='UNAVAILABLE', comparison='NOT_QUALIFIED'))
