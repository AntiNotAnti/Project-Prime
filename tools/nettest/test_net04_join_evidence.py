"""Constructed join falsifiers; not native emissions or campaign positives."""
import copy
import unittest
from net04_join_evidence import audit


def fixture():
    key = dict(AuthorityEpoch=17, MatchId=2, ShooterSlot=0, Generation=1, LifeId=1, ShotId=9)
    life = dict(key, ShotId=0)
    spawn = dict(Epoch=17, Match=2, Room=3, SpawnId=7, Generation=1)
    pickup = dict(Epoch=17, Match=2, Slot=0, Generation=1, Life=1, Sequence=2)
    funding = dict(Sequence=4, Spawn=spawn, Life=life, Type=0, ContactFrame=100,
                   SourceContactFrame=80, ExpiresAtFrame=1900, ObservationAuthorityFrame=120)
    shot = dict(Key=key, Room=3, AuthorityFrame=120, Lane=1,
                Source=dict(Source=0, Pose=0), LedgerAnswer=dict(Knowledge=2, Sequence=4),
                Knowledge=2, Funding=funding, Unavailable=0, Components=1,
                Fire=dict(ShotId=9, SourceLife=1, SourceFrame=90, AckFrame=120,
                          Weapon=0, Kind=0), Damage=dict(Body=10, Present=True))
    outcome = dict(ShotId=9, ClaimId=0, Result=0, VictimSlot=1, VictimGeneration=1,
                   VictimLife=2, DamageApplied=10, HealthAfter=90, HalfturretHealthAfter=0,
                   DamageSequence=3, Flags=64)
    pkey = dict(Shot=key, Victim=1, Generation=1, Life=2, Component=1, Kind=1)
    rows = dict(Certificates=[dict(Ordinal=2, ObservedFrame=120, Shot=shot, Retention=0)],
                Components=[dict(Ordinal=1, Value=dict(Key=key, Component=1, Room=3, Family=1),
                                 PlayerSource=True, AcceptedPlayback=True)],
                Claims=[dict(Ordinal=3, ObservedFrame=124, Key=key, ClaimId=6, ComponentId=1,
                             VictimSlot=1, VictimGeneration=1, VictimLifeId=2, Beam=0,
                             LaunchFrame=120, Flags=128, Damage=10)],
                Terminals=[dict(Ordinal=5, ObservedFrame=126, ShooterSlot=0, Key=key,
                                Outcome=dict(outcome, ClaimId=6))],
                Payments=[dict(Ordinal=4, ReservationFrame=124, PaymentFrame=125, Key=pkey,
                               Weapon=0, Lane=1, ClaimId=6, Outcome=outcome,
                               ActualTurretDamage=0, Correction=False, Impact=None)], Continuous=[])
    grant = dict(Sequence=4, Spawn=spawn, Life=life, Type='DoubleDamage', ContactFrame=100,
                 SourceContactFrame=80, ExpiresAtFrame=1900, EndFrame=1900, EndReason='Active')
    server = dict(Role='server', Protocol=56, TimedCaseJoins=dict(Schema=1, **rows), TimedGrants=[grant],
                  PickupVerdicts=[dict(Key=pickup, Spawn=spawn, Result='Grant', GrantSequence=4)],
                  PickupProofs=[dict(GrantSequence=4, Spawn=spawn, Life=life, Alive=True, CanPickup=True, Available=True)])
    views = [dict(Life=life, Type='DoubleDamage', Owner=True, Covered=True, Alive=True,
                  ConfirmedRemaining=0, PredictionSequence=2, GrantSequence=0, ObservedFrame=80),
             dict(Life=life, Type='DoubleDamage', Owner=True, Covered=True, Alive=True,
                  ConfirmedRemaining=1700, PredictionSequence=0, GrantSequence=4, ObservedFrame=100)]
    clients = [dict(Protocol=56, PickupClaims=[dict(Stage='offered', Key=pickup, Spawn=spawn, Type='DoubleDamage')],
                    TimedGrantViews=views),
               dict(Protocol=56, TimedGrantViews=[dict(views[-1], Owner=False)])]
    rebudget(server)
    return server, clients


def rebudget(server):
    evidence = server['TimedCaseJoins']
    evidence['Budgets'] = {name: dict(Observed=len(rows), Count=len(rows), Dropped=0)
                           for name, rows in evidence.items() if isinstance(rows, list)}


class JoinTests(unittest.TestCase):
    def run_case(self, mutation=None):
        server, clients = fixture()
        if mutation:
            mutation(server, clients)
        rebudget(server)
        return audit(server, clients)

    def test_positive_constructed_full_join(self):
        result = self.run_case()
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['metrics']['fullyJoinedPaidCases'], 1)
        self.assertEqual(result['sourceVerifiedFraction'], 1)
        self.assertTrue(result['suggestedEnforceCoverageGate'].startswith('NOT_ADJUDICATED'))

    def test_forged_funding_sequence_is_failure(self):
        result = self.run_case(lambda s, _: s['TimedCaseJoins']['Certificates'][0]['Shot']['Funding'].update(Sequence=99))
        self.assertEqual(result['status'], 'FAIL')

    def test_same_position_spawn_incarnation_alias_is_failure(self):
        def mutate(s, _):
            # Distinct incarnation copied into immutable grant only, never approximated by position.
            s['TimedGrants'][0] = copy.deepcopy(s['TimedGrants'][0])
            s['TimedGrants'][0]['Spawn']['Generation'] = 2
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_duplicate_actual_payment_is_failure(self):
        def mutate(s, _):
            row = copy.deepcopy(s['TimedCaseJoins']['Payments'][0]); row['Ordinal'] = 6
            s['TimedCaseJoins']['Payments'].append(row)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_ordered_correction_is_not_a_second_payment(self):
        def mutate(s, _):
            row = copy.deepcopy(s['TimedCaseJoins']['Payments'][0]); row.update(Ordinal=6, Lane=2, Correction=True)
            row['Outcome']['DamageApplied'] = 20
            s['TimedCaseJoins']['Payments'].append(row)
        result = self.run_case(mutate)
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['metrics']['fundedPayments'], 1)
        self.assertEqual(result['metrics']['corrections'], 1)

    def test_zero_delta_is_not_a_payment_positive(self):
        self.assertEqual(self.run_case(lambda s, _: s['TimedCaseJoins']['Payments'][0]['Outcome'].update(DamageApplied=0))['status'], 'FAIL')

    def test_equal_damage_cannot_alias_unrelated_health_sequence_or_effects(self):
        for field, value in (('HealthAfter', 89), ('HalfturretHealthAfter', 12), ('DamageSequence', 4), ('Flags', 65)):
            with self.subTest(field=field):
                result = self.run_case(lambda s, _: s['TimedCaseJoins']['Terminals'][0]['Outcome'].update({field: value}))
                self.assertEqual(result['status'], 'FAIL')

    def test_same_terminal_result_cannot_change_immutable_outcome(self):
        def mutate(s, _):
            row = copy.deepcopy(s['TimedCaseJoins']['Terminals'][0]); row['Ordinal'] = 7
            row['Outcome']['HealthAfter'] = 89
            s['TimedCaseJoins']['Terminals'].append(row)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_explicit_correction_supplies_later_exact_terminal_outcome(self):
        def mutate(s, _):
            paid = copy.deepcopy(s['TimedCaseJoins']['Payments'][0]); paid.update(Ordinal=6, Lane=2, Correction=True)
            paid['Outcome'].update(DamageApplied=20, HealthAfter=80, DamageSequence=4, Flags=65)
            s['TimedCaseJoins']['Payments'].append(paid)
            terminal = copy.deepcopy(s['TimedCaseJoins']['Terminals'][0]); terminal.update(Ordinal=7)
            terminal['Outcome'].update(paid['Outcome']); terminal['Outcome']['ClaimId'] = 6
            s['TimedCaseJoins']['Terminals'].append(terminal)
        result = self.run_case(mutate)
        self.assertEqual(result['status'], 'PASS', result)
        self.assertEqual(result['metrics']['fundedPayments'], 1)

    def test_claim_payload_alias_is_failure(self):
        def mutate(s, _):
            row = copy.deepcopy(s['TimedCaseJoins']['Claims'][0]); row.update(Ordinal=6, Damage=11)
            s['TimedCaseJoins']['Claims'].append(row)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_component_identity_alias_is_failure(self):
        def mutate(s, _):
            row = copy.deepcopy(s['TimedCaseJoins']['Components'][0]); row['Ordinal'] = 6
            s['TimedCaseJoins']['Components'].append(row)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_expired_source_observation_is_failure(self):
        result = self.run_case(lambda s, _: s['TimedCaseJoins']['Certificates'][0]['Shot']['Funding'].update(ObservationAuthorityFrame=1900))
        self.assertEqual(result['status'], 'FAIL')

    def test_contact_same_source_frame_is_failure(self):
        result = self.run_case(lambda s, _: s['TimedCaseJoins']['Certificates'][0]['Shot']['Fire'].update(SourceFrame=80))
        self.assertEqual(result['status'], 'FAIL')

    def test_source_unavailable_is_incomplete_not_false_verified(self):
        result = self.run_case(lambda s, _: s['TimedCaseJoins']['Certificates'][0]['Shot']['Source'].update(Pose=7))
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['sourceVerifiedFraction'], 0)

    def test_missing_component_or_observer_is_incomplete(self):
        self.assertEqual(self.run_case(lambda s, _: s['TimedCaseJoins'].update(Components=[]))['status'], 'INCOMPLETE')
        self.assertEqual(self.run_case(lambda _, c: c[1].update(TimedGrantViews=[]))['status'], 'INCOMPLETE')

    def test_prediction_sequence_alias_is_incomplete(self):
        self.assertEqual(self.run_case(lambda _, c: c[0]['TimedGrantViews'][0].update(PredictionSequence=55))['status'], 'INCOMPLETE')

    def test_capacity_retention_is_incomplete(self):
        self.assertEqual(self.run_case(lambda s, _: s['TimedCaseJoins']['Certificates'][0].update(Retention=3))['status'], 'INCOMPLETE')

    def test_no_positive_denominator_is_incomplete(self):
        self.assertEqual(self.run_case(lambda s, _: s['TimedCaseJoins'].update(Certificates=[]))['status'], 'INCOMPLETE')

    def test_overflow_is_incomplete_even_with_positive_rows(self):
        server, clients = fixture(); server['TimedCaseJoins']['Budgets']['Payments']['Dropped'] = 1
        self.assertEqual(audit(server, clients)['status'], 'INCOMPLETE')

    def test_native_missing_claim_is_not_invented_component_claim_proof(self):
        def mutate(s, _):
            s['TimedCaseJoins']['Payments'][0].update(Lane=0, ClaimId=0)
            s['TimedCaseJoins']['Claims'] = []; s['TimedCaseJoins']['Terminals'] = []
        result = self.run_case(mutate)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['nativePaidWithoutOwnerClaim'], 1)

    def test_native_reservation_cannot_borrow_other_life_contact(self):
        def mutate(s, _):
            s['TimedCaseJoins']['Certificates'][0]['Shot']['Lane'] = 0
            s['TimedCaseJoins']['Components'][0]['AcceptedPlayback'] = False
            s['PickupProofs'][0] = copy.deepcopy(s['PickupProofs'][0])
            s['PickupProofs'][0]['NativeReservation'] = True
            s['PickupProofs'][0]['Life']['LifeId'] = 99
        result = self.run_case(mutate)
        self.assertEqual(result['status'], 'INCOMPLETE')
        self.assertEqual(result['metrics']['fullyJoinedPaidCases'], 0)

    def test_nonplayer_component_does_not_prove_native_player_emission(self):
        def mutate(s, _):
            s['TimedCaseJoins']['Components'][0].update(PlayerSource=False, AcceptedPlayback=False)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')

    def test_terminal_actor_cannot_borrow_source_key_slot(self):
        self.assertEqual(self.run_case(lambda s, _: s['TimedCaseJoins']['Terminals'][0].update(ShooterSlot=7))['status'], 'FAIL')

    def test_zero_claim_and_native_lane_cannot_fake_full_join(self):
        def mutate(s, _):
            s['TimedCaseJoins']['Claims'][0]['ClaimId'] = 0
            s['TimedCaseJoins']['Terminals'][0]['Outcome']['ClaimId'] = 0
            s['TimedCaseJoins']['Payments'][0].update(Lane=0, ClaimId=0)
        self.assertEqual(self.run_case(mutate)['status'], 'FAIL')


if __name__ == '__main__':
    unittest.main()
