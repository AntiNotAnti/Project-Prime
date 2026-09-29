#!/usr/bin/env python3
"""Compare measured Power Beam timing at native boundaries; no movement claims."""
import argparse
import json
from pathlib import Path


def load(path, count, stride):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    if len(rows) != count or [r['frame'] for r in rows] != list(range(stride, count * stride + 1, stride)):
        raise ValueError('Incomplete, duplicate, or misordered combat timeline')
    for row in rows:
        if any(type(row[k]) is not bool for k in ('fire', 'shot')):
            raise ValueError('Noncanonical combat boolean')
        if any(type(row[k]) is not int or row[k] < 0 for k in ('timeSinceShot', 'chargeLevel', 'weapon')):
            raise ValueError('Invalid combat measurement')
        if row['weapon'] != 0 or row['shot'] != (row['timeSinceShot'] == 0):
            raise ValueError('Unexpected weapon or inconsistent shot/reset observation')
    return rows


def run(scenario_path, native_path, prime_path, input_phase=0):
    if input_phase not in (0,1): raise ValueError('Invalid input phase')
    spec = json.loads(Path(scenario_path).read_text())
    count = spec['nativeTicks']
    if not any(row.get('fire', False) for row in spec['inputs']):
        raise ValueError('Fixture never requests fire')
    native = load(native_path, count, 2)
    prime = load(prime_path, count * 2, 1)
    native_shots = [r['frame'] for r in native if r['shot']]
    prime_shots = [((r['frame'] - 1) // 2 + 1) * 2 for r in prime if r['shot']]
    if not native_shots or not prime_shots:
        raise ValueError('No actual shots observed')
    failures = []
    if native_shots != prime_shots:
        failures.append('Shot timeline differs')
    for tick, expected in enumerate(native):
        pair = prime[tick*2:tick*2+2]
        held = next(row for row in reversed(spec['inputs']) if row['tick'] <= tick).get('fire', False)
        previous_held = next((row for row in reversed(spec['inputs']) if row['tick'] <= tick-1), {}).get('fire', False)
        if expected['fire'] != held or [row['fire'] for row in pair] != [previous_held if input_phase else held, held]:
            failures.append(f'Unconsumed input at native tick {tick+1}')
        remaining = max(0, spec.get('initialFreezeNativeTicks', 0) - tick - 1)
        if expected.get('freezeTimer', 0) != remaining or pair[-1].get('freezeTimer', 0) != remaining * 2:
            failures.append(f'Freeze expiry differs at native tick {tick+1}')
        if pair[-1]['chargeLevel'] != expected['chargeLevel'] * 2:
            failures.append(f'Charge duration differs at native tick {tick+1}')
        # Successful firing resets the native byte, and the 60 Hz counter may
        # already have advanced on the second half. Compare elapsed whole native
        # ticks after the first shot; startup values and saturation differ.
        if native_shots[0] <= expected['frame'] and expected['timeSinceShot'] < 255:
            if pair[-1]['timeSinceShot'] // 2 != expected['timeSinceShot']:
                failures.append(f'Shot age differs at native tick {tick+1}')
    return dict(scenario=spec['name'], passed=not failures, nativeShots=native_shots,
                primeShotsAtNativeBoundaries=prime_shots,
                primeActualShotFrames=[r['frame'] for r in prime if r['shot']],
                chargeMapping='Prime 60 Hz charge = native 30 Hz charge * 2',
                shotMapping='Prime frame F belongs to native boundary 2*ceil(F/2); raw event frame retained',
                unverified=['damage', 'weapon-applied freeze', 'other weapons', 'ammo (Power Beam is free)'], failures=failures)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('scenario', type=Path); p.add_argument('native', type=Path); p.add_argument('prime', type=Path)
    p.add_argument('--input-phase', type=int, choices=(0,1), default=0)
    a = p.parse_args()
    result = run(a.scenario, a.native, a.prime, a.input_phase)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['passed'] else 1)
