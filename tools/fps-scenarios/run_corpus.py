#!/usr/bin/env python3
"""Capture and compare every shared fixture; a capture or coverage pass is not parity."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from check_coverage import run as check_coverage
from check_combat import run as check_combat
from check_projectiles import run as check_projectiles

COVERAGE = {'flat-wall', 'wall-angle', 'ceiling', 'shallow-slope', 'steep-slope', 'ledge-fall', 'jump-pad', 'knockback'}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('bridge', 'rom', 'prime', 'output'):
        p.add_argument('--' + name, type=Path, required=True)
    p.add_argument('--state', type=Path, action='append', required=True, help='Repeat for each room starting state')
    p.add_argument('--dotnet', default='dotnet')
    p.add_argument('--native-kernel', action='store_true', help='Offline 30 Hz experiment, not shipping 60 Hz acceptance')
    p.add_argument('--native-cadence60', action='store_true', help='Offline native movement in the 60 Hz scene; held intermediate authority')
    p.add_argument('--input-phase', type=int, choices=(0, 1), default=0, help='Which 60 Hz half receives each native command')
    p.add_argument('--scenarios', type=Path, default=Path(__file__).parent)
    a = p.parse_args()
    if a.input_phase and not a.native_cadence60:
        p.error("--input-phase 1 requires --native-cadence60")
    states = {}
    for state in a.state:
        room = json.loads(Path(str(state) + '.json').read_text())['room']
        if room in states:
            raise ValueError('Duplicate starting states for ' + room)
        states[room] = state.resolve()
    scenarios = sorted(a.scenarios.glob('*.json'))
    if not scenarios:
        raise ValueError('No scenarios')
    for scenario in scenarios:
        spec = json.loads(scenario.read_text())
        if spec.get('placementRequired') or spec['room'] not in states:
            raise ValueError('Unverified placement or missing room state: ' + spec['name'])
    root = a.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    recorder = Path(__file__).resolve().parents[1] / 'native-recorder' / 'record.py'
    report = []

    def execute(args, log):
        with log.open('w') as stream:
            return subprocess.run([str(arg) for arg in args], stdout=stream, stderr=subprocess.STDOUT, timeout=180).returncode

    for scenario in scenarios:
        spec = json.loads(scenario.read_text())
        folder = root / scenario.stem
        folder.mkdir()
        result = dict(scenario=spec['name'], mode='diagnostic-cadence60' if a.native_cadence60 else 'diagnostic-30hz' if a.native_kernel else 'current-60hz')
        try:
            result['nativeCaptureExit'] = execute([sys.executable, recorder, '--bridge', a.bridge.resolve(), '--rom', a.rom.resolve(),
                '--state', states[spec['room']], '--scenario', scenario.resolve(), '--output', folder/'native'], folder/'native.log')
            args = [a.dotnet, a.prime.resolve(), '-fpsscenario', scenario.resolve(), '-fpsoutput', folder/'prime.jsonl']
            if a.native_cadence60:
                args.extend(['-fpsnativecadence60', '-fpsinputphase', str(a.input_phase)])
            elif a.native_kernel:
                args.append('-fpsnativekernel')
            result['primeCaptureExit'] = execute(args, folder/'prime.log')
            if result['nativeCaptureExit'] or result['primeCaptureExit']:
                raise ValueError('Capture failed; see fixture logs')
            reference = folder/'native/native-reference.jsonl'
            if any(row.get('fire', False) for row in spec['inputs']):
                combat = check_combat(scenario, folder/'native/native-combat.jsonl', folder/'prime.jsonl.combat.jsonl', a.input_phase)
                (folder/'combat-comparison.json').write_text(json.dumps(combat, indent=2) + '\n')
                result['combatPassed'] = combat['passed']
                projectiles = check_projectiles(scenario, folder/'native/native-combat.jsonl', folder/'prime.jsonl.combat.jsonl')
                (folder/'projectile-comparison.json').write_text(json.dumps(projectiles, indent=2) + '\n')
                result['projectilesPassed'] = projectiles['passed']
            if spec['name'] in COVERAGE:
                coverage = check_coverage(scenario, reference, folder/'prime.jsonl')
                (folder/'coverage.json').write_text(json.dumps(coverage, indent=2) + '\n')
                result['coverage'] = 'PASS'
            result['parityExit'] = execute([a.dotnet, a.prime.resolve(), '-fpsphysicscheck', '-fpsreference', reference,
                '-fpsactual', folder/'prime.jsonl', '-fpsoutput', folder/'comparison'], folder/'comparison.log')
        except (ValueError, OSError, subprocess.TimeoutExpired) as error:
            result['error'] = str(error)
        report.append(result)
        (root/'summary.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(result), flush=True)
    return 0 if all(row.get('parityExit') == 0 and row.get('combatPassed', True) and row.get('projectilesPassed', True) and 'error' not in row for row in report) else 1


if __name__ == '__main__':
    raise SystemExit(main())
