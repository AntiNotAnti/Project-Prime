"""G1 activity evidence must demonstrate ordinary client activity and fail closed."""
import json
import struct
from pathlib import Path
import tempfile
import unittest

from net_ai_evidence import audit_campaign, canonical_lifecycle


def semantic(event, kind, actor=(255, 0, 0), target=(255, 0, 0), tick=None):
    tick = event if tick is None else tick
    raw = struct.pack('<BHQIIBBHHBHHiBiiBI', 55, 1, 123, event, tick, kind,
                      *actor, *target, -1, 0, -1, 0, 0, 0)
    return dict(Type=55, Epoch=123, Match=1, Id=event, Tick=tick,
                Baseline=False, Award=False, AwardFrontier=0, Payload=raw.hex())


def fixture():
    settings = dict(ai_net_client=2, ai_seed=1729, players=2, server_bot=False)
    reports = {'manifest.json': {'settings': settings},
               'server.json': dict(Role='server', HitClaims={'Received': 3, 'AppliedHere': 2},
                                   CampaignClaims={'Received': 3, 'Applied': 2},
                                   CombatPayments={'LifetimePayments': 2, 'PaidShotKeys': [], 'EvidenceOverflow': 0},
                                   Windows=[{'BotMask': 0}], Shots=[])}
    processes = [dict(name='server', exit=0, timedOut=False)]
    for slot in range(2):
        name = 'peer-' + str(slot)
        key = dict(AuthorityEpoch=123, MatchId=1, ShooterSlot=slot,
                   Generation=1, LifeId=1, ShotId=1)
        reports[name + '.json'] = dict(Role='simulation-client', AiClient=dict(
            Enabled=True, IsBot=False, RosterBot=False, LocalSlot=slot, Level=2,
            Seed=1729+slot, Travel=3.5, InputFrames=120, DecisionFrames=120,
            InvalidPositionSamples=0, InvalidDistanceSamples=0,
            SpawnedLives=1, AltFrames=0, Weapons=[0, 1]),
            Shots=[{'Stage': 'offered', 'Key': key}], HitClaims={'Declared': 2},
            Pickups=[dict(Stage='owner-native-consumption', Epoch=123, Match=1, Slot=slot,
                          Generation=1, Life=1, IsBot=False, X=0.0, Y=0.0, Z=0.0)])
        processes.append(dict(name=name, exit=0, timedOut=False))
        reports['server.json']['CombatPayments']['PaidShotKeys'].append(dict(key))
    return reports, processes


class AiEvidenceTests(unittest.TestCase):
    def lifetime_claims(self, value):
        return dict(ClientDeclared=value, ClientDeclaredScope='ProcessLifetime',
                    ClientDeclaredCountingUnit='New client claim declarations; excludes retries and survives match reset')

    def test_lifetime_client_declarations_survive_final_round_zero(self):
        reports, _ = fixture()
        for slot, value in enumerate((7, 2)):
            peer = reports[f'peer-{slot}.json']
            peer['HitClaims']['Declared'] = 0
            peer['CampaignClaims'] = self.lifetime_claims(value)
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['metrics']['declaredClientClaims'], 9)
        self.assertEqual([c['declaredClaimsScope'] for c in result['clients']], ['ProcessLifetime'] * 2)

    def test_old_final_round_zero_stays_incomplete(self):
        reports, _ = fixture()
        for slot in range(2):
            reports[f'peer-{slot}.json']['HitClaims']['Declared'] = 0
            reports[f'peer-{slot}.json']['CampaignClaims'] = {'Received': 0, 'Applied': 0}
        result = self.audit(reports)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['declaredClientClaims'], 0)
        self.assertTrue(all(c['declaredClaimsScope'] == 'LegacyPerMatch' for c in result['clients']))

    def test_zero_lifetime_cannot_fall_back_to_positive_legacy(self):
        reports, _ = fixture()
        for slot in range(2):
            reports[f'peer-{slot}.json']['HitClaims']['Declared'] = 0
            reports[f'peer-{slot}.json']['CampaignClaims'] = self.lifetime_claims(0)
        self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')
        reports['peer-0.json']['HitClaims']['Declared'] = 1
        self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_lifetime_count_rejects_bool_negative_fraction_overflow_and_null(self):
        for value in (True, False, -1, 0.5, '2', None, 2**63):
            with self.subTest(value=value):
                reports, _ = fixture()
                reports['peer-0.json']['CampaignClaims'] = self.lifetime_claims(value)
                self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_partial_or_forged_lifetime_scope_never_falls_back(self):
        for field in ('ClientDeclared', 'ClientDeclaredScope', 'ClientDeclaredCountingUnit'):
            reports, _ = fixture()
            claims = self.lifetime_claims(3)
            del claims[field]
            reports['peer-0.json']['CampaignClaims'] = claims
            self.assertEqual(self.audit(reports)['status'], 'FAIL')
        for field in ('ClientDeclaredScope', 'ClientDeclaredCountingUnit'):
            for value in (True, None, 'PerMatch', 'retried packets'):
                reports, _ = fixture()
                reports['peer-0.json']['CampaignClaims'] = self.lifetime_claims(3)
                reports['peer-0.json']['CampaignClaims'][field] = value
                self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_malformed_client_campaign_object_fails(self):
        for value in (True, 3, [], None, 'claims'):
            reports, _ = fixture()
            reports['peer-0.json']['CampaignClaims'] = value
            self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_mixed_old_and_new_reports_keep_per_client_scope(self):
        reports, _ = fixture()
        reports['peer-0.json']['HitClaims']['Declared'] = 0
        reports['peer-0.json']['CampaignClaims'] = self.lifetime_claims(7)
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['metrics']['declaredClientClaims'], 9)
        self.assertEqual([c['declaredClaimsScope'] for c in result['clients']], ['ProcessLifetime', 'LegacyPerMatch'])
        reports['peer-0.json']['CampaignClaims']['ClientDeclared'] = True
        self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_authority_counter_is_not_a_client_declaration_fallback(self):
        reports, _ = fixture()
        for slot in range(2):
            peer = reports[f'peer-{slot}.json']
            peer['HitClaims']['Declared'] = 0
            peer['CampaignClaims'] = {'Received': 300, 'Applied': 200}
        result = self.audit(reports)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['declaredClientClaims'], 0)

    def test_invalid_spatial_sample_counters_required_and_zero(self):
        for field in ('InvalidPositionSamples', 'InvalidDistanceSamples'):
            for value in (1, None, True, -1, 0.5, '0'):
                with self.subTest(field=field, value=value):
                    reports, _ = fixture()
                    reports['peer-0.json']['AiClient'][field] = value
                    self.assertEqual(self.audit(reports)['status'], 'FAIL')
            reports, _ = fixture()
            del reports['peer-0.json']['AiClient'][field]
            self.assertEqual(self.audit(reports)['status'], 'FAIL')
        reports, _ = fixture()
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['clients'][0]['invalidPositionSamples'], 0)
        self.assertEqual(result['clients'][0]['invalidDistanceSamples'], 0)
    def test_canonical_lifecycle_deduplicates_and_joins_real_respawn(self):
        facts = [semantic(1, 3, (0, 1, 1)), semantic(2, 4, (1, 1, 1), (0, 1, 1)),
                 semantic(3, 3, (0, 1, 2)), semantic(4, 3, (1, 1, 2))]
        life = canonical_lifecycle(facts + [facts[1]])
        self.assertEqual((len(life['spawns']), len(life['deaths']), len(life['respawns'])), (3, 1, 1))
        reports, _ = fixture()
        reports['server.json']['Facts'] = facts
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['clients'][0]['lifecycleEvidence']['deathEvents']['observed'], 1)
        self.assertEqual(result['clients'][0]['lifecycleEvidence']['respawnEvents']['observed'], 1)
        self.assertEqual(result['clients'][1]['lifecycleEvidence']['respawnEvents']['status'], 'NOT_RUN')

    def test_canonical_lifecycle_rejects_conflicts_and_forged_header(self):
        original = semantic(1, 3, (0, 1, 1))
        changed = semantic(1, 3, (0, 1, 2))
        with self.assertRaises(ValueError):
            canonical_lifecycle([original, changed])
        for field in ('Epoch', 'Match', 'Id', 'Tick'):
            forged = dict(original)
            forged[field] += 1
            with self.assertRaises(ValueError):
                canonical_lifecycle([forged])
        with self.assertRaises(ValueError):
            canonical_lifecycle([semantic(1, 4, (1, 1, 1), (255, 0, 0))])

    def test_unjoined_life_change_is_not_respawn_and_suicide_counts_once(self):
        facts = [semantic(1, 6, (0, 1, 1), (0, 1, 1)), semantic(2, 3, (0, 2, 2))]
        life = canonical_lifecycle(facts)
        self.assertEqual(len(life['deaths']), 1)
        self.assertEqual(life['respawns'], [])

    def test_respawn_life_and_tick_wrap_have_serial_number_order(self):
        facts = [semantic(1, 6, (0, 1, 65535), (0, 1, 65535), tick=0xffffffff),
                 semantic(2, 3, (0, 1, 1), tick=0)]
        self.assertEqual(len(canonical_lifecycle(facts)['respawns']), 1)
        stale = [semantic(1, 6, (0, 1, 2), (0, 1, 2)), semantic(2, 3, (0, 1, 1))]
        self.assertEqual(canonical_lifecycle(stale)['respawns'], [])

    def test_paid_source_keys_separate_ai_damage_from_bot_and_components(self):
        reports, _ = fixture()
        reports['manifest.json']['settings']['server_bot'] = True
        reports['server.json']['Windows'][0]['BotMask'] = 4
        ai = reports['peer-0.json']['Shots'][0]['Key']
        bot = dict(ai, ShooterSlot=2)
        reports['server.json']['Shots'] = [{'Stage': 'accepted', 'Key': bot}]
        reports['server.json']['CombatPayments'].update(PaidShotKeys=[ai, ai, bot], EvidenceOverflow=0)
        result = self.audit(reports)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['metrics']['paidAiSourceShotKeys'], 1)
        self.assertEqual(result['metrics']['componentPayments'], 2)
        self.assertEqual(result['clients'][1]['paidSourceShotKeys'], 0)
        reports['server.json']['CombatPayments']['EvidenceOverflow'] = 1
        self.assertEqual(self.audit(reports)['status'], 'FAIL')

    def test_missing_paid_keys_and_bot_only_payments_are_incomplete(self):
        reports, _ = fixture()
        del reports['server.json']['CombatPayments']['PaidShotKeys']
        self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')
        reports, _ = fixture()
        reports['manifest.json']['settings']['server_bot'] = True
        reports['server.json']['Windows'][0]['BotMask'] = 4
        bot = dict(reports['peer-0.json']['Shots'][0]['Key'], ShooterSlot=2)
        reports['server.json']['Shots'] = [{'Stage': 'accepted', 'Key': bot}]
        reports['server.json']['CombatPayments']['PaidShotKeys'] = [bot]
        result = self.audit(reports)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['paidAiSourceShotKeys'], 0)
        for malformed in ('invalid', None, True):
            reports['server.json']['CombatPayments']['PaidShotKeys'] = malformed
            self.assertEqual(self.audit(reports)['status'], 'FAIL')

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
        for group, field in (('CombatPayments', 'LifetimePayments'), ('CampaignClaims', 'Received')):
            reports, _ = fixture()
            reports['server.json'][group][field] = 0
            self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')
        reports, _ = fixture()
        for slot in range(2):
            reports[f'peer-{slot}.json']['Pickups'] = []
            reports[f'peer-{slot}.json']['HitClaims']['Declared'] = 0
        self.assertEqual(self.audit(reports)['status'], 'INCOMPLETE')

    def test_lifetime_claim_counters_required_without_snapshot_fallback(self):
        reports, _ = fixture()
        reports['server.json']['HitClaims'] = {'Received': 0, 'AppliedHere': 0}
        self.assertEqual(self.audit(reports)['status'], 'PASS')
        del reports['server.json']['CampaignClaims']
        self.assertEqual(self.audit(reports)['status'], 'FAIL')
        reports, _ = fixture()
        reports['server.json']['CampaignClaims'] = {'Received': 0, 'Applied': 0}
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
