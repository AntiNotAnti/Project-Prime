#!/usr/bin/env python3
"""Compare RPS -replayfidelity exports by full shot identity, never nearest time."""
import argparse
import json
import math
from pathlib import Path

IDENTITY = ('Match', 'Epoch', 'Slot', 'Generation', 'Life', 'Shot', 'Stage',
            'DamageEvent', 'Victim', 'VictimGeneration', 'VictimLife')
MAX_BYTES = 64 * 1024 * 1024
MAX_ROWS = 200000

def load(path, live_stage=None):
    with Path(path).open('rb') as source:
        data = source.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise ValueError('export exceeds byte budget')
    value = json.loads(data, parse_constant=lambda x: (_ for _ in ()).throw(ValueError(x)))
    if live_stage is not None:
        timeline = value.get('ShotTimeline', value)
        if timeline.get('Schema') != 2 or not timeline.get('Complete'):
            raise ValueError('complete schema-2 live shot timeline required')
        rows = []
        for row in timeline.get('Rows', []):
            if row.get('Stage') != live_stage:
                continue
            key = row['Key']
            rows.append(dict(Match=key['MatchId'], Epoch=key['AuthorityEpoch'], Slot=key['ShooterSlot'],
                Generation=key['Generation'], Life=key['LifeId'], Shot=key['ShotId'], Stage='authored-input',
                RecordingFrame=row['LocalFrame'], SourceFrame=row['SourceFrame'], AuthorityFrame=None,
                Weapon=row['Weapon'], Pose=row.get('Pose'), DamageEvent=None, Victim=None,
                VictimGeneration=None, VictimLife=None, Damage=None, Lethal=None, Headshot=None))
        value = dict(Schema=1, Decoder='Healthy', Rows=rows)
    if value.get('Schema') != 1 or not isinstance(value.get('Rows'), list) or len(value['Rows']) > MAX_ROWS:
        raise ValueError('unsupported schema or row budget')
    return value

def index(document):
    result, conflicts = {}, []
    for row in document['Rows']:
        if not all(isinstance(row.get(k), int) and row[k] > 0 for k in ('Match', 'Epoch', 'Generation', 'Life', 'Shot')):
            raise ValueError('missing stable shot identity')
        if not isinstance(row.get('Slot'), int) or not 0 <= row['Slot'] < 8:
            raise ValueError('invalid actor slot')
        key = tuple(row.get(k) for k in IDENTITY)
        # Repeated reliable/input carriers may have different arrival frames.
        canonical = {k: v for k, v in row.items() if k != 'RecordingFrame'}
        if key in result and canonical != result[key]:
            conflicts.append(list(key))
        else:
            result[key] = canonical
    return result, conflicts

def vector(pose, field):
    value = pose[field]
    result = [value[x] for x in ('X', 'Y', 'Z')]
    if not all(isinstance(x, (float, int)) and math.isfinite(x) for x in result):
        raise ValueError('nonfinite vector')
    return result

def angle(a, b):
    norm = math.sqrt(sum(x*x for x in a) * sum(x*x for x in b))
    if norm == 0:
        raise ValueError('zero aim/direction vector')
    return math.degrees(math.acos(max(-1, min(1, sum(x*y for x, y in zip(a, b))/norm))))

def compare(left, right):
    a, ac = index(left)
    b, bc = index(right)
    sort_key = lambda key: json.dumps(key, separators=(',', ':'))
    common = sorted(a.keys() & b.keys(), key=sort_key)
    rows, mismatches = [], 0
    for key in common:
        x, y = a[key], b[key]
        frame_delta = y['SourceFrame'] - x['SourceFrame']
        error = {'Identity': list(key), 'SourceFrameDelta': frame_delta,
                 'OriginDistance': None, 'AimDegrees': None, 'DirectionDegrees': None,
                 'MissingPresentation': ['owner-camera', 'drawn-weapon', 'animation-phase']}
        px, py = x.get('Pose'), y.get('Pose')
        if px is not None and py is not None:
            error['OriginDistance'] = math.dist(vector(px, 'Origin'), vector(py, 'Origin'))
            error['AimDegrees'] = angle(vector(px, 'Aim'), vector(py, 'Aim'))
            error['DirectionDegrees'] = angle(vector(px, 'Direction'), vector(py, 'Direction'))
        else:
            error['MissingPresentation'].append('authored-pose')
        changed = [field for field in ('Weapon', 'Damage', 'Lethal', 'Headshot', 'AuthorityFrame') if x.get(field) != y.get(field)]
        if px is not None and py is not None and px['Charge'] != py['Charge']:
            changed.append('Charge')
        if (px is None) != (py is None):
            changed.append('PoseAvailability')
        error['ChangedFields'] = changed
        # Floating angle roundoff at identical canonical vectors is not an error.
        error['Mismatch'] = bool(frame_delta or changed or (error['OriginDistance'] or 0) > 1e-6
                                 or (error['AimDegrees'] or 0) > 1e-5 or (error['DirectionDegrees'] or 0) > 1e-5)
        mismatches += error['Mismatch']
        rows.append(error)
    missing_left = [list(k) for k in sorted(b.keys() - a.keys(), key=sort_key)]
    missing_right = [list(k) for k in sorted(a.keys() - b.keys(), key=sort_key)]
    decoders = [left.get('Decoder', 'unavailable'), right.get('Decoder', 'unavailable')]
    return {'Schema': 1, 'Matched': len(common), 'Mismatches': mismatches,
            'UnmatchedLeft': missing_left, 'UnmatchedRight': missing_right,
            'ConflictingLeft': ac, 'ConflictingRight': bc, 'Decoder': decoders,
            'Passed': bool(common) and not (mismatches or missing_left or missing_right or ac or bc)
                      and all(d == 'Healthy' for d in decoders), 'Rows': rows}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--live-stage', choices=['source-authored', 'source-admitted', 'source-emitted'])
    parser.add_argument('left'); parser.add_argument('right'); parser.add_argument('--output', required=True)
    args = parser.parse_args()
    left, right = load(args.left, args.live_stage), load(args.right)
    if args.live_stage:
        # Outcome facts remain separate: admission/emission cannot imply payment.
        right = dict(right, Rows=[r for r in right['Rows'] if r['Stage'] == 'authored-input'])
    result = compare(left, right)
    Path(args.output).write_text(json.dumps(result, sort_keys=True, indent=2, allow_nan=False) + '\n')
    print(f"{'PASS' if result['Passed'] else 'FAIL'}: {result['Matched']} matched, {result['Mismatches']} mismatched, "
          f"{len(result['UnmatchedLeft']) + len(result['UnmatchedRight'])} unmatched")
    return 0 if result['Passed'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
