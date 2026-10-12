"""G1 activity evidence must demonstrate ordinary client activity and fail closed."""
import json
from pathlib import Path
import tempfile
import unittest

from net_ai_evidence import audit_campaign


def fixture():
    settings = dict(ai_net_client=2, ai_seed=1729, players=2, server_bot=False)
    reports = {'manifest.json': {'settings': settings},
               'server.json': dict(Role='server', HitClaims={'Received': 3, 'AppliedHere': 2},
                                   CombatPayments={'LifetimePayments': 2},
                                   Windows=[{'BotMask': 0}], Shots=[])}
    processes = [dict(name='server', exit=0, timedOut=False)]
    for slot in range(2):
        name = 'peer-' + str(slot)
        key = dict(AuthorityEpoch=123, MatchId=1, ShooterSlot=slot,
                   Generation=1, LifeId=1, ShotId=1)
        reports[name + '.json'] = dict(Role='simulation-client', AiClient=dict(
            Enabled=True, IsBot=False, RosterBot=False, LocalSlot=slot, Level=2,
            Seed=1729+slot, Travel=3.5, InputFrames=120, DecisionFrames=120,
            SpawnedLives=1, AltFrames=0, Weapons=[0, 1]),
            Shots=[{'Stage': 'offered', 'Key': key}], HitClaims={'Declared': 2},
            Pickups=[dict(Stage='owner-native-consumption', Epoch=123, Match=1, Slot=slot,
                          Generation=1, Life=1, IsBot=False, X=0.0, Y=0.0, Z=0.0)])
        processes.append(dict(name=name, exit=0, timedOut=False))
    return reports, processes


class AiEvidenceTests(unittest.TestCase):
    def audit(self, reports, processes=None, legacy=None):
        with tempfile.TemporaryDirectory() as directory:
            for name, value in reports.items():
                (Path(directory) / name).write_text(json.dumps(value))
            return audit_campaign(directory, processes or fixture()[1], legacy or {'status': 'PASS'})

    def test_positive_native_client_shape_passes(self):
        reports, processes = fixture()
        result = self.audit(reports, processes)
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['metrics']['componentPayments'], 2)
        self.assertEqual([row['offeredShots'] for row in result['clients']], [1, 1])

    def test_lifecycle_coverage_does_not_invent_respawns_or_deaths(self):
        reports, _ = fixture()
        reports['peer-0.json']['AiClient'].update(SpawnedLives=3, AltFrames=25)
        reports['peer-0.json']['States'] = [{'Hp': 0, 'Deaths': 7}, {'Hp': 100, 'Deaths': 7}]
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        life = result['clients'][0]['lifecycleEvidence']
        self.assertEqual(life['spawnedLives']['observed'], 3)
        self.assertEqual(life['spawnedLives']['status'], 'OBSERVED')
        self.assertEqual(life['altForms']['observed'], 25)
        self.assertEqual(life['altForms']['status'], 'OBSERVED')
        for name in ('deathEvents', 'respawnEvents', 'altTransitions'):
            self.assertIsNone(life[name]['observed'])
            self.assertEqual(life[name]['status'], 'NOT_RUN')

    def test_zero_lifecycle_activity_is_not_run_without_changing_activity_gate(self):
        reports, _ = fixture()
        reports['peer-0.json']['AiClient'].update(SpawnedLives=0, AltFrames=0)
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        life = result['clients'][0]['lifecycleEvidence']
        self.assertEqual(life['spawnedLives']['status'], 'NOT_RUN')
        self.assertEqual(life['altForms']['status'], 'NOT_RUN')
        self.assertEqual(life['altForms']['observed'], 0)

    def test_missing_report_and_counter_fail(self):
        for field in ('report', 'counter', 'ai'):
            reports, _ = fixture()
            if field == 'report':
                del reports['peer-0.json']
            elif field == 'counter':
                del reports['server.json']['CombatPayments']['LifetimePayments']
            else:
                del reports['peer-0.json']['AiClient']
            self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_zero_required_denominators_are_incomplete(self):
        for group, field in (('CombatPayments', 'LifetimePayments'), ('HitClaims', 'Received')):
            reports, _ = fixture()
            reports['server.json'][group][field] = 0
            self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')
        reports, _ = fixture()
        for slot in range(2):
            reports[f'peer-{slot}.json']['Pickups'] = []
            reports[f'peer-{slot}.json']['HitClaims']['Declared'] = 0
        self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')

    def test_idle_missing_shots_and_wrong_local_shooter_are_incomplete(self):
        for field in ('InputFrames', 'DecisionFrames', 'Travel', 'Shots', 'ShooterSlot'):
            reports, _ = fixture()
            peer = reports['peer-0.json']
            if field == 'Shots':
                peer['Shots'] = []
            elif field == 'ShooterSlot':
                peer['Shots'][0]['Key']['ShooterSlot'] = 7
            else:
                peer['AiClient'][field] = 0
            self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')

    def test_forged_bot_flags_and_roster_fail(self):
        for field in ('IsBot', 'RosterBot', 'Enabled'):
            reports, _ = fixture()
            reports['peer-0.json']['AiClient'][field] = field != 'Enabled'
            self.assertEqual(self.audit(reports)['status'], 'FAIL')
        reports, _ = fixture()
        reports['server.json']['Windows'][0]['BotMask'] = 1
        self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_forged_client_report_role_fails(self):
        reports, _ = fixture()
        reports['peer-0.json']['Role'] = 'server'
        self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_duplicate_slot_and_process_fail(self):
        reports, processes = fixture()
        reports['peer-1.json']['AiClient']['LocalSlot'] = 0
        self.assertEqual(self.audit(reports)['status'], 'FAIL')
        reports, processes = fixture()
        processes[-1]['name'] = 'peer-0'
        self.assertEqual(self.audit(reports, processes)['status'], 'FAIL')

    def test_wrong_seed_and_invalid_configured_seed_fail(self):
        reports, _ = fixture()
        reports['peer-0.json']['AiClient']['Seed'] = 1730
        self.assertEqual(self.audit(reports)['status'], 'FAIL')
        for seed in (0, -1, True, 4294967295):
            reports, _ = fixture()
            reports['manifest.json']['settings']['ai_seed'] = seed
            for slot in range(2):
                reports[f'peer-{slot}.json']['AiClient']['Seed'] = seed + slot
            self.assertEqual(self.audit(reports)['status'], 'FAIL', seed)

    def test_malformed_denominators_fail(self):
        for value in (None, True, -1, 1.5, '2', float('nan'), float('inf')):
            with self.subTest(value=value):
                reports, _ = fixture()
                reports['server.json']['CombatPayments']['LifetimePayments'] = value
                self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_shot_identity_counts_do_not_depend_on_json_key_order(self):
        reports, _ = fixture()
        shot = reports['peer-0.json']['Shots'][0]
        reports['peer-0.json']['Shots'].append(
            dict(Stage='offered', Key=dict(reversed(list(shot['Key'].items())))))
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['clients'][0]['offeredShots'], 1)

    def test_malformed_shot_identity_cannot_prove_real_shots(self):
        for value in (True, 0, -1, None, '1'):
            with self.subTest(value=value):
                reports, _ = fixture()
                reports['peer-0.json']['Shots'][0]['Key']['ShotId'] = value
                self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_missing_roster_samples_cannot_pass(self):
        reports, _ = fixture()
        reports['server.json']['Windows'] = []
        self.assertNotEqual(self.audit(reports)['status'], 'PASS')

    def test_native_pickup_owner_life_role_fail(self):
        for field, value in (('Slot', 7), ('Life', 0), ('IsBot', True)):
            reports, _ = fixture()
            reports['peer-0.json']['Pickups'][0][field] = value
            self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_legacy_failure_cannot_be_overridden(self):
        reports, _ = fixture()
        self.assertEqual(self.audit(reports, legacy={'status': 'INCOMPLETE'})['status'], 'FAIL')

    def test_requested_server_bot_requires_separate_slot_and_shots(self):
        reports, _ = fixture()
        reports['manifest.json']['settings']['server_bot'] = True
        self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')
        reports['server.json']['Windows'][0]['BotMask'] = 4
        key = dict(AuthorityEpoch=123, MatchId=1, ShooterSlot=2, Generation=1, LifeId=1, ShotId=9)
        reports['server.json']['Shots'] = [{'Stage': 'accepted', 'Key': key}]
        self.assertEqual(self.audit(reports)['status'], 'PASS')


if __name__ == '__main__':
    unittest.main()
