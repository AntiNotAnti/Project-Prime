"""Synthetic parser/comparator controls only; no native grant/asset acceptance."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parent))
spec = importlib.util.spec_from_file_location('hunter_tour_runner', Path(__file__).with_name('run_native_grant_hunter_tour.py'))
runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(runner)


def receipt(mode='Off', item='DoubleDamage'):
    life = {'AuthorityEpoch': 1, 'MatchId': 1, 'ShooterSlot': 0, 'Generation': 1, 'LifeId': 1, 'ShotId': 0}
    key = dict(life, ShotId=1)
    spawn = {'Epoch': 1, 'Match': 1, 'Room': 1, 'SpawnId': -1, 'Generation': 1}
    source = 121 if item == 'Deathalt' else 122
    fire = {'ShotId': 1, 'SourceFrame': source, 'SourceLife': 1, 'CanonicalBytes': '01'}
    outcome = {'Damage': 12, 'Fired': 1, 'Semantic': 1, 'ReplayWeaponFired': 1, 'IntentBytes': 'ABCD',
               'ReplayFacts': json.dumps([{'Kind': 6, 'RecordingFrame': 122, 'ServerTick': 122, 'Marker': None, 'Payload': '010100'}])}
    knowledge = 'Granted' if item == 'DoubleDamage' else 'NotGranted'
    certificate = {'Key': key.copy(), 'Fire': fire.copy(), 'Room': 1, 'Lane': 'NativeInput', 'TrustedSource': True,
                   'Unavailable': 'None', 'Components': 1, 'Knowledge': knowledge,
                   'CaptureMode': 'Shadow', 'Source': {'Source': 'Verified', 'Pose': 'Verified'},
                   'LedgerAnswer': {'Knowledge': knowledge, 'Sequence': 1 if item == 'DoubleDamage' else 0},
                   'Damage': {'Present': True, 'Body': 12}, 'AuthorizedDouble': item == 'DoubleDamage',
                   'Funding': {'Spawn': spawn.copy(), 'Life': life.copy(), 'Type': 'DoubleDamage', 'Sequence': 1,
                               'ContactFrame': 121, 'SourceContactFrame': 121, 'ExpiresAtFrame': 1921} if item == 'DoubleDamage' else None}
    timers = [1799, 1798, 0, 0] if item == 'DoubleDamage' else [1800, 1799, 1, 0]
    arm = {'Hunter': 'Samus', 'Item': item, 'Mode': mode, 'ContactFrame': 121, 'EffectiveFrame': 122, 'ExpiryFrame': 1921,
           'NativeFrames': 1802 if item == 'Deathalt' else 1801, 'NativePresentation': True, 'Spawn': spawn, 'Life': life,
           'GrantSequence': 1, 'Shot': {'Key': key, 'Fire': fire, 'Outcome': outcome},
           'Certificate': certificate if mode == 'Shadow' else None,
           'CertificateKnowledge': knowledge if mode == 'Shadow' else None}
    for name, value in zip(['ContactTimer', 'EffectiveTimer', 'LastActiveTimer', 'ExpiredTimer'], timers): arm[name] = value
    return {'schema': 3, 'status': 'PASS', 'completedArms': 1, 'checks': 1, 'hunter': 'Samus', 'item': item, 'mode': mode,
            'assemblySha256': 'engine', 'assemblyMvid': 'engine-mvid', 'entryAssemblySha256': 'entry', 'entryAssemblyMvid': 'entry-mvid', 'arm': arm}


class TourRunnerTests(unittest.TestCase):
    def validate(self, data):
        return runner.validate_arm(data, 'Samus', data['item'], data['mode'], 'engine', 'engine-mvid', 'entry')

    def test_complete_native_matrix_is_fixed(self):
        self.assertEqual(len(runner.HUNTERS) * len(runner.ITEMS) * len(runner.MODES), 42)

    def test_mode_only_instrumentation_difference_is_allowed(self):
        off, shadow = self.validate(receipt()), self.validate(receipt('Shadow'))
        self.assertTrue(runner.compare_pair(off, shadow))

    def test_real_room_incarnation_is_never_normalized(self):
        off, shadow = receipt()['arm'], receipt('Shadow')['arm']
        shadow['Spawn']['Room'] = 2
        with self.assertRaisesRegex(ValueError, 'exact native'):
            runner.compare_pair(off, shadow)

    def test_raw_replay_intent_and_fire_differences_are_not_hidden(self):
        for path in [('ReplayFacts',), ('IntentBytes',), ('Fire',)]:
            off, shadow = receipt()['arm'], receipt('Shadow')['arm']
            if path[0] == 'Fire': shadow['Shot']['Fire']['CanonicalBytes'] = '02'
            else: shadow['Shot']['Outcome'][path[0]] += '00'
            with self.assertRaises(ValueError): runner.compare_pair(off, shadow)

    def test_no_zero_positive_or_partial_arm_can_pass(self):
        for mutate in [lambda d: d.update(completedArms=0), lambda d: d['arm']['Shot']['Outcome'].update(Damage=0),
                       lambda d: d['arm']['Shot']['Outcome'].update(ReplayWeaponFired=0), lambda d: d['arm'].update(NativeFrames=1)]:
            data = receipt(); mutate(data)
            with self.assertRaises(ValueError): self.validate(data)

    def test_actual_loaded_dll_alias_fails(self):
        data = receipt(); data['entryAssemblySha256'] = 'different'
        with self.assertRaisesRegex(ValueError, 'identity mismatch'): self.validate(data)

    def test_exact_funding_identity_is_required(self):
        data = receipt('Shadow'); data['arm']['Certificate']['Funding']['Spawn']['Generation'] = 2
        with self.assertRaisesRegex(ValueError, 'exact genuine'): self.validate(data)

    def test_raw_source_ledger_and_capture_policy_falsifiers_fail(self):
        mutations = [lambda d: d['arm']['Shot']['Key'].update(MatchId=2),
                     lambda d: d['arm']['Shot']['Key'].update(ShooterSlot=2),
                     lambda d: d['arm']['Certificate']['LedgerAnswer'].update(Sequence=2),
                     lambda d: d['arm']['Certificate'].update(CaptureMode='Enforce'),
                     lambda d: d['arm']['Certificate']['Source'].update(Source='Unavailable'),
                     lambda d: d['arm']['Certificate']['Source'].update(Pose='Unavailable')]
        for mutate in mutations:
            data = receipt('Shadow'); mutate(data)
            with self.assertRaises(ValueError): self.validate(data)

    def test_cloak_and_deathalt_are_not_double_funding(self):
        for item in ['Cloak', 'Deathalt']:
            data = receipt('Shadow', item)
            self.validate(data)
            data['arm']['Certificate']['AuthorizedDouble'] = True
            with self.assertRaisesRegex(ValueError, 'must not authorize Double'): self.validate(data)


if __name__ == '__main__':
    unittest.main()
