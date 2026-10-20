import copy
import unittest
from compare_fidelity import compare

class ComparisonTests(unittest.TestCase):
    def document(self):
        pose = {key: dict(zip(('X', 'Y', 'Z'), values)) for key, values in
                [('Origin', (1, 2, 3)), ('Aim', (0, 0, 1)), ('Direction', (0, 0, 1))]}
        pose['Charge'] = 0
        return {'Schema': 1, 'Decoder': 'Healthy', 'Rows': [dict(Match=1, Epoch=2, Slot=0,
            Generation=1, Life=1, Shot=4, Stage='authored-input', SourceFrame=8, RecordingFrame=10,
            Weapon=1, Pose=pose)]}
    def test_identical(self):
        a = self.document()
        self.assertTrue(compare(a, a)['Passed'])
    def test_carrier_arrival_is_not_source_timing(self):
        a = self.document(); b = copy.deepcopy(a); b['Rows'][0]['RecordingFrame'] += 30
        self.assertTrue(compare(a, b)['Passed'])
    def test_each_identity_component_refuses_timestamp_join(self):
        for key in ('Match', 'Epoch', 'Slot', 'Generation', 'Life', 'Shot'):
            a = self.document(); b = copy.deepcopy(a); b['Rows'][0][key] += 1
            result = compare(a, b)
            self.assertEqual((result['Matched'], len(result['UnmatchedLeft']), len(result['UnmatchedRight'])), (0, 1, 1))
            self.assertFalse(result['Passed'])
    def test_changed_pose_clock_outcome(self):
        a = self.document(); b = copy.deepcopy(a); b['Rows'][0]['SourceFrame'] += 1
        b['Rows'][0]['Pose']['Origin']['X'] += 3
        b['Rows'][0]['Pose']['Direction'] = dict(X=1, Y=0, Z=0)
        result = compare(a, b)
        self.assertFalse(result['Passed']); self.assertEqual(result['Mismatches'], 1)
        self.assertEqual(result['Rows'][0]['OriginDistance'], 3)
        self.assertEqual(result['Rows'][0]['DirectionDegrees'], 90)
    def test_missing_presentation_is_disclosed(self):
        a = self.document(); a['Rows'][0]['Pose'] = None
        self.assertIn('authored-pose', compare(a, a)['Rows'][0]['MissingPresentation'])
    def test_conflicting_duplicate_fails(self):
        a = self.document(); b = copy.deepcopy(a['Rows'][0]); b['SourceFrame'] += 1
        a['Rows'].append(b)
        self.assertFalse(compare(a, a)['Passed'])
    def test_recovered_decoder_and_empty_trace_do_not_pass(self):
        a = self.document(); a['Decoder'] = 'Recovered'
        self.assertFalse(compare(a, a)['Passed'])
        a['Rows'] = []; self.assertFalse(compare(a, a)['Passed'])

if __name__ == '__main__': unittest.main()
