#!/usr/bin/env python3
"""ABBA comparison of real native RmlUi CPU work; not GPU/frame-rate results."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import time

from check import Bridge, FIXTURES, encode, seed

WIDTH, HEIGHT, DENSITY = 1280, 720, 1.0
CYCLES = 20


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def measure(call):
    start = time.perf_counter_ns()
    result = call()
    return result, (time.perf_counter_ns() - start) / 1_000_000


def distribution(values):
    values = sorted(values)
    return dict(samples=len(values), median_ms=statistics.median(values),
                p95_ms=values[max(0, math.ceil(len(values)*0.95)-1)],
                minimum_ms=values[0], maximum_ms=values[-1]) if values else None


def summarize(rows):
    output = {}
    for path in FIXTURES:
        samples = [row for row in rows if row['path'] == path]
        if not samples:
            continue
        page = {key: distribution([row[key] for row in samples])
                for key in ['open_ms', 'publication_ms', 'update_render_ms', 'close_ms']}
        page['idle_update_render_ms'] = distribution([v for row in samples for v in row['idle_update_render_ms']])
        page['draw_commands'] = dict(minimum=min(row['draw_commands'] for row in samples),
                                     maximum=max(row['draw_commands'] for row in samples),
                                     median=statistics.median(row['draw_commands'] for row in samples))
        output[path] = page
    return output


def run_arm(bridge, assets, label, arm_index):
    arm = dict(index=arm_index, variant=label, assets=str(assets), samples=[], errors=[])
    initialized, arm['initialize_ms'] = measure(lambda: bridge.initialize_backend(WIDTH, HEIGHT, DENSITY, encode(assets), 1))
    if not initialized:
        arm['errors'].append('Native CPU backend initialization failed')
        return arm
    try:
        # Warm each same fixture once before collecting samples. This is a
        # process-local warm benchmark; font/resource caches are not cold-start.
        for path in FIXTURES:
            doc = bridge.document_open(encode(path), 0)
            if not doc:
                arm['errors'].append('Warm open failed: ' + path)
                continue
            try:
                bridge.document_show(doc, 1)
                bridge.document_show(bridge.home_document(), 0)
                seed(bridge, doc, path)
                bridge.update()
                bridge.render(WIDTH, HEIGHT)
            finally:
                bridge.document_close(doc)
                bridge.update()
        for cycle in range(CYCLES):
            for path in FIXTURES:
                row = dict(path=path, cycle=cycle)
                doc, row['open_ms'] = measure(lambda: bridge.document_open(encode(path), 0))
                if not doc:
                    arm['errors'].append(f'Open failed cycle {cycle}: {path}')
                    continue
                try:
                    bridge.document_show(doc, 1)
                    # Closing the prior page restores the compatibility home.
                    bridge.document_show(bridge.home_document(), 0)
                    _, row['publication_ms'] = measure(lambda: seed(bridge, doc, path))
                    _, row['update_render_ms'] = measure(lambda: (bridge.update(), bridge.render(WIDTH, HEIGHT)))
                    row['draw_commands'] = bridge.draw_command_count()
                    row['geometry_count'] = bridge.draw_geometry_count()
                    row['texture_count'] = bridge.draw_texture_count()
                    row['unsupported_features'] = bridge.draw_features()
                    row['idle_update_render_ms'] = []
                    for _ in range(3):
                        _, elapsed = measure(lambda: (bridge.update(), bridge.render(WIDTH, HEIGHT)))
                        row['idle_update_render_ms'].append(elapsed)
                    arm['samples'].append(row)
                except Exception as error:
                    arm['errors'].append(f'Cycle {cycle} {path}: {error!r}')
                finally:
                    closed, row['close_ms'] = measure(lambda: bridge.document_close(doc))
                    if not closed:
                        arm['errors'].append(f'Close failed cycle {cycle}: {path}')
                    bridge.update()
        arm['remaining_documents'] = bridge.document_count()
        if arm['remaining_documents'] != 1:
            arm['errors'].append('Expected only compatibility home after closing all fixture pages')
    finally:
        bridge.shutdown()
    arm['per_page'] = summarize(arm['samples'])
    return arm


def main():
    global FIXTURES
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--library', type=Path, required=True)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--page', action='append', choices=sorted(FIXTURES), help='Repeat to benchmark only selected known fixtures; default is all fixtures')
    args = parser.parse_args()
    if args.page:
        FIXTURES = {path: FIXTURES[path] for path in dict.fromkeys(args.page)}
    library = args.library.resolve()
    variants = dict(before=args.before.resolve(), after=args.after.resolve())
    report = dict(schema_version=1, measurement='Actual native CPU draw-list backend 1; no GPU/framerate claim',
                  selected_fixture_paths=list(FIXTURES),
                  framebuffer=dict(width=WIDTH, height=HEIGHT, density=DENSITY),
                  arm_order=['before', 'after', 'after', 'before'], cycles_per_arm=CYCLES,
                  warmup='Each fixture opened, published, updated, rendered and closed once per arm',
                  timing_scope='Open measures document parsing/composition. Publication measures synthetic service binding writes. Update/render measures native CPU layout and draw-list capture. Idle is three immediate native update/render calls.',
                  library=str(library), library_sha256=sha(library),
                  benchmark_sha256=sha(Path(__file__).resolve()),
                  fixture_harness_sha256=sha(Path(__file__).resolve().with_name('check.py')),
                  assets={label: dict(root=str(root), markup_sha256={p.relative_to(root).as_posix(): sha(p)
                          for p in sorted(root.rglob('*')) if p.suffix in ['.rml', '.rcss']}) for label, root in variants.items()},
                  limitations=['Single process; font and resource caches may persist across arms. ABBA ordering balances order effects but does not eliminate environmental noise.',
                               'Synthetic service fixtures with real native markup/rendering; no physical input or end-to-end service claim.',
                               'Immediate samples measure CPU call costs, not display cadence, game-model rendering or GPU performance.'], arms=[])
    try:
        bridge = Bridge(library)
        for index, label in enumerate(report['arm_order']):
            report['arms'].append(run_arm(bridge, variants[label], label, index))
        report['overall_per_page'] = {label: summarize([row for arm in report['arms'] if arm['variant'] == label for row in arm['samples']])
                                      for label in variants}
        report['median_change_percent'] = {}
        for path in FIXTURES:
            before = report['overall_per_page']['before'].get(path)
            after = report['overall_per_page']['after'].get(path)
            if before and after:
                report['median_change_percent'][path] = {key: 100*(after[key]['median_ms']/before[key]['median_ms']-1)
                    for key in ['open_ms', 'publication_ms', 'update_render_ms', 'idle_update_render_ms'] if before[key]['median_ms'] > 0}
        report['summary'] = dict(arms=len(report['arms']), samples=sum(len(a['samples']) for a in report['arms']),
                                  errors=sum(len(a['errors']) for a in report['arms']))
    except Exception as error:
        report['fatal_error'] = repr(error)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    print(json.dumps(report.get('summary', {'fatal_error': report.get('fatal_error')})))
    return int('fatal_error' in report or report.get('summary', {}).get('errors', 0) > 0)


if __name__ == '__main__':
    raise SystemExit(main())
