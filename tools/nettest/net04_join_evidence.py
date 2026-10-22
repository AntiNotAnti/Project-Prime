#!/usr/bin/env python3
"""Strict authority/owner evidence joins. Never reconstructs an impact from claims."""
import argparse
import json
from pathlib import Path

KEY = ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')
SPAWN = ('Epoch', 'Match', 'Room', 'SpawnId', 'Generation')
ORDINARY = {0, 1, 2, 3, 4, 6}


def key(value):
    return tuple(value[name] for name in KEY)


def spawn(value):
    return tuple(value[name] for name in SPAWN)


def life(value):
    return key(value)[:-1] + (0,)


def enum(value, names):
    return names.index(value) if isinstance(value, str) else value


def payment_key(value):
    return key(value['Shot']) + (value['Victim'], value['Generation'], value['Life'],
                                 value['Component'], value['Kind'])


def claim_payment(value):
    kind = (1 if value['Flags'] & 128 else 2) | (4 if value['Flags'] & 32 else 0)
    return key(value['Key']) + (value['VictimSlot'], value['VictimGeneration'],
                              value['VictimLifeId'], value['ComponentId'], kind)


def signed_delta(a, b):
    delta = (a - b) & 0xffffffff
    return delta - 0x100000000 if delta >= 0x80000000 else delta


def outcome_signature(value):
    # ClaimId/Result correlate the answer; the remaining fields identify the
    # actual historical body/turret outcome, including effect authorization.
    return tuple(value[name] for name in ('ShotId', 'VictimSlot', 'VictimGeneration', 'VictimLife',
        'DamageApplied', 'HealthAfter', 'HalfturretHealthAfter', 'DamageSequence', 'Flags'))


def originating_native_self_death(record, grant, components, payments):
    """Prove a synchronous originating component killed its source before completion.

    Funding is captured before spawning; the completion certificate is exported
    after native collisions. Final Death rows can therefore close that very shot's
    grant within the same frame. No claim/correction or unrelated death proves it.
    """
    shot = record['Shot']; identity = key(shot['Key']); funding = shot['Funding']
    frame = funding['ObservationAuthorityFrame']
    if (grant['EndReason'] != 'Death' or frame != grant['EndFrame']
            or signed_delta(frame, funding['ExpiresAtFrame']) >= 0
            or shot['AuthorityFrame'] != frame or record['ObservedFrame'] != frame
            or shot['Source']['Source'] not in (0, 'Verified')
            or shot['Source']['Pose'] not in (0, 'Verified')
            or shot['Unavailable'] not in (0, 'None')):
        return False
    for paid in payments.values():
        pk = payment_key(paid['Key']); outcome = paid['Outcome']; witness = paid.get('Impact')
        component = components.get((identity, shot['Room'], paid['Key']['Component']))
        if (pk[:6] != identity or pk[6:9] != identity[2:5] or pk[10] not in (1, 2)
                or not 1 <= pk[9] <= shot['Components'] or component is None
                or paid['Lane'] not in (0, 'Native') or paid['Correction'] or paid['ClaimId']
                or paid['Weapon'] != shot['Fire']['Weapon'] or paid['ActualTurretDamage'] != 0
                or paid['ReservationFrame'] != frame or paid['PaymentFrame'] != frame
                or outcome['ShotId'] != identity[5] or outcome['ClaimId']
                or tuple(outcome[n] for n in ('VictimSlot', 'VictimGeneration', 'VictimLife')) != identity[2:5]
                or outcome['DamageApplied'] <= 0 or outcome['HealthAfter'] != 0
                or outcome['Result'] not in (0, 1) or outcome['Flags'] & 66 != 66
                or witness is None):
            continue
        if (not component['PlayerSource']
                or component['AcceptedPlayback'] != (shot['Lane'] in (1, 'AcceptedFire'))
                or component['Value'].get('AuthorityFrame') != frame
                or not component['Ordinal'] < paid['Ordinal'] < record['Ordinal']
                or payment_key(witness['Payment']) != pk or witness['Weapon'] != paid['Weapon']
                or witness['Frame'] != frame or not witness['Completed']
                or witness['NativeChannel'] <= 0 or witness['Outcome'] != outcome
                or witness['Direct'] != (pk[10] == 1)):
            continue
        return True
    return False


def audit(server, clients):
    failures, incomplete = [], []
    metrics = dict(fundedOrdinaryCertificates=0, verifiedSourceCertificates=0,
                   nativeComponents=0, fundedPayments=0, fullyJoinedPaidCases=0,
                   nativePaidWithoutOwnerClaim=0, nativeImpactObservations=0,
                   authorityNativePaidJoins=0, corrections=0, continuousPayments=0)
    cases = []
    fail = failures.append
    missing = incomplete.append
    try:
        if server.get('Role') != 'server' or server.get('Protocol') != 56:
            missing('declared protocol56 authority report missing')
        if not clients or any(report.get('Protocol') != 56 for report in clients):
            missing('protocol56 owner/observer reports missing')
        evidence = server.get('TimedCaseJoins')
        if not isinstance(evidence, dict) or evidence.get('Schema') != 1:
            missing('TimedCaseJoins schema1 authority producer missing')
            evidence = {}
        groups = ('Certificates', 'Components', 'Claims', 'Terminals', 'Payments', 'Continuous')
        rows = {name: evidence.get(name, []) for name in groups}
        budgets = evidence.get('Budgets', {})
        ordinals = set()
        for name in groups:
            budget = budgets.get(name)
            if not isinstance(budget, dict):
                missing(f'{name}: budget/denominator missing')
            elif budget.get('Dropped', 0) or budget.get('Observed') != budget.get('Count'):
                missing(f'{name}: evidence dropped or observed/count mismatch')
            elif budget.get('Count') != len(rows[name]):
                missing(f'{name}: retained row count differs from reported budget')
            for row in rows[name]:
                ordinal = row['Ordinal']
                if not isinstance(ordinal, int) or ordinal <= 0 or ordinal in ordinals:
                    fail(f'{name}: invalid/aliased event ordinal')
                ordinals.add(ordinal)
        if server.get('Overflow', 0):
            missing('authority legacy evidence overflow')
        for report in clients:
            if report.get('Overflow', 0):
                missing('owner/observer evidence overflow')
        certificates = {}
        for row in rows['Certificates']:
            shot = row['Shot']; identity = key(shot['Key'])
            retention = enum(row['Retention'], ('Stored', 'Duplicate', 'Alias', 'Capacity', 'Invalid'))
            if retention in (3, 4):
                missing(f'{identity}: certificate retention {retention}')
            if retention == 2:
                fail(f'{identity}: certificate source-key alias refused by producer')
            if identity in certificates:
                if certificates[identity]['Shot'] != shot:
                    fail(f'{identity}: immutable certificate changed')
            else:
                certificates[identity] = row
            if key(shot['Key'])[-1] != shot['Fire']['ShotId'] or shot['Fire']['SourceLife'] != identity[4]:
                fail(f'{identity}: certificate fire/source-life alias')
        components = {}
        for row in rows['Components']:
            value = row['Value']; identity = key(value['Key'])
            component = value['Component']
            if not component or not identity[-1]:
                continue  # Explicitly unkeyed/unsupported native terminal, not a certificate.
            compound = (identity, value['Room'], component)
            if compound in components:
                fail(f'{compound}: duplicate actual component identity')
            components[compound] = row
        claims = {}
        for row in rows['Claims']:
            if row['ClaimId'] == 0:
                fail('received claim has reserved zero ClaimId; cannot be a positive join')
            compound = (key(row['Key']), row['ClaimId'])
            signature = {k: v for k, v in row.items() if k not in ('Ordinal', 'ObservedFrame')}
            if compound in claims and claims[compound][1] != signature:
                fail(f'{compound}: claim identity aliases another immutable payload')
            claims.setdefault(compound, (row, signature))
        terminals = {}; terminal_history = {}
        for row in rows['Terminals']:
            if row['Outcome']['ClaimId'] == 0:
                fail('terminal has reserved zero ClaimId; cannot be a positive join')
            if row.get('Key') is None:
                continue  # Refusal without retained source identity is never a joined outcome.
            compound = (key(row['Key']), row['Outcome']['ClaimId'])
            if row['ShooterSlot'] != compound[0][2]:
                fail(f'{compound}: terminal authority actor/source slot mismatch')
            previous = terminals.get(compound)
            if previous and previous['Outcome']['Result'] != row['Outcome']['Result']:
                fail(f'{compound}: terminal claim result changed')
            terminals[compound] = row
            terminal_history.setdefault(compound, []).append(row)
        payments = {}; corrections = {}
        for row in rows['Payments']:
            compound = payment_key(row['Key']); outcome = row['Outcome']
            lane = enum(row['Lane'], ('Native', 'Admitted', 'Correction'))
            if row['Correction'] != (lane == 2) or (not row['Correction'] and (lane == 1) != bool(row['ClaimId'])):
                fail(f'{compound}: explicit payment lane/claim correlation is inconsistent')
            if tuple(outcome[n] for n in ('VictimSlot', 'VictimGeneration', 'VictimLife')) != compound[6:9]:
                fail(f'{compound}: payment victim-life alias')
            if outcome['ShotId'] != compound[5] or row['ActualTurretDamage'] < 0:
                fail(f'{compound}: payment outcome source alias/negative turret delta')
            if signed_delta(row['PaymentFrame'], row['ReservationFrame']) < 0:
                fail(f'{compound}: payment precedes reservation')
            if row['Correction']:
                metrics['corrections'] += 1
                if compound not in payments or row['Ordinal'] <= payments[compound]['Ordinal']:
                    missing(f'{compound}: correction lacks earlier observed payment')
                corrections.setdefault(compound, []).append(row)
                continue
            if compound in payments:
                fail(f'{compound}: duplicate real component/victim/kind payment')
            payments[compound] = row
            if not outcome['DamageApplied'] and not row['ActualTurretDamage']:
                fail(f'{compound}: committed payment lacks real body/turret reduction')
            witness = row.get('Impact')
            if witness is not None:
                if (payment_key(witness['Payment']) != compound or witness['Weapon'] != row['Weapon']
                        or not witness['Completed'] or witness['Outcome'] != outcome):
                    fail(f'{compound}: native impact witness does not identify its actual payment')
                else:
                    metrics['nativeImpactObservations'] += 1
        continuous_keys = set()
        for row in rows['Continuous']:
            phase = row['Key']; owner_tick = key(phase['OwnerTick'])
            if owner_tick in continuous_keys:
                fail(f'{owner_tick}: continuous phase paid twice, including target redirection')
            continuous_keys.add(owner_tick)
            metrics['continuousPayments'] += 1
        for compound, history in terminal_history.items():
            for before, after in zip(history, history[1:]):
                if outcome_signature(before['Outcome']) == outcome_signature(after['Outcome']):
                    continue
                received = claims.get(compound)
                exact_payment = claim_payment(received[0]) if received else None
                explicit = [row for row in corrections.get(exact_payment, [])
                            if before['Ordinal'] < row['Ordinal'] < after['Ordinal']
                            and outcome_signature(row['Outcome']) == outcome_signature(after['Outcome'])]
                if not explicit:
                    fail(f'{compound}: terminal outcome changed without its actual ordered correction')
        grants = {}
        for grant in server.get('TimedGrants', []):
            compound = (life(grant['Life']), grant['Sequence'])
            old = grants.get(compound)
            immutable = {k: v for k, v in grant.items() if k not in ('EndFrame', 'EndReason')}
            if old and {k: v for k, v in old.items() if k not in ('EndFrame', 'EndReason')} != immutable:
                fail(f'{compound}: A1 immutable grant funding alias')
            # Closing transitions narrow chronology rather than refreshing funding.
            grants[compound] = grant
        owner_claims = [row for report in clients for row in report.get('PickupClaims', []) if row['Stage'] == 'offered']
        views = [row for report in clients for row in report.get('TimedGrantViews', [])]
        verdicts = server.get('PickupVerdicts', [])
        for identity, record in certificates.items():
            shot = record['Shot']; fire = shot['Fire']
            knowledge = enum(shot['Knowledge'], ('Unavailable', 'NotGranted', 'Granted'))
            if knowledge != 2 or fire['Weapon'] not in ORDINARY or fire['Kind'] in (3, 4):
                continue
            metrics['fundedOrdinaryCertificates'] += 1
            funding = shot.get('Funding')
            local_missing = []
            if funding is None:
                fail(f'{identity}: Granted certificate has no immutable funding')
                continue
            compound = (life(shot['Key']), funding['Sequence'])
            answer = shot['LedgerAnswer']
            if (enum(answer['Knowledge'], ('Unavailable', 'NotGranted', 'Granted')) != 2
                    or not funding['Sequence'] or answer['Sequence'] != funding['Sequence']
                    or life(funding['Life']) != life(shot['Key']) or key(funding['Life'])[-1] != 0
                    or spawn(funding['Spawn'])[:3] != (identity[0], identity[1], shot['Room'])
                    or enum(funding['Type'], ('DoubleDamage', 'Cloak', 'DeathAlt')) != 0):
                fail(f'{identity}: certificate raw answer/funding identity mismatch')
            grant = grants.get(compound)
            if grant is None:
                local_missing.append('A1 funding grant')
            else:
                if (grant['Type'] != 'DoubleDamage' or spawn(grant['Spawn']) != spawn(funding['Spawn'])
                        or grant['ContactFrame'] != funding['ContactFrame']
                        or grant.get('SourceContactFrame') != funding['SourceContactFrame']
                        or grant['ExpiresAtFrame'] != funding['ExpiresAtFrame']):
                    fail(f'{identity}: frozen funding differs from actual A1 grant')
                if (signed_delta(fire['SourceFrame'], funding['SourceContactFrame']) <= 0
                        or signed_delta(funding['ObservationAuthorityFrame'], funding['ContactFrame']) <= 0
                        or signed_delta(funding['ObservationAuthorityFrame'], funding['ExpiresAtFrame']) >= 0):
                    fail(f'{identity}: source observation is outside actual contact+1/end interval')
                elif signed_delta(funding['ObservationAuthorityFrame'], grant['EndFrame']) >= 0:
                    if (funding['ObservationAuthorityFrame'] == grant['EndFrame']
                            and grant['EndReason'] == 'Death'):
                        if not originating_native_self_death(record, grant, components, payments):
                            local_missing.append('equal-frame Death requires exact originating native self-death proof')
                    else:
                        fail(f'{identity}: source observation is outside actual contact+1/end interval')
            source = shot['Source']
            verified = source['Source'] in (0, 'Verified') and source['Pose'] in (0, 'Verified') and shot['Unavailable'] in (0, 'None')
            if verified:
                metrics['verifiedSourceCertificates'] += 1
            else:
                local_missing.append('verified source/pose declaration')
            count = shot['Components']
            for component in range(1, count + 1):
                terminal = components.get((identity, shot['Room'], component))
                if terminal is None:
                    local_missing.append(f'actual originating component{component}')
                else:
                    metrics['nativeComponents'] += 1
                    if not terminal.get('PlayerSource') or terminal.get('AcceptedPlayback') != (shot['Lane'] in (1, 'AcceptedFire')):
                        fail(f'{identity}: component player/source playback role does not match the actual certificate lane')
                    if terminal['Ordinal'] >= record['Ordinal']:
                        fail(f'{identity}: completion certificate precedes actual component')
            pickup = [v for v in verdicts if v.get('Result') == 'Grant' and v.get('GrantSequence') == funding['Sequence']
                      and spawn(v['Spawn']) == spawn(funding['Spawn'])]
            if shot['Lane'] in (1, 'AcceptedFire'):
                offered = [c for c in owner_claims if spawn(c['Spawn']) == spawn(funding['Spawn']) and
                    (c['Key']['Epoch'], c['Key']['Match'], c['Key']['Slot'], c['Key']['Generation'], c['Key']['Life']) == identity[:5]
                    and c['Type'] == 'DoubleDamage']
                if any((v['Key']['Epoch'], v['Key']['Match'], v['Key']['Slot'], v['Key']['Generation'], v['Key']['Life']) != identity[:5] for v in pickup):
                    fail(f'{identity}: A2 Grant verdict source-life alias')
                if not pickup or not offered or not any(c['Key'] == v['Key'] for c in offered for v in pickup):
                    local_missing.append('A2 exact owner claim/authority Grant verdict')
                if not any(p.get('GrantSequence') == funding['Sequence'] and spawn(p['Spawn']) == spawn(funding['Spawn'])
                    and life(p['Life']) == life(shot['Key']) and p.get('Alive') and p.get('CanPickup') and p.get('Available')
                    for p in server.get('PickupProofs', [])):
                    local_missing.append('A2 captured authority contact proof')
                confirmed = [v for v in views if life(v['Life']) == life(shot['Key'])
                             and v.get('GrantSequence') == funding['Sequence'] and v.get('Covered')
                             and v.get('ConfirmedRemaining', 0) > 0 and v.get('Alive') and v.get('Type') == 'DoubleDamage']
                if not any(v.get('Owner') for v in confirmed) or not any(not v.get('Owner') for v in confirmed):
                    local_missing.append('A3 owner and observer confirmed grant handover')
                if not any(v.get('Owner') and v.get('Covered') and v.get('ConfirmedRemaining') == 0
                    and v.get('Type') == 'DoubleDamage' and life(v['Life']) == life(shot['Key'])
                    and any(c['Key']['Sequence'] == v.get('PredictionSequence') for c in offered)
                    and any(c.get('Owner') and c['ObservedFrame'] >= v['ObservedFrame'] for c in confirmed)
                    for v in views):
                    local_missing.append('A3 exact owner prediction-to-confirmation chronology')
            else:
                if not any(p.get('NativeReservation') and p.get('GrantSequence') == funding['Sequence']
                           and spawn(p['Spawn']) == spawn(funding['Spawn'])
                           and life(p['Life']) == life(shot['Key']) and p.get('Alive')
                           and p.get('CanPickup') and p.get('Available') for p in server.get('PickupProofs', [])):
                    local_missing.append('actual authority native reservation proof')
            funded_payments = [p for pk, p in payments.items() if pk[:6] == identity]
            for paid in funded_payments:
                metrics['fundedPayments'] += 1
                pk = payment_key(paid['Key'])
                if paid['Weapon'] != fire['Weapon'] or not 1 <= paid['Key']['Component'] <= count:
                    fail(f'{identity}: funded payment weapon/component alias')
                matching = [value[0] for ck, value in claims.items() if ck[0] == identity and claim_payment(value[0]) == pk]
                if paid['ClaimId']:
                    matching = [c for c in matching if c['ClaimId'] == paid['ClaimId']]
                if not matching:
                    if paid['Lane'] in (1, 'Admitted'):
                        local_missing.append('admitted payment exact claim receipt')
                    else:
                        metrics['nativePaidWithoutOwnerClaim'] += 1
                    continue
                matched = False
                for claim in matching:
                    if claim['Beam'] != fire['Weapon'] or claim['LaunchFrame'] != fire['AckFrame']:
                        fail(f'{identity}: claimed launch/weapon differs from captured native emission')
                        continue
                    terminal = terminals.get((identity, claim['ClaimId']))
                    if terminal is None:
                        continue
                    outcome = terminal['Outcome']
                    if outcome['Result'] not in (0, 1):
                        continue
                    if (outcome['VictimSlot'], outcome['VictimGeneration'], outcome['VictimLife']) != pk[6:9]:
                        fail(f'{identity}: terminal victim does not match exact component payment')
                        continue
                    history = [paid, *corrections.get(pk, [])]
                    known = [row for row in history if row['Ordinal'] < terminal['Ordinal']]
                    expected = max(known, key=lambda row: row['Ordinal']) if known else paid
                    if outcome_signature(outcome) != outcome_signature(expected['Outcome']):
                        fail(f'{identity}: terminal health/sequence/effects differ from exact paid outcome')
                        continue
                    if terminal['Ordinal'] <= paid['Ordinal'] or claim['Ordinal'] >= terminal['Ordinal']:
                        fail(f'{identity}: terminal claim/payment chronology is reversed')
                    matched = True
                if matched and verified and not local_missing:
                    if shot['Lane'] in (1, 'AcceptedFire'):
                        metrics['fullyJoinedPaidCases'] += 1
                    else:
                        metrics['authorityNativePaidJoins'] += 1
                elif not matched:
                    local_missing.append('exact terminal outcome for funded component payment')
            if local_missing:
                missing(f'{identity}: ' + ', '.join(sorted(set(local_missing))))
            cases.append({'key': identity, 'fundingSequence': funding['Sequence'], 'sourceVerified': verified,
                          'components': count, 'payments': len(funded_payments), 'missing': sorted(set(local_missing))})
        if not metrics['fundedOrdinaryCertificates']:
            missing('positive funded ordinary source denominator absent')
        if not metrics['fullyJoinedPaidCases']:
            missing('positive full A1/A2/A3/certificate/component/claim/payment join absent')
    except (KeyError, TypeError, ValueError, IndexError) as error:
        missing('required evidence shape/value unavailable: ' + str(error))
    denominator = metrics['fundedOrdinaryCertificates']
    return dict(schema=1, status='FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
                scope='bounded observed source/payment joins; no claimed-impact reconstruction or Enforce authorization',
                sourceVerifiedFraction=metrics['verifiedSourceCertificates'] / denominator if denominator else None,
                suggestedEnforceCoverageGate='NOT_ADJUDICATED: requires meaningful declared supported samples and independent impact/policy gates',
                metrics=metrics, cases=cases, failures=sorted(set(failures)), incomplete=sorted(set(incomplete)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--campaign', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    server = json.loads((args.campaign / 'server.json').read_text())
    clients = [json.loads(path.read_text()) for path in sorted(args.campaign.glob('peer-*.json'))
               if not path.name.endswith(('.drain.json', '.bombs.json', '.visibility.json', '.lobby.json'))]
    result = audit(server, clients)
    with args.out.open('x') as stream:
        json.dump(result, stream, indent=2); stream.write('\n')
    print(json.dumps({'status': result['status'], 'metrics': result['metrics']}))
    return 0 if result['status'] == 'PASS' else 1 if result['status'] == 'FAIL' else 2


if __name__ == '__main__':
    raise SystemExit(main())
