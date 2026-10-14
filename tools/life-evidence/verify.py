#!/usr/bin/env python3
"""Verify portable LIFE evidence without assets, native code or campaigns."""
import argparse
import hashlib
import json
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def projection(value, path='$'):
    if isinstance(value, dict):
        return [row for key, item in sorted(value.items())
                for row in projection(item, path + '.' + key)]
    if isinstance(value, list):
        return [(path, 'array', len(value))] + [row for index, item in enumerate(value)
                for row in projection(item, path + '[' + str(index) + ']')]
    if not isinstance(value, str):
        return [(path, type(value).__name__, value)]
    return [(path, 'string', None)]


def sanitize(value, workspace, home):
    if isinstance(value, str):
        return value.replace(str(workspace), '${WORKSPACE}').replace(str(home), '${HOME}')
    if isinstance(value, list):
        return [sanitize(item, workspace, home) for item in value]
    if isinstance(value, dict):
        return {sanitize(key, workspace, home): sanitize(item, workspace, home)
                for key, item in value.items()}
    return value


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify(evidence, original_root=None):
    manifest = json.loads((evidence / 'manifest.json').read_text())
    require(manifest['qualifiedSource'] == '38ae3ceb54b4f2ecf7c4c620a1f0542f42096305', 'source scope')
    derivatives = {}
    expected = {'manifest.json', 'README.md'}
    for entry in manifest['entries']:
        path = evidence / entry['derivativePath']
        require(path.resolve().is_relative_to(evidence.resolve()), 'outside evidence')
        expected.add(entry['derivativePath'])
        raw = path.read_bytes()
        require(sha(raw) == entry['derivativeSHA256'], 'derivative digest: ' + str(path))
        require(len(raw) == entry['derivativeBytes'], 'derivative byte count')
        require(not any(item in raw for item in [b'/Users/', b'/home/', b'/private/var/']), 'private path')
        require(path.suffix in ['.json', '.log'], 'unexpected binary/asset type')
        if entry['kind'] == 'json':
            data = json.loads(raw)
            numeric = json.dumps(projection(data), separators=(',', ':')).encode()
            require(sha(numeric) == entry['numericAndArrayStructureSHA256'], 'numeric/array change')
            derivatives[entry['derivativePath']] = data
        if original_root is not None:
            original = original_root / entry['originalRelativePath']
            original_bytes = original.read_bytes()
            require(sha(original_bytes) == entry['originalSHA256'], 'original digest')
            require(len(original_bytes) == entry['originalBytes'], 'original byte count')
            if entry['kind'] == 'json':
                original_data = json.loads(original_bytes)
                for omitted in entry['omittedTopLevelFields']:
                    value = original_data.pop(omitted['name'])
                    encoded = json.dumps(value, separators=(',', ':'), ensure_ascii=False).encode()
                    require(sha(encoded) == omitted['canonicalValueSHA256'], 'omitted-value digest')
                    require(len(encoded) == omitted['canonicalValueBytes'], 'omitted-value bytes')
                require(projection(original_data) == projection(data), 'original numeric/array inequality')
                require(sanitize(original_data, original_root, Path.home()) == data,
                        'original/derivative semantic inequality')
            else:
                require(sanitize(original_bytes.decode(), original_root, Path.home()).encode() == raw,
                        'original/derivative text inequality')
    shipped = {str(path.relative_to(evidence)) for path in evidence.rglob('*') if path.is_file()}
    require(shipped == expected, 'unlisted evidence files')
    q = derivatives['qualified-38ae/qualification-summary.json']
    require(q['source']['commit'] == manifest['qualifiedSource'] and not q['source']['dirty'], 'qualified source')
    require(q['focusedPureConfigRoutes'] == 31 and q['pythonTests'] == 123 and q['allPureQualified'], 'pure scope')
    require(q['actualLoadTotal'] == 610 and q['actualFrameTotal'] == 36600 and q['actualSaveTotal'] == 610,
            'aggregate denominators')
    require(q['sourceUnchanged'] and q['runtimeUnchanged'] and q['allOwnedGroupsClosed'], 'qualification closure')
    require(q['build']['initialClosure'] is False and q['build']['validatedResumeClosure'], 'initial closure history')
    totals = [0, 0, 0]
    for count, retries, snapshots in [(10, 1, 89), (100, 6, 719), (500, 30, 3519)]:
        data = derivatives[f'qualified-38ae/asset-{count}.json']
        receipt = derivatives[f'qualified-38ae/asset-{count}.receipt.json']
        require(data['status'] == 'PASS' and data['error'] is None, 'actual asset status')
        require(data['completedLoads'] == count and len(data['loads']) == count, 'load denominator')
        require(data['observedLoaded'] == count + 3 and data['simultaneousWorlds'] == 3, 'simultaneous denominator')
        require(data['detachedReplica'] == 'PASS' and data['liveWorlds'] == 0 and data['liveSimultaneous'] == 0,
                'eligible weak/replica result')
        require(len(data['snapshots']) == snapshots and data['observationDropped'] == 0, 'full snapshots')
        require(data['nativeGlAudio'] == 'NOT_RUN' and data['lobbyReconnectUiMedia'] == 'NOT_RUN', 'scope exclusions')
        require(data['assemblySha256'] == q['actualLoadedClient']['sha256'] and
                data['assemblyMvid'] == q['actualLoadedClient']['mvid'], 'actual asset/client identity')
        require(receipt['sourceBefore'] == receipt['sourceAfter'] and
                receipt['runtimeHashesBefore'] == receipt['runtimeHashesAfter'], 'asset source/runtime stability')
        require(receipt['replaySha256Before'] == receipt['replaySha256After'], 'recording stability')
        require(receipt['exit'] == 0 and receipt['qualified'] and not receipt['ownedSurvivors'], 'asset group closure')
        omitted = {item['name']: item for item in next(e for e in manifest['entries']
                   if e['derivativePath'] == f'qualified-38ae/asset-{count}.receipt.json')['omittedTopLevelFields']}
        require(omitted['assetManifestBefore']['canonicalValueSHA256'] ==
                omitted['assetManifestAfter']['canonicalValueSHA256'], 'asset identity equality')
        require(omitted['assetManifestBefore']['entryCount'] == 3826, 'asset count')
        require(sum(row['Retried'] for row in data['loads']) == retries, 'save retry count')
        require(all(row['Players'] == 8 and row['Entities'] > 0 and row['Steps'] == 60 and
                    row['SaveBytes'] > 0 and len(row['SaveHash']) == 64 for row in data['loads']), 'actual load/save rows')
        totals[0] += len(data['loads'])
        totals[1] += sum(row['Steps'] for row in data['loads'])
        totals[2] += len(data['snapshots'])
    negative = derivatives['negatives/ff16-asset-10.json']
    require(negative['status'] == 'FAIL' and negative['completedLoads'] == 0 and negative['observedLoaded'] == 3,
            'collision failure retained')
    require('no native collision candidates' in negative['error'], 'collision negative cause')
    require(derivatives['negatives/ff16-asset-10.receipt.json']['exit'] == 1, 'collision failure exit')
    require(derivatives['negatives/b583-build-closure.json']['originalBuildExit'] == 1 and
            derivatives['negatives/b583-build-closure.json']['ownedGroupClosed'], 'compile failure retained')
    require(derivatives['qualified-38ae/setup-closure-disposition.json']['closed'], 'collector closure')
    print(f'PASS: {len(manifest["entries"])} derivatives; {totals[0]} load rows, '
          f'{totals[1]} sequential native steps, {totals[2]} chronological snapshots; negatives retained.')
    if original_root is not None:
        print('PASS: every original SHA/byte count and sanitized numeric/array/text equality verified.')
    print('Scope: archival data verification only; no game/native process or whole-plan acceptance.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--original-workspace-root', type=Path,
                        help='Optional local immutable originals, for exact transform/equality verification.')
    args = parser.parse_args()
    verify(args.evidence, args.original_workspace_root)
