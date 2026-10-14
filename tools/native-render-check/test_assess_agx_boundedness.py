import copy
import unittest

import assess_agx_boundedness as assessor


class BoundednessTests(unittest.TestCase):
    def setUp(self):
        identity = {key: 'identity' for key in assessor.REQUIRED_IDENTITY}
        for key in assessor.HASH_FIELDS: identity[key] = 'a' * (40 if key == 'sourceSha' else 64)
        self.identity = identity
        self.evidence = {'synthetic': True, 'metric': assessor.METRIC, 'origins': ['agxFragmentVariant'],
                         'controls': [self.sample('control-a', 60, 1000), self.sample('control-b', 60, 1010)],
                         'windows': [self.sample('candidate', load, amount) for load, amount in [(0, 20), (60, 1000), (120, 1002), (300, 1005), (600, 1003)]]}

    def sample(self, run, loads, amount):
        return {'runId': run, 'sampleId': run + ':' + str(loads), 'loads': loads, 'context': 0,
                'identity': self.identity.copy(), 'dataQuality': 'VALID', 'captureSucceeded': True,
                'supported': True, 'originBytes': {'agxFragmentVariant': amount}}

    def test_synthetic_stabilization_after_warmup_passes_only_evidence_gate(self):
        result = assessor.assess(self.evidence)
        self.assertEqual(result['boundednessGate'], 'PASS', result)
        self.assertEqual(result['dataQuality'], 'VALID')
        self.assertEqual(result['noiseEnvelope']['origins']['agxFragmentVariant']['envelopeBytes'], 10)
        self.assertEqual(result['postWarmupWindows'], 3)
        self.assertEqual(result['releaseDisposition'], 'SYNTHETIC_TEST_ONLY')
        self.assertEqual(result['programAcceptance'], 'NOT_ACCEPTED')

    def test_two_actual_load_windows_with_growth_cannot_pass(self):
        self.evidence['synthetic'] = False
        self.evidence['windows'] = [self.sample('candidate', 60, 47008432), self.sample('candidate', 300, 235557328)]
        self.evidence['controls'] = [self.sample('control-a', 60, 47008432), self.sample('control-b', 60, 46890192)]
        result = assessor.assess(self.evidence)
        self.assertEqual(result['dataQuality'], 'VALID')
        self.assertEqual(result['boundednessGate'], 'FAIL')
        self.assertEqual(result['releaseDisposition'], 'UNRESOLVED_P0')
        self.assertTrue(result['materialGrowthObserved'])
        self.assertIn('insufficient successive increasing-load windows after warm-up', result['unverified'])
        self.assertEqual(result['noiseEnvelope']['origins']['agxFragmentVariant']['envelopeBytes'], 118240)

    def test_flat_two_windows_still_unverified(self):
        self.evidence['windows'] = self.evidence['windows'][:3]
        result = assessor.assess(self.evidence)
        self.assertEqual(result['boundednessGate'], 'UNVERIFIED')

    def test_noise_controls_must_be_explicit_independent_and_same_target(self):
        original = copy.deepcopy(self.evidence)
        for mutation in (lambda data: data.update(controls=[]), lambda data: data.update(controls=data['controls'][:1]),
                         lambda data: data['controls'][1].update(runId='control-a'), lambda data: data['controls'][1].update(loads=61)):
            evidence = copy.deepcopy(original); mutation(evidence)
            self.assertNotEqual(assessor.assess(evidence)['boundednessGate'], 'PASS')

    def test_candidate_drift_cannot_expand_noise_envelope(self):
        self.evidence['windows'][-1]['originBytes']['agxFragmentVariant'] = 5000
        result = assessor.assess(self.evidence)
        self.assertEqual(result['noiseEnvelope']['origins']['agxFragmentVariant']['envelopeBytes'], 10)
        self.assertEqual(result['boundednessGate'], 'FAIL')

    def test_small_successive_growth_that_accumulates_beyond_noise_fails(self):
        self.evidence['windows'] = [self.sample('candidate', load, amount) for load, amount in [(60, 1000), (120, 1005), (300, 1010), (600, 1015)]]
        result = assessor.assess(self.evidence)
        self.assertEqual(result['boundednessGate'], 'FAIL')
        self.assertEqual(result['postWarmupSpanBytes']['agxFragmentVariant'], 10)
        self.assertTrue(any(any(row['growthExceedsControlEnvelope'].values()) for row in result['comparisons']) is False)
        # The cumulative increase since the warm-up anchor must also fit noise.

    def test_falling_trace_does_not_fabricate_growth_or_establish_plateau(self):
        self.evidence['windows'] = [self.sample('candidate', load, amount) for load, amount in [(60, 2000), (120, 1200), (300, 1100), (600, 1000)]]
        result = assessor.assess(self.evidence)
        self.assertEqual(result['boundednessGate'], 'FAIL')
        self.assertFalse(result['materialGrowthObserved'])
        self.assertTrue(result['materialVariationObserved'])

    def test_hash_fixture_host_phase_and_context_mixing_rejected(self):
        for key in assessor.REQUIRED_IDENTITY:
            evidence = copy.deepcopy(self.evidence)
            evidence['windows'][-1]['identity'][key] = 'b' * (40 if key == 'sourceSha' else 64) if key in assessor.HASH_FIELDS else 'different'
            with self.subTest(key=key):
                result = assessor.assess(evidence)
                self.assertEqual(result['dataQuality'], 'INVALID', result)
                self.assertNotEqual(result['boundednessGate'], 'PASS')

    def test_failed_missing_unsupported_and_null_capture_never_passes(self):
        for mutation in (lambda row: row.update(captureSucceeded=False), lambda row: row.update(supported=False),
                         lambda row: row.update(dataQuality='UNVERIFIED'), lambda row: row['originBytes'].update(agxFragmentVariant=None),
                         lambda row: row['identity'].pop('fixtureSha256')):
            evidence = copy.deepcopy(self.evidence); mutation(evidence['windows'][-1])
            self.assertNotEqual(assessor.assess(evidence)['boundednessGate'], 'PASS')

    def test_rss_only_or_deleted_names_do_not_replace_outstanding_origins(self):
        self.evidence['metric'] = 'rss-minus-gc'
        self.evidence['allGlNamesDeleted'] = True
        self.assertEqual(assessor.assess(self.evidence)['boundednessGate'], 'UNVERIFIED')

    def test_nonincreasing_duplicate_windows_and_process_restarts_never_pass(self):
        original = copy.deepcopy(self.evidence)
        for mutation in (lambda data: data['windows'].reverse(), lambda data: data['windows'].append(data['windows'][-1]),
                         lambda data: data['windows'][-1].update(runId='restarted')):
            evidence = copy.deepcopy(original); mutation(evidence)
            self.assertNotEqual(assessor.assess(evidence)['boundednessGate'], 'PASS')

    def test_unsupported_null_and_observed_zero_are_distinct(self):
        for sample in self.evidence['controls'] + self.evidence['windows']: sample['originBytes']['agxFragmentVariant'] = 0
        self.assertEqual(assessor.assess(self.evidence)['boundednessGate'], 'PASS')
        self.evidence['windows'][-1]['originBytes']['agxFragmentVariant'] = None
        self.assertEqual(assessor.assess(self.evidence)['boundednessGate'], 'UNVERIFIED')

    def test_each_requested_origin_must_stabilize(self):
        self.evidence['origins'].append('agxVertexVariant')
        for row in self.evidence['controls'] + self.evidence['windows']: row['originBytes']['agxVertexVariant'] = 100
        self.evidence['windows'][-1]['originBytes']['agxVertexVariant'] = 101
        self.assertEqual(assessor.assess(self.evidence)['boundednessGate'], 'FAIL')

    def test_reject_weakened_window_policy_nonfinite_bool_negative_and_malformed(self):
        self.assertEqual(assessor.assess(self.evidence, minimum_windows=2)['dataQuality'], 'INVALID')
        for value in (float('nan'), True, -1, {}, 'zero'):
            evidence = copy.deepcopy(self.evidence); evidence['windows'][-1]['originBytes']['agxFragmentVariant'] = value
            self.assertEqual(assessor.assess(evidence)['dataQuality'], 'INVALID')
        self.assertEqual(assessor.assess({'origins': [{}]})['dataQuality'], 'INVALID')
        evidence = copy.deepcopy(self.evidence); evidence['controls'][0]['identity']['sourceSha'] = 'a' * 41
        self.assertEqual(assessor.assess(evidence)['dataQuality'], 'INVALID')

    def test_real_pass_does_not_claim_full_release_or_program_acceptance(self):
        self.evidence['synthetic'] = False
        result = assessor.assess(self.evidence)
        self.assertEqual(result['boundednessGate'], 'PASS')
        self.assertEqual(result['releaseDisposition'], 'UNRESOLVED_P0')
        self.assertEqual(result['programAcceptance'], 'NOT_ACCEPTED')


if __name__ == '__main__': unittest.main()
