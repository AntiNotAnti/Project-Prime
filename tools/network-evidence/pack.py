#!/usr/bin/env python3
"""Project an already-closed campaign into bounded immutable portable evidence."""
import argparse
import gc
import json
import re
from pathlib import Path
from chunks import write_chunks, encode, digest
from verify import portable, projection, remove
from compact_projection import project_role, positive_and_funded_keys, project_log

OMISSIONS = [['Forms'], ['SlowStates'], ['States'], ['Bombs', 'Rows']]
COMMON = ['receipt.json', 'campaign-runner.log', 'monitor-checkpoints.jsonl',
          'campaign/manifest.json', 'campaign/rotation.txt']
COMPLETE = ['strict-joined-evidence.json', 'strict-joined-evidence.log',
            'final-assessment.json', 'result.md', 'campaign/summary.json',
            'campaign/ai-summary.json', 'campaign/drain.release.json',
            'campaign/drain.server-release.json', 'campaign/drain.server-release.json.quiesce']
INTERRUPTED = ['interruption-recovery.json', 'partial-file-inventory.json']


def pack(workspace, source, destination, interrupted=False):
    if destination.exists():
        raise ValueError('refuse replacing existing immutable package')
    receipt = json.loads((source / 'receipt.json').read_text())
    if interrupted:
        recovery = json.loads((source / 'interruption-recovery.json').read_text())
        if recovery['liveOwnedMembers'] or recovery['status'] != 'INCOMPLETE_EXTERNALLY_INTERRUPTED':
            raise ValueError('interrupted attempt has not been recovered/closed')
    elif (receipt['status'] == 'RUNNING' or not receipt.get('sourceUnchanged') or
          not all(r['ownedProcessGroupClosed'] for r in receipt['runs'])):
        raise ValueError('campaign not finalized/closed')
    files = COMMON + (INTERRUPTED if interrupted else COMPLETE)
    roles = ['server'] + sorted(p.name[:-11] for p in (source / 'campaign').glob('peer-*.drain.json'))
    files += ['campaign/' + role + suffix for role in roles
              for suffix in (['.drain.json', '.log'] if interrupted else ['.json', '.drain.json', '.log'])]
    if not all((source / name).is_file() for name in files):
        raise ValueError('missing declared original before writing package')
    destination.mkdir(parents=True)
    entries = []
    qualifying_keys = []
    chronology = None
    for name in files:
        original = source / name
        if not original.is_file():
            raise ValueError('missing declared original: ' + name)
        raw = original.read_bytes()
        entry = {'originalRelativePath': str(original.relative_to(workspace)),
                 'originalSHA256': digest(raw), 'originalBytes': len(raw), 'omittedFields': []}
        is_json = name.endswith('.json') or name.endswith('.quiesce')
        if is_json:
            document = json.loads(raw)
            if re.fullmatch(r'campaign/(server|peer-\d+)\.json', name):
                if receipt['case'] == 'long2':
                    if name == 'campaign/server.json':
                        qualifying_keys = positive_and_funded_keys(document)
                        funded = next(c['Shot']['Key'] for c in document['TimedCaseJoins']['Certificates']
                                      if c['Shot']['Funding'] is not None)
                        chronology = {'life': [funded[k] for k in ['AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId']],
                                      'firstShot': 1646, 'lastShot': 1676}
                    entry['roleProjection'] = project_role(document, qualifying_keys, chronology)
                for path in OMISSIONS:
                    omitted = remove(document, path)
                    value = json.dumps(omitted, separators=(',', ':'), ensure_ascii=False).encode()
                    entry['omittedFields'].append({'path': path, 'canonicalValueSHA256': digest(value),
                        'canonicalValueBytes': len(value), 'entryCount': len(omitted),
                        'reason': 'Complete bulk field omitted; all source/payment/shot/fact rows and chronological timing windows retained.'})
                    del omitted, value
            document = portable(document, workspace)
            entry['numericAndArrayStructureSHA256'] = digest(json.dumps(projection(document), separators=(',', ':')).encode())
            logical = name if name.endswith('.json') else name + '.json'
        else:
            text = raw.decode()
            if name == 'campaign/server.log' and len(raw) > 256 * 1024:
                text, entry['lineProjection'] = project_log(text)
            document = {'text': portable(text, workspace)}
            logical = name + '.json'
        projected = encode(document)
        entry['kind'] = 'json' if is_json else 'escaped-text'
        if len(projected) > 750 * 1024 or re.fullmatch(r'campaign/(server|peer-\d+)\.json', name):
            folder = logical[:-5] + '.chunks'
            index = write_chunks(document, destination / folder)
            entry.update(kind='chunked-json' if is_json else 'chunked-text',
                         logicalPath=logical, chunksDirectory=folder,
                         topLevelArrayCounts={k: len(v) for k, v in document.items() if isinstance(v, list)})
            derivative_path, derivative = logical[:-5] + '.index.json', encode(index)
        else:
            derivative_path, derivative = logical, projected
        target = destination / derivative_path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(derivative)
        entry.update(derivativePath=derivative_path, derivativeSHA256=digest(derivative),
                     derivativeBytes=len(derivative))
        entries.append(entry)
        del raw, document, projected, derivative
        gc.collect()
    client = receipt['qualifiedLoadedClient']
    manifest = {'schema': 1, 'completionMode': 'interrupted' if interrupted else 'completed',
        'scope': 'Frozen attempt provenance; full NET NOT_ACCEPTED; no Enforce authorization.',
        'qualifiedSource': receipt['sourceBefore']['commit'],
        'qualifiedClientSHA256': client['sha256'], 'qualifiedClientMVID': client['mvid'],
        'projectionRules': {'strings': 'Exact workspace/home/CI-home prefixes sanitized only.',
            'roles': 'Four complete bulk fields omitted with canonical identities. Long2 additionally has explicit field/row/metric projections: all joins and ShotProof kept, Shot selection by original indices/positive-funding keys/exact chronology, Facts wholly omitted with type counts; every timing/transport window retained in declared numeric columns. No silent row cap.',
            'text': 'Full text retained except declared long-server-log line projection (first20/last30/all diagnostic matches); exact original indices/hash/counts preserved. Chunked if needed.',
            'encoding': 'Deterministic compact UTF-8 JSON, Unicode retained, final newline; 128Ki-character lossless text chunks, <=1MiB encoded, contiguous byte ranges/full projection digest.',
            'excluded': 'No game assets, binary/runtime files, Paths.txt/profiles, recordings or media copied.'},
        'entries': entries}
    (destination / 'manifest.json').write_bytes(encode(manifest))
    print(f'Packed {len(entries)} immutable original projections: {destination.name}')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--original-workspace-root', type=Path, required=True)
    p.add_argument('--source-directory', type=Path, required=True)
    p.add_argument('--evidence-directory', type=Path, required=True)
    p.add_argument('--interrupted', action='store_true')
    a = p.parse_args()
    pack(a.original_workspace_root.resolve(), a.source_directory.resolve(),
         a.evidence_directory.resolve(), a.interrupted)
