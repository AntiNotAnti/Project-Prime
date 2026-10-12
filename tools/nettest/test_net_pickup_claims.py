import copy
import struct
import unittest
import net_pickup_claims as audit


def fixture():
    key=dict(Epoch=9,Match=2,Slot=1,Generation=3,Life=4,Sequence=1)
    spawn=dict(Epoch=9,Match=2,Room=1,SpawnId=7,Generation=1)
    life=dict(AuthorityEpoch=9,MatchId=2,ShooterSlot=1,Generation=3,LifeId=4,ShotId=0)
    claim=struct.pack('<QHBHHIIiIBIfff',9,2,1,3,4,1,1,7,1,0,50,0,0,0)
    verdict=claim+struct.pack('<BIIQII',1,110,100,1,110,1910)
    row=dict(Key=key,Spawn=spawn,Type='DoubleDamage',SourceFrame=50,ObservedFrame=110,Payload=claim.hex())
    terminal=dict(Key=key,Spawn=spawn,Result='Grant',DecisionFrame=110,ProofFrame=100,GrantSequence=1,GrantFrame=110,ExpiresAtFrame=1910,ObservedFrame=110,Payload=verdict.hex())
    proof=dict(Key=key,Spawn=spawn,Type='DoubleDamage',SourceFrame=50,CapturedSourceFrame=50,ClaimPosition=[0,0,0],Life=life,AuthorityFrame=100,Position=[0,0,0],SpherePosition=[0,0,0],Radius=.5,MinHeight=-1,MaxHeight=1,Alt=False,Alive=True,CanPickup=True,ItemPosition=[.5,0,0],Available=True,ItemAuthorityFrame=100,Window=12,DecisionFrame=110,ProofFrame=100,GrantSequence=1,GrantFrame=110,ExpiresAtFrame=1910)
    grant=dict(Sequence=1,Spawn=spawn,Life=life,Type='DoubleDamage',ContactFrame=110,EffectiveFrame=111,ExpiresAtFrame=1910,EndFrame=1910,EndReason='Active',SourceContactFrame=50,SourceEffectiveFrame=51)
    blank=dict(Overflow=0,PickupClaims=[],PickupVerdicts=[],PickupItems=[],PickupProofs=[])
    server=copy.deepcopy(blank);peer=copy.deepcopy(blank)
    server.update(TimedGrants=[grant],PickupClaims=[dict(row,Stage='received')],PickupVerdicts=[dict(terminal,Stage='authority')],PickupProofs=[proof])
    peer.update(PickupClaims=[dict(row,Stage='offered'),dict(row,Stage='sent')],PickupVerdicts=[dict(terminal,Stage='received')])
    return server,[peer]


class AuditTests(unittest.TestCase):
    def result(self,s,p): return audit.audit(s,p)['status']
    def test_valid(self): self.assertEqual(self.result(*fixture()),'PASS')
    def test_captured_source_offsets(self):
        for offset in (0,1,2):
            s,p=fixture();s['PickupProofs'][0]['CapturedSourceFrame']=50+offset
            self.assertEqual(self.result(s,p),'PASS')
        for value in (49,53):
            s,p=fixture();s['PickupProofs'][0]['CapturedSourceFrame']=value
            self.assertEqual(self.result(s,p),'FAIL')
        s,p=fixture();del s['PickupProofs'][0]['CapturedSourceFrame']
        self.assertEqual(self.result(s,p),'FAIL')
    def test_captured_source_wrap(self):
        for offset in (0,1,2):
            s,p=fixture();source=2**32-1
            s['PickupProofs'][0].update(SourceFrame=source,CapturedSourceFrame=(source+offset)&audit.MASK)
            for r in s['PickupClaims']+p[0]['PickupClaims']:
                r['SourceFrame']=source;b=bytearray.fromhex(r['Payload']);struct.pack_into('<I',b,32,source);r['Payload']=b.hex()
            for r in s['PickupVerdicts']+p[0]['PickupVerdicts']:
                b=bytearray.fromhex(r['Payload']);struct.pack_into('<I',b,32,source);r['Payload']=b.hex()
            s['TimedGrants'][0].update(SourceContactFrame=source,SourceEffectiveFrame=0)
            self.assertEqual(self.result(s,p),'PASS')
    def test_missing_proof(self):
        s,p=fixture();s['PickupProofs']=[];self.assertEqual(self.result(s,p),'FAIL')
    def test_conflicting_retry(self):
        s,p=fixture();r=copy.deepcopy(s['PickupVerdicts'][0]);r['DecisionFrame']=111;b=bytearray.fromhex(r['Payload']);struct.pack_into('<I',b,49,111);r['Payload']=b.hex();s['PickupVerdicts'].append(r);self.assertEqual(self.result(s,p),'FAIL')
    def test_exact_retry(self):
        s,p=fixture();s['PickupVerdicts']*=2;p[0]['PickupClaims'].append(copy.deepcopy(p[0]['PickupClaims'][1]));self.assertEqual(self.result(s,p),'PASS')
    def test_spoof_life(self):
        s,p=fixture();s['PickupProofs'][0]['Life']=dict(s['PickupProofs'][0]['Life'],LifeId=5);self.assertEqual(self.result(s,p),'FAIL')
    def test_nonfinite(self):
        s,p=fixture();s['PickupProofs'][0]['SpherePosition']=[float('nan'),0,0];self.assertEqual(self.result(s,p),'FAIL')
    def test_reach_boundary_strict(self):
        s,p=fixture();s['PickupProofs'][0]['ItemPosition']=[.95,0,0];self.assertEqual(self.result(s,p),'FAIL');s['PickupProofs'][0]['ItemPosition']=[.949,0,0];self.assertEqual(self.result(s,p),'PASS')
    def test_float32_radius_boundary_neighbors(self):
        s,p=fixture();proof=s['PickupProofs'][0]
        radius=audit.fadd(proof['Radius'],.45)
        bits=struct.unpack('<I',struct.pack('<f',radius))[0]
        inside=struct.unpack('<f',struct.pack('<I',bits-1))[0]
        outside=struct.unpack('<f',struct.pack('<I',bits+1))[0]
        # JSON decimals are quantized back to the original native Single.
        proof['ItemPosition']=[inside,0,0];self.assertEqual(self.result(s,p),'PASS')
        proof['ItemPosition']=[radius,0,0];self.assertEqual(self.result(s,p),'FAIL')
        proof['ItemPosition']=[outside,0,0];self.assertEqual(self.result(s,p),'FAIL')
        # This value is inside a float64 .95 radius but rounds to the exact
        # native radius, which strict native contact correctly excludes.
        proof['ItemPosition']=[.94999999,0,0];self.assertEqual(self.result(s,p),'FAIL')
    def test_alt_center_cancellation_order(self):
        pose=dict(Position=[16383.5,0,0],SpherePosition=[16384,0,0],Radius=.5,
                  MinHeight=-1,MaxHeight=1,Alt=True)
        reported=[16383.9990234375,0,0];item=[16385.44921875,0,0]
        self.assertEqual(audit.fadd(reported[0],audit.fsub(16384,16383.5)),16384.5)
        self.assertTrue(audit.reaches(pose,item,reported))
        # Reordered float64 item-position-offset produces .9501953125 and
        # wrongly rejects the contact. Native center construction yields .94921875.
        reordered=item[0]-reported[0]-(pose['SpherePosition'][0]-pose['Position'][0])
        self.assertGreater(reordered,.95)
        pose=dict(Position=[16383.9990234375,0,0],SpherePosition=[16384.5,0,0],Radius=.5002,
                  MinHeight=-1,MaxHeight=1,Alt=True)
        self.assertFalse(audit.reaches(pose,[16384.951171875,0,0],[16383.5,0,0]))
    def test_float32_position_tolerance_neighbors(self):
        bits=struct.unpack('<I',struct.pack('<f',.75))[0]
        inside=struct.unpack('<f',struct.pack('<I',bits-1))[0]
        outside=struct.unpack('<f',struct.pack('<I',bits+1))[0]
        self.assertLessEqual(audit.distance_squared([inside,0,0],[0,0,0]),audit.fmul(.75,.75))
        self.assertGreater(audit.distance_squared([outside,0,0],[0,0,0]),audit.fmul(.75,.75))
    def test_expired_history(self):
        s,p=fixture();s['PickupProofs'][0]['Window']=12;s['PickupProofs'][0]['AuthorityFrame']=97;s['PickupProofs'][0]['ItemAuthorityFrame']=97;s['PickupProofs'][0]['ProofFrame']=97
        for r in [s['PickupVerdicts'][0],p[0]['PickupVerdicts'][0]]:
            r['ProofFrame']=97;b=bytearray.fromhex(r['Payload']);struct.pack_into('<I',b,53,97);r['Payload']=b.hex()
        self.assertEqual(self.result(s,p),'FAIL')
    def test_missing_owner_receipt(self):
        s,p=fixture();p[0]['PickupVerdicts']=[];self.assertEqual(self.result(s,p),'FAIL')
    def test_refusal_terminals_pass_without_grant_coverage(self):
        s,p=fixture();s['TimedGrants']=[];s['PickupProofs']=[]
        for row in [s['PickupVerdicts'][0],p[0]['PickupVerdicts'][0]]:
            claim=bytes.fromhex(row['Payload'])[:48]
            row.update(Result='HistoryUnavailable',ProofFrame=0,GrantSequence=0,GrantFrame=0,ExpiresAtFrame=0,
                       Payload=(claim+struct.pack('<BIIQII',9,110,0,0,0,0)).hex())
        result=audit.audit(s,p)
        self.assertEqual(result['terminalCoverage'],dict(status='PASS',observed=1))
        self.assertEqual(result['grantedClaimProofCoverage'],dict(status='NOT_RUN',observed=0))
        self.assertEqual(result['status'],'INCOMPLETE')
        self.assertIn('No Grant verdict',result['deferred'][0])
    def test_grant_coverage_is_explicit(self):
        result=audit.audit(*fixture())
        self.assertEqual(result['terminalCoverage']['status'],'PASS')
        self.assertEqual(result['grantedClaimProofCoverage'],dict(status='PASS',observed=1))
    def test_zero(self):
        s,p=fixture()
        for r in [s,*p]:
            for n in ['PickupClaims','PickupVerdicts','PickupProofs']:r[n]=[]
        s['TimedGrants']=[];self.assertEqual(self.result(s,p),'NOT_RUN')
    def test_unsent_deferred(self):
        s,p=fixture();s['PickupVerdicts']=[];s['PickupClaims']=[];s['PickupProofs']=[];s['TimedGrants']=[];p[0]['PickupVerdicts']=[];p[0]['PickupClaims']=p[0]['PickupClaims'][:1];self.assertEqual(self.result(s,p),'INCOMPLETE')
    def test_overflow(self):
        s,p=fixture();p[0]['Overflow']=1;self.assertEqual(self.result(s,p),'FAIL')
    def test_retroactive(self):
        s,p=fixture();s['PickupProofs'][0]['GrantFrame']=99;self.assertEqual(self.result(s,p),'FAIL')
    def test_bool_identity(self):
        s,p=fixture();p[0]['PickupClaims'][0]['Key']=dict(p[0]['PickupClaims'][0]['Key'],Slot=True);self.assertEqual(self.result(s,p),'FAIL')


if __name__=='__main__':unittest.main()
