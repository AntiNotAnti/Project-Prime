"""Audit authority native timed-grant transitions; not pickup-claim or damage proof."""
import argparse
import json
from pathlib import Path

TYPES = ('DoubleDamage', 'Cloak', 'DeathAlt')
ENDS = ('Expired', 'Renewed', 'Death', 'Retired', 'Room', 'Session')
MASK = (1 << 32) - 1


def audit(report):
    if not isinstance(report, dict):
        return dict(status='FAIL', failures=['report must be an object'])
    failures = []
    starts, closed, spawns = {}, set(), set()
    counts = {kind: 0 for kind in TYPES}
    ends = {reason: 0 for reason in ENDS}
    unknown_source = 0
    rows = report.get('TimedGrants')
    if rows is None:
        return dict(status='NOT_RUN', failures=[], reason='No timed-grant instrumentation')
    if not isinstance(rows, list):
        return dict(status='FAIL', failures=['TimedGrants must be a list'])
    if 'Overflow' in report and (type(report['Overflow']) is not int or report['Overflow'] != 0):
        failures.append('Campaign evidence overflow is nonzero or malformed')

    def integer(value, low, high):
        return isinstance(value, int) and not isinstance(value, bool) and low <= value <= high

    for index, row in enumerate(rows):
        try:
            if not isinstance(row, dict):
                raise ValueError('transition is not an object')
            spawn, life = row['Spawn'], row['Life']
            for name in ('ContactFrame', 'EffectiveFrame', 'ExpiresAtFrame', 'EndFrame'):
                if not integer(row[name], 0, MASK):
                    raise ValueError('invalid ' + name)
            if not integer(row['Sequence'], 1, (1 << 64) - 1):
                raise ValueError('invalid grant sequence')
            if not integer(spawn['Epoch'], 1, (1 << 64) - 1) or not integer(spawn['Match'], 1, 65535):
                raise ValueError('invalid spawn stream')
            if not integer(spawn['Room'], 1, MASK) or not integer(spawn['Generation'], 1, MASK):
                raise ValueError('invalid spawn incarnation')
            if not integer(spawn['SpawnId'], -1, 32767):
                raise ValueError('invalid authored/dynamic spawn ID')
            if not integer(life['AuthorityEpoch'], 1, (1 << 64) - 1) \
                    or not integer(life['MatchId'], 1, 65535) \
                    or life['AuthorityEpoch'] != spawn['Epoch'] or life['MatchId'] != spawn['Match']:
                raise ValueError('grant life and spawn stream differ')
            if not integer(life['ShooterSlot'], 0, 7) or not integer(life['Generation'], 1, 65535) \
                    or not integer(life['LifeId'], 1, 65535) or not integer(life['ShotId'], 0, 0):
                raise ValueError('invalid player life')
            kind, reason = row['Type'], row['EndReason']
            if kind not in TYPES or reason not in ('Active', *ENDS):
                raise ValueError('unknown type/end reason')
            if row['EffectiveFrame'] != (row['ContactFrame'] + 1) & MASK \
                    or row['ExpiresAtFrame'] != (row['ContactFrame'] + 1800) & MASK:
                raise ValueError('incorrect native phase/duration')
            source, effective = row['SourceContactFrame'], row['SourceEffectiveFrame']
            if source is None:
                if effective is not None:
                    raise ValueError('invented source boundary')
            elif not integer(source, 0, MASK) or not integer(effective, 0, MASK) \
                    or effective != (source + 1) & MASK:
                raise ValueError('invalid source boundary')
            key = (spawn['Epoch'], spawn['Match'], spawn['Room'], row['Sequence'])
            spawn_key = tuple(spawn[name] for name in ('Epoch', 'Match', 'Room', 'SpawnId', 'Generation'))
            if reason == 'Active':
                if key in starts or spawn_key in spawns:
                    raise ValueError('duplicate grant or double pickup of an incarnation')
                if row['EndFrame'] != row['ExpiresAtFrame']:
                    raise ValueError('active grant changed expiry')
                starts[key] = row
                spawns.add(spawn_key)
                counts[kind] += 1
                unknown_source += source is None
            else:
                if key not in starts or key in closed:
                    raise ValueError('closure without one unique creation')
                initial = starts[key]
                if any(row.get(name) != value for name, value in initial.items()
                       if name not in ('EndFrame', 'EndReason')):
                    raise ValueError('closure changed immutable grant identity/timing')
                if (row['EndFrame'] - row['ContactFrame']) & MASK > 1800:
                    raise ValueError('closure outside authority grant lifetime')
                if reason == 'Expired' and row['EndFrame'] != row['ExpiresAtFrame']:
                    raise ValueError('expiry closed at the wrong frame')
                closed.add(key)
                ends[reason] += 1
        except (KeyError, TypeError, ValueError) as error:
            failures.append(f'TimedGrants[{index}]: {error}')
    return dict(status='FAIL' if failures else 'PASS' if starts else 'NOT_RUN', failures=failures,
                grants=len(starts), byType=counts, closures=len(closed), byEndReason=ends,
                openAtCapture=len(starts) - len(closed), unknownSourceBoundary=unknown_source,
                scope='Native authority reservations/transition conservation; no client reconciliation or shot authorization proof')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    result = audit(json.loads(args.report.read_text()))
    print(json.dumps(result, indent=2, allow_nan=False))
    raise SystemExit(result['status'] == 'FAIL')
