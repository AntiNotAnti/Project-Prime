#!/usr/bin/env python3
"""Verify source09 evidence and distinct diagnostic negatives without runtime execution."""
import argparse, json
from pathlib import Path
from verify import projection, require, sanitize, sha
from verify_native import remove


def verify(evidence, originals=None, check_captures=False):
    manifest = json.loads((evidence / 'manifest.json').read_text())
    require(manifest['qualifiedSource'] == '09ada0ab153dd4c15f4ea3b55473772c64f75663', 'source09 scope')
    expected = {'manifest.json', 'README.md'}
    documents = {}
    for entry in manifest['entries']:
        path = evidence / entry['derivativePath']
        require(path.resolve().is_relative_to(evidence.resolve()), 'external path')
        expected.add(entry['derivativePath'])
        raw = path.read_bytes()
        require(sha(raw) == entry['derivativeSHA256'] and len(raw) == entry['derivativeBytes'], 'derivative identity')
        require(path.suffix in ['.json', '.log', '.md'], 'asset/media/profile/binary')
        require(not any(x in raw for x in [b'/Users/', b'/home/', b'/private/var/']), 'private host path')
        if entry['kind'] == 'json':
            doc = json.loads(raw); documents[entry['derivativePath']] = doc
            require(sha(json.dumps(projection(doc), separators=(',', ':')).encode()) ==
                    entry['numericAndArrayStructureSHA256'], 'numeric/array order')
        if originals is not None:
            original = (originals / entry['originalRelativePath']).read_bytes()
            require(sha(original) == entry['originalSHA256'] and len(original) == entry['originalBytes'], 'original identity')
            if entry['kind'] == 'json':
                source = json.loads(original)
                for omission in entry['omittedFields']:
                    value = remove(source, omission['path'])
                    canonical = json.dumps(value, separators=(',', ':'), ensure_ascii=False).encode()
                    require(sha(canonical) == omission['canonicalValueSHA256'] and
                            len(canonical) == omission['canonicalValueBytes'], 'omitted bulk identity')
                transformed = sanitize(source, originals, Path.home())
                require(transformed == doc and projection(transformed) == projection(doc), 'exact semantic/numeric/array equality')
            elif entry['kind'] == 'escaped-text':
                require(sanitize(original.decode(), originals, Path.home()) == json.loads(raw)['text'], 'escaped log equality')
            elif entry['kind'] == 'text-excerpt':
                lines = sanitize(original.decode(), originals, Path.home()).splitlines(keepends=True)
                excerpt = json.loads(raw)
                require(excerpt['originalLineCount'] == len(lines) and
                        excerpt['lines'] == [lines[i] for i in entry['selectedLineIndices']], 'exact stack excerpt')
            else:
                require(sanitize(original.decode(), originals, Path.home()).encode() == raw, 'text equality')
    require({str(p.relative_to(evidence)) for p in evidence.rglob('*') if p.is_file()} == expected, 'unlisted files')
    q = documents['qualified-09/qualification-summary.json']; client = q['actualLoadedClient']
    require(q['source']['commit'] == manifest['qualifiedSource'] and not q['source']['dirty'], 'qualified source')
    require(client['sha256'] == '323380522fb48d8d6903709f27f5f95ca8995aa26d625e1fa3bdfff0ef603647' and
            client['mvid'] == 'd6f53ab0-c704-4d18-8665-e83509063a09', 'actual source09 client')
    require(q['baselineRoutes'] == 15 and q['extraPureConfigRoutes'] == 20 and q['baselinePython'] == 123, 'local counters')
    require(q['fullNettestPython']['tests'] == 537 and q['fullNettestPython']['skipped'] == 1 and
            q['glComparisonPython']['tests'] == 11, 'actual Python scope')
    require(q['overall'] == 'NOT_ACCEPTED_RETAINED_IMPERIALIST_AND_TOUR_FAILURES', 'no false acceptance')
    require(q['sourceUnchanged'] and q['runtimeUnchanged'] and q['ownedGroupsClosed'], 'source/runtime/closure')
    require(q['assetResults'][0]['qualified'] and not q['assetResults'][1]['qualified'], 'native effect pass/Imperialist fail')
    shell = documents['full-shell-attempt1/receipt.json']
    require(shell['status'] == 'PASS' and shell['loadedClientMatches'] and shell['inputsUnchanged'], 'actual UI client')
    require(shell['acceptance']['completedCycles'] == ['1', '2'] and shell['acceptance']['debugCompletedCycles'] == ['1', '2'], 'distinct streams')
    require(shell['acceptance']['expectedCaptures'] == len(shell['acceptance']['captures']) == 43 and
            shell['acceptance']['missingSteps'] == 0 and shell['acceptance']['fullStandardScript'], 'UI denominator')
    require(shell['process']['exitCode'] == 0 and shell['process']['ownership']['closed'], 'UI group closure')
    ui = q['resultsUi']; require(ui['modalOwnershipAssertions'] == 13 and ui['existingResultsAssertions'] == 11, 'official UI controls')
    for receipt in ['net-assets/receipt.json', 'net-independent-assets/receipt.json']:
        r = documents[receipt]; require(r['status'] == 'FAIL_STOPPED_FIRST_ASSET', 'actual negative')
        require(r['sourceBefore'] == r['sourceAfter'] and r['physicalHashesBefore'] == r['physicalHashesAfter'], 'failed runtime identity')
    require(q['tourNegative']['exit'] == -6 and q['tourNegative']['qualifiedPairs'] == 0, 'recursion negative')
    friendly = documents['friendly-thin-diagnostic/receipt.json']
    require(friendly['exit'] == 0 and friendly['groupAbsentVerified'] and
            friendly['entrySHA256'] != client['sha256'], 'distinct friendly diagnostic, not full qualification')
    pair = documents['tour-thin-attempt2/pair.json']; diag = pair['Diagnostic']
    require(pair['status'] == 'FAIL_PAIR' and pair['Engine']['Sha256'] == client['sha256'], 'tour diagnostic engine/failure')
    require(pair['Entry']['Sha256'] != q['resultsUi']['actualEngineSha256'], 'separate tour entry')
    require(diag['OffSpawn']['Room'] == 1 and diag['ShadowSpawn']['Room'] == 2 and not diag['SpawnEqual'], 'room difference retained')
    require(diag['LifeEqual'] and diag['OffContact'] == diag['ShadowContact'] == 121 and
            diag['OffGrant'] == diag['ShadowGrant'] == 1 and diag['OffTimers'] == diag['ShadowTimers'] and
            diag['OffFrames'] == diag['ShadowFrames'] == 1801, 'actual observed shared chronology')
    require(diag['CanonicalIntentDifference']['Equal'] and diag['ReplayDifference']['PayloadDifference']['FirstDifference'] == 11,
            'intent/replay distinction')
    charged = documents['charged-volt-thin-d345/receipt.json']
    require(charged['clientSHA256'] != client['sha256'] and
            [(r['charge'], r['exit']) for r in charged['results']] == [(180, 1), (120, 0)], 'distinct d345 charge negative/control')
    require(all(r['groupAbsentVerified'] for r in charged['results']), 'charged diagnostic closure')
    ci = documents['ci-observed-09.json']; require(ci['headRefOid'] == manifest['qualifiedSource'], 'observed CI source')
    require(sum(r['conclusion'] == 'SUCCESS' for r in ci['statusCheckRollup']) == 29 and
            sum(r['status'] == 'IN_PROGRESS' for r in ci['statusCheckRollup']) == 3, 'observed-only CI states')
    if check_captures:
        require(originals is not None, '--check-captures needs original workspace')
        folder = originals / 'work/plan-completion-evidence/candidate-09ada0ab/full-shell-attempt1/shell-pictures'
        for capture in shell['acceptance']['captures']:
            raw = (folder / capture['name']).read_bytes()
            require(sha(raw) == capture['sha256'] and len(raw) == capture['bytes'], 'existing capture identity')
    print(f'PASS: {len(manifest["entries"])} immutable derivatives; source09 scoped positives and all diagnostic negatives preserved.')
    if originals is not None: print('PASS: original SHA/bytes and exact sanitized semantic/numeric/array/text equality.')
    if check_captures: print('PASS: all43 original UI capture hashes/bytes verified; no image copied.')
    print('Full NET/program remains NOT_ACCEPTED; no d345/latest-head or eventual CI acceptance inferred.')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--evidence', type=Path, required=True)
    p.add_argument('--original-workspace-root', type=Path)
    p.add_argument('--check-captures', action='store_true')
    a = p.parse_args(); verify(a.evidence, a.original_workspace_root, a.check_captures)
