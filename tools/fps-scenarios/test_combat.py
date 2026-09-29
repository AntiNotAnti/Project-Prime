import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('check_combat', Path(__file__).with_name('check_combat.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CombatComparisonTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.scenario = dict(name='single', nativeTicks=2, inputs=[dict(tick=0, fire=True), dict(tick=1, fire=False)])
        self.native = [dict(frame=2, fire=True, shot=True, timeSinceShot=0, chargeLevel=1, weapon=0),
                       dict(frame=4, fire=False, shot=False, timeSinceShot=1, chargeLevel=0, weapon=0)]
        self.prime = [dict(frame=1, fire=True, shot=True, timeSinceShot=0, chargeLevel=1, weapon=0),
                      dict(frame=2, fire=True, shot=False, timeSinceShot=1, chargeLevel=2, weapon=0),
                      dict(frame=3, fire=False, shot=False, timeSinceShot=2, chargeLevel=0, weapon=0),
                      dict(frame=4, fire=False, shot=False, timeSinceShot=3, chargeLevel=0, weapon=0)]

    def compare(self):
        (self.root/'scenario.json').write_text(json.dumps(self.scenario))
        for name, rows in [('native', self.native), ('prime', self.prime)]:
            (self.root/name).write_text(''.join(json.dumps(row)+'\n' for row in rows))
        return module.run(self.root/'scenario.json', self.root/'native', self.root/'prime')

    def test_valid_mapping_retains_actual_event(self):
        result = self.compare()
        self.assertTrue(result['passed'])
        self.assertEqual(result['primeActualShotFrames'], [1])
        self.assertEqual(result['primeShotsAtNativeBoundaries'], [2])

    def test_late_extra_missing_shots_fail(self):
        baseline = copy.deepcopy(self.prime)
        for replacement in [(False, False, True, False), (True, True, False, False), (False, False, False, False)]:
            with self.subTest(replacement=replacement):
                self.prime = copy.deepcopy(baseline)
                for row, shot in zip(self.prime, replacement):
                    row['shot'] = shot
                    row['timeSinceShot'] = 0 if shot else 5
                if any(replacement): self.assertFalse(self.compare()['passed'])
                else:
                    with self.assertRaises(ValueError): self.compare()

    def test_wrong_charge_and_unconsumed_input_fail(self):
        self.prime[1]['chargeLevel'] = 1
        self.prime[2]['fire'] = True
        result = self.compare()
        self.assertFalse(result['passed'])
        self.assertEqual(len(result['failures']), 2)

    def test_missing_duplicate_and_invalid_samples_fail(self):
        baseline = copy.deepcopy(self.prime)
        variants = [baseline[:-1], [baseline[0]] * 4,
                    [baseline[0] | {'shot': 1}] + baseline[1:],
                    [baseline[0] | {'chargeLevel': float('nan')}] + baseline[1:],
                    [baseline[0] | {'weapon': 1}] + baseline[1:]]
        for rows in variants:
            with self.subTest(rows=rows):
                self.prime = rows
                with self.assertRaises(ValueError): self.compare()


class ProjectileComparisonTests(unittest.TestCase):
    def setUp(self):
        CombatComparisonTests.setUp(self)
        beam = dict(slot=0, weapon=0, flags=4096, position=dict(x=1,y=2,z=3),
                    spawnPosition=dict(x=0,y=2,z=3), velocity=dict(x=1,y=0,z=0), age=1/30, lifespan=1)
        for row in self.native + self.prime:
            row['projectiles'] = [copy.deepcopy(beam)]

    def compare(self):
        CombatComparisonTests.compare(self)  # Write the complete timelines.
        from check_projectiles import run
        return run(self.root/'scenario.json', self.root/'native', self.root/'prime')

    def test_matching_projectiles(self):
        self.assertTrue(self.compare()['passed'])

    def test_trajectory_and_lifecycle_differences_fail(self):
        self.prime[1]['projectiles'][0]['position']['x'] += .001
        self.prime[3]['projectiles'] = []
        result = self.compare()
        self.assertFalse(result['passed'])
        self.assertEqual(result['mismatchCount'], 2)

    def test_nonfinite_and_duplicate_slots_rejected(self):
        self.prime[1]['projectiles'][0]['velocity']['x'] = float('nan')
        with self.assertRaises(ValueError): self.compare()
        self.prime[1]['projectiles'] = self.native[0]['projectiles'] * 2
        with self.assertRaises(ValueError): self.compare()


if __name__ == '__main__':
    unittest.main()
