import copy
import struct
import unittest

from net_turret_outcomes import FIELDS, SIGNATURE, authority_outcomes, decode, peer_outcomes


class TurretOutcomeTests(unittest.TestCase):
    def row(self, status='Emitted', source=100, authority=130, stage='authority'):
        key = (9, 2, 1, 3, 4, 5)
        raw = bytearray(123)
        struct.pack_into('<QHBHHI', raw, 0, *key)
        struct.pack_into('<IBB', raw, 19, authority, 1 if status == 'Emitted' else 2,
                         0 if status == 'Emitted' else 1)
        struct.pack_into('<IIIBBBBIB', raw, 25, 5, source, 90, 0, 4, 3, 0, 0, 1)
        struct.pack_into('<9f', raw, 46, 1, 2, 3, 0, 0, 1, 0, 1, 0)
        struct.pack_into('<3h', raw, 82, 0, 0, 32767)
        struct.pack_into('<H', raw, 92, 4)
        if status == 'Emitted':
            struct.pack_into('<6f', raw, 99, 4, 5, 6, 0, 0, 1)
        return dict(Key=dict(zip(FIELDS, key)), Payload=raw.hex(), AuthorityFrame=authority,
                    ObservedFrame=authority, Status=status,
                    Reason='None' if status == 'Emitted' else 'RecoveryExpired',
                    Stage=stage, CancelledFlights=0)

    def setup_authority(self, row):
        key, _, signature = decode(row)
        shot = dict(Key=row['Key'].copy(), **dict(zip(SIGNATURE, signature)),
                    Stage='committed' if row['Status'] == 'Emitted' else 'expired',
                    ObservedFrame=row['AuthorityFrame'])
        report = dict(Role='server', TurretOutcomes=[row], Shots=[shot],
                      CombatPayments=dict(EvidenceOverflow=0, PaidShotKeys=[]))
        return key, report, {key: shot}, {key: shot} if row['Status'] == 'Emitted' else {}

    def test_native_emitted_and_separate_clocks(self):
        row = self.row(source=100, authority=130)
        key, report, accepted, committed = self.setup_authority(row)
        terminal, refused, errors = authority_outcomes(report, accepted, committed)
        self.assertEqual(errors, [])
        self.assertEqual(set(terminal), {key})
        self.assertEqual(refused, {})
        self.assertEqual(decode(row)[2][0], 100)

    def test_explicit_expiry_and_zero_payments(self):
        key, report, accepted, committed = self.setup_authority(self.row('Refused'))
        terminal, refused, errors = authority_outcomes(report, accepted, committed)
        self.assertEqual(errors, [])
        self.assertEqual(set(refused), {key})
        report['Shots'] = []
        self.assertTrue(authority_outcomes(report, accepted, committed)[2])

    def test_expiry_cannot_use_generic_rejected_observation(self):
        _, report, accepted, committed = self.setup_authority(self.row('Refused'))
        report['Shots'][0]['Stage'] = 'rejected'
        self.assertTrue(authority_outcomes(report, accepted, committed)[2])

    def test_actual_encoded_source_targets(self):
        for target, generation, life in ((0, 0, 0), (0x80, 0, 0),
                                         (0x81, 1, 2), (0x88, 65535, 65535)):
            row = self.row()
            raw = bytearray.fromhex(row['Payload'])
            struct.pack_into('<BHH', raw, 94, target, generation, life)
            row['Payload'] = raw.hex()
            with self.subTest(target=target):
                self.assertEqual(decode(row)[0][-1], 5)

    def test_invalid_source_target_generation_and_life(self):
        invalid = [(target, 1, 2) for target in range(1, 9)]
        invalid += [(0x81, 0, 2), (0x88, 2, 0), (0x89, 1, 2),
                    (0x80, 1, 0), (0x80, 0, 1), (0, 1, 2)]
        for target, generation, life in invalid:
            row = self.row()
            raw = bytearray.fromhex(row['Payload'])
            struct.pack_into('<BHH', raw, 94, target, generation, life)
            row['Payload'] = raw.hex()
            with self.subTest(target=target, generation=generation, life=life), self.assertRaises(ValueError):
                decode(row)

    def test_receipt_and_applied_required(self):
        key, report, accepted, committed = self.setup_authority(self.row('Refused'))
        terminal = authority_outcomes(report, accepted, committed)[0]
        received = dict(report['TurretOutcomes'][0], Stage='received', ObservedFrame=160)
        applied = dict(received, Stage='refusal-applied', CancelledFlights=1)
        peer = dict(Role='client', TurretOutcomes=[received, applied])
        self.assertEqual(peer_outcomes(peer, terminal, {key}), ({key}, {key}, []))
        for rows in ([], [received], [applied]):
            peer['TurretOutcomes'] = rows
            self.assertTrue(peer_outcomes(peer, terminal, {key})[2])

    def test_reason_only_and_wrong_payload_lengths(self):
        row = self.row('Refused')
        for payload in (None, '', row['Payload'][:-2], row['Payload'] + '00', 'z' * 246):
            with self.subTest(payload=payload):
                bad = dict(row, Payload=payload)
                with self.assertRaises(ValueError):
                    decode(bad)

    def test_bool_is_not_identity_or_clock(self):
        for field in FIELDS:
            row = self.row()
            row['Key'][field] = True
            with self.subTest(field=field), self.assertRaises(ValueError):
                decode(row)
        for field in ('AuthorityFrame', 'ObservedFrame', 'CancelledFlights'):
            with self.subTest(field=field), self.assertRaises(ValueError):
                decode(dict(self.row(), **{field: True}))

    def test_duplicate_and_conflicting_terminals(self):
        _, report, accepted, committed = self.setup_authority(self.row())
        for other in (copy.deepcopy(report['TurretOutcomes'][0]), self.row('Refused')):
            report['TurretOutcomes'] = [self.row(), other]
            self.assertTrue(authority_outcomes(report, accepted, committed)[2])

    def test_foreign_full_key_cannot_match_by_shot_id(self):
        key, report, accepted, committed = self.setup_authority(self.row())
        terminal = authority_outcomes(report, accepted, committed)[0]
        for field, offset, fmt in (('AuthorityEpoch', 0, '<Q'), ('MatchId', 8, '<H'),
                                   ('Generation', 11, '<H'), ('LifeId', 13, '<H')):
            row = self.row(stage='received')
            row['Key'][field] += 1
            raw = bytearray.fromhex(row['Payload'])
            struct.pack_into(fmt, raw, offset, row['Key'][field])
            if field == 'LifeId': struct.pack_into('<H', raw, 92, row['Key'][field])
            row['Payload'] = raw.hex()
            with self.subTest(field=field):
                self.assertTrue(peer_outcomes(dict(Role='client', TurretOutcomes=[row]), terminal, {key})[2])

    def test_refusal_cannot_be_committed_or_paid(self):
        key, report, accepted, committed = self.setup_authority(self.row('Refused'))
        self.assertTrue(authority_outcomes(report, accepted, {key: accepted[key]})[2])
        report['CombatPayments']['PaidShotKeys'] = [dict(zip(FIELDS, key))]
        self.assertTrue(authority_outcomes(report, accepted, committed)[2])

    def test_emission_requires_actual_native_commit(self):
        _, report, accepted, _ = self.setup_authority(self.row())
        self.assertTrue(authority_outcomes(report, accepted, {})[2])

    def test_sequence_wrap_preserves_source_and_authority_clocks(self):
        row = self.row(source=2**32-3, authority=2)
        key, report, accepted, committed = self.setup_authority(row)
        terminal, _, errors = authority_outcomes(report, accepted, committed)
        self.assertEqual(errors, [])
        self.assertEqual(terminal[key][2][0], 2**32-3)
        peer = dict(Role='client', TurretOutcomes=[dict(row, Stage='received', ObservedFrame=5)])
        self.assertEqual(peer_outcomes(peer, terminal, {key})[2], [])


if __name__ == '__main__':
    unittest.main()
