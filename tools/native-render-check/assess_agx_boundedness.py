#!/usr/bin/env python3
"""Release evidence for outstanding direct AGX malloc origins, never RSS arithmetic."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from validate_context_campaign import read_json, sha256, validate_campaign

METRIC = 'current-outstanding-direct-agx-malloc-origin'
REQUIRED_IDENTITY = ('sourceSha', 'harnessSha256', 'clientSha256', 'assemblyMvid', 'harnessAssemblyMvid',
                     'fixtureSha256', 'osBuild', 'gpu', 'driver', 'runtime', 'openTkVersion', 'workloadSha256',
                     'profileSha256', 'contextStrategy', 'observationPhase')
HASH_FIELDS = ('sourceSha', 'harnessSha256', 'clientSha256', 'fixtureSha256', 'workloadSha256', 'profileSha256')


def integer(value):
    return isinstance(value, int) and not isinstance(value, bool)


def object_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def _assess(evidence, warmup_loads=60, minimum_windows=3):
    """Controls define the envelope before candidate observations are inspected.

    minimum_windows is successive increasing-load observations strictly AFTER
    the explicit warm-up target. Both adjacent increases and the total observed
    span must fit the control envelope. This is an evidence gate, not a proof of
    mathematical boundedness or authorization to release a runtime correction.
    """
    invalid, unknown = [], []
    result = {'schema': 1, 'metric': METRIC, 'dataQuality': 'UNVERIFIED', 'boundednessGate': 'UNVERIFIED',
              'releaseDisposition': 'UNRESOLVED_P0', 'programAcceptance': 'NOT_ACCEPTED',
              'warmupLoads': warmup_loads, 'minimumSuccessiveWindows': minimum_windows,
              'noiseEnvelope': None, 'windows': [], 'comparisons': [], 'issues': invalid, 'unverified': unknown}
    if not integer(warmup_loads) or warmup_loads < 0 or not integer(minimum_windows) or minimum_windows < 3:
        invalid.append('nonnegative explicit warm-up and at least three successive post-warm-up windows required')
        result['dataQuality'] = 'INVALID'
        return result
    if not isinstance(evidence, dict):
        invalid.append('evidence must be an object'); result['dataQuality'] = 'INVALID'; return result
    synthetic = evidence.get('synthetic') is True
    result['synthetic'] = synthetic
    if synthetic: result['releaseDisposition'] = 'SYNTHETIC_TEST_ONLY'
    if evidence.get('metric') != METRIC:
        unknown.append('outstanding direct AGX-origin metric unavailable; RSS/GL names cannot establish boundedness')
    origins = evidence.get('origins')
    if not isinstance(origins, list) or not origins or len(set(origins)) != len(origins) or any(origin not in ('agxFragmentVariant', 'agxVertexVariant') for origin in origins):
        invalid.append('explicit distinct AGX malloc origins required'); origins = []
    controls = evidence.get('controls') if isinstance(evidence.get('controls'), list) else []
    windows = evidence.get('windows') if isinstance(evidence.get('windows'), list) else []
    if len(controls) < 2:
        unknown.append('noise envelope requires at least two independent matched control runs')
    if not windows:
        unknown.append('no increasing-load native observations available')
    expected_identity = None

    def check(sample, kind):
        nonlocal expected_identity
        if not isinstance(sample, dict): invalid.append(kind + ' sample must be an object'); return False
        identity = sample.get('identity')
        if not isinstance(identity, dict) or any(not isinstance(identity.get(key), str) or not identity[key] for key in REQUIRED_IDENTITY):
            unknown.append(kind + ' immutable binary/fixture/host/workload identity incomplete'); return False
        for key in HASH_FIELDS:
            pattern = r'(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})' if key == 'sourceSha' else r'[0-9a-fA-F]{64}'
            if not isinstance(identity[key], str) or not re.fullmatch(pattern, identity[key]):
                invalid.append(kind + ' identity hash malformed: ' + key); return False
        identity = {key: identity[key].lower() if key in HASH_FIELDS else identity[key] for key in REQUIRED_IDENTITY}
        if expected_identity is None: expected_identity = identity
        elif identity != expected_identity:
            invalid.append(kind + ' binary, fixture, graphics, profiling, phase or context contract differs'); return False
        if sample.get('dataQuality') == 'INVALID' or sample.get('captureSucceeded') is not True:
            invalid.append(kind + ' contains a failed/missing capture or invalid campaign'); return False
        if sample.get('dataQuality') != 'VALID' or sample.get('supported') is not True:
            unknown.append(kind + ' native capture unsupported/unverified'); return False
        if not sample.get('runId') or not sample.get('sampleId') or not integer(sample.get('loads')) or sample['loads'] < 0:
            invalid.append(kind + ' immutable run/sample identity or exact load target missing'); return False
        values = sample.get('originBytes')
        if not isinstance(values, dict) or any(values.get(origin) is None for origin in origins):
            unknown.append(kind + ' origin bytes unavailable; null is not zero'); return False
        if any(not integer(values[origin]) or values[origin] < 0 for origin in origins):
            invalid.append(kind + ' origin byte count is not a nonnegative integer'); return False
        return True

    # Calibration precedes any use of candidate magnitudes. Candidate rows never
    # enlarge the envelope. Equal control targets include the same cleanup point.
    valid_controls = [sample for sample in controls if check(sample, 'control')]
    if len({sample.get('runId') for sample in valid_controls}) != len(valid_controls):
        invalid.append('control runs are duplicated/correlated; independent repetitions required')
    if len({sample.get('sampleId') for sample in valid_controls}) != len(valid_controls):
        invalid.append('control captures are duplicated')
    if len({sample.get('loads') for sample in valid_controls}) > 1:
        invalid.append('control workload targets differ')
    if valid_controls and valid_controls[0]['loads'] != warmup_loads:
        invalid.append('control target must match the explicitly declared warm-up anchor')
    if len(valid_controls) >= 2 and not invalid and not unknown:
        envelope = {origin: {'minimumBytes': min(sample['originBytes'][origin] for sample in valid_controls),
                             'maximumBytes': max(sample['originBytes'][origin] for sample in valid_controls),
                             'envelopeBytes': max(sample['originBytes'][origin] for sample in valid_controls) - min(sample['originBytes'][origin] for sample in valid_controls)}
                    for origin in origins}
        result['noiseEnvelope'] = {'method': 'observed maximum-minus-minimum across matched repeated controls; no candidate-derived tolerance',
                                   'controlRuns': len(valid_controls), 'controlLoads': valid_controls[0]['loads'], 'origins': envelope}
    else:
        envelope = None
    valid_windows = [sample for sample in windows if check(sample, 'window')]
    if len({sample.get('sampleId') for sample in valid_windows}) != len(valid_windows):
        invalid.append('increasing-load observations are duplicated')
    if any(right['loads'] <= left['loads'] for left, right in zip(valid_windows, valid_windows[1:])):
        invalid.append('successive workload targets must be strictly increasing in recorded order')
    if len({sample.get('runId') for sample in valid_windows}) > 1:
        unknown.append('increasing windows mix fresh processes; restart resets cannot prove within-process boundedness')
    result['identity'] = expected_identity
    result['windows'] = [{key: sample.get(key) for key in ('sampleId', 'loads', 'context', 'originBytes')} for sample in valid_windows]
    post_warmup = [sample for sample in valid_windows if sample['loads'] > warmup_loads]
    result['postWarmupWindows'] = len(post_warmup)
    if len(post_warmup) < minimum_windows:
        unknown.append('insufficient successive increasing-load windows after warm-up')
    material_growth = False
    material_variation = False
    if envelope is not None:
        for left, right in zip(valid_windows, valid_windows[1:]):
            if left['loads'] < warmup_loads: continue  # Warm-up growth is not a plateau failure.
            deltas = {origin: right['originBytes'][origin] - left['originBytes'][origin] for origin in origins}
            outside = {origin: deltas[origin] > envelope[origin]['envelopeBytes'] for origin in origins}
            material_growth |= any(outside.values())
            result['comparisons'].append({'fromLoads': left['loads'], 'toLoads': right['loads'], 'originDeltaBytes': deltas, 'growthExceedsControlEnvelope': outside})
        if len(post_warmup) >= 2:
            span = {origin: max(sample['originBytes'][origin] for sample in post_warmup) - min(sample['originBytes'][origin] for sample in post_warmup) for origin in origins}
            result['postWarmupSpanBytes'] = span
            material_variation |= any(span[origin] > envelope[origin]['envelopeBytes'] for origin in origins)
        with_anchor = [sample for sample in valid_windows if sample['loads'] >= warmup_loads]
        if len(with_anchor) >= 2:
            anchor_span = {origin: max(sample['originBytes'][origin] for sample in with_anchor) - min(sample['originBytes'][origin] for sample in with_anchor) for origin in origins}
            result['warmupAnchorSpanBytes'] = anchor_span
            material_variation |= any(anchor_span[origin] > envelope[origin]['envelopeBytes'] for origin in origins)
            material_growth |= any(max(sample['originBytes'][origin] for sample in with_anchor) - with_anchor[0]['originBytes'][origin] > envelope[origin]['envelopeBytes'] for origin in origins)
    result['materialGrowthObserved'] = material_growth
    result['materialVariationObserved'] = material_variation
    result['dataQuality'] = 'INVALID' if invalid else 'UNVERIFIED' if any('unsupported' in issue or 'unavailable' in issue or 'incomplete' in issue for issue in unknown) else 'VALID'
    if invalid:
        result['boundednessGate'] = 'UNVERIFIED'
    elif material_growth or material_variation:
        result['boundednessGate'] = 'FAIL'
    elif not unknown and envelope is not None and len(post_warmup) >= minimum_windows:
        result['boundednessGate'] = 'PASS'
    result['meaning'] = 'PASS is stabilization within the observed control envelope for this immutable workload/hardware scope only. FAIL is acceptance failure, not a mathematical unboundedness proof. No cause/fix/visual/platform/exact-head CI acceptance is inferred.'
    return result


def assess(evidence, warmup_loads=60, minimum_windows=3):
    """Malformed data fail closed without mutating caller evidence."""
    try:
        return _assess(evidence, warmup_loads, minimum_windows)
    except (ValueError, TypeError, KeyError, AttributeError) as error:
        return {'schema': 1, 'metric': METRIC, 'dataQuality': 'INVALID', 'boundednessGate': 'UNVERIFIED',
                'releaseDisposition': 'UNRESOLVED_P0', 'programAcceptance': 'NOT_ACCEPTED', 'issues': [str(error)]}


def load_context_campaign(directory, origins=('agxFragmentVariant',)):
    """Revalidate immutable raw receipts, then derive cumulative process loads."""
    directory = Path(directory)
    manifest_path = directory / 'runner-manifest.json'
    manifest = read_json(manifest_path)
    context_result = read_json(directory / 'context.json')
    validation = validate_campaign(manifest, directory)
    observed = {row['phase']: row for row in validation.get('nativeAttribution', [])}
    run_id = sha256(manifest_path)
    samples, cumulative = [], 0
    for index, count in enumerate(context_result.get('contextCycles', [])):
        cumulative += count * 2  # ResourceStress map-gl reference + candidate load.
        phase = f'context-{index:02d}-after-disposal'
        workload_path = directory / 'context.json.contexts' / f'context-{index:02d}-{count}cycles.json'
        workload = read_json(workload_path)
        native = observed.get(phase, {})
        receipt = workload.get('manifest', {})
        fixture = workload.get('workload', {})
        profiles = {key: value for key, value in manifest.get('environment', {}).items() if key != 'PROJECT_PRIME_USER_DATA'}
        for key in ('-resource-quiescent-samples', '-resource-quiescent-ms', '-resource-context-post-samples', '-resource-context-post-ms'):
            command = manifest.get('command', [])
            if key in command: profiles[key] = command[command.index(key) + 1]
        identity = {'sourceSha': manifest.get('sourceSha'), **manifest.get('identity', {}),
                    'assemblyMvid': receipt.get('assemblyMvid'), 'harnessAssemblyMvid': receipt.get('harnessAssemblyMvid'),
                    'fixtureSha256': manifest.get('fixtureIdentity', {}).get('sha256'),
                    'osBuild': manifest.get('host', {}).get('macOsBuild', {}).get('output'),
                    'gpu': fixture.get('renderer'), 'driver': fixture.get('glVersion'), 'runtime': receipt.get('runtime'),
                    'openTkVersion': receipt.get('openTkVersion'),
                    'workloadSha256': object_hash({'runnerWorkload': manifest.get('workload'), 'workload': fixture, 'frames': workload.get('simulationFramesPerArm'), 'rooms': workload.get('rooms'), 'failureStages': workload.get('failureStages'), 'arm': workload.get('arm')}),
                    'profileSha256': object_hash(profiles), 'contextStrategy': 'serial-unshared-create-dispose', 'observationPhase': 'after-context-disposal'}
        captures = [row for row in manifest.get('captures', []) if row.get('phase') == phase]
        capture = captures[0] if len(captures) == 1 else {}
        category = native.get('categories') or {}
        samples.append({'runId': run_id, 'sampleId': run_id + ':' + phase, 'context': index, 'loads': cumulative,
                        'dataQuality': validation['status'], 'identity': identity,
                        'captureSucceeded': bool(capture.get('steps')) and all(step.get('exitCode') == 0 for step in capture['steps']),
                        'supported': native.get('supported') is True,
                        'originBytes': {origin: category.get(origin, {}).get('mallocPrefixBytes') for origin in origins}})
        if workload.get('arm') != 'map-gl': samples[-1]['dataQuality'] = 'INVALID'
    return samples


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--campaign', type=Path, required=True, help='one process with successive increasing workloads')
    parser.add_argument('--noise-campaign', type=Path, action='append', required=True, help='repeat to provide independent matched controls')
    parser.add_argument('--control-loads', type=int, default=60)
    parser.add_argument('--warmup-loads', type=int, default=60)
    parser.add_argument('--minimum-windows', type=int, default=3)
    parser.add_argument('--origin', choices=('agxFragmentVariant', 'agxVertexVariant'), action='append')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    origins = args.origin or ['agxFragmentVariant']
    try:
        cached = {}
        def samples(directory):
            key = str(directory.resolve())
            if key not in cached: cached[key] = load_context_campaign(directory, origins)
            return cached[key]
        controls = [sample for directory in args.noise_campaign for sample in samples(directory) if sample['loads'] == args.control_loads]
        windows = samples(args.campaign)
        result = assess({'metric': METRIC, 'origins': origins, 'controls': controls, 'windows': windows}, args.warmup_loads, args.minimum_windows)
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        result = {'schema': 1, 'dataQuality': 'INVALID', 'boundednessGate': 'UNVERIFIED', 'releaseDisposition': 'UNRESOLVED_P0', 'programAcceptance': 'NOT_ACCEPTED', 'issues': [str(error)]}
    output = json.dumps(result, indent=2, allow_nan=False) + '\n'
    if args.output:
        if args.output.exists(): parser.error('output must be fresh to preserve previous evidence')
        args.output.parent.mkdir(parents=True, exist_ok=True); args.output.write_text(output)
    else: print(output, end='')
    return 0 if result.get('boundednessGate') == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
