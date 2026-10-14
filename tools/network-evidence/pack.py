#!/usr/bin/env python3
"""Project an already-closed campaign into bounded immutable portable evidence."""
import argparse
import gc
import hashlib
import json
import re
from pathlib import Path
from chunks import write_chunks, encode, digest
from verify import portable, projection, remove
from compact_projection import project_role, positive_and_funded_keys, funded_keys, project_log

OMISSIONS = [['Forms'], ['SlowStates'], ['States'], ['Bombs', 'Rows']]
COMMON = ['receipt.json', 'campaign-runner.log', 'monitor-checkpoints.jsonl',
          'campaign/manifest.json', 'campaign/rotation.txt']
COMPLETE = ['strict-joined-evidence.json', 'strict-joined-evidence.log',
            'final-assessment.json', 'result.md', 'campaign/summary.json',
            'campaign/ai-summary.json', 'campaign/drain.release.json',
            'campaign/drain.server-release.json', 'campaign/drain.server-release.json.quiesce']
INTERRUPTED = ['interruption-recovery.json', 'partial-file-inventory.json']
RECOVERED = ['partial-final-assessment.json', 'recovery-validation-receipt.json',
             'recovery-strict-joined-evidence.json', 'recovery-strict-joined-evidence.log',
             'recovery-generic-summary.json', 'recovery-generic-validation.log',
             'recovery-ai-summary.json', 'selected-invalidcharge-source-rows.json',
             'campaign/drain.release.json', 'campaign/drain.server-release.json',
             'campaign/drain.server-release.json.quiesce']


def pack(workspace, source, destination, interrupted=False, recovered_failure=False):
    if destination.exists():
        raise ValueError('refuse replacing existing immutable package')
    receipt = json.loads((source / 'receipt.json').read_text())
    if interrupted and recovered_failure:
        raise ValueError('ambiguous completion mode')
    if recovered_failure:
        recovery = json.loads((source / 'recovery-validation-receipt.json').read_text())
        if (recovery['liveOwnedGroupMembers'] or recovery['reportMutation'] or
                recovery['originalWrapperStatus'] != 'FAIL_PRECONDITION_OR_PARTIAL'):
            raise ValueError('failed wrapper recovery not finalized/closed')
    elif interrupted:
        recovery = json.loads((source / 'interruption-recovery.json').read_text())
        if recovery['liveOwnedMembers'] or recovery['status'] != 'INCOMPLETE_EXTERNALLY_INTERRUPTED':
            raise ValueError('interrupted attempt has not been recovered/closed')
    elif (receipt['status'] == 'RUNNING' or not receipt.get('sourceUnchanged') or
          not all(r['ownedProcessGroupClosed'] for r in receipt['runs'])):
        raise ValueError('campaign not finalized/closed')
    matrix = receipt.get('case', '').startswith('smoke-')
    optional_absent = []
    files = COMMON + (RECOVERED if recovered_failure else INTERRUPTED if interrupted else COMPLETE)
    if matrix:
        optional_absent = [n for n in ['monitor-checkpoints.jsonl', 'result.md'] if not (source / n).is_file()]
        files = [n for n in files if n not in optional_absent]
        if (source / 'selected-endframe-selfdeath-rows.json').is_file():
            files.append('selected-endframe-selfdeath-rows.json')
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
        is_json = name.endswith('.json') or name.endswith('.quiesce')
        # Large original role files stay external: hash in bounded blocks and parse
        # once without retaining a second complete raw-byte representation.
        if is_json and original.stat().st_size > 64 * 1024 * 1024:
            h = hashlib.sha256()
            with original.open('rb') as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    h.update(block)
            original_digest, original_bytes = h.hexdigest(), original.stat().st_size
            raw = None
        else:
            raw = original.read_bytes()
            original_digest, original_bytes = digest(raw), len(raw)
        entry = {'originalRelativePath': str(original.relative_to(workspace)),
                 'originalSHA256': original_digest, 'originalBytes': original_bytes, 'omittedFields': []}
        if is_json:
            if raw is None:
                with original.open(encoding='utf-8') as stream:
                    document = json.load(stream)
            else:
                document = json.loads(raw)
            if re.fullmatch(r'campaign/(server|peer-\d+)\.json', name):
                if receipt['case'] in ['long2', 'long8'] or matrix:
                    if name == 'campaign/server.json':
                        qualifying_keys = funded_keys(document) if receipt['case'] == 'long8' or matrix else positive_and_funded_keys(document)
                        if receipt['case'] == 'long2':
                            funded = next(c['Shot']['Key'] for c in document['TimedCaseJoins']['Certificates']
                                          if c['Shot']['Funding'] is not None)
                            chronology = {'life': [funded[k] for k in ['AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId']],
                                          'firstShot': 1646, 'lastShot': 1676}
                    entry['roleProjection'] = project_role(document, qualifying_keys, chronology,
                        'all-funded-certificate-keys' if receipt['case'] == 'long8' or matrix else None)
                    if receipt['case'] == 'long8' or matrix:
                        entry['roleSelectionContext'] = 'all-funded-certificate-keys'
                        entry['roleProjection'].pop('qualifyingShotKeys')
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
    manifest = {'schema': 1, 'completionMode': 'recovered-failed-wrapper' if recovered_failure else 'interrupted' if interrupted else 'completed',
        'scope': 'Frozen attempt provenance; full NET NOT_ACCEPTED; no Enforce authorization.',
        'qualifiedSource': receipt['sourceBefore']['commit'],
        'qualifiedClientSHA256': client['sha256'], 'qualifiedClientMVID': client['mvid'],
        'projectionRules': {'strings': 'Exact workspace/home/CI-home prefixes sanitized only.',
            'roles': 'Four complete bulk fields omitted with canonical identities. Declared compact role projections keep all joins and ShotProof; Long2 Shot selection covers source-positive/funded keys plus invalidity chronology; Long8 raw Shot-detail selection covers every funded key while all other positive/contradictory authority certificate/ShotProof rows remain complete. Facts wholly omitted with type counts; every timing/transport window retained in declared numeric columns. No silent row cap.',
            'text': 'Full text retained except declared long-server-log line projection (first20/last30/all diagnostic matches); exact original indices/hash/counts preserved. Chunked if needed.',
            'encoding': 'Deterministic compact UTF-8 JSON, Unicode retained, final newline; 128Ki-character lossless text chunks, <=1MiB encoded, contiguous byte ranges/full projection digest.',
            'excluded': 'No game assets, binary/runtime files, Paths.txt/profiles, recordings or media copied.'},
        'entries': entries}
    manifest['optionalProducerArtifactsAbsent'] = optional_absent
    if receipt['case'] == 'long8' or matrix:
        manifest['roleSelectionContexts'] = {'all-funded-certificate-keys': qualifying_keys}
    (destination / 'manifest.json').write_bytes(encode(manifest))
    print(f'Packed {len(entries)} immutable original projections: {destination.name}')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--original-workspace-root', type=Path, required=True)
    p.add_argument('--source-directory', type=Path, required=True)
    p.add_argument('--evidence-directory', type=Path, required=True)
    p.add_argument('--interrupted', action='store_true')
    p.add_argument('--recovered-failure', action='store_true')
    a = p.parse_args()
    pack(a.original_workspace_root.resolve(), a.source_directory.resolve(),
         a.evidence_directory.resolve(), a.interrupted, a.recovered_failure)
