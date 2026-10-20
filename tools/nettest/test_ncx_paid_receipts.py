"""Constructed JSON parser/latency controls, not captured UDP/native evidence."""
import copy
import unittest
from ncx_paid_receipts import audit
from test_ncx_fastpath import paid, captured


def report(mode='Paid', start=99, end=99):
    row=paid(); row['LegalFrame']=start; row['PaidBypass']=mode=='Paid'
    r=captured([row], Mode=mode, PaidReceipts=int(mode=='Paid'))
    key=row['Key']; shot=key['Shot']
    def event(stage, frame, timestamp):
        return dict(Stage=stage, Key=copy.deepcopy(shot), Component=1 if stage=='claim-received' else 0,
                    Phase=0, Kind=1 if stage=='claim-received' else 0, Victim=1,
                    VictimGeneration=4, VictimLife=5, ClaimId=73, DamageEventId=0 if stage=='claim-received' else 31,
                    SourceFrame=0, AckFrame=90 if stage=='claim-received' else 0, AckSubFrame=0,
                    LocalFrame=frame, AuthorityFrame=frame, Timestamp=timestamp, Weapon=4 if stage=='claim-received' else 255,
                    Value=12, Result=-1 if stage=='claim-received' else 1)
    r['ShotTimeline']=dict(Schema=1, Enabled=True, Complete=True, Overflow=0, UnkeyedRows=0,
        Clock=dict(Domain='ProcessStopwatch',Frequency=1000000000),
        Rows=[event('claim-received',start,1000),event('terminal-decided',end,1100)])
    return r


class PaidReceiptLatencyTests(unittest.TestCase):
    def test_same_tick_paid_and_default_policy(self):
        for mode in ('Shadow','Paid'):
            r=report(mode); before=copy.deepcopy(r); result=audit(r,mode.lower())
            self.assertEqual(result['status'],'PASS'); self.assertEqual(result['eligibleSamples'],1)
            self.assertEqual(result['p95AuthorityTicks'],0); self.assertEqual(r,before)
            self.assertEqual(result['samples'][0]['paidBypass'],mode=='Paid')
            self.assertEqual(len(result['samples'][0]['token']),64)
    def test_valid_unsigned_frame_wrap(self):
        self.assertEqual(audit(report(start=0xffffffff,end=0))['p95AuthorityTicks'],1)
    def test_latency_over_two_ticks_fails(self):
        r=audit(report(end=102)); self.assertEqual(r['status'],'FAIL'); self.assertEqual(r['p95AuthorityTicks'],3)
    def test_missing_or_cross_sequence_terminal_is_incomplete(self):
        for change in ('missing','sequence','life','shot'):
            r=report();e=r['ShotTimeline']['Rows'][-1]
            if change=='missing':r['ShotTimeline']['Rows'].pop()
            elif change=='sequence':e['DamageEventId']=32
            elif change=='life':e['VictimLife']=6
            else:e['Key']['ShotId']=20
            self.assertEqual(audit(r)['status'],'INCOMPLETE')
    def test_ambiguous_component_or_request_does_not_borrow_latency(self):
        r=report();row=copy.deepcopy(r['CombatFastPath']['Rows'][0]);row['Key']['Component']=2
        r['CombatFastPath']['Rows'].append(row)
        result=audit(r);self.assertNotEqual(result['status'],'PASS');self.assertEqual(result['eligibleSamples'],0)
    def test_cached_retry_after_first_terminal_does_not_extend_decision(self):
        r=report(); rows=r['ShotTimeline']['Rows']; later=copy.deepcopy(rows[-1]);later.update(LocalFrame=150,AuthorityFrame=150,Timestamp=2000)
        rows.append(later);self.assertEqual(audit(r)['p95AuthorityTicks'],0)
    def test_disabled_overflow_or_missing_cohort_is_incomplete(self):
        for field,value in [('Enabled',False),('Complete',False),('Overflow',1),('UnkeyedRows',1)]:
            r=report();r['ShotTimeline'][field]=value;self.assertEqual(audit(r)['status'],'INCOMPLETE')
        r=report();r['CombatFastPath']=captured([])['CombatFastPath'];self.assertEqual(audit(r)['status'],'INCOMPLETE')
    def test_wrong_clock_authority_or_mode_fails_closed(self):
        r=report();r['ShotTimeline']['Clock']['Domain']='client';self.assertEqual(audit(r)['status'],'INCOMPLETE')
        r=report();r['Role']='client';self.assertEqual(audit(r)['status'],'INCOMPLETE')
        self.assertEqual(audit(report(),'shadow')['status'],'FAIL')
    def test_false_bypass_and_falsified_count_fail(self):
        r=report();r['CombatFastPath']['Rows'][0]['PaidBypass']=False;self.assertEqual(audit(r)['status'],'FAIL')
        r=report();r['CombatFastPath']['PaidReceipts']=2;self.assertEqual(audit(r)['status'],'FAIL')
        r=report('Shadow');r['CombatFastPath']['Rows'][0]['PaidBypass']=True;self.assertEqual(audit(r)['status'],'FAIL')
    def test_negative_time_or_wrong_authority_domain_fail(self):
        r=report();r['ShotTimeline']['Rows'][-1]['Timestamp']=900;self.assertEqual(audit(r)['status'],'FAIL')
        r=report();r['ShotTimeline']['Rows'][-1]['AuthorityFrame']=100;self.assertEqual(audit(r)['status'],'FAIL')
    def test_invalid_typed_capture_is_incomplete(self):
        r=report();r['CombatFastPath']['Rows'][0]['PaidBypass']='true';self.assertEqual(audit(r)['status'],'INCOMPLETE')


if __name__=='__main__':unittest.main()
