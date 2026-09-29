#!/usr/bin/env python3
"""Strict measured projectile pool comparison at native boundaries."""
import argparse
import json
import math
from pathlib import Path
from check_combat import load


def run(scenario_path, native_path, prime_path):
    spec = json.loads(Path(scenario_path).read_text())
    native = load(native_path, spec['nativeTicks'], 2)
    prime = load(prime_path, spec['nativeTicks'] * 2, 1)[1::2]
    maxima = dict(position=0.0, spawnPosition=0.0, velocity=0.0, age=0.0, lifespan=0.0)
    failures = []
    measured = 0

    def slots(row):
        beams = row['projectiles']
        result = {}
        for beam in beams:
            slot = beam['slot']
            if type(slot) is not int or slot < 0 or slot in result:
                raise ValueError('Invalid or duplicate projectile slot')
            for key in ('weapon', 'flags'):
                if type(beam[key]) is not int or beam[key] < 0:
                    raise ValueError('Invalid projectile identity/flags')
            for key in maxima:
                values = [beam[key][axis] for axis in ('x','y','z')] if key in ('position','spawnPosition','velocity') else [beam[key]]
                if any(type(v) not in (int, float) or not math.isfinite(v) for v in values):
                    raise ValueError('Nonfinite projectile measurement')
            result[slot] = beam
        return result

    for expected, actual in zip(native, prime):
        left, right = slots(expected), slots(actual)
        frame = expected['frame']
        if left.keys() != right.keys():
            failures.append(dict(frame=frame, kind='activeSlots', native=list(left), prime=list(right)))
        for slot in left.keys() & right.keys():
            measured += 1
            a, b = left[slot], right[slot]
            for key in ('weapon', 'flags'):
                if a[key] != b[key]: failures.append(dict(frame=frame, slot=slot, kind=key, native=a[key], prime=b[key]))
            for key in maxima:
                error = math.dist([a[key][axis] for axis in ('x','y','z')], [b[key][axis] for axis in ('x','y','z')]) if key in ('position','spawnPosition','velocity') else abs(a[key]-b[key])
                maxima[key] = max(maxima[key], error)
                if error > 0.0001:
                    failures.append(dict(frame=frame, slot=slot, kind=key, error=error))
    if not measured: raise ValueError('No paired live projectile observations')
    return dict(scenario=spec['name'], passed=not failures, measuredProjectiles=measured,
                tolerance=0.0001, maxima=maxima, firstMismatch=failures[0] if failures else None,
                mismatchCount=len(failures), failures=failures)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('scenario', type=Path); p.add_argument('native', type=Path); p.add_argument('prime', type=Path)
    a = p.parse_args()
    result = run(a.scenario, a.native, a.prime)
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['passed'] else 1)
