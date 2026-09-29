#!/usr/bin/env python3
"""Verify that BOTH recordings exercise a geometry fixture, independently of parity."""
import argparse
import hashlib
import json
from pathlib import Path


def verify(name, rows, impulse=None):
    if not rows:
        raise ValueError('Empty trace')
    contacts = [c for r in rows for c in (r.get('contacts') if 'contacts' in r
                else (r.get('stages') or {}).get('contacts', []))]
    penetrating = [c['plane'] for c in contacts if c['penetrationDepth'] > 0]
    predicates = {
        'flat-wall': lambda p: abs(p['x']) < .01 and abs(p['y']) < .01 and p['z'] < -.99,
        'wall-angle': lambda p: abs(p['y']) < .01 and abs(p['x']) > .2 and abs(p['z']) > .2,
        'ceiling': lambda p: p['y'] < -.9,
        'shallow-slope': lambda p: .7 < p['y'] < .99,
        'steep-slope': lambda p: .1 < p['y'] <= .7,
    }
    if name in predicates:
        hits = [p for p in penetrating if predicates[name](p)]
        if not hits:
            raise ValueError(f'{name}: no penetrating contact with the required geometry')
        return dict(contactCount=len(contacts), qualifyingContacts=len(hits), firstPlane=hits[0])
    if name == 'knockback':
        if impulse is None:
            raise ValueError('Missing prescribed impulse')
        before = rows[0]['velocityBefore']
        if any(abs(before[c] - impulse['velocity'][i]) > 1e-6 for i,c in enumerate('xyz')):
            raise ValueError('Initial impulse velocity was not established')
        count = 0
        for r in rows:
            stage = r.get('nativeStages') or r.get('stages')
            if not stage:
                raise ValueError('Missing acceleration stages')
            scale = stage.get('sampleHz', 60) / 30
            delta = [stage['afterAcceleration'][c] - stage['preMovement'][c] for c in 'xyz']
            expected = [v/scale for v in impulse['acceleration']]
            active = r['frame'] <= impulse['nativeTicks'] * 2
            if any(abs(delta[i] - (expected[i] if active else 0)) > 1e-6 for i in range(3)):
                raise ValueError('Timed impulse acceleration differs from the prescribed duration or rate')
            count += active
        return dict(acceleratedSamples=count, impulseNativeTicks=impulse['nativeTicks'])
    if name == 'jump-pad':
        launched = [r for r in rows if r.get('jumpPadActive') and r['velocityActual']['y'] > .45]
        rise = max(r['positionActual']['y'] for r in rows) - rows[0]['positionBefore']['y']
        if not launched or rise < 1:
            raise ValueError('Missing active jump-pad flag, upward impulse or ascent')
        return dict(firstLaunchFrame=launched[0]['frame'], rise=rise)
    if name == 'ledge-fall':
        if not rows[0]['standing']:
            raise ValueError('Ledge fixture must start standing')
        falling = [i for i, r in enumerate(rows) if not r['standing'] and r['velocityActual']['y'] < 0]
        if not falling or not any(r['standing'] for r in rows[falling[-1] + 1:]):
            raise ValueError('Missing standing -> falling -> landed transition')
        drop = rows[0]['positionBefore']['y'] - rows[-1]['positionActual']['y']
        if drop < 1:
            raise ValueError('Ledge fixture did not land at a lower elevation')
        return dict(firstFallingFrame=rows[falling[0]]['frame'], drop=drop)
    raise ValueError('No coverage predicate for ' + name)


def run(scenario_path, native_path, prime_path):
    scenario = json.loads(scenario_path.read_text())
    result = dict(scenario=scenario['name'], coverage={}, sha256={}, nativeParity='Not evaluated')
    for label, path in [('native', native_path), ('prime', prime_path)]:
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        boundaries = [r['frame'] for r in rows if r['frame'] % 2 == 0]
        if boundaries != list(range(2, scenario['nativeTicks'] * 2 + 1, 2)):
            raise ValueError(f'{label}: missing, duplicate or unordered boundaries')
        result['coverage'][label] = verify(scenario['name'], rows, scenario.get('initialImpulse'))
        result['sha256'][label] = hashlib.sha256(path.read_bytes()).hexdigest()
    result['sha256']['scenario'] = hashlib.sha256(scenario_path.read_bytes()).hexdigest()
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('scenario', 'native', 'prime'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(run(args.scenario, args.native, args.prime), indent=2))
