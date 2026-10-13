#!/usr/bin/env python3
"""Validate source attribution and summarize completed native profiling windows."""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import statistics

_spec = importlib.util.spec_from_file_location('draw_analyze', Path(__file__).with_name('analyze.py'))
analyze = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(analyze)
OWNERS = ('Unclassified', 'RoomMesh', 'PaletteMesh', 'SingleTransformMesh', 'FirstPerson',
          'Particle', 'TrailSingle', 'TrailMulti', 'TrailStack', 'ProceduralShape', 'Hud')


def validate_attribution(record):
    value = record.get('drawAttribution')
    if not isinstance(value, dict) or value.get('frames') != 240:
        raise ValueError('missing/incomplete draw attribution window')
    total = analyze.number(value.get('averageImmediateDraws'), 'average immediate draws')
    bins = value.get('averageImmediateDrawsByPassAndOwner')
    if not isinstance(bins, dict):
        raise ValueError('missing immediate source bins')
    classified = 0
    for name, owners in bins.items():
        if not isinstance(owners, dict) or not name:
            raise ValueError('invalid source pass')
        for owner, count in owners.items():
            if owner not in OWNERS:
                raise ValueError('unknown draw owner: ' + owner)
            classified += analyze.number(count, name + '.' + owner)
    expected = analyze.number(record.get('counters', {}).get('ImmediateDraws'), 'executed immediate draws')
    if not math.isclose(total, expected, abs_tol=1e-8, rel_tol=1e-10) or not math.isclose(total, classified, abs_tol=1e-8, rel_tol=1e-10):
        raise ValueError('source bins do not conserve executed immediate draw total')
    cpu = value.get('cpuByOwner')
    if not isinstance(cpu, dict) or set(cpu) != set(OWNERS):
        raise ValueError('missing owner CPU distributions')
    for owner, scopes in cpu.items():
        for kind in ('setupWall', 'geometryWall'):
            stats = scopes.get(kind) if isinstance(scopes, dict) else None
            if not isinstance(stats, dict):
                raise ValueError('missing owner CPU scope')
            samples = analyze.number(stats.get('samples'), owner + '.samples')
            if samples > 240 or samples != int(samples):
                raise ValueError('invalid owner sample count')
            if samples == 0:
                if any(stats.get(p) is not None for p in ('p50Ms', 'p95Ms', 'p99Ms')):
                    raise ValueError('absent owner CPU samples must have null timings')
            else:
                values = [analyze.number(stats.get(p), owner + '.' + p) for p in ('p50Ms', 'p95Ms', 'p99Ms')]
                if values != sorted(values):
                    raise ValueError('unordered owner percentiles')
    return value


def measured_stdout(raw):
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
    marker = 'NATIVERENDER measurement-start'
    if raw.count(marker) != 1:
        raise ValueError('need exactly one native simulation warmup marker')
    before, after = raw.split(marker, 1)
    # Capability/failure status can precede simulation warmup. Preserve it while
    # excluding every warmup CPU/GPU/frame timing window.
    statuses = []
    for line in before.splitlines():
        if '[glgpupass-json]' in line:
            record = json.loads(line.split('[glgpupass-json]', 1)[1].strip())
            if record.get('samples') == 0 or record.get('supported') is False:
                statuses.append(line)
    return '\n'.join(statuses) + '\n' + after


def summarize(path, warmup_windows=2):
    run = analyze.read_run(path, warmup_windows, measured_stdout(Path(path).read_text(encoding='utf-8')))
    if any(context != run['context'][0] for context in run['context']):
        raise ValueError('fixture/context changed during attribution run')
    if {r['window'] for r in run['passes']} != run['frameWindowIds']:
        raise ValueError('attribution must cover every retained presentation window')
    windows = [validate_attribution(r) for r in run['passes']]
    keys = {(p, o) for w in windows for p, owners in w['averageImmediateDrawsByPassAndOwner'].items() for o in owners}
    bins = {}
    for p, o in sorted(keys):
        bins.setdefault(p, {})[o] = statistics.median(w['averageImmediateDrawsByPassAndOwner'].get(p, {}).get(o, 0) for w in windows)
    cpu = {}
    for owner in OWNERS:
        cpu[owner] = {}
        for kind in ('setupWall', 'geometryWall'):
            present = [w['cpuByOwner'][owner][kind] for w in windows if w['cpuByOwner'][owner][kind]['samples']]
            cpu[owner][kind] = {'windowsWithOwner': len(present),
                'medianWindowP95Ms': statistics.median(w['p95Ms'] for w in present) if present else None,
                'medianWindowP99Ms': statistics.median(w['p99Ms'] for w in present) if present else None}
    frame = {key: statistics.median(w[key] for w in run['frame']) for key in analyze.FRAME_METRICS}
    return {'schema': 1, 'purpose': 'attribution-only; no optimization or FPS conclusion',
            'source': str(Path(path).resolve()), 'sourceSha256': hashlib.sha256(Path(path).read_bytes()).hexdigest(),
            'context': run['context'][0], 'retainedWindows': len(windows), 'warmupWindows': warmup_windows,
            'medianWindowImmediateDraws': statistics.median(w['averageImmediateDraws'] for w in windows),
            'medianWindowImmediateByPassAndOwner': bins, 'cpuByOwner': cpu, 'frame': frame,
            'gpuStatus': 'AVAILABLE' if analyze.gpu_summary(run) is not None else 'UNVERIFIED',
            'limits': 'CPU scopes include driver/backpressure wall time and are inclusive, not additive. Owner metadata does not prove immutable geometry. Medians of window percentiles are not pooled percentiles. Present wall is not GPU time. Missing GPU evidence remains unverified.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('log', type=Path)
    parser.add_argument('--warmup-windows', type=int, default=2)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.warmup_windows < 0:
        parser.error('warmup windows must be nonnegative')
    if args.output.exists():
        parser.error('refusing existing evidence overwrite')
    report = summarize(args.log, args.warmup_windows)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print('validated draw attribution:', args.output)


if __name__ == '__main__':
    main()
