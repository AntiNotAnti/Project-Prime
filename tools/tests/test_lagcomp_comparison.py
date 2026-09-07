"""A/B comparisons fail closed when either run changes its workload or policy."""
import copy
import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'run-lagcomp-comparison.py'
spec = importlib.util.spec_from_file_location('lagcomp_comparison', SCRIPT)
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class ComparisonTests(unittest.TestCase):
    def pair(self):
        on = {field: [] for field in comparison.EQUAL_FIELDS}
        on.update(Config=dict(Enabled=True, ProjectileCatchUpEnabled=True, Scenario='trace', Seed=7, DelayTicks=6,
                              JitterTicks=2, LossPerThousand=30),
                  Shots=[dict(Command=10, Seed=123, Flags=0)], RootShots=1,
                  QueueDrops=0, CombatDropped=0, ActualLagCompEnabled=True,
                  ActualProjectileCatchUpEnabled=True, ExpectedPolicy='HistoricalTrace',
                  Hits=[], DamageEvents=0, TotalDamage=0)
        off = copy.deepcopy(on)
        off['Config']['Enabled'] = off['Config']['ProjectileCatchUpEnabled'] = False
        off['ActualLagCompEnabled'] = off['ActualProjectileCatchUpEnabled'] = False
        return on, off

    def test_equal_workload_allows_different_hit_outcomes(self):
        on, off = self.pair()
        on.update(Hits=[dict(Command=10, Amount=5)], DamageEvents=1, TotalDamage=5)
        result = comparison.compare(on, off)
        self.assertEqual(result['on_total_damage'], 5)
        self.assertEqual(result['baseline_total_damage'], 0)

    def test_seed_and_shot_count_mismatch_are_rejected(self):
        for field, value in [('Shots', [dict(Command=10, Seed=124, Flags=0)]), ('RootShots', 2)]:
            with self.subTest(field=field):
                on, off = self.pair()
                off[field] = value
                with self.assertRaisesRegex(ValueError, field):
                    comparison.compare(on, off)

    def test_each_workload_field_must_match(self):
        for field in ('ScriptHash', 'Deliveries', 'Accepted', 'TargetTrajectory', 'Origin'):
            with self.subTest(field=field):
                on, off = self.pair()
                off[field] = 'changed'
                with self.assertRaisesRegex(ValueError, field):
                    comparison.compare(on, off)

    def test_actual_policy_is_checked(self):
        on, off = self.pair()
        off['ActualLagCompEnabled'] = True
        with self.assertRaisesRegex(ValueError, 'Actual server policy'):
            comparison.compare(on, off)

    def test_trace_only_preserves_trace_damage_and_checks_flags(self):
        on, baseline = self.pair()
        baseline['Config']['Enabled'] = baseline['ActualLagCompEnabled'] = True
        comparison.compare(on, baseline, baseline='trace-only')
        baseline['Hits'] = [dict(Command=10, Amount=9)]
        with self.assertRaisesRegex(ValueError, 'historical trace outcomes'):
            comparison.compare(on, baseline, baseline='trace-only')
        baseline['Hits'] = []
        baseline['ActualProjectileCatchUpEnabled'] = True
        with self.assertRaisesRegex(ValueError, 'Actual server policy'):
            comparison.compare(on, baseline, baseline='trace-only')

    def test_trace_only_allows_projectile_damage_changes(self):
        on, baseline = self.pair()
        on['Config']['Scenario'] = baseline['Config']['Scenario'] = 'travel'
        on['ExpectedPolicy'] = baseline['ExpectedPolicy'] = 'ProjectileCatchUp'
        baseline['Config']['Enabled'] = baseline['ActualLagCompEnabled'] = True
        on['Hits'] = [dict(Command=10, Amount=5)]
        comparison.compare(on, baseline, baseline='trace-only')

    def test_overflows_and_empty_shots_are_rejected(self):
        for field in ('QueueDrops', 'CombatDropped'):
            with self.subTest(field=field):
                on, off = self.pair()
                on[field] = 1
                with self.assertRaisesRegex(ValueError, 'overflow'):
                    comparison.compare(on, off)
        on, off = self.pair()
        on['Shots'] = off['Shots'] = []
        on['RootShots'] = off['RootShots'] = 0
        with self.assertRaisesRegex(ValueError, 'No accepted root shots'):
            comparison.compare(on, off)

    def test_charged_exclusion_does_not_hide_uncharged_precursor(self):
        on, off = self.pair()
        for result in (on, off):
            result['Config']['Scenario'] = 'area'
            result['ExpectedPolicy'] = 'None'
            result['Shots'].append(dict(Command=20, Seed=456, Flags=1))
            result['RootShots'] = 2
        on['Hits'] = [dict(Command=10, Amount=5)]
        comparison.compare(on, off)
        on['Hits'].append(dict(Command=20, Amount=5))
        with self.assertRaisesRegex(ValueError, 'Excluded root shots'):
            comparison.compare(on, off)

    def test_repeated_on_run_checks_outcomes_too(self):
        on, _ = self.pair()
        repeated = copy.deepcopy(on)
        comparison.compare(on, repeated, repeat_on=True)
        repeated['TotalDamage'] = 1
        with self.assertRaisesRegex(ValueError, 'deterministic'):
            comparison.compare(on, repeated, repeat_on=True)


if __name__ == '__main__':
    unittest.main()
