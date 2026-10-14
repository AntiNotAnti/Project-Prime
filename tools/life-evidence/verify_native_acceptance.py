#!/usr/bin/env python3
"""Verify the bounded 7588 native acceptance evidence; never run the application."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

REPO = Path(__file__).resolve().parents[2]
PACKAGE = REPO / 'docs/evidence/native-acceptance-2026-10-14'
SOURCE = '7588d98689899c9868e8a0105b78653c7a1db50d'
ENGINE = '2689662ee38f0bac7018937d160953e3d1bbfce56dd34c5eb79abdf6ec3fb694'
ENGINE_MVID = '5e534c23-57c6-4026-a55a-f8233293a732'
ENTRY = '80ad46d74a4eb31228709dcb4909eaae624763229ae2c2ad16629eebccb1650f'
ENTRY_MVID = '0151d5f2-81b8-4566-96e3-aadf793388ee'
THIN = 'd5018508916afbbb243902a8ebb562cf6f2af0bd3cfefe9e9b6757d5acd067b2'
COUNTS = {'timed-native-effect-scene': 450, 'timed-imperialist-impact-scene': 949,
          'timed-shot-native': 6463, 'timed-shot-accepted': 343,
          'timed-shot-rescue': 5673, 'timed-shot-replay-scene': 118}
BULK = {'sourceTreeLines', 'assetManifestBefore', 'assetManifestAfter', 'assetsBefore', 'assetsAfter'}


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()


def load(path):
    return json.loads(path.read_text())


def substitutions(originals):
    # No machine-specific paths are saved in this script or its manifest.
    assets = load(originals / 'net-assets/receipt.json')['assets']
    return [(assets, '<licensed-assets>'), (str(originals.resolve()), '<candidate>'),
            (str(originals.resolve().parents[2]), '<workspace>')]


def text_projection(value, replacements):
    for old, new in replacements:
        value = value.replace(old, new)
    return re.sub(r'/Users/[^/\s]+', '<user-home>', value)


def projection(value, replacements):
    if isinstance(value, dict):
        result = {}
        for key, child in value.items():
            if key in BULK:
                # Commit the original content digest and positive denominator,
                # without publishing a retail filename manifest or source tree.
                summary = {'omittedBulk': True, 'canonicalSha256': sha(canonical(child))}
                if isinstance(child, dict) and 'fileCount' in child:
                    summary.update(fileCount=child['fileCount'], bytes=child['bytes'], sha256=child['sha256'])
                else:
                    summary['entries'] = len(child) if isinstance(child, dict) else len(child.splitlines())
                result[key] = summary
            else:
                result[text_projection(key, replacements)] = projection(child, replacements)
        return result
    if isinstance(value, list):
        return [projection(child, replacements) for child in value]
    return text_projection(value, replacements) if isinstance(value, str) else value


def transform(raw, kind, replacements):
    if kind == 'verbatim':
        return raw
    if kind == 'json-projection':
        return (json.dumps(projection(json.loads(raw), replacements), indent=2, ensure_ascii=False) + '\n').encode()
    return text_projection(raw.decode(), replacements).encode()


def selection(originals):
    receipts = ['source-native-qualification.json', 'source-runtime-build-qualification.json',
                'receipt.json', 'extra-pure-receipt.json', 'net-assets/receipt.json',
                'native-tour-42/receipt.json', 'original3-scene-consumers/receipt.json',
                'unchanged-ui-reader-source-comparison.json', 'setup-closure/disposition.json',
                'setup-closure/original-setup-receipt.json', 'official-native-gl-build-receipt.json', 'gl-regression-executor-receipt.json', 'gl-regression-attempt1/manifest.json', 'gl-regression-attempt1/parity.json', 'gl-regression-attempt1/validation.json', 'gl-regression-attempt1/00-eight-high-palette-0.json', 'gl-regression-attempt1/01-eight-high-palette-1.json']
    files = {name: 'json-projection' for name in receipts}
    for receipt, field, prefix in [('receipt.json', 'steps', ''), ('extra-pure-receipt.json', 'results', ''),
                                  ('net-assets/receipt.json', 'runs', 'net-assets/'),
                                  ('original3-scene-consumers/receipt.json', 'steps', 'original3-scene-consumers/')]:
        for step in load(originals / receipt)[field]:
            name = prefix + step['name'] + '.log'
            require((originals / name).is_file(), 'missing original log: ' + name)
            files[name] = 'text-projection'
    for arm in sorted((originals / 'native-tour-42').glob('*/arm.json')):
        require('/Users/' not in arm.read_text(), 'scalar arm contains private path')
        files[str(arm.relative_to(originals))] = 'verbatim'
        files[str(arm.with_name('arm.log').relative_to(originals))] = 'text-projection'
    for name in ['official-native-gl-build.log', 'gl-regression-executor.log', 'gl-regression-attempt1/00-eight-high-palette-0.log', 'gl-regression-attempt1/01-eight-high-palette-1.log']:
        files[name] = 'text-projection'
    return files


def capture_inventory(originals):
    captures = []
    for item in load(originals / 'gl-regression-attempt1/manifest.json')['files']:
        name = 'gl-regression-attempt1/' + item['file']
        raw = (originals / name).read_bytes()
        require(sha(raw) == item['sha256'], 'original GL capture differs from original comparison manifest')
        captures.append({'file': name, 'sha256': sha(raw), 'bytes': len(raw), 'payloadPackaged': False})
    return captures


def package(originals):
    require(not PACKAGE.exists(), 'package must be fresh; never overwrite original or prior evidence')
    PACKAGE.mkdir(parents=True)
    replacements = substitutions(originals)
    records = []
    for name, kind in sorted(selection(originals).items()):
        raw = (originals / name).read_bytes()
        derivative = transform(raw, kind, replacements)
        target = PACKAGE / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(derivative)
        record = {'file': name, 'transformation': kind,
                  'originalSha256': sha(raw), 'originalBytes': len(raw),
                  'derivativeSha256': sha(derivative), 'derivativeBytes': len(derivative)}
        if name.endswith('.json'):
            record['semanticSha256'] = sha(canonical(json.loads(derivative)))
        records.append(record)
    manifest = {'schema': 1, 'sourceCommit': SOURCE, 'originalRootRole': 'candidate-7588d986',
                'scope': 'Source/build controls, six asset-backed native fixtures, 42 genuine native pickup/shot arms, and three current-engine PassiveReplayScene readers. Scoped current15 rendered comparison/name retirement only; no performance/UDP/driver-residency acceptance.',
                'fullPlanStatus': 'NOT_ACCEPTED', 'files': records, 'captureFiles': capture_inventory(originals),
                'bulkPolicy': 'Only complete source tree and retail asset manifest objects are replaced by exact canonical hashes/counts. All other structured fields are retained; private path prefixes use role aliases. All 42 scalar arm JSON files are verbatim original bytes.'}
    (PACKAGE / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


def tour_checker():
    sys.path.insert(0, str(REPO / 'tools/nettest'))
    spec = importlib.util.spec_from_file_location('acceptance_tour', REPO / 'tools/nettest/run_native_grant_hunter_tour.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def semantics(directory):
    qualifier = load(directory / 'source-native-qualification.json')
    require(qualifier['status'] == 'PASS_AVAILABLE_SOURCE_NATIVE_QUALIFICATION' and qualifier['fullPlanStatus'] == 'NOT_ACCEPTED', 'qualification scope changed')
    require(qualifier['sourceBefore'] == qualifier['sourceAfter'] and qualifier['sourceBefore']['commit'] == SOURCE and qualifier['sourceBefore']['dirty'] == '', 'qualified source identity changed')
    require(qualifier['actualLoadedClient']['sha256'] == ENGINE and qualifier['actualLoadedClient']['mvid'] == ENGINE_MVID and qualifier['actualLoadedNettest']['sha256'] == ENTRY and qualifier['actualLoadedNettest']['mvid'] == ENTRY_MVID, 'full loaded identity changed')
    require(qualifier['allOwnedGroupsClosed'] and all(qualifier['inputsUnchanged'].values()), 'closure or input stability missing')
    require(qualifier['physicalHashesBefore'] == qualifier['physicalHashesAfter'], 'physical runtime changed')
    gates = qualifier['availableGateResults']
    require(list(gates['nativeFixtureAssertions'].values()) == list(COUNTS.values()), 'native qualification assertion denominators differ')
    require(gates['focusedPureConfigRoutes'] == 35 and gates['fullNettestPython'] == {'status': 'PASS', 'tests': 554, 'skipped': 1} and gates['glComparisonPython'] == {'status': 'PASS', 'tests': 11}, 'control denominators changed')
    baseline, extra = load(directory / 'receipt.json'), load(directory / 'extra-pure-receipt.json')
    require(len(baseline['steps']) == 19 and len(extra['results']) == 22 and baseline['focusedRoutes'] == 15 and extra['qualified'], 'control receipts incomplete')
    for step in baseline['steps'] + extra['results']:
        require(step['exit'] == 0, 'control exit failed')
    require(baseline['steps'][0]['ownedProcessGroupClosed'] is False and baseline['resume']['originalImmediateClosure'] is False and baseline['resume']['originalBuildClosureNow']['closed'] is True, 'original collector closure failure/recovery evidence missing')
    require(all(s['ownedProcessGroupClosed'] for s in baseline['steps'][1:]) and all(s['closureProbe']['closed'] and not s.get('timedOut', False) for s in extra['results']), 'control process closure missing')
    require(baseline['sourceBefore'] == baseline['sourceAfter'] == extra['sourceBefore'] == extra['sourceAfter'] == qualifier['sourceBefore'], 'control qualified source differs')
    require(extra['sourceUnchanged'] and extra['runtimeUnchanged'], 'control source/runtime drift')
    pylog = (directory / 'python-all-nettest.log').read_text()
    require('Ran 554 tests' in pylog and 'OK (skipped=1)' in pylog, 'raw Python denominator missing')
    assets = load(directory / 'net-assets/receipt.json')
    require(assets['assetManifestBefore']['entries'] > 0 and assets['actualLoadedClient']['sha256'] == ENGINE, 'native real asset/input denominator missing')
    require(assets['status'] == 'PASS' and assets['sourceBefore'] == assets['sourceAfter'] and assets['sourceBefore']['commit'] == SOURCE and assets['physicalHashesBefore'] == assets['physicalHashesAfter'] and assets['assetManifestBefore'] == assets['assetManifestAfter'], 'native source/runtime/assets drift')
    require({r['name'] for r in assets['runs']} == set(COUNTS), 'native six-fixture denominator changed')
    for run in assets['runs']:
        require(run['exit'] == 0 and run['qualified'] and run['ownedSurvivors'] == [] and run['sourceBefore'] == run['sourceAfter'] == qualifier['sourceBefore'] and run['physicalHashesBefore'] == run['physicalHashesAfter'] == qualifier['physicalHashesAfter'], 'native fixture incomplete/unstable')
        require([(m['name'], m['mvid']) for m in run['loadedModules']] == [('nettest', ENTRY_MVID), ('ProjectPrime', ENGINE_MVID)], 'native loaded identity mismatch')
        log = (directory / 'net-assets' / (run['name'] + '.log')).read_text()
        require(re.search(r'PASS: ' + str(COUNTS[run['name']]) + r'\b', log), 'native raw positive assertion denominator missing')
    checker = tour_checker()
    tour = load(directory / 'native-tour-42/receipt.json')
    require(tour['status'] == 'PASS' and len(tour['runs']) == 42 and len(tour['parityPairs']) == 21 and tour['genuineContacts'] == 42 and tour['nativeShots'] == 42 and tour['nativeFrames'] == 75656 and tour['sourceBefore'] == tour['sourceAfter'] and tour['sourceBefore']['sha'] == SOURCE and tour['assetsBefore'] == tour['assetsAfter'], 'tour incomplete/unstable')
    require(sha((REPO / 'tools/nettest/run_native_grant_hunter_tour.py').read_bytes()) == tour['inputHashes']['runner'], 'actual tour validator source differs from recorded runner')
    arms = {}
    pids = set()
    for index, run in enumerate(tour['runs']):
        require(run['status'] == 'PASS' and run['positiveReceiptValid'] and run['exitCode'] == 0 and not run['timedOut'] and run['ownership']['closed'] and run['ownership']['membersAfterClose'] == [], 'tour owned process failed')
        pids.add(run['pid'])
        label = f'{index:02d}-{run["hunter"]}-{run["item"]}-{run["mode"]}'
        raw = (directory / 'native-tour-42' / label / 'arm.json').read_bytes()
        require(sha(raw) == run['resultSha256'], 'tour parent/raw scalar arm link changed')
        data = json.loads(raw)
        require(data['entryAssemblyMvid'] == ENTRY_MVID, 'tour full entry MVID changed')
        log = (directory / 'native-tour-42' / label / 'arm.log').read_text()
        marker = f'NATIVE GRANT ARM PASS hunter={run["hunter"]} item={run["item"]} mode={run["mode"]} contacts=1 nativeShots=1 nativeFrames={run["nativeFrames"]} checks={data["checks"]}'
        require(log.count('NATIVE GRANT ARM PASS ') == 1 and marker in log, 'actual native arm log positive denominator missing')
        arm = checker.validate_arm(data, run['hunter'], run['item'], run['mode'], ENGINE, ENGINE_MVID, ENTRY)
        arms[(run['hunter'], run['item'], run['mode'])] = arm
    require(tour['assetsBefore']['fileCount'] > 0 and tour['assetsBefore']['bytes'] > 0 and tour['inputHashes']['client'] == ENGINE and tour['inputHashes']['nettest'] == ENTRY, 'tour immutable positive input identity required')
    require(len(pids) == 42 and len(arms) == 42 and sum(a['NativeFrames'] for a in arms.values()) == 75656, 'fresh complete arm matrix required')
    for pair in tour['parityPairs']:
        off, shadow = (arms[(pair['hunter'], pair['item'], mode)] for mode in checker.MODES)
        checker.compare_pair(off, shadow)
        require(pair['status'] == 'PASS' and pair['roomIncarnation'] == shadow['Spawn']['Room'] and pair['intentSha256'] == sha(bytes.fromhex(shadow['Shot']['Outcome']['IntentBytes'])) and pair['replaySha256'] == sha(shadow['Shot']['Outcome']['ReplayFacts'].encode()), 'raw exact pair link changed')
    readers = load(directory / 'original3-scene-consumers/receipt.json')
    require(readers['status'].startswith('PASS') and readers['sourceBefore'] == readers['sourceAfter'] and readers['sourceBefore']['commit'] == SOURCE and readers['physicalClientUnchanged'] and readers['physicalThinEntryUnchanged'] and readers['physicalClientSha256'] == ENGINE and readers['physicalThinEntrySha256'] == THIN and THIN != ENTRY, 'reader identity scope changed')
    require([s['name'] for s in readers['steps']] == ['build', 'authority', 'client0', 'client1'], 'three reader denominator changed')
    for step in readers['steps']:
        require(step['exit'] == 0 and step['ownedSurvivors'] == [], 'reader ownership/exit failed')
        if step['name'] != 'build':
            log = (directory / 'original3-scene-consumers' / (step['name'] + '.log')).read_text()
            require(step['loadedClientMatches'] and step['thinEntryMatches'] and ENGINE in log and ENGINE_MVID in log and THIN in log and 'MVID=c8d56952-8809-4080-b6ed-95f1e941e653' in log, 'reader actual loaded identity missing')
            final = re.findall(r'^SCENE_FINAL (.*)$', log, re.M)
            require(len(final) == 1 and re.search(r'frames=[1-9]\d*', final[0]) and re.search(r'sceneEvents=[1-9]\d*', final[0]) and 'result=Success error= warning= pendingBombs=0 activeNetSession=False authority=False' in final[0], 'reader actual Scene.Step positive/cleanup missing')
    gl_semantics(directory)
    return {'source': SOURCE, 'controls': 35, 'pythonTests': 554, 'skipped': 1,
            'nativeFixtureAssertions': list(COUNTS.values()), 'genuineContacts': 42,
            'nativeShots': 42, 'exactPairs': 21, 'nativeFrames': 75656, 'passiveReaders': 3,
            'currentRenderedComparisons': 15, 'currentPerformanceUdp': 'NOT_ACCEPTED', 'fullPlanStatus': 'NOT_ACCEPTED'}


def gl_semantics(directory):
    build = load(directory / 'official-native-gl-build-receipt.json')
    execution = load(directory / 'gl-regression-executor-receipt.json')
    for record in (build, execution):
        require(record['sourceBefore'] == record['sourceAfter'] and record['sourceBefore']['commit'] == SOURCE and record['sourceBefore']['dirty'] == '' and record['inputsUnchanged'], 'GL immutable source/input gate failed')
        require(record['qualifiedClient']['sha256'] == ENGINE and record['qualifiedClient']['mvid'] == ENGINE_MVID, 'GL qualified engine identity changed')
    require(build['status'] == 'PASS_OFFICIAL_SAME_SOURCE_PHYSICAL_NATIVE_TOOL' and build['exitCode'] == 0 and build['emittedClientPhysical'] and build['qualifiedClientSha256Before'] == build['emittedClientSha256'] == build['qualifiedClientSha256After'] == ENGINE, 'GL physical official build identity failed')
    require(build['ownership']['closed'] and build['ownership']['signals'] == [] and build['ownership']['membersAfterClose'] == [], 'GL official build closure failed')
    require(execution['status'] == 'PASS_SCOPED_EXACT_NATIVE_FIDELITY_AND_NAME_RETIREMENT' and execution['comparisonCount'] == 15 and execution['exactRGBA'] == 9 and execution['exactDepth'] == 6 and execution['allExact'], 'GL actual comparison denominator missing')
    process = execution['process']
    require(process['status'] == 'PASS' and process['exitCode'] == 0 and not process['timedOut'] and process['ownership']['closed'] and process['ownership']['signals'] == [] and process['ownership']['membersAfterClose'] == [], 'GL executor ownership failed')
    require(execution['physicalRuntimeFilesBefore'] == execution['physicalRuntimeFilesAfter'] and execution['fixtureIdentityMatches'], 'GL runtime/assets drift')
    manifest = load(directory / 'gl-regression-attempt1/manifest.json')
    require(manifest['complete'] and manifest['source'] == SOURCE and manifest['sourceDirty'] is False and manifest['binary']['clientSha256'] == ENGINE and manifest['binary']['expectedLoadedClientMvid'] == ENGINE_MVID and manifest['fixtureIdentityMatches'] and manifest['fixtureSha256'] == execution['fixtureSha256'], 'GL native manifest identity incomplete')
    require(manifest['binary']['harnessSha256'] == build['physicalRuntimeFiles']['native-render-check.dll']['sha256'] == execution['physicalRuntimeFilesBefore']['native-render-check.dll']['sha256'] and build['physicalRuntimeFiles']['native-render-check.dll']['physical'], 'GL physical harness identity changed')
    require(len(manifest['records']) == len(execution['contexts']) == 2, 'GL two independent contexts required')
    comparisons = load(directory / 'gl-regression-attempt1/parity.json')
    require(comparisons['passed'] and len(comparisons['comparisons']) == 15, 'GL raw15 comparisons required')
    expected = {f'frame-{i}-{surface}.png' for i in range(4) for surface in ('world', 'hud')} | {'frame-0-world-repeat.png'} | {f'frame-{i}-world-depth-float32.bin' for i in range(4)} | {'frame-0-world-depth-repeat-float32.bin', 'shadow-depth-float32.bin'}
    require({c['file'] for c in comparisons['comparisons']} == expected and sum(c['kind'] == 'exactRGBA' for c in comparisons['comparisons']) == 9 and sum(c['kind'] == 'exactFloat32DepthBits' for c in comparisons['comparisons']) == 6, 'GL exact named capture coverage changed')
    hashes = {f['file']: f['sha256'] for f in manifest['files']}
    for comparison in comparisons['comparisons']:
        require(comparison['passed'] and comparison['referenceSha256'] == comparison['candidateSha256'] and hashes['00-eight-high-palette-0-shots/' + comparison['file']] == comparison['referenceSha256'] and hashes['01-eight-high-palette-1-shots/' + comparison['file']] == comparison['candidateSha256'], 'GL original exact pixel/depth evidence differs')
    for index, (record, context) in enumerate(zip(manifest['records'], execution['contexts'])):
        require(record['arm'] == context['arm'] == index and record['exitCode'] == 0 and record['environment'] == {'PROJECT_PRIME_GL_PALETTE_PREP': str(index)}, 'GL OFF/ON context identity changed')
        result = load(directory / 'gl-regression-attempt1' / record['result'])
        require(result['complete'] and result['assemblyMvid'] == ENGINE_MVID and result['renderSize'] == [1920, 1080] and result['simulationFrames'] > 0 and [c['simulationFrame'] for c in result['captures']] == [240, 540, 840, 1140], 'GL real complete capture frames required')
        require(context['actualLoadedPhysicalClientMatches'] and context['assemblyMvid'] == ENGINE_MVID and context['renderSize'] == [1920, 1080] and context['simulationFrames'] == [240, 540, 840, 1140] and context['cleanup'] == result['cleanup'], 'GL actual loaded/context evidence differs')
        cleanup = result['cleanup']
        retirement = cleanup['detailedRetirement']
        positive = ('legacyListNamesBefore', 'legacyListsRecognizedBefore', 'bufferNamesBefore', 'buffersRecognizedBefore', 'vertexArrayNamesBefore', 'vertexArraysRecognizedBefore', 'programNamesBefore', 'programsRecognizedBefore', 'textureNamesBefore', 'framebufferNamesBefore', 'renderbufferNamesBefore')
        require(all(retirement[k] > 0 for k in positive) and all(v == 0 for k, v in retirement.items() if k.endswith('After')), 'GL native owned name retirement missing')
        require(all(v == 0 for k, v in cleanup.items() if k.endswith('After')) and cleanup['shadowReleased'] and cleanup['programOwnerFieldsReleased'] and cleanup['ownedTexturesBefore'] > 0 and cleanup['combinedBytesBefore'] > 0 and cleanup['retainedBytesBefore'] > 0, 'GL retained resource closure failed')
    validation = load(directory / 'gl-regression-attempt1/validation.json')
    require(validation['status'] == 'VALID' and validation['performanceDisposition'] == 'REQUIRES_REMAINING_GATES' and validation['gpuTiming'] == 'UNVERIFIED' and validation['acceptedSamplesPerArm'] == {'0': 0, '1': 0}, 'capture-only evidence must not claim performance')


def verify(directory=PACKAGE, originals=None):
    manifest = load(directory / 'manifest.json')
    require(manifest['sourceCommit'] == SOURCE and manifest['fullPlanStatus'] == 'NOT_ACCEPTED', 'manifest scope mismatch')
    files = manifest['files']
    require(len(files) == 156 and sum(f['transformation'] == 'verbatim' for f in files) == 42, 'complete portable file denominator required')
    require(len({f['file'] for f in files}) == len(files), 'duplicate manifest member')
    replacements = substitutions(originals) if originals else None
    for item in files:
        relative = Path(item['file'])
        require(not relative.is_absolute() and '..' not in relative.parts, 'unsafe package member')
        data = (directory / relative).read_bytes()
        require(sha(data) == item['derivativeSha256'] and len(data) == item['derivativeBytes'], 'derivative digest/bytes mismatch: ' + str(relative))
        require(b'/Users/' not in data and b'Library/Application Support' not in data, 'private path leaked')
        if relative.suffix == '.json':
            require(sha(canonical(json.loads(data))) == item['semanticSha256'], 'semantic hash changed')
        if item['transformation'] == 'verbatim':
            require(item['originalSha256'] == item['derivativeSha256'] and item['originalBytes'] == item['derivativeBytes'], 'verbatim evidence changed')
        if originals:
            raw = (originals / relative).read_bytes()
            require(sha(raw) == item['originalSha256'] and len(raw) == item['originalBytes'], 'immutable original changed: ' + str(relative))
            reproduced = transform(raw, item['transformation'], replacements)
            require(reproduced == data, 'original/derivative projection unequal: ' + str(relative))
    by_name = {item['file']: item for item in files}
    for receipt, field, prefix in [('receipt.json', 'steps', ''), ('extra-pure-receipt.json', 'results', ''), ('net-assets/receipt.json', 'runs', 'net-assets/'), ('original3-scene-consumers/receipt.json', 'steps', 'original3-scene-consumers/')]:
        for step in load(directory / receipt)[field]:
            require(step['logSha256'] == by_name[prefix + step['name'] + '.log']['originalSha256'], 'receipt/original log hash link changed')
    for index, run in enumerate(load(directory / 'native-tour-42/receipt.json')['runs']):
        label = f'{index:02d}-{run["hunter"]}-{run["item"]}-{run["mode"]}'
        require(run['logSha256'] == by_name['native-tour-42/' + label + '/arm.log']['originalSha256'], 'tour original log hash link changed')
    for name, field, target in [('official-native-gl-build-receipt.json', 'buildLogSha256', 'official-native-gl-build.log'), ('gl-regression-executor-receipt.json', 'officialBuildReceiptSha256', 'official-native-gl-build-receipt.json'), ('gl-regression-executor-receipt.json', 'nativeManifestSha256', 'gl-regression-attempt1/manifest.json'), ('gl-regression-executor-receipt.json', 'paritySha256', 'gl-regression-attempt1/parity.json'), ('gl-regression-executor-receipt.json', 'validationSha256', 'gl-regression-attempt1/validation.json')]:
        require(load(directory / name)[field] == by_name[target]['originalSha256'], 'GL original receipt link changed')
    require(load(directory / 'gl-regression-executor-receipt.json')['process']['logSha256'] == by_name['gl-regression-executor.log']['originalSha256'], 'GL executor original log link changed')
    gl_manifest = load(directory / 'gl-regression-attempt1/manifest.json')
    for record, context in zip(gl_manifest['records'], load(directory / 'gl-regression-executor-receipt.json')['contexts']):
        require(record['resultSha256'] == context['resultSha256'] == by_name['gl-regression-attempt1/' + record['result']]['originalSha256'] and record['logSha256'] == context['logSha256'] == by_name['gl-regression-attempt1/' + record['log']]['originalSha256'], 'GL original context/record links changed')
    for name, digest in load(directory / 'source-native-qualification.json')['supportingReceipts'].items():
        require(digest == by_name[name]['originalSha256'], 'qualification original supporting receipt changed')
    capture_files = manifest['captureFiles']
    gl_files = load(directory / 'gl-regression-attempt1/manifest.json')['files']
    require(len(capture_files) == 30 and len({f['file'] for f in capture_files}) == 30 and {f['file']: f['sha256'] for f in capture_files} == {'gl-regression-attempt1/' + f['file']: f['sha256'] for f in gl_files}, 'GL capture inventory must retain exact30original hashes')
    require(sum(f['file'].endswith('.png') for f in capture_files) == 18 and sum(f['file'].endswith('.bin') for f in capture_files) == 12 and all(f['bytes'] > 0 and f['payloadPackaged'] is False for f in capture_files), 'GL capture count/payload policy changed')
    if originals:
        require(capture_files == capture_inventory(originals), 'original GL capture hash/byte inventory changed')
    summary = semantics(directory)
    return dict(status='PASS_PORTABLE_NATIVE_EVIDENCE', files=len(files), originalsReproduced=originals is not None, **summary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, help='Create a fresh derivative from an existing external candidate directory; original files remain read-only')
    parser.add_argument('--originals', type=Path, help='Additionally verify original hashes/bytes and deterministic projection equality')
    args = parser.parse_args()
    if args.package:
        package(args.package.resolve())
    print(json.dumps(verify(originals=(args.originals or args.package)), indent=2))


if __name__ == '__main__':
    main()
