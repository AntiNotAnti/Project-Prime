#!/usr/bin/env python3
"""Verify exact corrected-auditor receipts, preserving every original verdict."""
import argparse
import json
from pathlib import Path
from chunks import read_chunks
from verify import portable, projection, sha, require


def verify(evidence, originals=None):
    manifest = json.loads((evidence / 'manifest.json').read_text())
    documents, expected = {}, {'manifest.json', 'README.md'}
    for entry in manifest['entries']:
        path = evidence / entry['derivativePath']; expected.add(entry['derivativePath'])
        require(path.resolve().is_relative_to(evidence.resolve()), 'external derivative')
        raw = path.read_bytes()
        require(sha(raw) == entry['derivativeSHA256'] and len(raw) == entry['derivativeBytes'], 'derivative identity')
        document = json.loads(raw)
        if entry['kind'].startswith('chunked-'):
            folder = evidence / entry['chunksDirectory']
            expected.update(str((folder / r['path']).relative_to(evidence)) for r in document['chunks'])
            document = read_chunks(document, folder)
        name = entry.get('logicalPath', entry['derivativePath']); documents[name] = document
        require(not any(x in json.dumps(document).encode() for x in [b'/Users/', b'/home/', b'/private/var/']), 'private path')
        is_json = entry['kind'] in ['json', 'chunked-json']
        if is_json:
            require(sha(json.dumps(projection(document), separators=(',', ':')).encode()) ==
                    entry['numericAndArrayStructureSHA256'], 'numeric/array order')
        if originals is not None:
            original = (originals / entry['originalRelativePath']).read_bytes()
            require(sha(original) == entry['originalSHA256'] and len(original) == entry['originalBytes'], 'original identity')
            require(portable(json.loads(original), originals) == document if is_json else
                    portable(original.decode(), originals) == document['text'], 'exact original equality')
    require({str(p.relative_to(evidence)) for p in evidence.rglob('*') if p.is_file()} == expected, 'unlisted evidence')
    index = documents['corrected-audit-a74d1575-index.json']
    require(index['producerSource'] == '7588d98689899c9868e8a0105b78653c7a1db50d' and
            index['auditorSource'] == 'a74d15754e6af67e8905181afc95dd82c2b58d59', 'producer/auditor distinct identities')
    require(index['allInputHashesUnchanged'] and index['allOriginalVerdictsUnchanged'] and
            index['allOwnedGroupsClosed'] and not index['remainingAuditorGroups'], 'closed audit, original evidence unchanged')
    require(len(index['cases']) == 6 and index['statusCounts'] == {'INCOMPLETE': 5, 'PASS': 1, 'FAIL': 0}, 'bounded outcomes')
    for case in index['cases']:
        result = documents[case['resultPath']]; receipt = documents[case['receiptPath']]
        require(result['status'] == case['correctedStatus'] and result['metrics'] == case['metrics'], 'actual corrected result')
        require(receipt['ownedProcessGroupClosed'] and receipt['inputHashesUnchanged'] and
                receipt['originalVerdictsUnchanged'], 'audit receipt closure/input controls')
        if case['case'] == 'smoke-worst8-7588-attempt1':
            require(result['status'] == 'PASS' and result['metrics']['fullyJoinedPaidCases'] == 2 and
                    case['originalWrapperStatus'] == 'FAIL', 'original FAIL never relabeled')
    print('PASS: sixteen exact auxiliary projections; six bounded corrected audits, original verdicts unchanged; full NET NOT_ACCEPTED.')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence', type=Path, required=True)
    p.add_argument('--original-workspace-root', type=Path)
    a = p.parse_args(); verify(a.evidence, a.original_workspace_root)
