"""Synthetic consumer contracts only; these fixtures are not native campaign evidence."""
import copy
import unittest

from completed_rotation_evidence import (ARCHIVE_SCOPE, LIFECYCLE_SCOPE,
                                         completed_rotation_coverage)
from net2_evidence import adjudicate, rotation_coverage


class CompletedRotationTests(unittest.TestCase):
    def setUp(self):
        self.processes = [dict(name=name, exit=0, timedOut=False) for name in ('server', 'peer-0', 'peer-1')]
        self.sha = 'a' * 64
        self.keys = [dict(Epoch=111, Match=i + 1, StartGeneration=100 + i, Room='ROOM ' + str(i))
                     for i in range(6)]
        self.reports = {}
        for index, process in enumerate(self.processes):
            name = process['name']; events = []
            for ordinal, key in enumerate(self.keys):
                common = dict(key, Sequence=ordinal * 2 + 1, AuthorityFrame=1)
                if name == 'server':
                    events.append(dict(common, Stage='authority-started'))
                    if ordinal < 5:
                        events.append(dict(common, Sequence=ordinal * 2 + 2, Stage='authority-completed',
                                           AuthorityFrame=180, Completed=True, EndReason='time limit',
                                           SimFrames=180, StepFailures=0, DroppedSteps=0, Stalls=0))
                else:
                    common.update(Slot=index - 1, Generation=3, BootstrapRevision=ordinal + 1,
                                  AppliedWorld=True, GameplayReady=True, WorldAdmissionConfirmed=True)
                    events.append(dict(common, Stage='client-applied-confirmed'))
                    if ordinal < 5:
                        events.append(dict(common, Sequence=ordinal * 2 + 2, Stage='client-completed',
                                           AuthorityFrame=180, Completed=True, EndReason='time limit'))
            self.reports[name] = dict(RotationLifecycle=dict(Schema=1, Scope=LIFECYCLE_SCOPE, Overflow=0, Events=events))
        self.archives = dict(Schema=1, Scope=ARCHIVE_SCOPE, ProducerSha256=self.sha, ReaderSha256=self.sha,
                             Files=[dict(Process=p['name'], Path=p['name'] + '/session.ppdemo',
                                         Sha256Before='b' * 64, Sha256After='b' * 64, Health='Healthy',
                                         FullRecordRead=True, Finalized=True,
                                         CompletedMatches=copy.deepcopy(self.keys[:5])) for p in self.processes])

    def audit(self, count=5):
        return completed_rotation_coverage(self.reports, self.processes, count, self.archives, self.sha)

    def events(self, name):
        return self.reports[name]['RotationLifecycle']['Events']

    def incomplete(self, fragment=None):
        status, gaps = self.audit()
        self.assertEqual(status, 'INCOMPLETE')
        self.assertTrue(gaps)
        if fragment:
            self.assertTrue(any(fragment in gap for gap in gaps), gaps)

    def test_proposed_complete_owner_and_archive_contract(self):
        self.assertEqual(self.audit(), ('PASS', []))

    def initial_continuous_zero(self):
        # Actual initial Continuous session metadata: BeginLobbyMatch has not
        # invoked NetMatchStart.Begin yet. Later starts keep their real values.
        self.keys[0]['StartGeneration'] = 0
        for report in self.reports.values():
            for row in report['RotationLifecycle']['Events'][:2]:
                row.update(StartGeneration=0, SessionPolicy='Continuous', StartStage='None')
        for file in self.archives['Files']:
            file['CompletedMatches'][0]['StartGeneration'] = 0

    def test_initial_continuous_zero_generation_uses_explicit_session_witness(self):
        self.initial_continuous_zero()
        self.assertEqual(self.audit(), ('PASS', []))

    def test_zero_generation_requires_continuous_none_on_every_event(self):
        self.initial_continuous_zero()
        for name in self.reports:
            for index in (0, 1):
                for field, value in [('SessionPolicy', None), ('SessionPolicy', 'Lobby'),
                                     ('SessionPolicy', 'Unknown'), ('SessionPolicy', 0),
                                     ('StartStage', None), ('StartStage', 'InMatch'),
                                     ('StartStage', 'Preparing'), ('StartStage', 0)]:
                    with self.subTest(process=name, event=index, field=field, value=value):
                        row = self.events(name)[index]; original = row[field]
                        if value is None:
                            del row[field]
                        else:
                            row[field] = value
                        self.incomplete('lifecycle identity')
                        row[field] = original

    def test_zero_generation_is_still_compared_exactly_across_processes(self):
        self.initial_continuous_zero()
        self.events('peer-1')[0]['StartGeneration'] = 1
        self.incomplete('applied/completed')

    def test_archive_zero_identity_cannot_authorize_a_missing_zero_event_witness(self):
        self.initial_continuous_zero()
        del self.events('server')[1]['SessionPolicy']
        self.incomplete('lifecycle identity')

    def test_archive_zero_identity_must_match_authority_exactly(self):
        self.archives['Files'][1]['CompletedMatches'][0]['StartGeneration'] = 0
        self.incomplete('peer-0: finalized archives')

    def test_zero_generation_rejects_boolean_negative_and_noninteger_values(self):
        self.initial_continuous_zero()
        for value in (False, True, -1, 0.0, '0'):
            with self.subTest(value=value):
                self.events('server')[0]['StartGeneration'] = value
                self.incomplete('lifecycle identity')
        self.events('server')[0]['StartGeneration'] = 0
        self.assertEqual(self.audit(), ('PASS', []))

    def test_explicit_count_rejects_legacy_reports_instead_of_counting_server_labels(self):
        for report in self.reports.values():
            report.clear(); report.update(Windows=[dict(Epoch=111, Match=i + 1, Room='ROOM ' + str(i)) for i in range(8)],
                                         ServerSegments=[dict(Frames=180, StepFailures=0) for _ in range(8)])
        self.incomplete('missing owner-thread')

    def test_current_smoke_default_remains_unchanged_and_strict_opt_in_is_incomplete(self):
        from test_net2_evidence import EvidenceTests
        fixture = EvidenceTests(); fixture.setUp()
        try:
            fixture.rotation_fixture()
            fixture.write()
            self.assertEqual(fixture.result()['coverage']['mapRotation'], 'PASS')
            strict = adjudicate(fixture.out, fixture.processes, required_completed_matches=5)
            self.assertEqual(strict['status'], 'INCOMPLETE')
            self.assertEqual(strict['coverage']['mapRotation'], 'INCOMPLETE')
            self.assertTrue(any('owner-thread' in row for row in strict['mapRotationEvidenceGaps']))
        finally:
            fixture.doCleanups()

    def test_invalid_requested_counts_never_become_smoke(self):
        for value in (True, False, 0, -1, 5.0, '5', None):
            with self.subTest(value=value):
                self.assertEqual(self.audit(value)[0], 'INCOMPLETE')
        self.assertEqual(rotation_coverage(self.reports, self.processes, {'required_completed_matches': True})[0], 'INCOMPLETE')

    def test_server_only_five_completions_cannot_qualify_clients(self):
        self.events('peer-1').clear()
        self.incomplete('applied/completed')

    def test_sixth_successor_required_in_authority_and_each_client(self):
        for name in self.reports:
            with self.subTest(process=name):
                saved = copy.deepcopy(self.events(name))
                self.events(name).pop()
                self.incomplete()
                self.reports[name]['RotationLifecycle']['Events'] = saved

    def test_same_room_rematches_still_need_distinct_full_start_identity(self):
        for report in self.reports.values():
            for row in report['RotationLifecycle']['Events']:
                row['Room'] = 'SAME ROOM'
        for file in self.archives['Files']:
            for row in file['CompletedMatches']:
                row['Room'] = 'SAME ROOM'
        self.assertEqual(self.audit(), ('PASS', []))
        self.events('peer-1')[4]['StartGeneration'] -= 1
        self.incomplete('applied/completed')

    def test_each_identity_component_is_compared_exactly(self):
        for field, changed in [('Epoch', 222), ('Match', 200), ('StartGeneration', 200), ('Room', 'OTHER ROOM')]:
            with self.subTest(field=field):
                row = self.events('peer-1')[2]; old = row[field]; row[field] = changed
                self.incomplete('applied/completed'); row[field] = old

    def test_wrong_local_generation_or_bootstrap_cannot_complete_admission(self):
        for field in ('Slot', 'Generation', 'BootstrapRevision'):
            with self.subTest(field=field):
                row = self.events('peer-1')[1]; old = row[field]; row[field] += 1
                self.incomplete('incarnation/admission'); row[field] = old

    def test_unconfirmed_frozen_or_unapplied_worlds_are_not_completions(self):
        for field in ('AppliedWorld', 'GameplayReady', 'WorldAdmissionConfirmed'):
            with self.subTest(field=field):
                row = self.events('peer-1')[0]; row[field] = False
                self.incomplete('incomplete lifecycle'); row[field] = True

    def test_partial_shutdown_segment_is_not_a_completed_round(self):
        row = self.events('server')[9]; row['Completed'] = False
        self.incomplete('incomplete lifecycle')

    def test_server_drops_failures_and_stalls_are_not_silently_waived(self):
        for field in ('StepFailures', 'DroppedSteps', 'Stalls'):
            with self.subTest(field=field):
                row = self.events('server')[1]; row[field] = 1
                self.incomplete(field + '=1'); row[field] = 0

    def test_performance_violations_report_each_counter_and_keep_archive_checks(self):
        row = self.events('server')[1]
        row.update(StepFailures=1, DroppedSteps=14, Stalls=2)
        self.archives = None
        status, gaps = self.audit()
        self.assertEqual(status, 'INCOMPLETE')
        for field, value in [('StepFailures', 1), ('DroppedSteps', 14), ('Stalls', 2)]:
            expected = ('server: authority performance gate ' + field + '=' + str(value)
                        + ' for Epoch=111 Match=1 StartGeneration=100 Room=ROOM 0')
            self.assertIn(expected, gaps)
        self.assertTrue(any('source-bound finalized' in gap for gap in gaps), gaps)
        self.assertFalse(any('malformed' in gap for gap in gaps), gaps)

    def test_performance_violation_does_not_hide_client_identity_gap(self):
        self.events('server')[1]['Stalls'] = 2
        self.events('peer-1')[2]['Room'] = 'OTHER ROOM'
        status, gaps = self.audit()
        self.assertEqual(status, 'INCOMPLETE')
        self.assertTrue(any('Stalls=2' in gap for gap in gaps), gaps)
        self.assertTrue(any('peer-1: actual applied/completed' in gap for gap in gaps), gaps)
        self.assertFalse(any('server: malformed' in gap for gap in gaps), gaps)

    def test_performance_counter_missing_or_invalid_is_still_malformed(self):
        for field in ('StepFailures', 'DroppedSteps', 'Stalls'):
            for value in (None, -1, True, False, 1.0, '1'):
                with self.subTest(field=field, value=value):
                    row = self.events('server')[1]
                    if value is None:
                        del row[field]
                    else:
                        row[field] = value
                    status, gaps = self.audit()
                    self.assertEqual(status, 'INCOMPLETE')
                    self.assertTrue(any('server: malformed' in gap for gap in gaps), gaps)
                    self.assertFalse(any('authority performance gate' in gap for gap in gaps), gaps)
                    row[field] = 0
        self.assertEqual(self.audit(), ('PASS', []))

    def test_ordered_events_and_positive_native_activity_required(self):
        row = self.events('peer-0')[1]; row['AuthorityFrame'] = 2
        self.incomplete('incarnation/admission')
        row['AuthorityFrame'] = 180
        self.events('peer-0')[1], self.events('peer-0')[2] = self.events('peer-0')[2], self.events('peer-0')[1]
        self.incomplete('reordered')

    def test_retransmitted_or_duplicated_event_cannot_inflate_denominator(self):
        self.events('server').append(dict(self.events('server')[1], Sequence=99))
        self.incomplete('duplicate')

    def test_overflow_and_wrong_witness_scope_are_incomplete(self):
        witness = self.reports['peer-0']['RotationLifecycle']; witness['Overflow'] = 1
        self.incomplete('overflowed'); witness['Overflow'] = 0; witness['Scope'] = 'PacketHeadersOnly'
        self.incomplete('owner-thread')

    def test_missing_or_stale_reader_identity_cannot_qualify_archives(self):
        for field in ('ProducerSha256', 'ReaderSha256'):
            with self.subTest(field=field):
                old = self.archives[field]; self.archives[field] = 'c' * 64
                self.incomplete('source-bound'); self.archives[field] = old
        self.archives = None
        self.incomplete('source-bound')

    def test_each_actual_process_must_have_finalized_complete_archive_coverage(self):
        self.archives['Files'].pop()
        self.incomplete('peer-1: finalized archives')

    def test_client_session_archive_can_span_five_completed_matches(self):
        # Conversely, authority may produce one finalized file per match.
        file = self.archives['Files'].pop(0)
        for i, key in enumerate(self.keys[:5]):
            self.archives['Files'].append(dict(file, Path='server/match-' + str(i), CompletedMatches=[key]))
        self.assertEqual(self.audit(), ('PASS', []))

    def test_changed_unfinalized_or_unread_archive_is_not_accepted(self):
        for field, value in [('Sha256After', 'c' * 64), ('Health', 'Recoverable'), ('FullRecordRead', False), ('Finalized', False)]:
            with self.subTest(field=field):
                row = self.archives['Files'][1]; old = row[field]; row[field] = value
                self.incomplete(); row[field] = old

    def test_archive_room_match_epoch_and_start_must_match_completed_sequence(self):
        self.archives['Files'][1]['CompletedMatches'][2]['StartGeneration'] = 999
        self.incomplete('peer-0: finalized archives')

    def test_archive_reader_order_cannot_differ_from_actual_world_sequence(self):
        matches = self.archives['Files'][1]['CompletedMatches']
        matches[0], matches[1] = matches[1], matches[0]
        self.incomplete('authority completion order')

    def test_wire_identity_bounds_reject_nonexistent_start_streams(self):
        self.events('server')[0]['Match'] = 65536
        self.incomplete('lifecycle identity')

    def test_late_or_reconnecting_process_has_no_implicit_missing_world_exemption(self):
        self.events('peer-1')[:4] = []
        self.incomplete('applied/completed')

    def test_abnormal_exit_and_missing_report_cannot_be_green(self):
        self.processes[1]['exit'] = 1
        self.incomplete('exit normally')
        self.processes[1]['exit'] = 0
        del self.reports['peer-1']
        self.incomplete('every process')


if __name__ == '__main__':
    unittest.main()
