import copy
import struct
import unittest
import net_grant_reconciliation as audit


def fixture():
    life=dict(AuthorityEpoch=9,MatchId=2,ShooterSlot=0,Generation=1,LifeId=1,ShotId=0)
    spawn=dict(Epoch=9,Match=2,Room=1,SpawnId=10,Generation=1)
    wire=struct.pack('<QHBHHIIBQiIIIBI',9,2,0,1,1,1,1,0,1,10,1,100,1900,0,100)
    state=dict(Stage='authority',Life=life,Room=1,Revision=1,Type='DoubleDamage',Sequence=1,Spawn=spawn,ContactFrame=100,EndFrame=1900,Reason='Active',AuthorityFrame=100,Resume=False,ObservedFrame=100,Payload=wire.hex())
    grant=dict(Sequence=1,Spawn=spawn,Life=life,Type='DoubleDamage',ContactFrame=100,EffectiveFrame=101,ExpiresAtFrame=1900,EndFrame=1900,EndReason='Active',SourceContactFrame=90,SourceEffectiveFrame=91)
    view=dict(Life=life,Type='DoubleDamage',ObservedFrame=101,AuthorityFrame=101,Owner=True,Covered=True,Remaining=1799,ConfirmedRemaining=1799,GrantSequence=1,PredictionSequence=0,NativeRemaining=1799,Alive=True)
    prediction=dict(view,ObservedFrame=100,AuthorityFrame=100,Remaining=1800,ConfirmedRemaining=0,GrantSequence=0,PredictionSequence=1,NativeRemaining=1800)
    server=dict(Overflow=0,TimedGrants=[grant],TimedGrantStates=[state],TimedGrantViews=[])
    client=dict(Overflow=0,TimedGrantStates=[dict(state,Stage='received')],TimedGrantViews=[prediction,view,dict(view,Owner=False)])
    return server,[client]


class Tests(unittest.TestCase):
    def result(self,s,c):return audit.audit(s,c)['status']
    def test_positive(self):self.assertEqual(self.result(*fixture()),'PASS')
    def test_wire_length(self):
        s,c=fixture();s['TimedGrantStates'][0]['Payload']=s['TimedGrantStates'][0]['Payload'][:-2];self.assertEqual(self.result(s,c),'FAIL')
    def test_reserved_reason_bits(self):
        s,c=fixture();r=s['TimedGrantStates'][0];b=bytearray.fromhex(r['Payload']);b[48]=0x40;r['Payload']=b.hex();self.assertEqual(self.result(s,c),'FAIL')
    def test_resume_bit(self):
        s,c=fixture()
        for r in [s['TimedGrantStates'][0],c[0]['TimedGrantStates'][0]]:
            b=bytearray.fromhex(r['Payload']);b[48]|=0x80;r.update(Resume=True,Payload=b.hex())
        self.assertEqual(self.result(s,c),'PASS')
    def test_structured_mismatch(self):
        s,c=fixture();s['TimedGrantStates'][0]['Revision']=2;self.assertEqual(self.result(s,c),'FAIL')
    def test_native_mismatch(self):
        s,c=fixture();s['TimedGrants'][0]['Spawn']=dict(s['TimedGrants'][0]['Spawn'],Generation=2);self.assertEqual(self.result(s,c),'FAIL')
    def test_orphan_receipt(self):
        s,c=fixture();s['TimedGrantStates']=[];self.assertEqual(self.result(s,c),'FAIL')
    def test_native_view_mismatch(self):
        s,c=fixture();c[0]['TimedGrantViews'][1]['NativeRemaining']=1800;self.assertEqual(self.result(s,c),'FAIL')
    def test_life_fence(self):
        s,c=fixture();c[0]['TimedGrantViews'][1]['Life']=dict(c[0]['TimedGrantViews'][1]['Life'],LifeId=2);self.assertEqual(self.result(s,c),'FAIL')
    def test_provisional_confirmed(self):
        s,c=fixture();c[0]['TimedGrantViews'][0]['ConfirmedRemaining']=1800;self.assertEqual(self.result(s,c),'FAIL')
    def test_contact_phase(self):
        s,c=fixture();c[0]['TimedGrantViews'][1]['AuthorityFrame']=100;self.assertEqual(self.result(s,c),'FAIL')
    def test_expired_confirmed(self):
        s,c=fixture();c[0]['TimedGrantViews'][1]['AuthorityFrame']=1900;self.assertEqual(self.result(s,c),'FAIL')
    def test_dead_confirmed(self):
        s,c=fixture();c[0]['TimedGrantViews'][1]['Alive']=False;self.assertEqual(self.result(s,c),'FAIL')
    def closure_fixture(self,reason='Death'):
        s,c=fixture();closed=copy.deepcopy(s['TimedGrants'][0]);closed.update(EndFrame=105,EndReason=reason);s['TimedGrants'].append(closed)
        state=copy.deepcopy(s['TimedGrantStates'][0]);state.update(Revision=2,EndFrame=105,Reason=reason,AuthorityFrame=105,ObservedFrame=105)
        b=bytearray.fromhex(state['Payload']);struct.pack_into('<I',b,19,2);struct.pack_into('<I',b,44,105);b[48]=audit.REASONS.index(reason);struct.pack_into('<I',b,49,105);state['Payload']=b.hex()
        s['TimedGrantStates'].append(state);c[0]['TimedGrantStates'].append(dict(state,Stage='received',ObservedFrame=130))
        v=copy.deepcopy(c[0]['TimedGrantViews'][1]);v.update(ObservedFrame=110,AuthorityFrame=110,Remaining=1790,ConfirmedRemaining=1790,NativeRemaining=1790);c[0]['TimedGrantViews'].append(v)
        return s,c
    def test_delayed_death_correction(self):
        result=audit.audit(*self.closure_fixture())
        self.assertEqual(result['status'],'PASS');self.assertEqual(result['closureCorrectionLag']['observedSamples'],1)
        self.assertEqual(result['closureCorrectionLag']['maxFrames'],20)
    def test_delayed_renewal_correction(self):
        self.assertEqual(self.result(*self.closure_fixture('Renewed')),'PASS')
    def test_missing_closure_delivery(self):
        s,c=self.closure_fixture();c[0]['TimedGrantStates']=c[0]['TimedGrantStates'][:1]
        self.assertEqual(self.result(s,c),'FAIL')
    def test_active_after_delivered_closure(self):
        s,c=self.closure_fixture();v=c[0]['TimedGrantViews'][-1];v.update(ObservedFrame=131,AuthorityFrame=131,Remaining=1769,ConfirmedRemaining=1769,NativeRemaining=1769)
        self.assertEqual(self.result(s,c),'FAIL')
    def superseding_renewal_fixture(self):
        s,c=self.closure_fixture('Renewed');c[0]['TimedGrantStates']=c[0]['TimedGrantStates'][:1]
        native=copy.deepcopy(s['TimedGrants'][0]);native.update(Sequence=2,Spawn=dict(native['Spawn'],Generation=2),ContactFrame=104,EffectiveFrame=105,ExpiresAtFrame=1904,EndFrame=1904,SourceContactFrame=94,SourceEffectiveFrame=95)
        s['TimedGrants'].append(native)
        state=copy.deepcopy(s['TimedGrantStates'][0]);state.update(Revision=3,Sequence=2,Spawn=native['Spawn'],ContactFrame=104,EndFrame=1904,AuthorityFrame=104,ObservedFrame=104)
        b=bytearray.fromhex(state['Payload']);struct.pack_into('<I',b,19,3);struct.pack_into('<Q',b,24,2);struct.pack_into('<I',b,36,2);struct.pack_into('<I',b,40,104);struct.pack_into('<I',b,44,1904);struct.pack_into('<I',b,49,104);state['Payload']=b.hex()
        s['TimedGrantStates'].append(state);c[0]['TimedGrantStates'].append(dict(state,Stage='received',ObservedFrame=130))
        return s,c
    def test_superseding_same_life_renewal_receipt(self):
        self.assertEqual(self.result(*self.superseding_renewal_fixture()),'PASS')
    def test_superseding_renewal_cannot_replace_death_notice(self):
        s,c=self.superseding_renewal_fixture();s['TimedGrants'][1]['EndReason']='Death';row=s['TimedGrantStates'][1];row['Reason']='Death';b=bytearray.fromhex(row['Payload']);b[48]=3;row['Payload']=b.hex()
        self.assertEqual(self.result(s,c),'FAIL')
    def test_superseding_renewal_cannot_start_before_old_end_minus_one(self):
        s,c=self.superseding_renewal_fixture();g=s['TimedGrants'][-1];g.update(ContactFrame=103,EffectiveFrame=104,ExpiresAtFrame=1903,EndFrame=1903)
        for r in [s['TimedGrantStates'][-1],c[0]['TimedGrantStates'][-1]]:
            r.update(ContactFrame=103,EndFrame=1903);b=bytearray.fromhex(r['Payload']);struct.pack_into('<I',b,40,103);struct.pack_into('<I',b,44,1903);r['Payload']=b.hex()
        self.assertEqual(self.result(s,c),'FAIL')
    def test_stale_visual_after_superseding_renewal(self):
        s,c=self.superseding_renewal_fixture();c[0]['TimedGrantViews'][-1].update(ObservedFrame=131,AuthorityFrame=131,ConfirmedRemaining=0)
        self.assertEqual(self.result(s,c),'FAIL')
    def test_unconfirmed_visual_after_delivered_closure(self):
        s,c=self.closure_fixture();v=c[0]['TimedGrantViews'][-1];v.update(ObservedFrame=131,AuthorityFrame=131,ConfirmedRemaining=0)
        self.assertEqual(self.result(s,c),'FAIL')
    def test_closure_correction_deadline(self):
        s,c=self.closure_fixture();v=c[0]['TimedGrantViews'][-1];v.update(ObservedFrame=1010,AuthorityFrame=1010,Remaining=890,ConfirmedRemaining=890,NativeRemaining=890);c[0]['TimedGrantStates'][-1]['ObservedFrame']=1100
        self.assertEqual(self.result(s,c),'FAIL')
    def test_provisional_owner_is_not_confirmed_owner_coverage(self):
        s,c=fixture();c[0]['TimedGrantViews']=[c[0]['TimedGrantViews'][0],c[0]['TimedGrantViews'][2]]
        r=audit.audit(s,c);self.assertEqual(r['status'],'INCOMPLETE');self.assertEqual(r['counts']['owner'],0)
    def test_no_activity(self):
        s,c=fixture();s.update(TimedGrants=[],TimedGrantStates=[]);c[0].update(TimedGrantStates=[],TimedGrantViews=[]);self.assertEqual(self.result(s,c),'NOT_RUN')
    def test_missing_observer_coverage(self):
        s,c=fixture();c[0]['TimedGrantViews']=c[0]['TimedGrantViews'][:2];self.assertEqual(self.result(s,c),'INCOMPLETE')
    def test_overflow(self):
        s,c=fixture();c[0]['Overflow']=1;self.assertEqual(self.result(s,c),'FAIL')
    def test_conflicting_revision(self):
        s,c=fixture();r=copy.deepcopy(s['TimedGrantStates'][0]);r['Resume']=True;b=bytearray.fromhex(r['Payload']);b[48]|=128;r['Payload']=b.hex();s['TimedGrantStates'].append(r);self.assertEqual(self.result(s,c),'FAIL')


if __name__=='__main__':unittest.main()
