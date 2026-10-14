#!/usr/bin/env python3
"""Verify immutable completed campaign derivatives; never execute the game."""
import argparse
import importlib.util
import json
from pathlib import Path
from chunks import read_chunks

_helpers = Path(__file__).resolve().parents[1] / 'life-evidence' / 'verify.py'
_spec = importlib.util.spec_from_file_location('portable_helpers', _helpers)
_module = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_module)
sha, sanitize, projection, require = (_module.sha, _module.sanitize,
                                     _module.projection, _module.require)


def canonical(value):
    return json.dumps(value, separators=(',', ':'), ensure_ascii=False).encode()


def portable(value, workspace):
    value = sanitize(value, workspace, Path.home())
    if isinstance(value, str):
        return value.replace('/home/runner', '${CI_HOME}')
    if isinstance(value, list):
        return [portable(v, workspace) for v in value]
    if isinstance(value, dict):
        return {k: portable(v, workspace) for k, v in value.items()}
    return value


def remove(document, path):
    parent = document
    for key in path[:-1]:
        parent = parent[key]
    return parent.pop(path[-1])


def verify(evidence, originals=None):
    manifest = json.loads((evidence / 'manifest.json').read_text())
    documents, expected = {}, {'README.md', 'manifest.json'}
    for entry in manifest['entries']:
        path = evidence / entry['derivativePath']
        require(path.resolve().is_relative_to(evidence.resolve()), 'external derivative')
        expected.add(entry['derivativePath'])
        raw = path.read_bytes()
        require(sha(raw) == entry['derivativeSHA256'] and
                len(raw) == entry['derivativeBytes'], 'derivative identity')
        require(path.suffix == '.json', 'unexpected asset/binary/text payload')
        require(not any(x in raw for x in [b'/Users/', b'/home/', b'/private/var/']), 'private path')
        document = json.loads(raw)
        if entry['kind'] == 'chunked-json':
            folder = evidence / entry['chunksDirectory']
            require(folder.resolve().is_relative_to(evidence.resolve()), 'external chunk directory')
            expected.update(str((folder / c['path']).relative_to(evidence)) for c in document['chunks'])
            document = read_chunks(document, folder)
            require(not any(x in canonical(document) for x in [b'/Users/', b'/home/', b'/private/var/']), 'private recomposed path')
            require({k: len(v) for k, v in document.items() if isinstance(v, list)} ==
                    entry['topLevelArrayCounts'], 'projected array counts')
        if entry['kind'] in ['json', 'chunked-json']:
            documents[entry.get('logicalPath', entry['derivativePath'])] = document
            require(sha(json.dumps(projection(document), separators=(',', ':')).encode()) ==
                    entry['numericAndArrayStructureSHA256'], 'numeric/array structure')
        else:
            require(entry['kind'] == 'escaped-text' and set(document) == {'text'}, 'text schema')
        if originals is not None:
            original = (originals / entry['originalRelativePath']).read_bytes()
            require(sha(original) == entry['originalSHA256'] and
                    len(original) == entry['originalBytes'], 'original identity')
            if entry['kind'] in ['json', 'chunked-json']:
                source = json.loads(original)
                for omission in entry['omittedFields']:
                    value = remove(source, omission['path'])
                    require(sha(canonical(value)) == omission['canonicalValueSHA256'] and
                            len(canonical(value)) == omission['canonicalValueBytes'] and
                            len(value) == omission['entryCount'], 'omitted complete-field identity')
                require(portable(source, originals) == document and
                        projection(source) == projection(document), 'exact preserved semantic/numeric/array equality')
            else:
                require(portable(original.decode(), originals) == document['text'], 'complete text equality')
    require({str(p.relative_to(evidence)) for p in evidence.rglob('*') if p.is_file()} == expected,
            'unlisted evidence files')
    receipt = documents['receipt.json']
    require(receipt['executed'] and receipt['sourceBefore'] == receipt['sourceAfter'] and
            receipt['sourceBefore']['commit'] == manifest['qualifiedSource'] and
            not receipt['sourceBefore']['dirty'], 'frozen source')
    require(receipt['sourceUnchanged'] and receipt['runtimeUnchanged'] and receipt['sdkUnchanged'] and
            receipt['physicalHashesBefore'] == receipt['physicalHashesAfter'], 'physical/runtime stability')
    client = receipt['qualifiedLoadedClient']
    require(client['sha256'] == manifest['qualifiedClientSHA256'] and
            client['mvid'] == manifest['qualifiedClientMVID'], 'source-qualified client')
    require(all(r['ownedProcessGroupClosed'] for r in receipt['runs']) and
            receipt['joinedAuditRun']['ownedProcessGroupClosed'], 'completed process groups')
    summary, ai = documents['campaign/summary.json'], documents['campaign/ai-summary.json']
    require(summary['status'] == ai['status'] == 'PASS' and not summary['failures'] and
            not ai['failures'], 'transport/activity scope')
    require(documents['campaign/manifest.json']['drain']['status'] == 'PASS', 'bounded drain')
    for role in ['server', 'peer-0', 'peer-1']:
        d = documents['campaign/' + role + '.json']
        require(d['Version'] == client['buildId'] and d['Protocol'] == 56 and
                d['Overflow'] == 0 and d['Transport']['QueueDrops'] == 0, 'role identity/overflow')
        require(d['ShotProof']['Mode'] == 'Shadow' and not d['TimedModifiers']['Enforcing'], 'no Enforce')
        require(len(d['Windows']) > 0 and len(d['TransportWindows']) > 0, 'complete chronology retained')
    if receipt['case'] == 'smoke2':
        server = documents['campaign/server.json']; joins = server['TimedCaseJoins']
        counts = {'Certificates': 51, 'Components': 51, 'Claims': 23, 'Terminals': 22,
                  'Payments': 24, 'Continuous': 0}
        require(all(len(joins[k]) == n and joins['Budgets'][k]['Count'] == n and
                    joins['Budgets'][k]['Dropped'] == 0 for k, n in counts.items()), 'complete join rows')
        audit = documents['strict-joined-evidence.json']
        require(receipt['status'] == audit['status'] == 'INCOMPLETE' and
                receipt['joinedAuditRun']['exit'] == 2 and not audit['failures'] and
                len(audit['incomplete']) == 2, 'strict incomplete outcome')
        require(audit['metrics']['fullyJoinedPaidCases'] == audit['metrics']['fundedOrdinaryCertificates'] == 0 and
                audit['metrics']['nativeImpactObservations'] == 23, 'no fabricated funded denominator')
        require(server['ServerSegments'][0]['DroppedSteps'] == 7 and
                server['ServerSegments'][0]['Stalls'] == 1, 'simulation negatives retained')
        require({g['Type'] for g in server['TimedGrants']} == {'Cloak'} and
                len({g['Sequence'] for g in server['TimedGrants']}) == 1, 'observed grant scope')
    print(f'PASS: {len(manifest["entries"])} immutable completed derivatives; full NET NOT_ACCEPTED.')
    if originals is not None:
        print('PASS: every original SHA/bytes, complete omission identity, exact preserved values/array order/text.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--original-workspace-root', type=Path)
    args = parser.parse_args()
    verify(args.evidence, args.original_workspace_root)
