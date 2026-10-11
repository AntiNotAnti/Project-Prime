import copy
import unittest
import movement_validation_evidence as module


def report(enabled=True):
    hunters = [dict(Hunter=h, Alt=alt, **{f: 0 for f in module.COUNTS + module.MAXIMA})
               for h in range(7) for alt in (False, True)]
    return {'MovementValidation': dict(Enabled=enabled, HardRejections=0, Invalid=0,
                                      Overflow=0, Hunters=hunters, Rows=[])}


def candidate(hunter=0, **changes):
    row = dict(Epoch=99, Match=2, Slot=0, Generation=1, Life=1, Hunter=hunter,
               Frame=0, ElapsedFrames=1, Alt=False, HorizontalPerFrame=2.,
               UpwardPerFrame=0., AccelerationPerFrame=1., HorizontalSignal=True,
               VerticalSignal=False, AccelerationSignal=False)
    row.update(changes)
    return row


class MovementEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.server = report(); self.peers = {n: report() for n in ('peer-0', 'peer-1')}
        self.summary = dict(status='PASS', coverage=dict(boundedHeadlessDrain='PASS'),
                            processes=[dict(name=n) for n in ('server', *self.peers)])
        self.manifest = dict(settings=dict(movement_validation_shadow=True))
        self.counts = self.server['MovementValidation']['Hunters'][0]
        self.counts.update(Baselines=1, Evaluated=2, MaximumHorizontal=.1)

    def audit(self): return module.audit(self.server, self.peers, self.summary, self.manifest)
    def assert_status(self, status): self.assertEqual(self.audit()['status'], status, self.audit())

    def test_on_positive_reports_actual_hunter_not_all_hunters(self):
        self.assert_status('PASS'); r = self.audit()
        self.assertEqual(r['metrics']['authorityEvaluated'], 2)
        self.assertEqual(r['metrics']['authority']['measuredHunters'], ['Samus'])
        self.assertEqual(r['confidence'], 'ADVISORY_ONLY')

    def test_advisory_candidates_are_allowed_and_exact_rows_retained(self):
        self.counts.update(WouldRefuse=1, MaximumHorizontal=2.)
        self.server['MovementValidation']['Rows'] = [candidate()]
        self.assert_status('PASS')
        self.assertEqual(self.audit()['metrics']['authority']['totals']['WouldRefuse'], 1)

    def test_alt_unsupported_exempt_denominators(self):
        alt = self.server['MovementValidation']['Hunters'][13]
        alt.update(Baselines=1, Exempt=5, Unsupported=7, MaximumHorizontal=.6)
        self.assert_status('PASS')
        self.assertEqual(self.audit()['metrics']['authority']['totals']['Unsupported'], 7)

    def test_off_baseline_has_all_zero_counts_rows(self):
        self.manifest['settings']['movement_validation_shadow'] = False
        self.server = report(False); self.peers = {n: report(False) for n in self.peers}
        self.assert_status('PASS')
        self.server['MovementValidation']['Hunters'][0]['Baselines'] = 1
        self.assert_status('FAIL')

    def test_on_vacuous_zero_or_only_exempt_is_incomplete(self):
        self.counts['Evaluated'] = 0; self.counts['MaximumHorizontal'] = 0
        self.assert_status('INCOMPLETE')
        self.counts['Exempt'] = 4; self.assert_status('INCOMPLETE')

    def test_missing_reports_peers_setting_are_incomplete(self):
        self.server = {}; self.assert_status('INCOMPLETE')
        self.setUp(); self.peers.pop('peer-1'); self.assert_status('INCOMPLETE')
        self.setUp(); self.manifest = {}; self.assert_status('INCOMPLETE')

    def test_base_and_drain_gate_not_waived(self):
        self.summary['status'] = 'FAIL'; self.assert_status('FAIL')
        self.setUp(); self.summary['coverage'] = {}; self.assert_status('INCOMPLETE')
        self.setUp(); self.summary['coverage']['boundedHeadlessDrain'] = 'FAIL'; self.assert_status('FAIL')

    def test_enabled_typed_and_matching(self):
        for value in (False, 1, None, 'true'):
            self.setUp(); self.server['MovementValidation']['Enabled'] = value; self.assert_status('FAIL')

    def test_guard_client_measurements_fail(self):
        for field in module.COUNTS + module.MAXIMA:
            self.setUp(); self.peers['peer-0']['MovementValidation']['Hunters'][0][field] = 1
            self.assert_status('FAIL')

    def test_invalid_hard_rejection_overflow_fail(self):
        for field in ('Invalid', 'HardRejections', 'Overflow'):
            for value in (1, -1, True, '0', None, 2**63):
                self.setUp(); self.server['MovementValidation'][field] = value; self.assert_status('FAIL')

    def test_count_schema_type_negative_missing_or_duplicate_fail(self):
        for field in module.COUNTS:
            for value in (-1, True, 1.5, None, 2**63):
                self.setUp(); self.counts[field] = value; self.assert_status('FAIL')
        self.setUp(); self.server['MovementValidation']['Hunters'].pop(); self.assert_status('FAIL')
        self.setUp(); self.server['MovementValidation']['Hunters'][1] = copy.deepcopy(self.counts); self.assert_status('FAIL')
        self.setUp(); del self.counts['Exempt']; self.assert_status('FAIL')

    def test_false_zero_counts_rows_and_denominator_disagreement_fail(self):
        self.server['MovementValidation']['Rows'] = [candidate()]; self.assert_status('FAIL')
        self.setUp(); self.counts['WouldRefuse'] = 1; self.assert_status('FAIL')
        self.setUp(); self.counts['InputAbsentWhileMoving'] = 3; self.assert_status('FAIL')
        self.setUp(); self.counts['LongGaps'] = 2; self.assert_status('FAIL')
        self.setUp(); self.server['MovementValidation']['Hunters'][1]['Evaluated'] = 1; self.assert_status('FAIL')
        self.setUp(); self.counts['Unsupported'] = 1; self.assert_status('FAIL')

    def test_maxima_finite_nonnegative_and_measured(self):
        for value in (-1, float('nan'), float('inf'), True, '0'):
            self.setUp(); self.counts['MaximumHorizontal'] = value; self.assert_status('FAIL')
        self.setUp(); self.counts['Evaluated'] = 0; self.assert_status('FAIL')

    def test_candidate_identity_signal_finiteness_and_bounds(self):
        for changes in (dict(Epoch=0), dict(Slot=8), dict(Generation=0), dict(Life=0),
                        dict(Frame=2**32), dict(Hunter=7), dict(ElapsedFrames=0),
                        dict(ElapsedFrames=61), dict(Alt=True), dict(HorizontalSignal=False),
                        dict(VerticalSignal=1), dict(HorizontalPerFrame=float('nan')),
                        dict(AccelerationPerFrame=-1), dict(HorizontalPerFrame=3)):
            self.setUp(); self.counts.update(WouldRefuse=1, MaximumHorizontal=2.)
            self.server['MovementValidation']['Rows'] = [candidate(**changes)]; self.assert_status('FAIL')

    def test_duplicate_exact_row_and_wrong_per_hunter_counter_fail(self):
        self.counts.update(WouldRefuse=2, MaximumHorizontal=2.)
        self.server['MovementValidation']['Rows'] = [candidate(), candidate()]; self.assert_status('FAIL')
        self.setUp(); self.counts.update(WouldRefuse=1, MaximumHorizontal=2.)
        self.server['MovementValidation']['Rows'] = [candidate(hunter=6)]; self.assert_status('FAIL')

    def test_malformed_lists_rows_fail_without_exception(self):
        for field in ('Hunters', 'Rows'):
            self.setUp(); self.server['MovementValidation'][field] = None; self.assert_status('FAIL')
        self.setUp(); self.server['MovementValidation']['Rows'] = [{}]; self.assert_status('FAIL')


if __name__ == '__main__': unittest.main()
