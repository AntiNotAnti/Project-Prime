"""Protocol 52 turret terminal conservation; raw payloads, never reason-only exemptions."""
import math
import re
import struct

FIELDS = ("AuthorityEpoch", "MatchId", "ShooterSlot", "Generation", "LifeId", "ShotId")
SIGNATURE = ("SourceFrame", "ContinuousPhase", "Weapon", "Charge", "Kind")
REASONS = {0: "None", 1: "RecoveryExpired", 2: "OwnerRetired", 3: "TurretDestroyed", 4: "Capacity"}


def key_of(row):
    key = tuple(row['Key'][f] for f in FIELDS)
    if any(type(v) is not int for v in key) or not (0 < key[0] < 2**64 and 0 < key[1] < 2**16
            and 0 <= key[2] < 8 and all(0 < key[i] < 2**16 for i in (3, 4)) and 0 < key[5] < 2**32):
        raise ValueError('invalid full shot identity')
    return key


def decode(row):
    key = key_of(row)
    raw = row['Payload']
    if not isinstance(raw, str) or not re.fullmatch(r'[0-9a-fA-F]{246}', raw):
        raise ValueError('missing exact 123-byte terminal payload')
    raw = bytes.fromhex(raw)
    if struct.unpack_from('<QHBHHI', raw) != key:
        raise ValueError('payload identity differs from evidence')
    frame, status, reason = struct.unpack_from('<IBB', raw, 19)
    if type(row['AuthorityFrame']) is not int or row['AuthorityFrame'] != frame:
        raise ValueError('authority clock differs from payload')
    if status not in (1, 2) or reason not in REASONS or row['Status'] != {1:'Emitted',2:'Refused'}[status] or row['Reason'] != REASONS[reason]:
        raise ValueError('invalid terminal disposition')
    fire = raw[25:99]
    shot, source, ack, sub, kind, weapon, charge, phase, pose = struct.unpack_from('<IIIBBBBIB', fire)
    if shot != key[5] or struct.unpack_from('<H', fire, 67)[0] != key[4] or (kind, weapon, charge) != (4, 3, 0) or pose not in (1, 3):
        raise ValueError('invalid exact source fire contract')
    vectors = struct.unpack_from('<9f', fire, 21)
    if any(not math.isfinite(v) for v in vectors) or any(abs(v) >= 100000 for v in vectors[:3]) or any(sum(v*v for v in vectors[i:i+3]) < 1e-6 for i in (3,6)):
        raise ValueError('invalid source geometry')
    if sum(v*v for v in struct.unpack_from('<3h', fire, 57)) == 0:
        raise ValueError('invalid source view')
    target, generation, life = struct.unpack_from('<BHH', fire, 69)
    if not ((target, generation, life) in ((0, 0, 0), (128, 0, 0)) or 129 <= target <= 136 and generation and life):
        raise ValueError('invalid source target')
    emission = struct.unpack_from('<6f', raw, 99)
    if status == 1:
        if reason != 0 or any(not math.isfinite(v) for v in emission) or any(abs(v) >= 100000 for v in emission[:3]) or abs(sum(v*v for v in emission[3:])-1) >= .05:
            raise ValueError('invalid native emission geometry')
    elif reason == 0 or any(v != 0 for v in emission):
        raise ValueError('refusal includes emission geometry')
    observed, cancelled = row['ObservedFrame'], row['CancelledFlights']
    if type(observed) is not int or not 0 <= observed < 2**32 or type(cancelled) is not int or cancelled < 0:
        raise ValueError('invalid receipt clock/cancellation count')
    return key, raw, (source, phase, weapon, charge, kind)


def terminal_rows(report):
    stages, errors = {}, []
    rows = report.get('TurretOutcomes')
    if not isinstance(rows, list): return {}, ['missing turret outcome evidence']
    for row in rows:
        try:
            key, payload, signature = decode(row)
            stage = row['Stage']
            if stage not in ('authority', 'received', 'refusal-applied') or (stage == 'authority') != (report.get('Role') == 'server'):
                raise ValueError('invalid authority/receipt direction')
            if stage == 'refusal-applied' and row['Status'] != 'Refused':
                raise ValueError('emission marked cancelled')
            identity = stage, key
            if identity in stages: raise ValueError('duplicate terminal observation')
            stages[identity] = (row, payload, signature)
        except (KeyError, TypeError, ValueError, struct.error) as error:
            errors.append('invalid turret outcome: ' + str(error))
    return stages, errors


def authority_outcomes(report, accepted, committed):
    stages, errors = terminal_rows(report)
    terminal, refused = {}, {}
    try:
        ledger = report['CombatPayments']
        if type(ledger['EvidenceOverflow']) is not int or ledger['EvidenceOverflow'] != 0:
            raise ValueError('payment evidence overflow')
        paid = [key_of({'Key': key}) for key in ledger['PaidShotKeys']]
        if len(set(paid)) != len(paid): raise ValueError('duplicate payment evidence identity')
    except (KeyError, TypeError, ValueError) as error:
        errors.append(str(error)); paid = []
    for (_, key), value in stages.items():
        row, payload, signature = value
        source = accepted.get(key) or committed.get(key)
        if source is None or tuple(source.get(f) for f in SIGNATURE) != signature:
            errors.append('terminal lacks exact admitted/native fire ' + str(key)); continue
        if row['ObservedFrame'] != row['AuthorityFrame']:
            errors.append('terminal decision clock differs from observation ' + str(key)); continue
        if row['Status'] == 'Emitted':
            if key not in committed or tuple(committed[key].get(f) for f in SIGNATURE) != signature:
                errors.append('emitted terminal lacks exact native commit ' + str(key)); continue
        else:
            if key not in accepted or key in committed or key in paid:
                errors.append('refused terminal is unadmitted, emitted or paid ' + str(key)); continue
            # A wire refusal cannot relabel an ordinary expiry as a successful
            # emission. Preserve the original authority terminal observation.
            expected_stage = 'expired' if row['Reason'] == 'RecoveryExpired' else 'rejected'
            if not any(key_of(s) == key and s['Stage'] == expected_stage
                       and s['ObservedFrame'] == row['AuthorityFrame']
                       and tuple(s.get(f) for f in SIGNATURE) == signature for s in report['Shots']):
                errors.append('refusal lacks native terminal observation ' + str(key)); continue
            refused[key] = source
        terminal[key] = value
    expected = {key for key, row in {**accepted, **committed}.items() if row['Kind'] == 4}
    for key in expected - terminal.keys(): errors.append('admitted turret lacks authority outcome ' + str(key))
    return terminal, refused, errors


def peer_outcomes(report, terminal, required):
    stages, errors = terminal_rows(report)
    received, refused = set(), set()
    for (stage, key), (row, payload, signature) in stages.items():
        if key not in terminal or payload != terminal[key][1]:
            errors.append('peer terminal differs from authority ' + str(key)); continue
        if stage == 'received': received.add(key)
        elif stage == 'refusal-applied':
            if ('received', key) not in stages: errors.append('refusal applied without receipt ' + str(key))
            else: refused.add(key)
    for key in required - received: errors.append('missing authority turret receipt ' + str(key))
    for key in received:
        if terminal[key][0]['Status'] == 'Refused' and key not in refused:
            errors.append('refusal was not applied ' + str(key))
    return received, refused, errors
