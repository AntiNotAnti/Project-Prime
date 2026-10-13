#!/usr/bin/env python3
"""Fail-closed matched native OpenGL baseline/candidate evidence review."""
import argparse
import json
import math
from pathlib import Path
import statistics
import sys

PREFIXES = {'[glframe-json]': 'frame', '[glpasses-json]': 'passes', '[glgpu-json]': 'gpu', '[glgpupass-json]': 'gpuPass'}
CONTEXT = ('assemblyMvid', 'machine', 'renderer', 'glVersion', 'room', 'playerCount',
           'width', 'height', 'shadows', 'renderScale', 'cap', 'vsync', 'flags')
FRAME_METRICS = ('renderCpuP95Ms', 'renderCpuP99Ms', 'presentWallP99Ms',
                 'frameIntervalP95Ms', 'frameIntervalP99Ms', 'onePercentLowEstimateFps', 'simulationHz')


def number(value, label, positive=False):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0 or (positive and value == 0):
        raise ValueError('invalid metric: ' + label)
    return value


def validate_tree(value, label):
    if isinstance(value, dict):
        for key, item in value.items():
            validate_tree(item, label + '.' + key)
    elif value is not None:
        number(value, label)


def read_run(path, warmup_windows):
    raw = Path(path).read_text(encoding='utf-8')
    try:
        wrapper = json.loads(raw)
    except json.JSONDecodeError:
        wrapper = None
    if isinstance(wrapper, dict):
        raw = wrapper.get('stdout', '')
        if isinstance(raw, list):
            raw = '\n'.join(raw)
        if not isinstance(raw, str):
            raise ValueError('harness stdout must be text')
    if 'GL error' in raw or '[glvisualcheck] FAIL' in raw:
        raise ValueError('GL error or visual failure in capture')
    records = {kind: [] for kind in PREFIXES.values()}
    for line in raw.splitlines():
        for prefix, kind in PREFIXES.items():
            if prefix in line:
                record = json.loads(line.split(prefix, 1)[1].strip())
                if record.get('schema') != 1:
                    raise ValueError('unsupported telemetry schema')
                records[kind].append(record)
                break
    frames = records['frame']
    if not frames:
        raise ValueError('no frame windows')
    # Explicit count discards the first N completed windows. A tagged warmup
    # window is always excluded, even when it lies beyond that count.
    selected = [f for i, f in enumerate(frames) if i >= warmup_windows and f.get('warmup') is not True]
    if len(selected) < 4:
        raise ValueError('need four completed windows after warmup')
    ids = [f.get('window', i) for i, f in enumerate(frames)]
    if len(set(ids)) != len(ids):
        raise ValueError('duplicate frame window')
    kept = {ids[i] for i, f in enumerate(frames) if i >= warmup_windows and f.get('warmup') is not True}
    contexts = []
    for frame in selected:
        if frame.get('samples') != 240:
            raise ValueError('frame window must contain 240 presentations')
        context = dict(frame.get('context', {}))
        context.setdefault('machine', context.get('machineIdentity'))
        context.setdefault('flags', frame.get('flags'))
        if any(context.get(k) is None or context.get(k) == '' for k in CONTEXT):
            raise ValueError('incomplete comparison context')
        if not isinstance(context['flags'], dict):
            raise ValueError('flags must be an explicit dictionary')
        contexts.append({k: context[k] for k in CONTEXT})
        for key, value in context.items():
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                if key != 'cap':
                    number(value, 'context.' + key)
                elif not math.isfinite(value) or value < -1:
                    raise ValueError('invalid cap sentinel')
        for key, value in frame.items():
            if key.endswith(('Ms', 'Fps', 'Hz')) and value is not None:
                number(value, key)
        for key in FRAME_METRICS:
            number(frame.get(key), key, positive=True)
        if abs(frame['simulationHz'] - 60) > 1.5 or frame.get('droppedSimulationSteps') != 0 or frame.get('stalls') != 0:
            raise ValueError('simulation rate, dropped steps or stalls failed')
    filtered = {'frame': selected, 'context': contexts}
    filtered['gpuUnavailable'] = any(r.get('supported') is False or r.get('disjoint') is True
                                     or (r.get('samples') == 0 and r.get('droppedQueries', 0) != 0)
                                     for r in records['gpuPass'])
    for kind in ('passes', 'gpu', 'gpuPass'):
        items = records[kind]
        filtered[kind] = []
        occurrences = {}
        for i, original in enumerate(items):
            if original.get('supported') is False or (kind == 'gpuPass' and original.get('samples') == 0):
                continue
            group = str(original.get('pass', 'world')).lower() if kind in ('gpu', 'gpuPass') else kind
            ordinal = occurrences.get(group, 0)
            occurrences[group] = ordinal + 1
            # Legacy query windows have no frame ID. Count independently per
            # pass, never across interleaved GPU pass lines/status messages.
            inferred = ids[ordinal] if ordinal < len(ids) else None
            window = original.get('window', inferred)
            if window in kept and original.get('warmup') is not True:
                record = dict(original)
                record['window'] = window
                filtered[kind].append(record)
        for r in filtered[kind]:
            number(r.get('samples'), kind + '.samples', positive=True)
            if kind == 'passes':
                if r['samples'] != 240 or not isinstance(r.get('cpu'), dict) or not r['cpu']:
                    raise ValueError('incomplete CPU pass window')
                for name, stats in r['cpu'].items():
                    if not isinstance(stats, dict):
                        raise ValueError('invalid CPU pass statistics')
                    number(stats.get('samples'), name + '.samples')
                    if stats['samples'] > 240:
                        raise ValueError('CPU pass samples exceed window')
                    for metric in ('p50Ms', 'p95Ms', 'p99Ms'):
                        if stats['samples'] > 0:
                            number(stats.get(metric), name + '.' + metric)
                validate_tree(r['cpu'], 'cpu')
                validate_tree(r.get('allocatedBytes', {}), 'allocatedBytes')
                validate_tree(r.get('counters', {}), 'counters')
            else:
                for key in ('p50Ms', 'p95Ms', 'p99Ms', 'worldGpuP50Ms', 'worldGpuP95Ms', 'worldGpuP99Ms', 'droppedQueries'):
                    if r.get(key) is not None:
                        number(r[key], key)
    pass_ids = [r.get('window', i) for i, r in enumerate(filtered['passes'])]
    if len(pass_ids) != len(set(pass_ids)):
        raise ValueError('duplicate CPU pass window')
    filtered['frameWindowIds'] = kept
    return filtered


def med(frames, metric):
    return statistics.median(f[metric] for f in frames)


def gpu_summary(run):
    if run['gpuUnavailable']:
        return None
    groups = {}
    for r in run['gpuPass'] or run['gpu']:
        groups.setdefault(str(r.get('pass', 'world')).lower(), []).append(r)
    if not groups:
        return None
    result = {}
    for name, items in groups.items():
        window_ids = [r.get('window', i) for i, r in enumerate(items)]
        if len(window_ids) != len(set(window_ids)):
            raise ValueError('duplicate GPU pass window')
        if set(window_ids) != run['frameWindowIds']:
            return None
        if len(items) < 4 or any(r.get('supported', True) is not True or r.get('droppedQueries') != 0 for r in items):
            return None
        values = [r.get('p99Ms', r.get('worldGpuP99Ms')) for r in items]
        if any(v is None for v in values):
            return None
        result[name] = statistics.median(number(v, name, positive=True) for v in values)
    return result


def evaluate(baseline, candidate, warmup_windows, visual=None, experiment_flag=None):
    if warmup_windows < 0:
        raise ValueError('warmup count must be nonnegative')
    a, b = (read_run(path, warmup_windows) for path in (baseline, candidate))
    fingerprints = []
    for run in (a, b):
        for context in run['context']:
            context = dict(context)
            context['flags'] = dict(context['flags'])
            if experiment_flag:
                context['flags'].pop(experiment_flag, None)
            fingerprints.append(json.dumps(context, sort_keys=True))
    if len(set(fingerprints)) != 1:
        raise ValueError('build, machine, workload, graphics settings or undeclared flags differ')
    af, bf = a['frame'], b['frame']
    improvement = 1 - med(bf, 'frameIntervalP99Ms') / med(af, 'frameIntervalP99Ms')
    ag, bg = gpu_summary(a), gpu_summary(b)
    gpu_verified = ag is not None and bg is not None and ag.keys() == bg.keys()
    gpu_ok = gpu_verified and all(bg[k] <= ag[k] * 1.03 for k in ag)
    visual_ok = (isinstance(visual, dict) and visual.get('source') == 'native-game-on-device'
                 and visual.get('passed') is True and visual.get('separateCapture') is True
                 and isinstance(visual.get('captures'), list) and bool(visual['captures']))
    unconstrained = a['context'][0]['cap'] == -1 and str(a['context'][0]['vsync']).lower() in ('off', 'false', '0')
    gates = {'cpuPassTelemetryComplete': len(a['passes']) == len(af) and len(b['passes']) == len(bf),
             'frameP99ImprovesThreePercent': improvement >= .03 - 1e-12,
             'frameP95WithinFivePercent': med(bf, 'frameIntervalP95Ms') <= med(af, 'frameIntervalP95Ms') * 1.05,
             'onePercentLowWithinTwoPercent': med(bf, 'onePercentLowEstimateFps') >= med(af, 'onePercentLowEstimateFps') * .98,
             'gpuWithinThreePercent': bool(gpu_ok), 'nativeVisualAccepted': visual_ok,
             'unconstrainedCapture': unconstrained}
    return {'schema': 1, 'decision': 'promote' if all(gates.values()) else 'keep experimental',
            'automaticActivation': False, 'gpuStatus': 'verified' if gpu_verified else 'UNVERIFIED',
            'p99FrameImprovementFraction': improvement, 'gates': gates,
            'windows': {'baseline': len(af), 'candidate': len(bf)},
            'cpuPassWindows': {'baseline': a['passes'], 'candidate': b['passes']},
            'gpuP99Ms': {'baseline': ag, 'candidate': bg}}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for flag in ('baseline', 'candidate', 'output'):
        parser.add_argument('--' + flag, type=Path, required=True)
    parser.add_argument('--warmup-windows', type=int, required=True)
    parser.add_argument('--visual-evidence', type=Path)
    parser.add_argument('--experiment-flag')
    args = parser.parse_args(argv)
    try:
        visual = json.loads(args.visual_evidence.read_text()) if args.visual_evidence else None
        report = evaluate(args.baseline, args.candidate, args.warmup_windows, visual, args.experiment_flag)
        status = 0
    except (ValueError, TypeError, KeyError, OSError) as exc:
        report, status = {'schema': 1, 'decision': 'invalid evidence', 'automaticActivation': False, 'error': str(exc)}, 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(report['decision'])
    return status


if __name__ == '__main__':
    sys.exit(main())
