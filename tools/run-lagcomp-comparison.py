#!/usr/bin/env python3
"""Paired deterministic simulation evidence. This does not run a WAN/UDP experiment."""
import argparse
import copy
import json
import pathlib
import subprocess
import sys

SCENARIOS = ('trace', 'travel', 'homing', 'continuous', 'area')
EQUAL_FIELDS = ('Schema', 'ScriptHash', 'InitialRng1', 'InitialRng2', 'Origin',
                'TargetCenter', 'TargetAxis', 'HealthRestoredEachTick',
                'MovementPrescribedEachTick', 'ChargeHoldTicks', 'FirePeriod',
                'Deliveries', 'Accepted', 'Shots', 'RootShots', 'TargetTrajectory')


def compare(on, off, *, baseline='off', repeat_on=False):
    if repeat_on:
        baseline = 'repeat-on'
    if baseline not in ('off', 'trace-only', 'repeat-on'):
        raise ValueError('Unknown comparison baseline.')
    baseline_lag = baseline != 'off'
    baseline_catch_up = baseline == 'repeat-on'
    for result, lag, catch_up in ((on, True, True), (off, baseline_lag, baseline_catch_up)):
        if result['Config']['Enabled'] is not lag or result['Config']['ProjectileCatchUpEnabled'] is not catch_up:
            raise ValueError('Pair did not execute the requested comparison modes.')
    a, b = dict(on['Config']), dict(off['Config'])
    for options in (a, b):
        options.pop('Enabled'); options.pop('ProjectileCatchUpEnabled')
    if a != b:
        raise ValueError('Scenario configurations differ between runs.')
    for field in EQUAL_FIELDS:
        if on[field] != off[field]:
            raise ValueError(f'Pair rejected: {field} differs; hit counts are not comparable.')
    if on['RootShots'] <= 0 or len(on['Shots']) != on['RootShots']:
        raise ValueError('No accepted root shots or inconsistent shot count.')
    for result, lag, catch_up in ((on, True, True), (off, baseline_lag, baseline_catch_up)):
        if result['ActualLagCompEnabled'] is not lag or result['ActualProjectileCatchUpEnabled'] is not catch_up:
            raise ValueError('Actual server policy differs from the requested comparison mode.')
    if on['QueueDrops'] or off['QueueDrops']:
        raise ValueError('Catch-up queue overflow invalidates the comparison.')
    if on['CombatDropped'] or off['CombatDropped']:
        raise ValueError('Combat event journal overflow invalidates the comparison.')
    if repeat_on and on != off:
        raise ValueError('Repeated ON run was not byte-for-byte deterministic as JSON data.')
    if baseline == 'trace-only' and a['Scenario'] == 'trace' and on['Hits'] != off['Hits']:
        raise ValueError('Trace-only baseline changed historical trace outcomes.')
    if not repeat_on and on['ExpectedPolicy'] == 'None':
        # Charging normally emits an uncharged root before the charged release.
        # Only the resolved excluded variant should require identical outcomes.
        excluded = {shot['Command'] for shot in on['Shots']
                    if a['Scenario'] == 'continuous' or shot['Flags'] & 1}
        on_hits = [hit for hit in on['Hits'] if hit['Command'] in excluded]
        off_hits = [hit for hit in off['Hits'] if hit['Command'] in excluded]
        if on_hits != off_hits:
            raise ValueError('Excluded root shots produced different resolved damage in ON/OFF.')
    return dict(scenario=a['Scenario'], baseline=baseline, seed=a['Seed'], delay_ticks=a['DelayTicks'],
                jitter_ticks=a['JitterTicks'], loss_per_thousand=a['LossPerThousand'],
                root_shots=on['RootShots'], on_damage_events=on['DamageEvents'],
                baseline_damage_events=off['DamageEvents'], on_total_damage=on['TotalDamage'],
                baseline_total_damage=off['TotalDamage'], script_sha256=on['ScriptHash'],
                evidence='repeatability only' if repeat_on else 'paired controlled simulation',
                passed=True)


def self_test():
    sample = {field: 1 for field in EQUAL_FIELDS}
    sample.update(Config={'Enabled': True, 'ProjectileCatchUpEnabled': True, 'Scenario': 'trace'}, Shots=[{'Command': 7}],
                  RootShots=1, CombatDropped=0)
    other = copy.deepcopy(sample); other['Config']['Enabled'] = False
    other['Config']['ProjectileCatchUpEnabled'] = False
    for field in ('ScriptHash', 'Deliveries', 'Accepted', 'Shots', 'TargetTrajectory'):
        bad = copy.deepcopy(other); bad[field] = 'MISMATCH'
        try:
            compare(sample, bad)
        except ValueError as error:
            if field not in str(error):
                raise
        else:
            raise AssertionError(f'{field} mismatch accepted')
    print('comparison guard self-test: 5 mismatched pairs rejected')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet')
    parser.add_argument('--harness')
    parser.add_argument('--data')
    parser.add_argument('--output', type=pathlib.Path)
    parser.add_argument('--scenario', action='append', choices=SCENARIOS)
    parser.add_argument('--delay', type=int, action='append')
    parser.add_argument('--ticks', type=int, default=1200)
    parser.add_argument('--seed', type=int, default=0xA1758)
    parser.add_argument('--version', default='AMHE1')
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--baseline', action='append', choices=('off', 'trace-only'),
                       help='Defaults to both baselines: fully off and historical traces only.')
    modes.add_argument('--repeat-on', action='store_true',
                        help='Preparation only: verify two identical ON runs; do not claim an A/B result.')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if not all((args.dotnet, args.harness, args.data, args.output)):
        parser.error('--dotnet, --harness, --data and --output are required')
    if args.output.exists():
        parser.error("--output must be a new directory to keep prior evidence intact")
    args.output.mkdir(parents=True)
    results = []
    for scenario in args.scenario or SCENARIOS:
        for delay in args.delay or (0, 4, 8):
            if not 0 <= delay <= 30:
                parser.error('--delay must be 0 to 30 ticks')
            def run_case(suffix, lag, catch_up):
                name = f'{scenario}-delay{delay}-{suffix}'
                config = dict(Scenario=scenario, Enabled=lag, ProjectileCatchUpEnabled=catch_up,
                              Seed=args.seed, Ticks=args.ticks, DelayTicks=delay,
                              JitterTicks=min(2, delay), LossPerThousand=30, Version=args.version)
                config_path = args.output / (name + '.config.json')
                output_path = args.output / (name + '.json')
                config_path.write_text(json.dumps(config, indent=2))
                with (args.output / (name + '.log')).open('w') as log:
                    subprocess.run([args.dotnet, args.harness, '--lagcomp-script', args.data,
                                    str(config_path), str(output_path)], stdout=log,
                                   stderr=subprocess.STDOUT, check=True, timeout=45)
                return json.loads(output_path.read_text())

            on = run_case('on', True, True)
            for baseline in ('repeat-on',) if args.repeat_on else (args.baseline or ('off', 'trace-only')):
                other = run_case(baseline, baseline != 'off', baseline == 'repeat-on')
                result = compare(on, other, baseline=baseline, repeat_on=args.repeat_on)
                results.append(result)
                print(json.dumps(result), flush=True)
    (args.output / 'comparison.json').write_text(json.dumps(results, indent=2))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, subprocess.SubprocessError) as error:
        print(f'COMPARISON FAILED: {error}', file=sys.stderr)
        sys.exit(1)
