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

    def uniform_record(self):
        average = dict(requests=3, uploads=2, bytes=128, skips=1)
        return {'counters': {'UniformArrayUploads': 2, 'UniformArrayBytes': 128, 'UniformArraySkips': 1,
                             'LegacyScalarUniformRequests': 5, 'LegacyScalarUniformSubmissions': 2,
                             'LegacyScalarUniformSkips': 3}, 'uniformAttribution': {
            'schema': 1, 'frames': 240, 'programCapacity': 32, 'legacyScalarBackend': 'MEASURED_DESKTOP_FACADE',
            'matrixArraysByProgram': [{'program': 7, 'status': 'TRACKED', 'average': copy.deepcopy(average)}],
            'matrixArraysBySource': [{'pass': 'Opaque', 'owner': 'PaletteMesh', 'origin': 'Setup',
                                     'average': copy.deepcopy(average)}]}}

    def test_uniform_partitions_and_unknown_availability(self):
        record = self.uniform_record()
        draw.validate_uniform_attribution(record)
        record['uniformAttribution']['matrixArraysByProgram'][0].update(program=None, status='CAPACITY_OVERFLOW')
        record['uniformAttribution']['matrixArraysBySource'][0].update({'pass': 'Unscoped', 'owner': 'Unclassified', 'origin': 'Unscoped'})
        record['uniformAttribution']['legacyScalarBackend'] = 'UNVERIFIED_PLATFORM'
        draw.validate_uniform_attribution(record)
        self.assertIsNone(draw.validate_uniform_attribution({}))

    def test_uniform_counter_loss_duplicate_owner_and_false_bytes_rejected(self):
        for change in ('loss', 'duplicate', 'owner', 'bytes', 'scalar', 'partial'):
            record = self.uniform_record()
            value = record['uniformAttribution']
            if change == 'loss':
                record['counters']['UniformArrayUploads'] = 3
            elif change == 'duplicate':
                value['matrixArraysByProgram'].append(copy.deepcopy(value['matrixArraysByProgram'][0]))
            elif change == 'owner':
                value['matrixArraysBySource'][0]['owner'] = 'InventedOwner'
            elif change == 'bytes':
                value['matrixArraysBySource'][0]['average'].update(uploads=0, skips=3)
            elif change == 'scalar':
                record['counters']['LegacyScalarUniformRequests'] = 6
            elif change == 'partial':
                value['frames'] = 239
            with self.subTest(change=change), self.assertRaises(ValueError):
                draw.validate_uniform_attribution(record)

    def test_uniform_program_capacity_never_claims_unbounded_identity(self):
        record = self.uniform_record()
        template = record['uniformAttribution']['matrixArraysByProgram'][0]
        record['uniformAttribution']['matrixArraysByProgram'] = [dict(copy.deepcopy(template), program=i + 1) for i in range(33)]
        with self.assertRaises(ValueError):
            draw.validate_uniform_attribution(record)
