import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('analyze', Path(__file__).with_name('analyze.py'))
analyze = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyze)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.context = dict(assemblyMvid='same', machine='host', renderer='gpu', glVersion='2.1',
                            room='MP9 CRYOCHASM', playerCount=1, width=1280, height=720,
                            shadows='on', renderScale=1, cap=-1, vsync='off', flags={'experiment': False})
        self.visual = dict(source='native-game-on-device', passed=True, separateCapture=True, captures=['capture-manifest.json'])

    def log(self, name, scale=1, gpu=True, mutate=None, windows=5, wrapper=False):
        lines = []
        for i in range(windows):
            f = dict(schema=1, samples=240, window=i, warmup=i == 0, context=copy.deepcopy(self.context),
                     renderCpuP95Ms=5, renderCpuP99Ms=6, presentWallP99Ms=1,
                     frameIntervalP95Ms=8 * scale, frameIntervalP99Ms=10 * scale,
                     onePercentLowEstimateFps=100 / scale, simulationHz=60,
                     droppedSimulationSteps=0, stalls=0)
            if mutate:
                mutate(f)
            lines.append('[glframe-json] ' + json.dumps(f))
            lines.append('[glpasses-json] ' + json.dumps(dict(schema=1, samples=240, window=i, warmup=i == 0,
                         cpu={'prepare': dict(samples=240, p50Ms=1, p95Ms=2, p99Ms=3)}, allocatedBytes={'p50': 0}, counters={})))
            if gpu:
                lines.append('[glgpu-json] ' + json.dumps(dict(schema=1, samples=240, window=i,
                             **gpu if isinstance(gpu, dict) else dict(worldGpuP99Ms=5, droppedQueries=0))))
        path = Path(self.tmp.name) / name
        raw = '\n'.join(lines)
        path.write_text(json.dumps({'stdout': raw}) if wrapper else raw)
        return path

    def test_promote_same_binary_separate_visual(self):
        result = analyze.evaluate(self.log('a'), self.log('b', .95, wrapper=True), 1, self.visual)
        self.assertEqual(result['decision'], 'promote')
        self.assertEqual(result['windows']['candidate'], 4)

    def test_missing_visual_or_gpu_never_promotes(self):
        a, b = self.log('a'), self.log('b', .95, gpu=False)
        result = analyze.evaluate(a, b, 1, self.visual)
        self.assertEqual(result['gpuStatus'], 'UNVERIFIED')
        self.assertEqual(result['decision'], 'keep experimental')
        self.assertFalse(analyze.evaluate(a, self.log('c', .95), 1)['gates']['nativeVisualAccepted'])

    def test_insufficient_post_warmup(self):
        with self.assertRaises(ValueError):
            analyze.evaluate(self.log('a', windows=4), self.log('b'), 1)

    def test_mismatch_and_declared_experiment(self):
        def flag(f):
            f['context']['flags']['experiment'] = True
        a, b = self.log('a'), self.log('b', .95, mutate=flag)
        with self.assertRaises(ValueError):
            analyze.evaluate(a, b, 1, self.visual)
        self.assertEqual(analyze.evaluate(a, b, 1, self.visual, 'experiment')['decision'], 'promote')
        def machine(f):
            f['context']['machine'] = 'other'
        with self.assertRaises(ValueError):
            analyze.evaluate(a, self.log('c', mutate=machine), 1, self.visual, 'experiment')

    def test_nonfinite_bool_and_zero_metrics_rejected(self):
        for value in (float('nan'), float('inf'), True, -1, 0):
            with self.subTest(value=value), self.assertRaises(ValueError):
                analyze.read_run(self.log('bad', mutate=lambda f: f.update(frameIntervalP99Ms=value)), 1)

    def test_gpu_unsupported_dropped_and_pass_regression(self):
        for gpu in (dict(pass_='shadow', p99Ms=5, droppedQueries=1),
                    dict(p99Ms=None, supported=False, droppedQueries=0)):
            self.assertEqual(analyze.evaluate(self.log('a'), self.log('b', .95, gpu=gpu), 1, self.visual)['decision'], 'keep experimental')
        gpu = {'pass': 'world', 'p99Ms': 6, 'supported': True, 'droppedQueries': 0}
        self.assertFalse(analyze.evaluate(self.log('a'), self.log('b', .95, gpu=gpu), 1, self.visual)['gates']['gpuWithinThreePercent'])

    def test_extended_gpu_prefix_preserves_legacy_and_normalizes_names(self):
        a, b = self.log('a'), self.log('b', .95)
        for path, pass_name in ((a, 'World'), (b, 'world')):
            lines = []
            for i in range(5):
                lines.append('[glgpupass-json] ' + json.dumps(dict(schema=1, samples=240, window=i,
                             **{'pass': pass_name, 'p99Ms': 5, 'supported': True, 'droppedQueries': 0})))
            path.write_text(path.read_text() + '\n' + '\n'.join(lines))
        result = analyze.evaluate(a, b, 1, self.visual)
        self.assertEqual(result['decision'], 'promote')
        self.assertEqual(result['gpuP99Ms']['candidate'], {'world': 5})
        b.write_text(b.read_text() + '\n[glgpupass-json] ' + json.dumps(dict(schema=1, supported=False,
                     reason='unsupported', p95Ms=None)))
        self.assertEqual(analyze.evaluate(a, b, 1, self.visual)['gpuStatus'], 'UNVERIFIED')

    def test_absent_cpu_scopes_remain_null_and_missing_all_stays_experimental(self):
        a, b = self.log('a'), self.log('b', .95)
        for path in (a, b):
            path.write_text(path.read_text().replace('"p99Ms": 3}', '"p99Ms": 3}, "Shadow": {"samples": 0, "p50Ms": null, "p95Ms": null, "p99Ms": null}'))
        self.assertEqual(analyze.evaluate(a, b, 1, self.visual)['decision'], 'promote')
        b.write_text('\n'.join(line for line in b.read_text().splitlines() if '[glpasses-json]' not in line))
        self.assertFalse(analyze.evaluate(a, b, 1, self.visual)['gates']['cpuPassTelemetryComplete'])

    def test_actual_cap_sentinels(self):
        self.assertEqual(analyze.evaluate(self.log('a'), self.log('b', .95), 1, self.visual)['decision'], 'promote')
        self.context['cap'] = 0
        self.assertFalse(analyze.evaluate(self.log('a'), self.log('b', .95), 1, self.visual)['gates']['unconstrainedCapture'])
        self.context['cap'] = -2
        with self.assertRaises(ValueError):
            analyze.read_run(self.log('bad'), 1)

    def test_interleaved_gpu_passes_filter_warmup_independently(self):
        a, b = self.log('a'), self.log('b', .95)
        for path in (a, b):
            lines = [line for line in path.read_text().splitlines() if '[glgpu-json]' not in line]
            lines.append('[glgpupass-json] ' + json.dumps(dict(schema=1, supported=True, samples=0)))
            for i in range(5):
                for name in ('World', 'Shadow'):
                    lines.append('[glgpupass-json] ' + json.dumps(dict(schema=1, samples=240,
                        **{'pass': name, 'p99Ms': 100 if i == 0 else 5, 'supported': True, 'droppedQueries': 0})))
            path.write_text('\n'.join(lines))
        result = analyze.evaluate(a, b, 1, self.visual)
        self.assertEqual(result['gpuP99Ms']['baseline'], {'world': 5, 'shadow': 5})
        self.assertEqual(result['decision'], 'promote')
        original = b.read_text()
        b.write_text(original + '\n[glgpupass-json] ' + json.dumps(dict(schema=1, supported=True, disjoint=True, samples=0, droppedQueries=1)))
        self.assertEqual(analyze.evaluate(a, b, 1, self.visual)['gpuStatus'], 'UNVERIFIED')
        b.write_text(original)
        lines = b.read_text().splitlines()
        lines.pop()
        b.write_text('\n'.join(lines))
        self.assertEqual(analyze.evaluate(a, b, 1, self.visual)['gpuStatus'], 'UNVERIFIED')

    def test_context_fields_and_simulation_safety(self):
        a = self.log('a')
        for key in analyze.CONTEXT:
            if key == 'flags':
                continue
            with self.subTest(key=key), self.assertRaises(ValueError):
                analyze.evaluate(a, self.log('b', mutate=lambda f: f['context'].pop(key)), 1)
        for key, value in (('simulationHz', 55), ('droppedSimulationSteps', 1), ('stalls', 1)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                analyze.read_run(self.log('b', mutate=lambda f: f.update({key: value})), 1)

    def test_invalid_cpu_pass_and_duplicate_gpu_window(self):
        path = self.log('a')
        raw = path.read_text().replace('"p99Ms": 3', '"p99Ms": NaN')
        path.write_text(raw)
        with self.assertRaises(ValueError):
            analyze.read_run(path, 1)
        path = self.log('a')
        gpu_line = next(line for line in path.read_text().splitlines() if '[glgpu-json]' in line and '"window": 1' in line)
        path.write_text(path.read_text() + '\n' + gpu_line)
        with self.assertRaises(ValueError):
            analyze.evaluate(path, self.log('b', .95), 1, self.visual)

    def test_cap_bound_and_p95_regression(self):
        self.context['cap'] = 144
        self.assertEqual(analyze.evaluate(self.log('a'), self.log('b', .95), 1, self.visual)['decision'], 'keep experimental')
        self.context['cap'] = -1
        b = self.log('b', .95, mutate=lambda f: f.update(frameIntervalP95Ms=9))
        self.assertFalse(analyze.evaluate(self.log('a'), b, 1, self.visual)['gates']['frameP95WithinFivePercent'])


if __name__ == '__main__':
    unittest.main()
