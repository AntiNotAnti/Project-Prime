import copy
import json
from pathlib import Path
import tempfile
import unittest

from ncx_timeline import REQUIRED, audit, main


KEY = dict(AuthorityEpoch=1, MatchId=1, ShooterSlot=0, Generation=1, LifeId=1, ShotId=1)


def row(stage, timestamp=100, **values):
    result = dict(Stage=stage, Key=copy.deepcopy(KEY), Component=1, Phase=0, Kind=1,
                  Victim=1, VictimGeneration=1, VictimLife=1, ClaimId=1,
                  DamageEventId=1, SourceFrame=1, AckFrame=1, AckSubFrame=0,
                  LocalFrame=1, AuthorityFrame=1, Timestamp=timestamp, Weapon=0,
                  Value=10, Result=0)
    result.update(values)
    return result


def report(rows, role='client', frequency=1000, overflow=0):
    return dict(Role=role, ShotTimeline=dict(Schema=1,
        Clock=dict(Domain='ProcessStopwatch', Frequency=frequency), Rows=rows,
        Overflow=overflow, UnkeyedRows=0, Complete=True))


def as_schema2(source):
    result = copy.deepcopy(source)
    timeline = result['ShotTimeline']
    timeline.update(Schema=2, Provenance=dict(Pose='presentation only',
                    UnusedMetadata='unexported-v2-extension-canary'))
    for index, event in enumerate(timeline['Rows']):
        event['Pose'] = None if index % 2 == 0 else dict(
            Origin=dict(X=1.0, Y=2.0, Z=3.0), Aim=dict(X=0.0, Y=0.0, Z=1.0),
            Direction=dict(X=0.0, Y=0.0, Z=1.0), Charge=0)
        event['UnusedMetadata'] = 'unexported-v2-extension-canary'
    return result


class TimelineTests(unittest.TestCase):
    def test_schema2_optional_pose_and_unknown_metadata_preserve_core_observations(self):
        legacy = {'server': report([row('claim-received', 100), row('terminal-decided', 110)], 'server'),
                  'client': report([row('source-emitted', 50), row('predicted-feedback', 55)])}
        extended = {label: as_schema2(source) for label, source in legacy.items()}
        before = copy.deepcopy(extended)
        self.assertEqual(audit(legacy), audit(extended))
        self.assertEqual(audit(extended)['counts']['uniqueObservedShotKeys'], 1)
        self.assertEqual(extended, before)
        self.assertNotIn('unexported-v2-extension-canary', json.dumps(audit(extended)))

    def test_unknown_future_or_untyped_timeline_schema_is_rejected(self):
        for schema in (None, 0, 3, 1 << 31, True, '2', 2.0):
            with self.subTest(schema=schema):
                source = report([row('source-emitted')]); source['ShotTimeline']['Schema'] = schema
                result = audit({'server': source})
                self.assertEqual(result['processes'][0]['clockStatus'], 'UNAVAILABLE')
                self.assertEqual(result['counts']['uniqueObservedShotKeys'], 0)
                self.assertEqual(result['status'], 'INCOMPLETE')

    def test_schema2_does_not_hide_malformed_core_rows_or_clock(self):
        for field, value in (('Timestamp', -1), ('LocalFrame', 1 << 32), ('AckFrame', True)):
            source = as_schema2(report([row('source-emitted', **{field: value})]))
            result = audit({'server': source})
            self.assertEqual(result['counts']['uniqueObservedShotKeys'], 0, field)
            self.assertEqual(result['status'], 'INCOMPLETE', field)
        source = as_schema2(report([row('source-emitted', Key=dict(KEY, ShotId=0))]))
        self.assertEqual(audit({'server': source})['counts']['uniqueObservedShotKeys'], 0)
        source = as_schema2(report([row('source-emitted')]))
        source['ShotTimeline']['Clock']['Domain'] = 'OtherClock'
        self.assertEqual(audit({'server': source})['processes'][0]['clockStatus'], 'UNAVAILABLE')
        source = as_schema2(report([row('unknown-v2-stage')]))
        result = audit({'server': source})
        self.assertEqual(result['counts']['unknownStageRows'], 1)
        self.assertEqual(result['status'], 'INCOMPLETE')

    def test_retirement_preserves_phase_and_is_not_payment_or_delivery(self):
        rows = [row('receipt-retired', Phase=9, Result=1),
                row('receipt-retired', Phase=10, Result=3),
                row('claim-cancelled', Result=5)]
        result = audit({'client': report(rows)})
        self.assertEqual(result['counts']['retiredReceipts'], 2)
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 0)
        self.assertEqual(result['counts']['deliveredTerminals'], 0)
        self.assertNotIn('receipt-retired', REQUIRED)
        self.assertNotIn('claim-cancelled', REQUIRED)
        self.assertEqual([r['Phase'] for r in result['shots'][0]['observations']], [9, 10, 0])
        duplicate = audit({'client': report(rows + rows[:1])})
        self.assertTrue(any('duplicate receipt retirement' in issue for issue in duplicate['incomplete']))

    def test_duplicate_claim_retries_are_not_payments_or_unique_claims(self):
        result = audit({'server': report([row('claim-received')] * 3, 'server')})
        self.assertEqual(result['counts']['uniqueReceivedClaims'], 1)
        self.assertEqual(result['counts']['claimRetryObservations'], 2)
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 0)

    def test_admission_is_not_payment_and_partial_is_incomplete(self):
        result = audit({'server': report([row('source-admitted')], 'server')})
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 0)
        self.assertIn('payment-committed', result['shots'][0]['missingStages'])

    def test_payment_identity_includes_component_kind_and_victim_life(self):
        rows = [row('payment-committed'), row('payment-committed', Kind=2),
                row('payment-committed', Component=2), row('payment-committed', VictimLife=2)]
        result = audit({'server': report(rows + rows[:1], 'server')})
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 4)
        self.assertEqual(result['counts']['committedPaymentRows'], 5)
        self.assertTrue(any('duplicate committed' in i for i in result['incomplete']))

    def test_every_full_key_field_changes_token_including_life_and_uint_wrap(self):
        rows = [row('source-emitted')]
        for field in KEY:
            key = dict(KEY)
            key[field] += 1
            rows.append(row('source-emitted', Key=key))
        rows.append(row('source-emitted', Key=dict(KEY, ShotId=0xffffffff)))
        result = audit([report(rows)])
        tokens = [shot['token'] for shot in result['shots']]
        self.assertEqual(len(set(tokens)), 8)
        self.assertTrue(all(len(t) == 64 for t in tokens))
        self.assertNotIn('AuthorityEpoch', json.dumps(result))

    def test_wrap_frames_are_metadata_not_negative_elapsed(self):
        result = audit([report([row('source-emitted', 5, SourceFrame=0xffffffff),
                                row('predicted-feedback', 9, SourceFrame=0)])])
        elapsed = result['shots'][0]['localLatencies'][0]
        self.assertEqual(elapsed['milliseconds'], 4)

    def test_invalid_timestamps_clock_rows_and_overflow_are_incomplete(self):
        for bad in (-1, 1.5, True, 1 << 63):
            result = audit([report([row('source-emitted', bad)])])
            self.assertEqual(result['status'], 'INCOMPLETE')
            self.assertEqual(result['counts']['uniqueObservedShotKeys'], 0)
        self.assertTrue(any('overflow' in i for i in audit([report([], overflow=1)])['incomplete']))
        for frequency in (0, -1, True):
            self.assertTrue(any('clock' in i for i in audit([report([], frequency=frequency)])['incomplete']))

    def test_local_time_uses_own_frequency_and_no_cross_process_duration(self):
        result = audit({'server': report([row('source-admitted', 900000)], 'server', 100),
                       'client-secret': report([row('source-emitted', 10), row('predicted-feedback', 30)], frequency=2000)})
        durations = result['shots'][0]['localLatencies']
        self.assertEqual(len(durations), 1)
        self.assertEqual(durations[0]['milliseconds'], 10)
        self.assertNotIn('client-secret', json.dumps(result))
        self.assertEqual(result['causalClockOffsetIntervals'], [])

    def test_causal_offset_interval_with_disjoint_clock_origins(self):
        result = audit({'server': report([row('intent-received', 10100), row('terminal-decided', 10200)], 'server'),
                       'client': report([row('intent-offered', 100), row('terminal-delivered', 400)])})
        bounds = result['causalClockOffsetIntervals'][0]
        self.assertEqual(bounds['status'], 'AVAILABLE')
        self.assertEqual(bounds['lowerSeconds'], 9.8)
        self.assertEqual(bounds['upperSeconds'], 10)
        self.assertNotIn('milliseconds', json.dumps(bounds))

    def test_receipt_offset_requires_exact_claim_and_full_key(self):
        for changed in (dict(ClaimId=2), dict(Key=dict(KEY, LifeId=2))):
            result = audit({'server': report([row('intent-received', 10100), row('terminal-decided', 10200)], 'server'),
                           'client': report([row('intent-offered', 100), row('terminal-delivered', 400, **changed)])})
            self.assertTrue(all(i['status'] == 'UNAVAILABLE' for i in result['causalClockOffsetIntervals']))

    def test_inconsistent_causal_bounds_have_no_estimate(self):
        result = audit({'server': report([row('intent-received', 10), row('terminal-decided', 100)], 'server'),
                       'client': report([row('intent-offered', 9), row('terminal-delivered', 20)])})
        bounds = result['causalClockOffsetIntervals'][0]
        self.assertEqual(bounds['status'], 'INCONSISTENT')
        self.assertNotIn('lowerSeconds', bounds)
        self.assertEqual(result['status'], 'INCOMPLETE')

    def test_source_and_render_never_prove_pixels_or_collision(self):
        result = audit([report([row('source-emitted'), row('render-submitted', 110)])])
        self.assertEqual(result['scope']['actualPixels'], 'UNAVAILABLE')
        self.assertEqual(result['scope']['collisionObservation'], 'UNAVAILABLE')
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertNotIn('pixel-visible', result['stages'])

    def test_authored_and_native_contact_are_not_emission_or_payment(self):
        result = audit([report([row('source-authored'), row('native-contact')])])
        self.assertEqual(result['counts']['uniqueSourceKeys'], 0)
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 0)
        self.assertEqual(result['scope']['collisionObservation'], 'AVAILABLE')
        self.assertEqual(result['scope']['actualPixels'], 'UNAVAILABLE')

    def test_unkeyed_or_incomplete_producer_and_unknown_stages(self):
        for field, value in (('UnkeyedRows', 1), ('Complete', False)):
            sample = report([row('source-emitted')])
            sample['ShotTimeline'][field] = value
            self.assertTrue(any('producer' in i for i in audit([sample])['incomplete']))
        result = audit([report([row('future-stage')])])
        self.assertEqual(result['processes'][0]['rows'], 0)
        self.assertEqual(result['counts']['unknownStageRows'], 1)
        self.assertEqual(result['counts']['uniqueSourceKeys'], 0)
        self.assertEqual(result['stageCoverage']['source-emitted']['status'], 'UNAVAILABLE')
        self.assertEqual(result['shots'], [])

    def test_unknown_stage_cannot_export_email_endpoint_or_arbitrary_text(self):
        private_stage = 'contact alice@example.test endpoint=203.0.113.8:27888 secret-token'
        result = audit({'client-private': report([row(private_stage), row('source-emitted')])})
        exported = json.dumps(result)
        for private in (private_stage, 'alice@example.test', '203.0.113.8', 'secret-token', 'client-private'):
            self.assertNotIn(private, exported)
        self.assertEqual(result['counts']['unknownStageRows'], 1)
        self.assertEqual(result['processes'][0]['rows'], 1)
        self.assertEqual(len(result['shots'][0]['observations']), 1)
        self.assertEqual(result['status'], 'INCOMPLETE')

    def test_refusal_branch_is_optional_and_result_requires_signed_int32(self):
        sample = row('payment-refused', Result=-(1 << 31))
        result = audit({'server': report([sample], 'server')})
        self.assertFalse(result['stageCoverage']['payment-refused']['required'])
        self.assertEqual(result['shots'][0]['observations'][0]['Result'], -(1 << 31))
        self.assertEqual(result['counts']['uniqueCommittedPayments'], 0)
        for bad in (True, 1 << 31, -(1 << 31) - 1):
            result = audit({'server': report([row('payment-refused', Result=bad)], 'server')})
            self.assertEqual(result['counts']['uniqueObservedShotKeys'], 0)

    def test_loss_and_repeated_carriers_use_first_event_bounds_without_packet_pairing(self):
        result = audit({'server': report([row('intent-received', 1040), row('intent-received', 1020),
                                        row('terminal-decided', 1050), row('terminal-decided', 1030)], 'server'),
                       'client': report([row('intent-offered', 35), row('intent-offered', 10),
                                        row('intent-offered', 25), row('terminal-delivered', 70),
                                        row('terminal-delivered', 50)])})
        bounds = result['causalClockOffsetIntervals'][0]
        # First offer may have been lost; a later decision may have produced the
        # first delivery. Neither fact justifies pairing by list/retry index.
        self.assertEqual(bounds['status'], 'AVAILABLE')
        self.assertEqual(bounds['upperPairs'], 1)
        self.assertEqual(bounds['lowerPairs'], 1)
        self.assertEqual(bounds['upperSeconds'], 1.01)
        self.assertEqual(bounds['lowerSeconds'], .98)
        self.assertEqual(bounds['uncertaintySeconds'], .03)

    def test_claim_offers_distinguish_resends_from_received_retries(self):
        result = audit({'server': report([row('claim-received')], 'server'),
                       'client-0': report([row('claim-declared')] + [row('claim-offered')] * 3)})
        self.assertEqual(result['counts']['claimResendOffers'], 2)
        self.assertEqual(result['counts']['uniqueOfferedClaims'], 1)
        self.assertEqual(result['counts']['claimRetryObservations'], 0)
        self.assertFalse(result['stageCoverage']['claim-offered']['required'])

    def test_full_uint32_damage_sequence_preserved(self):
        result = audit([report([row('terminal-delivered', DamageEventId=0xffffffff)])])
        self.assertEqual(result['processes'][0]['rows'], 1)
        self.assertEqual(result['shots'][0]['observations'][0]['DamageEventId'], 0xffffffff)
        bad = audit([report([row('terminal-delivered', DamageEventId=1 << 32)])])
        self.assertEqual(bad['counts']['uniqueObservedShotKeys'], 0)

    def test_miss_has_unknown_payment_evidence_not_a_payment_failure(self):
        result = audit({'server': report([row('source-admitted')], 'server'),
                       'client': report([row('source-emitted')])})
        self.assertEqual(result['shots'][0]['paymentEvidence'], 'UNAVAILABLE')
        self.assertEqual(result['scope']['legalHitDenominator'], 'UNAVAILABLE')
        self.assertIn('valid misses', result['scope']['completeness'])
        self.assertFalse(any('false payment' in reason or 'payment bug' in reason
                             for reason in result['incomplete']))

    def test_complete_observations_still_do_not_claim_actual_pixels(self):
        client_stages = {'source-emitted', 'intent-offered', 'claim-declared',
                         'terminal-delivered', 'predicted-feedback', 'render-submitted'}
        result = audit({'client-0': report([row(stage) for stage in sorted(client_stages)]),
                       'server': report([row(stage) for stage in sorted(REQUIRED - client_stages)], 'server')})
        self.assertEqual(result['status'], 'COMPLETE', result['incomplete'])
        self.assertEqual(result['scope']['actualPixels'], 'UNAVAILABLE')
        self.assertTrue(all('Key' not in r for r in result['shots'][0]['observations']))

    def test_delivered_receipts_deduplicate_by_exact_claim_and_victim_life(self):
        result = audit([report([row('terminal-delivered')] * 2 + [
            row('terminal-delivered', ClaimId=2), row('terminal-delivered', VictimLife=2)])])
        self.assertEqual(result['counts']['deliveredTerminals'], 3)

    def test_cli_accepts_manifest_peer_labels_and_missing_expected_report(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'manifest.json').write_text(json.dumps(dict(commands=[dict(name='peer-0'), dict(name='client-0')])))
            (path / 'server.json').write_text(json.dumps(report([], 'server')))
            (path / 'peer-0.json').write_text(json.dumps(report([row('source-emitted')])))
            main([directory])
            result = json.loads((path / 'ncx-timeline.json').read_text())
            self.assertEqual(result['counts']['uniqueSourceKeys'], 1)
            self.assertEqual(result['status'], 'INCOMPLETE')
            self.assertTrue(any('unreadable report' in i for i in result['incomplete']))

    def test_invalid_full_key_ranges_fail_closed_without_wrap_normalization(self):
        for field, value in (('ShotId', 1 << 32), ('ShotId', 0),
                             ('AuthorityEpoch', 1 << 64), ('ShooterSlot', 8), ('LifeId', True)):
            result = audit([report([row('source-emitted', Key=dict(KEY, **{field: value}))])])
            self.assertEqual(result['counts']['uniqueObservedShotKeys'], 0)
            self.assertEqual(result['status'], 'INCOMPLETE')

    def test_empty_or_malformed_reports_are_not_zero_pass(self):
        for reports in ({}, {'server': {}}, [report([])], [None]):
            self.assertEqual(audit(reports)['status'], 'INCOMPLETE')

    def test_cli_creates_once_and_does_not_copy_manifest_or_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'manifest.json').write_text(json.dumps(dict(secret='private-name')))
            (path / 'server.json').write_text(json.dumps(report([row('source-admitted')], 'server')))
            (path / 'client0.json').write_text(json.dumps(report([row('source-emitted')])))
            self.assertEqual(main([directory]), 0)
            original = (path / 'ncx-timeline.json').read_text()
            self.assertNotIn('private-name', original)
            with self.assertRaises(SystemExit):
                main([directory])
            self.assertEqual((path / 'ncx-timeline.json').read_text(), original)


if __name__ == '__main__':
    unittest.main()
