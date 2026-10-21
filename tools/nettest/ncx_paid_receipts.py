"""Authority-only paid-receipt decision latency; never client delivery/one-way time.

Joins a unique exact Ready payment/request to local receipt and terminal events.
Ready is sufficient because the immutable payment already exists. Native fresh
contacts are excluded; this does not certify early damage or a causal frontier.
"""
import argparse
from collections import defaultdict
import json
import math
from pathlib import Path
from ncx_fastpath import audit as audit_parity, row_value, token
from ncx_timeline import STAGES, integer, timeline_schema, row_value as timeline_row

U32 = (1 << 32) - 1


def correlation(key, claim):
    return (*key[:9], claim)


def audit(report, expected_mode=None):
    failures, incomplete, samples = [], [], []
    parity = audit_parity({'server': report})
    if parity['status'] == 'FAIL': failures.append('material parity audit failed')
    elif parity['status'] != 'PASS': incomplete.append('material parity audit incomplete')
    try:
        if str(report['Role']).lower() not in ('server', 'host'):
            raise ValueError('not authority')
        capture = report['CombatFastPath']
        mode = capture['Mode']
        mode = {0: 'Off', 1: 'Shadow', 2: 'Paid'}.get(mode, mode) if type(mode) is int else mode
        if mode not in ('Shadow', 'Paid'): raise ValueError('observation disabled')
        if expected_mode and mode.lower() != expected_mode: failures.append('unexpected policy mode')
        timeline = report['ShotTimeline']
        timeline_schema(timeline['Schema'])
        if timeline['Clock']['Domain'] != 'ProcessStopwatch': raise ValueError('unknown clock')
        integer(timeline['Clock']['Frequency'], 1, (1 << 63) - 1)
        if timeline.get('Enabled') is not True or timeline.get('Complete') is not True:
            incomplete.append('authority timeline disabled or incomplete')
        for field in ('Overflow', 'UnkeyedRows'):
            if integer(timeline[field], 0, (1 << 63) - 1): incomplete.append('authority timeline ' + field)
        ready = defaultdict(list)
        for raw in capture['Rows']:
            row = row_value(raw)
            if row['stage'] == 'ClaimReady' and row['state'] == 'VerifiedPaid':
                ready[correlation(row['key'], row['shape'][0])].append((raw, row))
        received, terminals = defaultdict(list), defaultdict(list)
        previous_timestamp = -1
        for event in timeline['Rows']:
            shot = timeline_row(event)
            if event['Timestamp'] < previous_timestamp:
                failures.append('authority timeline timestamp order regressed')
            previous_timestamp = event['Timestamp']
            if event['Stage'] not in STAGES:
                incomplete.append('unknown authority timeline stage')
            if event['Stage'] not in ('claim-received', 'terminal-decided'): continue
            if event['LocalFrame'] != event['AuthorityFrame']:
                failures.append('authority frame domain mismatch')
            corr = (*shot, event['Victim'], event['VictimGeneration'], event['VictimLife'], event['ClaimId'])
            (received if event['Stage'] == 'claim-received' else terminals)[corr].append(event)
        for corr, entries in ready.items():
            if len(entries) != 1:
                incomplete.append('ambiguous full payment/request terminal correlation'); continue
            raw, row = entries[0]
            if row['corrected']:
                incomplete.append('corrected receipt excluded'); continue
            if not row['compared'] or not row['agrees'] or row['terminal_disagreements']:
                incomplete.append('receipt lacks immutable terminal parity'); continue
            bypass = raw.get('PaidBypass', False)
            if type(bypass) is not bool: raise ValueError('invalid bypass')
            if (mode == 'Paid') != bypass:
                failures.append('eligible receipt policy bypass mismatch'); continue
            paid = row['paid']; key = row['key']
            starts = [e for e in received[corr] if e['LocalFrame'] == row['legal']
                      and (e['Component'], e['Kind'], e['Phase']) == (key[9], key[10], key[11])
                      and e['Weapon'] == row['weapon'] and e['AckFrame'] == row['shape'][5]
                      and e['Value'] == row['shape'][3]]
            ends = [e for e in terminals[corr] if e['DamageEventId'] == paid['DamageSequence']
                    and e['Value'] == paid['DamageApplied'] and e['Result'] in (0, 1, 13)]
            if not starts or not ends:
                incomplete.append('missing exact authority received/terminal join'); continue
            start = min(starts, key=lambda e:e['Timestamp'])
            if any(e['Timestamp'] < start['Timestamp'] for e in ends):
                incomplete.append('matching terminal predates unique ready request')
            later = [e for e in ends if e['Timestamp'] >= start['Timestamp']]
            if not later:
                failures.append('terminal precedes authority request'); continue
            end = min(later, key=lambda e:e['Timestamp'])
            ticks = (end['LocalFrame'] - row['legal']) & U32
            if end['LocalFrame'] != end['AuthorityFrame'] or start['LocalFrame'] != start['AuthorityFrame']:
                failures.append('authority frame domain mismatch'); continue
            if ticks > 1024:
                failures.append('terminal frame outside bounded authority window'); continue
            samples.append(dict(token=token(row), sufficientToDecisionTicks=ticks,
                                receivedToDecisionTicks=(end['LocalFrame']-start['LocalFrame']) & U32,
                                paidBypass=bypass))
        if not ready or not samples: incomplete.append('no positive exact paid receipt latency cohort')
        values = sorted(x['sufficientToDecisionTicks'] for x in samples)
        p95 = values[math.ceil(.95*len(values))-1] if values else None
        if p95 is not None and p95 > 2: failures.append('paid receipt authority decision p95 exceeds two ticks')
    except (KeyError, TypeError, ValueError, OverflowError):
        incomplete.append('malformed or unavailable authority receipt/timeline evidence')
        mode, p95 = None, None
    return dict(schema=1, status='FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
                mode=mode, eligibleSamples=len(samples), p95AuthorityTicks=p95,
                samples=samples, failures=sorted(set(failures)), incomplete=sorted(set(incomplete)),
                scope='Authority-local sufficient Ready/received to terminal-decided ticks only; exact retained payment/request joins. Timeline schemas 1 and 2 share these validated core fields; optional Pose/Provenance are outside this audit. Default settlement Shadow and Paid both already decide paid receipts in the same Receive call. No transport receipt, one-way delay, CPU improvement, new fast payment or cross-shooter frontier claim. Raw identities/claim IDs are hashed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--expected-mode', choices=('shadow', 'paid'))
    args = parser.parse_args()
    result = audit(json.loads(args.report.read_text()), args.expected_mode)
    with args.out.open('x') as output: output.write(json.dumps(result, indent=2)+'\n')
    print(result['status'], result['eligibleSamples'], result['p95AuthorityTicks'])
    raise SystemExit(0 if result['status']=='PASS' else 1)
