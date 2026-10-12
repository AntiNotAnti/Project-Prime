import copy
import unittest

from net_timed_grants import audit, MASK, TYPES, ENDS


def start(**changes):
    row = dict(Sequence=1, Spawn=dict(Epoch=1, Match=1, Room=1, SpawnId=10, Generation=1),
               Life=dict(AuthorityEpoch=1, MatchId=1, ShooterSlot=0, Generation=1, LifeId=1, ShotId=0),
               Type='DoubleDamage', ContactFrame=100, EffectiveFrame=101, ExpiresAtFrame=1900,
               SourceContactFrame=5000, SourceEffectiveFrame=5001, EndFrame=1900, EndReason='Active')
    row.update(changes)
    return row


def close(row, reason='Expired', frame=None):
    result = copy.deepcopy(row)
    result.update(EndReason=reason, EndFrame=row['ExpiresAtFrame'] if frame is None else frame)
    return result


class TimedGrantAuditTests(unittest.TestCase):
    def result(self, rows):
        return audit(dict(TimedGrants=rows))

    def rejects(self, rows):
        result = self.result(rows)
        self.assertEqual(result['status'], 'FAIL', result)
        self.assertTrue(result['failures'], result)

    def test_all_types_and_end_reasons(self):
        rows = []
        for index, reason in enumerate(ENDS):
            row = start(Sequence=index + 1, Type=TYPES[index % 3])
            row['Spawn']['Generation'] = index + 1
            rows.extend([row, close(row, reason, 1900 if reason == 'Expired' else 150)])
        result = self.result(rows)
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['grants'], 6)
        self.assertEqual(result['closures'], 6)
        self.assertEqual(result['openAtCapture'], 0)
        self.assertEqual(result['byType'], {kind: 2 for kind in TYPES})
        self.assertEqual(result['byEndReason'], {reason: 1 for reason in ENDS})

    def test_empty_and_missing_instrumentation_not_run(self):
        for report in ({}, {'TimedGrants': None}, {'TimedGrants': []}):
            with self.subTest(report=report):
                self.assertEqual(audit(report)['status'], 'NOT_RUN')

    def test_open_grant_and_unknown_source_counted(self):
        row = start(SourceContactFrame=None, SourceEffectiveFrame=None)
        result = self.result([row])
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['unknownSourceBoundary'], 1)
        self.assertEqual(result['openAtCapture'], 1)

    def test_scope_and_source_authority_rollover(self):
        contact = MASK - 900
        row = start(ContactFrame=contact, EffectiveFrame=(contact + 1) & MASK,
                    ExpiresAtFrame=(contact + 1800) & MASK, EndFrame=(contact + 1800) & MASK,
                    SourceContactFrame=MASK, SourceEffectiveFrame=0)
        rows = [row, close(row)]
        # A new room is an independent scope even with identical sequence and authored ID.
        other = copy.deepcopy(row)
        other['Spawn']['Room'] = 2
        rows += [other, close(other, 'Room', (contact + 100) & MASK)]
        self.assertEqual(self.result(rows)['status'], 'PASS')

    def test_exact_spawn_and_life_fences(self):
        for container, field, value in [('Spawn', 'Epoch', 2), ('Spawn', 'Match', 2),
                                        ('Life', 'AuthorityEpoch', 2), ('Life', 'MatchId', 2)]:
            row = start(); row[container][field] = value
            with self.subTest(container=container, field=field):
                self.rejects([row])
        for field, value in [('ShooterSlot', 8), ('ShooterSlot', -1), ('Generation', 0),
                             ('Generation', 65536), ('LifeId', 0), ('LifeId', 65536), ('ShotId', 1)]:
            row = start(); row['Life'][field] = value
            with self.subTest(field=field, value=value): self.rejects([row])

    def test_bool_and_noninteger_identity_fields(self):
        fields = [('Spawn', field) for field in ('Epoch', 'Match', 'Room', 'SpawnId', 'Generation')]
        fields += [('Life', field) for field in ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')]
        for container, field in fields:
            for value in (True, False, 1.0, '1', None):
                row = start(); row[container][field] = value
                with self.subTest(container=container, field=field, value=value): self.rejects([row])
        for value in (True, False, 0, -1, 1 << 64, 1.0, '1'):
            with self.subTest(sequence=value): self.rejects([start(Sequence=value)])

    def test_duplicate_creation_spawn_and_close(self):
        row = start()
        self.rejects([row, copy.deepcopy(row)])
        same_spawn = start(Sequence=2)
        same_spawn['Life']['ShooterSlot'] = 1
        self.rejects([row, same_spawn])
        same_sequence = copy.deepcopy(row); same_sequence['Spawn']['Generation'] = 2
        self.rejects([row, same_sequence])
        self.rejects([row, close(row), close(row)])
        self.rejects([close(row)])

    def test_closure_cannot_change_immutable_fields(self):
        row = start()
        changes = [('Life', 'ShooterSlot', 1), ('Life', 'Generation', 2), ('Life', 'LifeId', 2),
                   ('Spawn', 'SpawnId', 11), ('Spawn', 'Generation', 2), ('Spawn', 'Room', 2),
                   ('Spawn', 'Epoch', 2), ('Spawn', 'Match', 2)]
        for container, field, value in changes:
            ending = close(row); ending[container][field] = value
            with self.subTest(container=container, field=field): self.rejects([row, ending])
        for field, value in [('Sequence', 2), ('Type', 'Cloak'), ('SourceContactFrame', 5001),
                             ('SourceEffectiveFrame', 5002), ('ContactFrame', 101), ('EffectiveFrame', 102),
                             ('ExpiresAtFrame', 1901)]:
            ending = close(row); ending[field] = value
            with self.subTest(field=field): self.rejects([row, ending])

    def test_wrong_duration_frame_and_source(self):
        for field, value in [('ContactFrame', -1), ('ContactFrame', MASK + 1), ('EffectiveFrame', 100),
                             ('ExpiresAtFrame', 1899), ('EndFrame', 1899), ('SourceContactFrame', True),
                             ('SourceEffectiveFrame', 5000), ('SourceEffectiveFrame', None), ('Type', 'Unknown'),
                             ('EndReason', 'Unknown')]:
            with self.subTest(field=field, value=value): self.rejects([start(**{field: value})])
        row = start()
        for reason, frame in [('Expired', 1899), ('Death', 99), ('Retired', 1901)]:
            with self.subTest(reason=reason, frame=frame): self.rejects([row, close(row, reason, frame)])
        self.rejects([start(SourceContactFrame=None, SourceEffectiveFrame=1)])

    def test_malformed_list_and_rows(self):
        for rows in ({}, 'text', 1, True):
            with self.subTest(rows=rows): self.rejects(rows)
        for row in (None, [], 'text', 1, {}, {'Spawn': None, 'Life': None}):
            with self.subTest(row=row): self.rejects([row])
        for field in ('Spawn', 'Life'):
            for value in (None, [], 1, 'text'):
                row = start(); row[field] = value
                with self.subTest(field=field, value=value): self.rejects([row])

    def test_malformed_report_returns_failure(self):
        for report in (None, [], 'text', 1, True):
            with self.subTest(report=report):
                self.assertEqual(audit(report)['status'], 'FAIL')

    def test_campaign_overflow_never_passes_partial_transitions(self):
        for overflow in (1, -1, True, '0', None):
            self.assertEqual(audit(dict(TimedGrants=[start()], Overflow=overflow))['status'], 'FAIL')
        self.assertEqual(audit(dict(TimedGrants=[start()], Overflow=0))['status'], 'PASS')


if __name__ == '__main__':
    unittest.main()
