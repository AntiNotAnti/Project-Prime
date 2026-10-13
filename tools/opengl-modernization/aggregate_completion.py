"""Aggregate validated completion evidence without running native work.

Reported percentiles are averages of run/window summaries, never pooled
percentiles. Profile windows use summarize_completion.py measurement alignment.
"""
import argparse
import importlib.util
import json
import pathlib
import statistics

ROOT = pathlib.Path(__file__).resolve().parents[2]
METRICS = ('meanCompletedFrameFps', 'frameIntervalP95Ms', 'frameIntervalP99Ms')


def mean(values):
    values = list(values)
    return statistics.mean(values) if values and all(v is not None for v in values) else None


def measured_passes(raw):
    after = raw.split('NATIVERENDER measurement-start', 1)[1]
    frames = [json.loads(line.split('[glframe-json]', 1)[1])
              for line in after.splitlines() if '[glframe-json]' in line][2:]
    ids = {frame['window'] for frame in frames}
    passes = [json.loads(line.split('[glpasses-json]', 1)[1])
              for line in raw.splitlines() if '[glpasses-json]' in line]
    return [p for p in passes if p['window'] in ids]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prefix', default='completion-final')
    parser.add_argument('--report', type=pathlib.Path,
                        default=ROOT / 'docs/performance/opengl-completion-results.json')
    parser.add_argument('--output', type=pathlib.Path)
    args = parser.parse_args()
    if not args.prefix or any(c in args.prefix for c in '/\\') or args.prefix in ('.', '..'):
        parser.error('prefix must be a filename component')
    spec = importlib.util.spec_from_file_location('completion_summary',
                                                pathlib.Path(__file__).with_name('summarize_completion.py'))
    summarizer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(summarizer)
    report = json.loads(args.report.read_text())
    if not report.get('complete') or report.get('errors') or report.get('pending'):
        parser.error('report must be complete without errors or pending evidence')
    if report.get('prefix') != args.prefix:
        parser.error('report prefix differs from requested prefix')
    runs = [r for r in report['runs'] if r['name'].startswith(args.prefix + '-')]
    result = {
        'schema': 1, 'prefix': args.prefix, 'report': str(args.report.resolve()),
        'method': 'Equal-weight arithmetic mean of per-run harness metrics; CPU/counters are equal-weight means of each run median across complete 240-sample measurement-aligned windows. Exclude first two glframe windows after NATIVERENDER measurement-start, then align glpasses by window ID. These are summary statistics, not pooled percentiles. Missing metrics remain null.',
        'groups': {}, 'profile': {},
    }
    groups = ('same', 'cross', 'profile', 'shadow-low', 'shadow-high', 'shadow-ultra')
    for group in groups:
        selected = [r for r in runs if (r['group'] == group if not group.startswith('shadow-')
                    else r['group'] == 'shadow' and r['configuration']['shadows'].lower() == group[7:])]
        if not selected:
            continue
        entry = {}
        for arm in ('baseline', 'candidate'):
            arm_runs = [r for r in selected if r['arm'] == arm]
            entry[arm] = {'runs': len(arm_runs), **{metric: mean(r['harness'].get(metric) for r in arm_runs)
                                                   for metric in METRICS}}
        entry['candidateChangePercent'] = {
            metric: (entry['candidate'][metric] / entry['baseline'][metric] - 1) * 100
            if entry['candidate'][metric] is not None and entry['baseline'][metric] not in (None, 0)
            else None for metric in METRICS}
        result['groups'][group] = entry
    for arm in ('baseline', 'candidate'):
        per_run = []
        for run in runs:
            if run['group'] != 'profile' or run['arm'] != arm:
                continue
            raw_result = pathlib.Path(run['rawResult'])
            if not raw_result.is_absolute():
                raw_result = ROOT / raw_result
            validated, _ = summarizer.read(raw_result)
            if validated['harness'] != run['harness'] or validated['assemblyMvid'] != run['assemblyMvid']:
                raise ValueError('Report differs from validated raw evidence: ' + run['name'])
            rows = measured_passes(raw_result.with_suffix('.log').read_text())
            if len(rows) != validated['profile']['retainedWindows']:
                raise ValueError('Profile window count differs from established selector')
            cpu = {p: {metric: summarizer.med(row['cpu'][p][metric] for row in rows)
                       for metric in ('p50Ms', 'p95Ms', 'p99Ms')} for p in rows[0]['cpu']}
            keys = set().union(*(row['counters'] for row in rows))
            counters = {key: statistics.median(row['counters'][key] for row in rows)
                        if all(key in row['counters'] for row in rows) else None for key in sorted(keys)}
            per_run.append({'name': run['name'], 'completePostWarmupWindows': len(rows),
                            'cpu': cpu, 'counters': counters})
        if not per_run:
            continue
        cpu_keys = set().union(*(r['cpu'] for r in per_run))
        counter_keys = set().union(*(r['counters'] for r in per_run))
        result['profile'][arm] = {
            'perRun': per_run,
            'cpu': {p: {metric: mean(r['cpu'].get(p, {}).get(metric) for r in per_run)
                       for metric in ('p50Ms', 'p95Ms', 'p99Ms')} for p in sorted(cpu_keys)},
            'counters': {key: mean(r['counters'].get(key) for r in per_run) for key in sorted(counter_keys)},
        }
    output = args.output or ROOT / 'artifacts/opengl-modernization' / (args.prefix + '-aggregate.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'output': str(output.resolve()), 'groups': result['groups']}, indent=2))


if __name__ == '__main__':
    main()
