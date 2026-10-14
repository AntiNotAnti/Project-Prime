#!/usr/bin/env python3
"""Verify immutable native LIFE evidence; never run game/native code."""
import argparse
import json
from pathlib import Path
from verify import projection, require, sanitize, sha


def remove(document, path):
    parent = document
    for key in path[:-1]:
        parent = parent[key]
    return parent.pop(path[-1])


def verify(evidence, original_root=None, check_artifacts=False):
    manifest = json.loads((evidence / 'manifest.json').read_text())
    require(manifest['qualifiedSource'] == '88f9d29e58d0cf6254b2d33e682955bd432d0bd2', 'historical source')
    expected = {'manifest.json', 'README.md'}
    data = {}
    for entry in manifest['entries']:
        relative = entry['derivativePath']
        path = evidence / relative
        require(path.resolve().is_relative_to(evidence.resolve()), 'outside evidence')
        expected.add(relative)
        raw = path.read_bytes()
        require(sha(raw) == entry['derivativeSHA256'] and len(raw) == entry['derivativeBytes'], 'derivative identity')
        require(path.suffix in ['.json', '.log'], 'binary/profile/asset type')
        require(not any(x in raw for x in [b'/Users/', b'/home/', b'/private/var/']), 'private path')
        if entry['kind'] == 'json':
            document = json.loads(raw)
            data[relative] = document
            numeric = json.dumps(projection(document), separators=(',', ':')).encode()
            require(sha(numeric) == entry['numericAndArrayStructureSHA256'], 'numeric/array structure')
        if original_root is not None:
            original = (original_root / entry['originalRelativePath']).read_bytes()
            require(sha(original) == entry['originalSHA256'] and len(original) == entry['originalBytes'], 'original identity')
            if entry['kind'] == 'json':
                original_data = json.loads(original)
                for omission in entry['omittedFields']:
                    value = remove(original_data, omission['path'])
                    encoded = json.dumps(value, separators=(',', ':'), ensure_ascii=False).encode()
                    require(sha(encoded) == omission['canonicalValueSHA256'] and
                            len(encoded) == omission['canonicalValueBytes'], 'omitted bulk identity')
                require(projection(original_data) == projection(document), 'original numeric/array equality')
                require(sanitize(original_data, original_root, Path.home()) == document, 'original semantic equality')
            elif entry['kind'] == 'escaped-text':
                require(sanitize(original.decode(), original_root, Path.home()) == json.loads(raw)['text'],
                        'original escaped-text equality')
            else:
                require(sanitize(original.decode(), original_root, Path.home()).encode() == raw, 'original text equality')
    require({str(p.relative_to(evidence)) for p in evidence.rglob('*') if p.is_file()} == expected, 'unlisted files')
    qualification = data['qualification-and-packaging/qualification-summary.json']
    client = qualification['actualLoadedClient']
    require(client['sha256'] == '785d507a682811f31d4262ab39ac8427074d4fc3563c8a9cdc863787f53cb009' and
            client['mvid'] == '896c230f-be96-4e23-b0cc-3fb2824d1a15', 'qualified engine identity')
    require(qualification['baseline'] == 'PASS15+123Python' and qualification['extraPureConfig'] == 'PASS16', 'local scope')
    tools = data['qualification-and-packaging/full-tools-normal1-build-receipt.json']
    require(tools['status'] == 'PASS_COMPLETE_OFFICIAL_TOOL_BUILD_EXACT_ENGINE', 'official complete tool source')
    for tool in tools['tools']:
        require(tool['exit'] == 0 and tool['fullSourceUnchanged'] and tool['sourceFiles'], 'source tool build')
        require(tool['emittedClientSha256'] == client['sha256'] and tool['closureDisposition']['closed'], 'tool client/closure')
        require(tool['physicalHashes']['ProjectPrime.dll'] == client['sha256'], 'physical tool client')
    startup = data['qualification-and-packaging/tool-normal1-startup/receipt.json']
    require(all(r['qualified'] and r['exit'] == 0 and not r['ownedSurvivors'] and
                r['loadedModules'][0]['mvid'] == client['mvid'] for r in startup['runs']), 'actual tool loaded module')
    for attempt in ['life-native-attempt1', 'life-native-attempt2']:
        runs = data[attempt + '/manifest.json']['runs']
        require(runs[0]['status'] == 'FAIL' and runs[0]['exitCode'] == 1, 'loader negative retained')
        require(all(r['status'] == 'NOT_RUN' for r in runs[1:]), 'no false later stage pass')
    stage3 = {r['name']: r for r in data['life-native-attempt3/manifest.json']['runs']}
    for name in ['native-arms', 'replica', 'export', 'music']:
        require(stage3[name]['status'] == 'PASS' and stage3[name]['exitCode'] == 0 and
                stage3[name]['ownership']['closed'], 'actual stage pass/closure')
    require(stage3['shell']['status'] == 'FAIL' and stage3['shell']['exitCode'] == 0 and
            'cycles=' in stage3['shell']['error'], 'mirrored-stream oracle negative')
    require(stage3['respawn']['status'] == 'NOT_RUN', 'attempt3 respawn scope')
    stage4 = data['life-native-attempt4/manifest.json']['runs']
    require(stage4[0]['status'] == 'FAIL' and stage4[0]['exitCode'] == 1 and
            stage4[1]['status'] == 'NOT_RUN', 'modal negative retained')
    respawn = data['life-native-attempt5-respawn/manifest.json']['runs'][0]
    require(respawn['status'] == 'PASS' and respawn['exitCode'] == 0 and respawn['ownership']['closed'], 'separate respawn pass')
    require('cycles=9/9' in respawn['acceptance']['summary'] and 'failures=0' in respawn['acceptance']['summary'], 'respawn denominator')
    classification = data['qualification-and-packaging/shell-modal-preview-classification.json']
    require('no actual latest-main shell campaign' in classification['scope'], 'source-only classification scope')
    require(all(len(set(blobs.values())) == 1 for blobs in classification['mainMatchedBlobs'].values()), 'matched source blobs')
    for arm in ['00-combined', '01-map-gl', '02-studio', '03-audio']:
        document = data[f'life-native-attempt3/native-arms/{arm}.json']
        require(document['complete'] and document['cyclesCompleted'] == 10, 'completed resource cycles')
        if 'cyclesRequested' in document:
            require(document['cyclesRequested'] == 10, 'requested resource cycles')
        require(document.get('assemblyMvid', document['manifest']['assemblyMvid']) == client['mvid'], 'actual resource engine')
        if 'ownedResourcesAfterEveryUnloadZero' in document:
            require(document['ownedResourcesAfterEveryUnloadZero'], 'owned resource retirement')
    captures = data['capture-artifacts.json']
    require(captures['counts'] == {'png': 266, 'mp4': 18, 'resourceReadbackPairs': 20,
                                  'resourceEqualPixelPairs': 20, 'resourceEqualFloatDepthPairs': 20}, 'artifact counts')
    require(all(r['pixelsEqual'] and r['floatDepthEqual'] and
                r['referencePixelsSHA256'] == r['candidatePixelsSHA256'] and
                r['referenceFloatDepthSHA256'] == r['candidateFloatDepthSHA256'] for r in captures['resourcePairs']), 'readback parity')
    require(len(captures['replicaFilePairs']) == 19 and all(r['samePNGBytes'] for r in captures['replicaFilePairs']), 'PNG encoding observations')
    if check_artifacts:
        require(original_root is not None, '--check-artifacts needs --original-workspace-root')
        for artifact in captures['files']:
            raw = (original_root / artifact['path']).read_bytes()
            require(sha(raw) == artifact['sha256'] and len(raw) == artifact['bytes'], 'existing capture identity')
    failure = data['negatives-f24/build-failure-closure.json']
    require(failure['originalBuildExit'] == 1 and failure['closed'], 'new fixture compile negative')
    print(f'PASS: {len(manifest["entries"])} portable derivatives; four resource arms, replica/export/music and separate respawn; all actual negatives retained.')
    if original_root is not None:
        print('PASS: original SHA/bytes and exact sanitized semantic/numeric/array/text equality.')
    if check_artifacts:
        print('PASS: 266 existing PNG/18 MP4 file hashes and byte counts; no artifact copied.')
    print('Scope: immutable data verification only; no game process, FPS, AGX boundedness, new-source UI or full-plan acceptance.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--original-workspace-root', type=Path)
    parser.add_argument('--check-artifacts', action='store_true', help='Optional existing-file hash check only.')
    args = parser.parse_args()
    verify(args.evidence, args.original_workspace_root, args.check_artifacts)
