"""Constructed JSON red/positive controls, not captured native or UDP evidence."""
import copy
import json
import unittest
from ncx_fastpath import audit, COUNTERS, STATES, REASONS


def ack(**changes):
    value = dict(ShotId=0, ClaimId=0, Result=0, VictimSlot=0, VictimGeneration=0,
                 VictimLife=0, DamageApplied=0, HealthAfter=0, HalfturretHealthAfter=0,
                 DamageSequence=0, Flags=0)
    value.update(changes)
    return value


def request(**changes):
    value = dict(Id=0, Weapon=0, Flags=0, Damage=0, Frame=0, AckFrame=0, LaunchFrame=0,
                 Point=dict(X=0, Y=0, Z=0), Direction=dict(X=0, Y=0, Z=0))
    value.update(changes)
    return value


def native():
    return dict(Key=dict(Shot=dict(AuthorityEpoch=11, MatchId=7, ShooterSlot=0,
                                  Generation=2, LifeId=3, ShotId=19),
                         Victim=1, Generation=4, Life=5, Component=1, Kind=1, Phase=0),
                Stage=2, LegalFrame=99, GeometryFrame=90, Weapon=4,
                Decision=dict(State=3, Reason=9, Damage=dict(Status=1, Reason=20,
                              NominalDamage=12, HealthDebit=12, Lethal=False, Head=False), Paid=ack()),
                Attempts=1, Compared=True, Agrees=True, ActualDebit=12, ActualHealth=88,
                Payments=1, Terminals=0, Corrected=False, PreHealth=100, TerminalDisagreements=0,
                ActualOutcome=ack(ShotId=19, VictimSlot=1, VictimGeneration=4, VictimLife=5,
                                  DamageApplied=12, HealthAfter=88, DamageSequence=31, Flags=64),
                Request=request())


def native_evidence(row):
    """Explicit synthetic capture facts; never described as native evidence."""
    source_life = dict(row['Key']['Shot'], ShotId=0)
    victim_life = dict(AuthorityEpoch=11, MatchId=7, ShooterSlot=1, Generation=4, LifeId=5, ShotId=0)
    def gate(key):
        return dict(Life=key, Frame=99, Phase=1, Complete=True, Health=100, Active=True, Biped=True,
                    Bot=False, SpawnProtected=False, MatchSpawnProtected=False, DamageInvulnerable=False,
                    CameraBlocked=False, SuppressDamage=False, AimTrainer=False, ExistingStatus=False,
                    Enhanced=False, Teams=False, FriendlyFire=False, InstaGib=False, OneInChamber=False,
                    Team=-1, Mode=3, TeamCount=0)
    zero = dict(X=0, Y=0, Z=0); unit = dict(X=1, Y=0, Z=0)
    return dict(SourceVerified=True, GeometryVerified=True, PriorPayment=0, TransientEmission=False,
        SourceGates=gate(source_life), VictimGates=gate(victim_life),
        Profile=dict(Key=victim_life, PackedEffectiveness=sum(2 << (i * 2) for i in range(9)),
                     DamageLevel=1, Handicap=0, Hunter=0, Balanced=False, Transitioning=False,
                     BalancedDisruptImmunity=0, EffectStateKnown=True, EffectFrame=99),
        Source=dict(Key=copy.deepcopy(row['Key']['Shot']), Room=1, AuthorityFrame=99, Lane=1, TrustedSource=True,
                    Source=dict(Source=0, Pose=0, DoubleHintAgrees=True, AmmoType=1, Cost=1, Cooldown=10),
                    Unavailable=0, Knowledge=1, Funding=None, DoubleHintAgrees=True, AuthorizedDouble=False, Components=1,
                    Fire=dict(ShotId=19, SourceLife=3, SourceFrame=90, AckFrame=90, AckSubFrame=0,
                              Kind=0, Weapon=4, Charge=0, ContinuousPhase=0, PoseFlags=1,
                              Origin=zero, Direction=unit, Aim=unit, View=unit, Reticle=dict(X=0, Y=0)),
                    Damage=dict(Present=True, FirstSegmentKnown=True, FirstCollisionFrame=90, Body=12,
                                Head=24, Splash=0, Radius=.1, Speed=1, Reach=0, Curve=0, Balanced=False,
                                Effects=0, DirectOnlyEffects=0, Origin=zero, NativeVelocity=unit)),
        Impact=dict(Payment=copy.deepcopy(row['Key']), Weapon=4, Frame=99, NativeChannel=12, NativeHead=False,
                    Direct=True, OrdinaryDirect=True, Point=unit, Origin=zero, SegmentStart=zero, SegmentEnd=unit))


def qualified_native():
    row = native(); row['NativeEvidence'] = json.loads(json.dumps(native_evidence(row)))
    return row


def paid():
    row = native(); row.update(Stage=1, Payments=0, Terminals=1, PreHealth=0)
    material = ack(ShotId=19, VictimSlot=1, VictimGeneration=4, VictimLife=5,
                   DamageApplied=12, HealthAfter=88, DamageSequence=31, Flags=64)
    row['Decision'] = dict(State=2, Reason=8, Damage=dict(Status=0, Reason=0,
                           NominalDamage=0, HealthDebit=0, Lethal=False, Head=False), Paid=material)
    row['ActualOutcome'] = dict(material, ClaimId=73, Result=1)
    row['Request'] = request(Id=73, Weapon=4, Flags=128, Damage=12, Frame=99, AckFrame=90, LaunchFrame=90)
    return row


def fallback():
    row = paid(); row.update(Stage=0, Compared=False, Agrees=False, ActualDebit=0, ActualHealth=0, Terminals=0)
    row['Decision'].update(State=0, Reason=6, Paid=ack()); row['ActualOutcome'] = ack()
    return row


def captured(rows, **changes):
    """Construct the runtime's reported counter vector, then falsify independently in tests."""
    counts = dict.fromkeys(COUNTERS, 0)
    for row in rows:
        state = row['Decision']['State']; state = STATES[state] if type(state) is int else state
        reason = row['Decision']['Reason']; reason = REASONS[reason] if type(reason) is int else reason
        if row['Corrected']:
            counts['CorrectedExcluded'] += 1
            continue
        eligible = state in ('VerifiedPaid', 'VerifiedNative'); pay = state == 'VerifiedNative'; reject = state == 'Reject'
        counts['fastEligible'] += int(eligible); counts['fastWouldPay'] += int(pay); counts['fastWouldReject'] += int(reject)
        for counter, wanted in [('missingSource', 'MissingSource'), ('headMismatch', 'HeadMismatch'),
            ('wallMismatch', 'WallMismatch'), ('orderingDeferred', 'OrderingDeferred'), ('familyUnsupported', 'FamilyUnsupported')]:
            counts[counter] += int(reason == wanted)
        counts['wouldDoublePay'] += int(pay and row['Payments'] > 1)
        counts['wouldAlterTrade'] += int(pay and row['Compared'] and row['Decision']['Damage']['Lethal'] != (row['ActualHealth'] == 0))
        counts['Compared'] += int(row['Compared']); counts['Disagreements'] += int(row['Compared'] and not row['Agrees'])
        counts['Uncompared'] += int(not row['Compared'] and (eligible or reject))
    value = dict(Schema=1, Mode=1, PerShooter=1024, Rows=rows, Overflow=0, Invalid=0, UnjoinedTerminal=0, **counts)
    value.update(changes)
    return dict(Role='server', CombatFastPath=value)


class FastPathAuditTests(unittest.TestCase):
    def result(self, rows, **changes):
        return audit({'server': captured(rows, **changes)})

    def test_constructed_paid_receipt_positive_is_scoped_and_privacy_safe(self):
        source = captured([paid()]); original = copy.deepcopy(source)
        result = audit({'private-authority-name': source})
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['processes'][0]['positiveEligibleComparisons'], 1)
        self.assertEqual(source, original)
        text = json.dumps(result)
        for identifier in ('AuthorityEpoch', 'ShooterSlot', 'ShotId', 'ClaimId', 'private-authority-name', 'Point'):
            self.assertNotIn(identifier, text)
        self.assertEqual(len(result['processes'][0]['observations'][0]['token']), 64)
        self.assertIn('source/resources/collision', result['scope'])

    def test_known_numeric_and_text_enums_and_flags_are_equivalent(self):
        row = paid(); expected = self.result([row]); row['Stage'] = 'ClaimReady'
        row['Decision'].update(State='VerifiedPaid', Reason='PaidDuplicate')
        row['Decision']['Damage'].update(Status='Unavailable', Reason='Unknown')
        row['Decision']['Paid'].update(Result='Applied', Flags='OutcomePresent')
        row['ActualOutcome'].update(Result='AlreadyResolved', Flags='OutcomePresent')
        result = self.result([row], Mode='Shadow')
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['processes'][0]['observations'], expected['processes'][0]['observations'])

    def test_native_material_positive_requires_separate_authorization_export(self):
        result = self.result([native()])
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertTrue(any('authorization capture' in item for item in result['incomplete']))

    def test_constructed_native_capture_and_frozen_formula_positive(self):
        self.assertEqual(self.result([qualified_native()])['status'], 'PASS')
        row = qualified_native(); evidence = row['NativeEvidence']
        evidence['Profile'].update(DamageLevel=1.25, Handicap=50)
        evidence['Profile']['PackedEffectiveness'] &= ~(3 << 8)
        evidence['Profile']['PackedEffectiveness'] |= 1 << 8  # Half effectiveness.
        # Native truncation: 12 -> 6 -> 7 -> 3, independent of source statement.
        row['Decision']['Damage'].update(NominalDamage=3, HealthDebit=3)
        row.update(ActualDebit=3, ActualHealth=97); row['ActualOutcome'].update(DamageApplied=3, HealthAfter=97)
        self.assertEqual(self.result([row])['status'], 'PASS')
        row['Decision']['Damage'].update(NominalDamage=4, HealthDebit=4)
        row.update(ActualDebit=4, ActualHealth=96); row['ActualOutcome'].update(DamageApplied=4, HealthAfter=96)
        self.assertEqual(self.result([row])['status'], 'FAIL')

    def test_native_capture_head_and_block_positive_controls(self):
        row = qualified_native(); evidence = row['NativeEvidence']
        evidence['Impact'].update(NativeHead=True, NativeChannel=24)
        row['Decision']['Damage'].update(NominalDamage=24, HealthDebit=24, Head=True)
        row.update(ActualDebit=24, ActualHealth=76); row['ActualOutcome'].update(DamageApplied=24, HealthAfter=76, Flags=65)
        self.assertEqual(self.result([row])['status'], 'PASS')
        row = qualified_native(); row['NativeEvidence']['VictimGates']['SpawnProtected'] = True
        row['Decision'].update(State=4, Reason=10)
        row['Decision']['Damage'].update(Status=2, Reason=14, NominalDamage=0, HealthDebit=0)
        row.update(ActualDebit=0, ActualHealth=100, Payments=0, ActualOutcome=ack())
        self.assertEqual(self.result([row, paid()])['status'], 'PASS')
        self.assertEqual(self.result([row])['status'], 'INCOMPLETE')

    def test_native_capture_cannot_borrow_source_geometry_or_context_stamps(self):
        mutations = [lambda e: e.update(SourceVerified=False), lambda e: e.update(GeometryVerified=False),
            lambda e: e.update(PriorPayment=1), lambda e: e['Source']['Key'].update(ShotId=20),
            lambda e: e['Source'].update(TrustedSource=False), lambda e: e['Source']['Source'].update(Cost=-1),
            lambda e: e['Source']['Fire'].update(ContinuousPhase=1 << 28),
            lambda e: e['SourceGates'].update(Phase=0), lambda e: e['VictimGates'].update(Frame=98),
            lambda e: e['VictimGates']['Life'].update(LifeId=6), lambda e: e['Profile'].update(EffectFrame=98),
            lambda e: e['Profile'].update(DamageLevel=.5), lambda e: e['Profile'].update(Handicap=9),
            lambda e: e['Impact'].update(NativeHead=True), lambda e: e['Impact'].update(NativeChannel=13),
            lambda e: e['Impact']['SegmentEnd'].update(X=2), lambda e: e['Source']['Damage'].update(Effects=1)]
        for mutation in mutations:
            row = qualified_native(); mutation(row['NativeEvidence'])
            self.assertNotEqual(self.result([row])['status'], 'PASS')

    def test_granted_double_requires_complete_exact_life_funding(self):
        row = qualified_native(); evidence = row['NativeEvidence']; source = evidence['Source']
        source.update(Knowledge=2, AuthorizedDouble=True)
        source['Fire']['ContinuousPhase'] = 1 << 29
        source['Funding'] = dict(Sequence=1, Spawn=dict(Epoch=11, Match=7, Room=1, SpawnId=123, Generation=1),
            Life=dict(source['Key'], ShotId=0), Type=0, ContactFrame=80, SourceContactFrame=80,
            ExpiresAtFrame=1880, ObservationAuthorityFrame=90)
        self.assertEqual(self.result([row])['status'], 'PASS')
        for mutation in (lambda e: e['Source'].update(Funding=None),
                         lambda e: e['Source']['Funding']['Life'].update(LifeId=9),
                         lambda e: e['Source']['Funding'].update(ExpiresAtFrame=1881),
                         lambda e: e['Source']['Funding'].update(ObservationAuthorityFrame=80),
                         lambda e: e['Source']['Funding']['Spawn'].update(Room=2),
                         lambda e: e['Source'].update(AuthorityFrame=2000)):
            changed = copy.deepcopy(row); mutation(changed['NativeEvidence'])
            self.assertNotEqual(self.result([changed])['status'], 'PASS')

    def test_native_debit_health_head_lethal_and_payment_red_controls(self):
        mutations = [lambda r: r.update(ActualDebit=11), lambda r: r.update(ActualHealth=87),
                     lambda r: r.update(Payments=2), lambda r: r['ActualOutcome'].update(Flags=65),
                     lambda r: r['ActualOutcome'].update(Flags=66), lambda r: r['ActualOutcome'].update(ShotId=20),
                     lambda r: r['Decision']['Damage'].update(HealthDebit=13),
                     lambda r: r['Decision']['Damage'].update(Lethal=True)]
        for mutation in mutations:
            row = native(); mutation(row)
            self.assertEqual(self.result([row])['status'], 'FAIL')

    def test_paid_material_identity_head_and_sticky_disagreement_red_controls(self):
        mutations = [lambda r: r['ActualOutcome'].update(ClaimId=74), lambda r: r['ActualOutcome'].update(VictimLife=6),
                     lambda r: r['ActualOutcome'].update(DamageApplied=11), lambda r: r['ActualOutcome'].update(DamageSequence=32),
                     lambda r: r['ActualOutcome'].update(Flags=65), lambda r: r['Decision']['Paid'].update(Flags=65),
                     lambda r: r.update(TerminalDisagreements=1), lambda r: r.update(Agrees=False)]
        for mutation in mutations:
            row = paid(); mutation(row)
            self.assertEqual(self.result([row])['status'], 'FAIL')
        row = paid(); row.update(TerminalDisagreements=1, Agrees=False)
        self.assertEqual(self.result([row])['status'], 'FAIL')

    def test_all_structured_counters_are_recomputed(self):
        for counter in COUNTERS:
            source = captured([paid()]); source['CombatFastPath'][counter] += 1
            self.assertEqual(audit({'server': source})['status'], 'FAIL', counter)

    def test_duplicate_exact_identity_fails_but_full_claim_shape_is_distinct(self):
        self.assertEqual(self.result([paid(), paid()])['status'], 'FAIL')
        first = paid(); second = paid(); second['Request']['Damage'] = 11
        result = self.result([first, second])
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(len({r['token'] for r in result['processes'][0]['observations']}), 2)
        second = paid(); second['Request']['Point']['X'] = -0.0
        self.assertEqual(self.result([first, second])['status'], 'FAIL')

    def test_missing_empty_off_fallback_and_uncompared_never_pass(self):
        self.assertEqual(audit({})['status'], 'INCOMPLETE')
        self.assertEqual(audit({'server': {}})['status'], 'INCOMPLETE')
        self.assertEqual(self.result([])['status'], 'INCOMPLETE')
        self.assertEqual(self.result([paid()], Mode=0)['status'], 'INCOMPLETE')
        self.assertEqual(self.result([fallback()])['status'], 'INCOMPLETE')
        row = paid(); row.update(Compared=False, Agrees=False, Terminals=0)
        self.assertEqual(self.result([row])['status'], 'INCOMPLETE')
        row = native(); row.update(Compared=False, Agrees=False)
        self.assertEqual(self.result([row])['status'], 'INCOMPLETE')

    def test_overflow_invalid_unjoined_and_corrections_are_explicitly_incomplete(self):
        for field in ('Overflow', 'Invalid', 'UnjoinedTerminal'):
            self.assertEqual(self.result([paid()], **{field: 1})['status'], 'INCOMPLETE')
        corrected = paid(); corrected['Corrected'] = True; corrected['ActualOutcome']['DamageSequence'] += 1
        result = self.result([corrected])
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['processes'][0]['counters']['CorrectedExcluded'], 1)
        self.assertIsNone(result['processes'][0]['observations'][0]['independentlyAgrees'])

    def test_unknown_enums_missing_wire_fields_and_nonfinite_shapes_are_incomplete(self):
        for mutation in (lambda r: r['Decision'].update(State=99), lambda r: r['Decision'].update(Reason='new-reason'),
                         lambda r: r['ActualOutcome'].update(Flags='future-flag'),
                         lambda r: r['Decision'].update(Paid=dict(Accepted=True)),
                         lambda r: r['Request']['Point'].update(X=float('nan'))):
            source = captured([paid()]); mutation(source['CombatFastPath']['Rows'][0])
            self.assertEqual(audit({'server': source})['status'], 'INCOMPLETE')
        for value in (True, -1, 1.5, 1 << 63):
            self.assertEqual(self.result([paid()], fastEligible=value)['status'], 'INCOMPLETE')

    def test_state_stage_and_supported_family_contradictions_fail(self):
        for mutation in (lambda r: r.update(Stage=0), lambda r: r.update(Weapon=2),
                         lambda r: r['Key'].update(Component=2), lambda r: r.update(Payments=1),
                         lambda r: r['Decision'].update(Reason=9),
                         lambda r: r['Request'].update(Flags=129)):
            row = paid(); mutation(row)
            self.assertEqual(self.result([row])['status'], 'FAIL')

    def test_client_only_or_ignored_client_data_cannot_manufacture_authority_parity(self):
        self.assertEqual(audit({'peer-0': dict(Role='client', CombatFastPath=captured([paid()])['CombatFastPath'])})['status'], 'INCOMPLETE')
        self.assertEqual(audit({'server': captured([paid()]), 'peer-0': {}})['status'], 'PASS')


if __name__ == '__main__':
    unittest.main()
