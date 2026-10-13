import argparse
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import run_minimal_gl_controls as runner


class MinimalRunnerTests(unittest.TestCase):
    def test_phase_matrix_distinguishes_contexts_from_draw_iterations(self):
        self.assertEqual(len(runner.expected_phases('fresh-contexts', 2)), 6)
        self.assertEqual(len(runner.expected_phases('no-shader', 2)), 6)
        for mode in ('compile-only', 'compile-delete', 'rebind-draw'):
            self.assertEqual(len(runner.expected_phases(mode, 30)), 3)

    def test_dry_run_preserves_explicit_inputs_without_native_start(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            binary = root / 'native-render-check.dll'
            binary.write_bytes(b'harness')
            (root / 'ProjectPrime.dll').write_bytes(b'client')
            output = root / 'new-campaign'
            argv = ['runner', '--binary', str(binary), '--output', str(output), '--mode', 'fresh-contexts', '--variant', 'tiny', '--iterations', '2',
                    '--define', 'CONTROL=1', '--bind', 'probe:1', '--sampler-unit', '2', '--depth-vertex', '--capture-native-stacks', '--compress-stacks', '--dry-run']
            with patch('sys.argv', argv), patch.object(runner.subprocess, 'check_output', side_effect=['commit\n', '']), patch.object(runner.subprocess, 'Popen') as native:
                self.assertEqual(runner.main(), 0)
                native.assert_not_called()
            data = json.loads((output / 'runner-manifest.json').read_text())
            self.assertEqual(len(data['expectedNativePhases']), 6)
            self.assertIn('--minimal-gl-phase-acks', data['command'])
            self.assertIn('CONTROL=1', data['command'])
            self.assertIn('probe:1', data['command'])
            self.assertTrue(data['compressStacks'])
            self.assertEqual(data['sharedContexts'], 'NOT_RUN')
            self.assertEqual(data['boundedness'], 'UNVERIFIED')

    def test_existing_output_rejected_before_native_start(self):
        with tempfile.TemporaryDirectory() as temp, patch('sys.argv', ['runner', '--binary', 'unused', '--output', temp]), patch.object(runner.subprocess, 'Popen') as native:
            with self.assertRaises(SystemExit): runner.main()
            native.assert_not_called()

    def fixture(self, root, mode='compile-delete'):
        binary = root / 'harness.dll'; binary.write_bytes(b'harness')
        identity = {'harnessSha256': runner.sha256(binary), 'clientSha256': 'a' * 64}
        records = [{'context': 0, 'phase': phase} for phase in ('before-workload', 'after-workload', 'after-disposal')]
        for generation in range(1, 3):
            records.extend([{'operation': 'compile', 'status': 1}, {'operation': 'compile', 'status': 1},
                            {'operation': 'link', 'status': 1}, {'operation': 'retire', 'remainingRecognized': 0, 'cleanupErrors': []}])
            if mode != 'compile-only':
                records.extend([{'operation': 'use-program'}, {'operation': 'draw', 'pixels': {'nonBlackPixels': 128, 'changedPixels': None}}])
        result = {**identity, 'processId': 42, 'complete': True, 'error': None, 'completedUnits': 2, 'compileProgramGenerations': 2, 'records': records}
        path = root / 'minimal-gl.json'; path.write_text(json.dumps(result))
        log = root / 'minimal-gl.log'; log.write_text('MINIMALGL PASS minimal-gl.json\n')
        manifest = {'logSha256': runner.sha256(log), 'exitCode': 0, 'complete': True, 'identityMatches': True, 'identity': identity, 'resultSha256': runner.sha256(path),
                    'mode': mode, 'variant': 'tiny', 'iterations': 2, 'captureNativeStacks': False, 'sourceDirty': False, 'captures': []}
        return manifest, result

    def test_valid_without_native_capture_is_unverified(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, result = self.fixture(root)
            validation = runner.validate_minimal_campaign(manifest, root)
            self.assertEqual(validation['status'], 'UNVERIFIED', validation)
            self.assertEqual(validation['issues'], [])
            self.assertEqual(validation['boundedness'], 'UNVERIFIED')

    def test_compile_only_explicitly_allows_zero_draws(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, result = self.fixture(root, 'compile-only')
            self.assertEqual(runner.validate_minimal_campaign(manifest, root)['issues'], [])

    def test_failed_status_gap_and_vacuous_pixels_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, original = self.fixture(root)
            for mutation in (lambda data: data.update(complete=False), lambda data: data.update(completedUnits=1),
                             lambda data: data['records'][3].update(status=0), lambda data: data['records'][-1]['pixels'].update(nonBlackPixels=0),
                             lambda data: data['records'][-1]['pixels'].update(changedPixels=0), lambda data: data['records'].pop(0)):
                result = copy.deepcopy(original); mutation(result)
                path = root / 'minimal-gl.json'; path.write_text(json.dumps(result)); manifest['resultSha256'] = runner.sha256(path)
                self.assertEqual(runner.validate_minimal_campaign(manifest, root)['status'], 'INVALID')

    def test_requested_capture_phases_cannot_be_missing(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, _ = self.fixture(root)
            manifest['captureNativeStacks'] = True
            self.assertEqual(runner.validate_minimal_campaign(manifest, root)['status'], 'INVALID')

    def test_complete_native_capture_valid_and_truncation_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, _ = self.fixture(root)
            manifest['captureNativeStacks'] = True
            for phase in runner.expected_phases('compile-delete', 2):
                steps = []
                for tool in ('vmmap', 'malloc_history'):
                    body = "1 calls for 4096 bytes:\n0 libsystem_malloc.dylib 0x123 malloc + 1\n" if tool == 'malloc_history' else 'Physical footprint: 12M\nREGION TYPE\nMALLOC  16M 8M 4M 0K\n'
                    path = root / (phase + '.' + tool); path.write_text(body)
                    command = [tool, '42'] + (['-allBySize', '-fullStacks'] if tool == 'malloc_history' else ['-summary'])
                    steps.append({'command': command, 'exitCode': 0, 'path': str(path), 'sha256': runner.sha256(path)})
                manifest['captures'].append({'phase': phase, 'pid': 42, 'steps': steps})
            validation = runner.validate_minimal_campaign(manifest, root)
            self.assertEqual(validation['status'], 'VALID', validation)
            step = manifest['captures'][0]['steps'][1]
            path = Path(step['path']); path.write_text('1 calls for 4096 bytes:\n'); step['sha256'] = runner.sha256(path)
            self.assertEqual(runner.validate_minimal_campaign(manifest, root)['status'], 'INVALID')

    def test_changed_result_hash_invalid(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); manifest, _ = self.fixture(root)
            (root / 'minimal-gl.json').write_text('{}')
            self.assertEqual(runner.validate_minimal_campaign(manifest, root)['status'], 'INVALID')


if __name__ == '__main__': unittest.main()
