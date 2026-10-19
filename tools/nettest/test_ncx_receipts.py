import copy
import json
import unittest
from ncx_receipts import REASONS, audit
from test_ncx_timeline import row, report


def captured(rows, **counts):
    result = report(rows)
    result['ClaimReceiptAccounting'] = dict.fromkeys((*REASONS, 'Created', 'Outstanding'), 0)
    result['ClaimReceiptAccounting'].update(counts)
    return result


class ReceiptAuditTests(unittest.TestCase):
    def test_exact_delivery_is_not_a_payment_and_has_no_raw_source_identifiers(self):
        source = captured([row('claim-declared'), row('receipt-retired', Result=0)], Created=1, Delivered=1)
        result = audit({'peer-0': source})
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['processes'][0]['keyedRetirements'], 1)
        self.assertNotIn('AuthorityEpoch', json.dumps(result))
        self.assertEqual(source['ShotTimeline']['Rows'][1]['Stage'], 'receipt-retired')

    def test_every_cancellation_timeout_and_eviction_disposition_is_closed(self):
        for index, name in enumerate(REASONS):
            source = captured([row('claim-declared'), row('receipt-retired', Result=index)], Created=1, **{name: 1})
            self.assertEqual(audit({'peer-0': source})['status'], 'PASS', name)

    def test_component_phase_and_life_cannot_borrow_another_declaration(self):
        for field in ('Component', 'Phase', 'VictimLife', 'ClaimId'):
            rows = [row('claim-declared'), row('receipt-retired', Result=0, **{field: 2})]
            self.assertEqual(audit({'peer-0': captured(rows, Created=1, Delivered=1)})['status'], 'FAIL', field)

    def test_duplicate_retirement_is_a_failure(self):
        rows = [row('claim-declared'), row('receipt-retired'), row('receipt-retired')]
        self.assertEqual(audit({'peer-0': captured(rows, Created=1, Delivered=1)})['status'], 'FAIL')

    def test_missing_rows_overflow_and_outstanding_are_incomplete(self):
        source = captured([], Created=1, Delivered=1)
        self.assertEqual(audit({'peer-0': source})['status'], 'INCOMPLETE')
        source = captured([row('claim-declared')], Created=1, Outstanding=1)
        self.assertEqual(audit({'peer-0': source})['status'], 'INCOMPLETE')
        source = captured([]); source['ShotTimeline']['Overflow'] = 1
        self.assertEqual(audit({'peer-0': source})['status'], 'INCOMPLETE')

    def test_counters_do_not_conserve_is_a_failure(self):
        self.assertEqual(audit({'peer-0': captured([], Created=1)})['status'], 'FAIL')

    def test_unavailable_and_invalid_counts_or_reasons_never_pass(self):
        for value in (-1, True, 1.5, 1 << 63):
            self.assertEqual(audit({'peer-0': captured([], Created=value)})['status'], 'INCOMPLETE')
        source = captured([row('claim-declared'), row('receipt-retired', Result=7)], Created=1, Delivered=1)
        self.assertEqual(audit({'peer-0': source})['status'], 'INCOMPLETE')
        self.assertEqual(audit({'peer-0': {}})['status'], 'INCOMPLETE')

    def test_no_fire_observer_zero_counts_pass_without_claiming_activity(self):
        source = captured([]); before = copy.deepcopy(source)
        self.assertEqual(audit({'peer-2': source})['status'], 'PASS')
        self.assertEqual(source, before)
        self.assertEqual(audit({'server': {}})['status'], 'INCOMPLETE')
