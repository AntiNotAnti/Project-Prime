#!/usr/bin/env python3
"""Verify immutable completed campaign derivatives; never execute the game."""
import argparse
import importlib.util
import json
from pathlib import Path
from chunks import read_chunks
from compact_projection import apply_declared_role_projection, positive_and_funded_keys, funded_keys, project_log

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


def role_specification(entry, manifest):
    spec = dict(entry['roleProjection'])
    if 'roleSelectionContext' in entry:
        spec['qualifyingShotKeys'] = manifest['roleSelectionContexts'][entry['roleSelectionContext']]
    return spec


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
        if entry['kind'] in ['chunked-json', 'chunked-text']:
            folder = evidence / entry['chunksDirectory']
            require(folder.resolve().is_relative_to(evidence.resolve()), 'external chunk directory')
            expected.update(str((folder / c['path']).relative_to(evidence)) for c in document['chunks'])
            document = read_chunks(document, folder)
            require(not any(x in canonical(document) for x in [b'/Users/', b'/home/', b'/private/var/']), 'private recomposed path')
            if entry['kind'] == 'chunked-json':
                require({k: len(v) for k, v in document.items() if isinstance(v, list)} ==
                        entry['topLevelArrayCounts'], 'projected array counts')
        if entry['kind'] in ['json', 'chunked-json']:
            documents[entry.get('logicalPath', entry['derivativePath'])] = document
            require(sha(json.dumps(projection(document), separators=(',', ':')).encode()) ==
                    entry['numericAndArrayStructureSHA256'], 'numeric/array structure')
        else:
            require(entry['kind'] in ['escaped-text', 'chunked-text'] and set(document) == {'text'}, 'text schema')
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
                if 'roleProjection' in entry:
                    spec = role_specification(entry, manifest)
                    apply_declared_role_projection(source, spec)
                require(portable(source, originals) == document and
                        projection(source) == projection(document), 'exact preserved semantic/numeric/array equality')
            else:
                text = original.decode()
                if 'lineProjection' in entry:
                    text, metadata = project_log(text)
                    require(metadata == entry['lineProjection'], 'exact original line selection')
                require(portable(text, originals) == document['text'], 'declared text equality')
    require({str(p.relative_to(evidence)) for p in evidence.rglob('*') if p.is_file()} == expected,
            'unlisted evidence files')
    receipt = documents['receipt.json']
    role_entries = [e for e in manifest['entries'] if 'roleProjection' in e]
    if role_entries:
        server = documents['campaign/server.json']
        keys = funded_keys(server) if receipt['case'] == 'long8' or receipt['case'].startswith('smoke-') else positive_and_funded_keys(server)
        chronology = None
        if receipt['case'] == 'long2':
            funded = next(c['Shot']['Key'] for c in server['TimedCaseJoins']['Certificates']
                          if c['Shot']['Funding'] is not None)
            chronology = {'life': [funded[k] for k in ['AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId']],
                          'firstShot': 1646, 'lastShot': 1676}
        require(all(role_specification(e, manifest)['qualifyingShotKeys'] == [list(k) for k in keys] and
                    e['roleProjection']['chronology'] == chronology for e in role_entries),
                'all source-positive/funded keys and invalidity chronology retained')
    if manifest.get('completionMode') == 'interrupted':
        recovery = documents['interruption-recovery.json']
        require(receipt['status'] == 'RUNNING' and receipt['case'] == 'long2' and
                recovery['status'] == 'INCOMPLETE_EXTERNALLY_INTERRUPTED', 'interrupted status retained')
        require(not recovery['finalReportsPresent'] and not recovery['finalDrainReached'] and
                recovery['liveOwnedMembers'] == [] and recovery['cause'].startswith('UNKNOWN'), 'unknown interruption, not success')
        original_entry = next(e for e in manifest['entries'] if e.get('logicalPath', e['derivativePath']) == 'receipt.json')
        require(recovery['originalReceiptSha256'] == original_entry['originalSHA256'] and
                receipt['sourceBefore']['commit'] == manifest['qualifiedSource'], 'original RUNNING receipt/source identity')
        require(all(not r['complete'] for r in documents['partial-file-inventory.json']['recordings']), 'no partial replay success')
        if originals is not None:
            source_folder = originals / Path(original_entry['originalRelativePath']).parent
            for record in documents['partial-file-inventory.json']['recordings']:
                artifact = source_folder / record['path']
                require(artifact.resolve().is_relative_to(source_folder.resolve()), 'external partial recording')
                import hashlib
                h = hashlib.sha256()
                with artifact.open('rb') as stream:
                    for block in iter(lambda: stream.read(1024 * 1024), b''):
                        h.update(block)
                require(artifact.stat().st_size == record['bytes'] and h.hexdigest() == record['sha256'], 'partial recording identity only')
        print('PASS: interrupted attempt provenance retained; completion/interruption cause/replay success NOT_ESTABLISHED.')
        return
    require(receipt['executed'] and receipt['sourceBefore'] == receipt['sourceAfter'] and
            receipt['sourceBefore']['commit'] == manifest['qualifiedSource'] and
            not receipt['sourceBefore']['dirty'], 'frozen source')
    require(receipt['sourceUnchanged'] and receipt['runtimeUnchanged'] and receipt['sdkUnchanged'] and
            receipt['physicalHashesBefore'] == receipt['physicalHashesAfter'], 'physical/runtime stability')
    client = receipt['qualifiedLoadedClient']
    require(client['sha256'] == manifest['qualifiedClientSHA256'] and
            client['mvid'] == manifest['qualifiedClientMVID'], 'source-qualified client')
    recovered_failure = manifest.get('completionMode') == 'recovered-failed-wrapper'
    if recovered_failure:
        recovery = documents['recovery-validation-receipt.json']
        original_entry = next(e for e in manifest['entries'] if e.get('logicalPath', e['derivativePath']) == 'receipt.json')
        require(recovery['originalWrapperReceiptSha256'] == original_entry['originalSHA256'] and
                recovery['originalWrapperStatus'] == receipt['status'] == 'FAIL_PRECONDITION_OR_PARTIAL' and
                recovery['liveOwnedGroupMembers'] == [] and not recovery['reportMutation'], 'failed wrapper, post-hoc closure only')
        summary, ai = documents['recovery-generic-summary.json'], documents['recovery-ai-summary.json']
        require(summary['status'] == ai['status'] == 'FAIL' and
                all(p['exit'] is None for p in summary['processes']), 'unknown native exits not recovered as PASS')
    else:
        require(all(r['ownedProcessGroupClosed'] for r in receipt['runs']) and
                receipt['joinedAuditRun']['ownedProcessGroupClosed'], 'completed process groups')
        summary, ai = documents['campaign/summary.json'], documents['campaign/ai-summary.json']
        require(summary['status'] == ai['status'] == 'PASS' and not summary['failures'] and
                not ai['failures'], 'transport/activity scope')
    require(documents['campaign/manifest.json']['drain']['status'] == 'PASS', 'bounded drain')
    run_manifest = documents['campaign/manifest.json']
    for role in ['server'] + ['peer-' + str(i) for i in range(run_manifest['settings']['players'])]:
        d = documents['campaign/' + role + '.json']
        require(d['Version'] == client['buildId'] and d['Protocol'] == 56 and
                d['Overflow'] == (6660 if recovered_failure and role == 'server' else 0) and
                d['Transport']['QueueDrops'] == 0, 'role identity/explicit overflow outcome')
        require(d['ShotProof']['Mode'] == 'Shadow' and not d['TimedModifiers']['Enforcing'], 'no Enforce')
        require(len(d['Windows'].get('rows', [])) > 0 if isinstance(d['Windows'], dict) else len(d['Windows']) > 0,
                'declared chronology retained')
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
    elif receipt['case'] == 'long2':
        server = documents['campaign/server.json']; joins = server['TimedCaseJoins']
        counts = {'Certificates': 368, 'Components': 2968, 'Claims': 708,
                  'Terminals': 704, 'Payments': 166, 'Continuous': 425}
        require(all(len(joins[k]) == n and joins['Budgets'][k]['Count'] == n and
                    joins['Budgets'][k]['Dropped'] == 0 for k, n in counts.items()), 'long2 complete proof rows')
        audit = documents['strict-joined-evidence.json']
        require(receipt['status'] == audit['status'] == 'INCOMPLETE' and not audit['failures'] and
                receipt['joinedAuditRun']['exit'] == 2 and audit['sourceVerifiedFraction'] == 0.0,
                'long2 strict incomplete')
        require(audit['metrics']['fundedOrdinaryCertificates'] == len(audit['cases']) == 2 and
                audit['metrics']['verifiedSourceCertificates'] == audit['metrics']['fullyJoinedPaidCases'] ==
                audit['metrics']['fundedPayments'] == 0, 'long2 positive and missing denominator')
        require(server['Frames'] == 72252 and server['ServerSegments'][0]['DroppedSteps'] == 9 and
                server['ServerSegments'][0]['Stalls'] == 1, 'long2 authority pacing negatives')
        require([len(documents['campaign/' + r + '.json']['Windows']['rows']) for r in ['server', 'peer-0', 'peer-1']] ==
                [1193, 1191, 1191], 'every timing-window row retained in declared metric columns')
        assessment = documents['final-assessment.json']
        require([len(assessment['roles'][r]['presentationWindows']) for r in ['server', 'peer-0', 'peer-1']] ==
                [1193, 1191, 1191], 'long2 complete chronological presentation windows')
        require([assessment['roles'][r]['presentationCountersSummary']['maximumStalledFrames']
                 for r in ['peer-0', 'peer-1']] == [69744, 70042], 'presentation distinct from simulation counters')
    elif receipt['case'] == 'long8':
        server = documents['campaign/server.json']; joins = server['TimedCaseJoins']
        counts = {'Certificates': 8683, 'Components': 10979, 'Claims': 4936,
                  'Terminals': 4651, 'Payments': 3639, 'Continuous': 374}
        require(all(len(joins[k]) == n and joins['Budgets'][k]['Count'] == n and
                    joins['Budgets'][k]['Dropped'] == 0 for k, n in counts.items()), 'long8 complete proof rows')
        audit = documents['recovery-strict-joined-evidence.json']
        require(audit['status'] == 'INCOMPLETE' and not audit['failures'] and
                audit['sourceVerifiedFraction'] == 0.296875, 'long8 strict canary, no full acceptance')
        metrics = audit['metrics']
        require([metrics[k] for k in ['fundedOrdinaryCertificates', 'verifiedSourceCertificates',
                'nativeComponents', 'fundedPayments', 'fullyJoinedPaidCases', 'nativePaidWithoutOwnerClaim']] ==
                [64, 19, 64, 29, 6, 1], 'genuine bounded positive joins')
        require(server['Frames'] == 72820 and server['ServerSegments'][0]['DroppedSteps'] == 8 and
                server['ServerSegments'][0]['Stalls'] == 1, 'long8 simulation negative scope')
        forms = next(o for e in role_entries if e.get('logicalPath') == 'campaign/server.json'
                     for o in e['omittedFields'] if o['path'] == ['Forms'])
        require(forms['entryCount'] == 1048576 and server['Overflow'] == 6660, 'legacy Forms overflow retained')
        partial = documents['partial-final-assessment.json']
        require(partial['guardFailure'].startswith('free disk below') and
                receipt['error'] == partial['error'] == '[Errno 1] Operation not permitted' and
                partial['liveOwnedMembers'] == [], 'primary guard plus masked cleanup failure preserved')
        selected = documents['selected-invalidcharge-source-rows.json']
        server_entry = next(e for e in role_entries if e.get('logicalPath') == 'campaign/server.json')
        require(selected['originalSha256'] == server_entry['originalSHA256'] and
                selected['originalBytes'] == server_entry['originalBytes'], 'selected report original source identity')
        first_invalid = [i for i, c in enumerate(server['TimedCaseJoins']['Certificates'])
                         if c['Shot']['Knowledge'] == 2 and c['Shot']['Source']['Source'] == 10][:3]
        require([r['certificateIndex'] for r in selected['selected']] == first_invalid, 'declared first-three selection')
        kept_indices = next(f['selectedOriginalIndices'] for f in server_entry['roleProjection']['fields']
                            if f['path'] == ['Shots'])
        for selection in selected['selected']:
            require(server['TimedCaseJoins']['Certificates'][selection['certificateIndex']] == selection['certificate'],
                    'actual selected invalid-charge certificate index')
            for row in selection['matchingEmissionRows']:
                require(server['ShotProof']['EmissionRows'][row['index']] == row['row'], 'actual matching emission index')
            for row in selection['matchingLegacyProofRows']:
                require(server['ShotProof']['Rows'][row['index']] == row['row'], 'actual matching legacy proof index')
            for row in selection['matchingShotRows']:
                require(server['Shots'][kept_indices.index(row['index'])] == row['row'], 'actual matching original shot index')
    elif receipt['case'].startswith('smoke-'):
        assessment = documents['final-assessment.json']; audit = documents['strict-joined-evidence.json']
        server = documents['campaign/server.json']; joins = server['TimedCaseJoins']
        require(assessment['wrapperStatus'] == receipt['status'] and assessment['strict']['status'] == audit['status'],
                'original matrix verdict unchanged')
        require(all(len(joins[k]) == joins['Budgets'][k]['Count'] and joins['Budgets'][k]['Dropped'] == 0
                    for k in ['Certificates', 'Components', 'Claims', 'Terminals', 'Payments', 'Continuous']),
                'matrix complete proof rows')
        require(server['Frames'] == assessment['authorityFrames'] and
                server['ServerSegments'][0]['DroppedSteps'] == assessment['authorityDroppedSteps'] and
                server['ServerSegments'][0]['Stalls'] == assessment['authorityStalls'], 'matrix pacing negatives')
        if receipt['case'] == 'smoke-worst8':
            require(audit['status'] == receipt['status'] == 'FAIL' and len(audit['failures']) == 1,
                    'original worst8 strict failure retained')
            require('selected-endframe-selfdeath-rows.json' in documents, 'causal self-death sample retained')
    print(f'PASS: {len(manifest["entries"])} immutable completed derivatives; full NET NOT_ACCEPTED.')
    if originals is not None:
        print('PASS: every original SHA/bytes, complete omission identity, exact preserved values/array order/text.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--original-workspace-root', type=Path)
    args = parser.parse_args()
    verify(args.evidence, args.original_workspace_root)
