import copy
import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('draw_attribution', Path(__file__).with_name('draw_attribution.py'))
draw = importlib.util.module_from_spec(spec)
spec.loader.exec_module(draw)


class AttributionTests(unittest.TestCase):
    def record(self):
        zero = {'samples': 0, 'p50Ms': None, 'p95Ms': None, 'p99Ms': None}
        return {'counters': {'ImmediateDraws': 10}, 'drawAttribution': {
            'frames': 240, 'averageImmediateDraws': 10,
            'averageImmediateDrawsByPassAndOwner': {'Opaque': {'Particle': 2}, 'Hud': {'Hud': 7}, 'Unscoped': {'Unclassified': 1}},
            'cpuByOwner': {owner: {'setupWall': copy.deepcopy(zero), 'geometryWall': copy.deepcopy(zero)} for owner in draw.OWNERS}}}

    def test_simulation_warmup_removed_but_gpu_failure_status_preserved(self):
        before = '[glframe-json] invalid warmup CPU window'
        status = '[glgpupass-json] ' + json.dumps({'schema': 1, 'samples': 0, 'supported': False})
        after = '[glframe-json] measured window'
        raw = before + '\n' + status + '\nNATIVERENDER measurement-start\n' + after
        result = draw.measured_stdout(json.dumps({'stdout': raw.splitlines()}))
        self.assertNotIn(before, result)
        self.assertIn(status, result)
        self.assertIn(after, result)

    def test_missing_duplicate_or_invalid_harness_stdout_rejected(self):
        for raw in ('no warmup marker', 'NATIVERENDER measurement-start NATIVERENDER measurement-start', json.dumps({'stdout': 42})):
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                draw.measured_stdout(raw)

    def test_complete_partition_and_absent_cpu(self):
        self.assertEqual(draw.validate_attribution(self.record())['averageImmediateDraws'], 10)

    def test_lost_double_or_unrecognized_draws_fail_closed(self):
        for change in ('lost', 'doubled', 'unknown'):
            record = self.record()
            bins = record['drawAttribution']['averageImmediateDrawsByPassAndOwner']['Opaque']
            if change == 'lost':
                bins['Particle'] = 1
            elif change == 'doubled':
                bins['Particle'] = 3
            else:
                bins['InventedOwner'] = bins.pop('Particle')
            with self.subTest(change=change), self.assertRaises(ValueError):
                draw.validate_attribution(record)

    def test_partial_window_nonfinite_and_false_absence_rejected(self):
        for field, value in (('frames', 239), ('averageImmediateDraws', float('nan'))):
            record = self.record()
            record['drawAttribution'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                draw.validate_attribution(record)
        record = self.record()
        record['drawAttribution']['cpuByOwner']['Hud']['setupWall']['p95Ms'] = 0
        with self.assertRaises(ValueError):
            draw.validate_attribution(record)

    def test_owner_timing_is_optional_but_complete_and_ordered_when_present(self):
        record = self.record()
        stats = record['drawAttribution']['cpuByOwner']['Particle']['geometryWall']
        stats.update(samples=240, p50Ms=1, p95Ms=2, p99Ms=3)
        draw.validate_attribution(record)
        stats['samples'] = 241
        with self.assertRaises(ValueError):
            draw.validate_attribution(record)
        stats['samples'] = 240
        stats['p99Ms'] = 1
        with self.assertRaises(ValueError):
            draw.validate_attribution(record)
