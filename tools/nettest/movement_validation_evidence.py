#!/usr/bin/env python3
"""N12 advisory-only accepted movement evidence; never an enforcement verdict."""
import argparse
import json
import math
from pathlib import Path

HUNTERS = ('Samus', 'Kanden', 'Trace', 'Sylux', 'Noxus', 'Spire', 'Weavel')
COUNTS = ('Baselines', 'Evaluated', 'Exempt', 'Unsupported', 'WouldRefuse',
          'DuplicateOrOld', 'LongGaps', 'InputAbsentWhileMoving')
MAXIMA = ('MaximumHorizontal', 'MaximumUpward')


def integer(value, label, maximum=2**63-1, minimum=0):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError('invalid ' + label)
    return value


def number(value, label):
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
        raise ValueError('invalid ' + label)
    return value


def inspect(report, enabled, authority):
    if type(report['Enabled']) is not bool or report['Enabled'] != enabled:
        raise ValueError('Enabled disagrees with explicit expected setting')
    for field in ('HardRejections', 'Invalid', 'Overflow'):
        if integer(report[field], field): raise ValueError(field + ' must be zero')
    if not isinstance(report['Hunters'], list) or not isinstance(report['Rows'], list):
        raise ValueError('missing hunter counts or candidate rows')
    counters = {}
    for row in report['Hunters']:
        hunter = integer(row['Hunter'], 'Hunter', 6)
        if type(row['Alt']) is not bool: raise ValueError('invalid Alt')
        key = (hunter, row['Alt'])
        if key in counters: raise ValueError('duplicate hunter/form counts')
        counts = {f: integer(row[f], f) for f in COUNTS}
        counts.update({f: number(row[f], f) for f in MAXIMA})
        if counts['WouldRefuse'] > counts['Evaluated'] or counts['InputAbsentWhileMoving'] > counts['Evaluated']:
            raise ValueError('candidate/input denominator exceeds evaluated')
        if counts['LongGaps'] > counts['Baselines']: raise ValueError('long gaps exceed baselines')
        if row['Alt'] and counts['Evaluated'] or not row['Alt'] and counts['Unsupported']:
            raise ValueError('unsupported alt and evaluated biped denominators confused')
        if not counts['Evaluated'] and not counts['Unsupported'] and any(counts[f] for f in MAXIMA):
            raise ValueError('speed maxima lack measured denominator')
        if (not enabled or not authority) and any(counts.values()):
            raise ValueError('disabled/client report contains measurements')
        counters[key] = counts
    if set(counters) != {(h, alt) for h in range(7) for alt in (False, True)}:
        raise ValueError('incomplete seven-hunter/form counter schema')
    row_counts, seen = {}, set()
    for row in report['Rows']:
        key = tuple(integer(row[f], f, maximum=maximum, minimum=minimum) for f, maximum, minimum in
                    [('Epoch', 2**64-1, 1), ('Match', 65535, 1), ('Slot', 7, 0),
                     ('Generation', 65535, 1), ('Life', 65535, 1), ('Frame', 2**32-1, 0)])
        if key in seen: raise ValueError('duplicate exact candidate identity/frame')
        seen.add(key)
        hunter = integer(row['Hunter'], 'row Hunter', 6)
        integer(row['ElapsedFrames'], 'ElapsedFrames', 60, 1)
        if row['Alt'] is not False: raise ValueError('alt movement cannot carry biped refusal candidate')
        values = [number(row[f], f) for f in ('HorizontalPerFrame', 'UpwardPerFrame', 'AccelerationPerFrame')]
        signals = [row[f] for f in ('HorizontalSignal', 'VerticalSignal', 'AccelerationSignal')]
        if any(type(v) is not bool for v in signals) or not any(signals):
            raise ValueError('candidate row lacks typed advisory signal')
        if values[0] > counters[hunter, False]['MaximumHorizontal'] or values[1] > counters[hunter, False]['MaximumUpward']:
            raise ValueError('candidate speed exceeds reported measured maximum')
        row_counts[hunter] = row_counts.get(hunter, 0) + 1
    if len(report['Rows']) > 128: raise ValueError('candidate evidence exceeds native bounded capacity')
    for hunter in range(7):
        if row_counts.get(hunter, 0) != counters[hunter, False]['WouldRefuse']:
            raise ValueError('WouldRefuse count disagrees with retained advisory rows')
    if (not enabled or not authority) and report['Rows']:
        raise ValueError('disabled/client report contains candidate rows')
    totals = {f: sum(r[f] for r in counters.values()) for f in COUNTS}
    byhunter = {HUNTERS[h]: {'biped': counters[h, False], 'alt': counters[h, True]} for h in range(7)}
    return {'totals': totals, 'perHunter': byhunter, 'candidateRows': report['Rows'],
            'measuredHunters': [HUNTERS[h] for h in range(7) if any(counters[h, a][f] for a in (False, True) for f in ('Evaluated', 'Exempt', 'Unsupported'))]}


def audit(server, peers, summary, manifest):
    failures, incomplete = [], []
    metrics = {'authority': None, 'clients': {}, 'authorityEvaluated': 0}
    enabled = manifest.get('settings', manifest).get('movement_validation_shadow')
    if type(enabled) is not bool: incomplete.append('explicit movement_validation_shadow setting unavailable')
    if summary.get('status') == 'FAIL': failures.append('base campaign failed')
    elif summary.get('status') != 'PASS': incomplete.append('base campaign did not pass')
    drain = summary.get('coverage', {}).get('boundedHeadlessDrain')
    if drain == 'FAIL': failures.append('bounded campaign drain failed')
    elif drain != 'PASS': incomplete.append('bounded campaign drain did not pass')
    expected = {p['name'] for p in summary.get('processes', []) if p.get('name') != 'server'}
    if not expected or set(peers) != expected: incomplete.append('complete expected peer evidence unavailable')
    for name, evidence, authority in [('server', server, True), *((n, e, False) for n, e in peers.items())]:
        report = evidence.get('MovementValidation')
        if not isinstance(report, dict): incomplete.append(name + ': movement validation report missing'); continue
        if type(enabled) is not bool: continue
        try:
            data = inspect(report, enabled, authority)
            if authority:
                metrics['authority'] = data; metrics['authorityEvaluated'] = data['totals']['Evaluated']
                if enabled and not data['totals']['Evaluated']:
                    incomplete.append('authority has no evaluated biped movement coverage')
            else: metrics['clients'][name] = data
        except (KeyError, TypeError, ValueError, OverflowError) as exc:
            failures.append(name + ': ' + str(exc))
    return {'status': 'FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
            'failures': failures, 'incomplete': incomplete, 'metrics': metrics,
            'confidence': 'ADVISORY_ONLY',
            'scope': 'Opt-in authority accepted owner-source envelope observations only; WouldRefuse is reported, never required zero or treated as cheat/enforcement proof. Per-hunter evaluated/exempt/unsupported denominators do not establish seven-hunter native collision parity. Client authority guard and baseline gameplay/drain are required; no hard movement correction is validated.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('campaign', type=Path); parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    def load(name):
        try: return json.loads((args.campaign / name).read_text())
        except (OSError, ValueError): return {}
    summary = load('summary.json')
    peers = {p['name']: load(p['name'] + '.json') for p in summary.get('processes', []) if p.get('name') != 'server'}
    result = audit(load('server.json'), peers, summary, load('manifest.json'))
    encoded = json.dumps(result, indent=2) + '\n'
    if args.output: args.output.write_text(encoded)
    else: print(encoded, end='')
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__': raise SystemExit(main())
