#!/usr/bin/env python3
"""Stdlib-only per-slice Netcode 2.1 evidence contract (G0; no gameplay changes).

Use new_manifest(slice_name, metric_specs) then fill identity, config, seed,
networkProfile, provenance, metrics and checks from actual measurements. Each metric
has countingUnit, minimum, expected, observed and status; counts are nonnegative
integers. Required denominator names are separate; additional metrics are allowed.
Missing measurements stay null/NOT_RUN. Measured zero is valid when the declared
minimum and expected counts are both zero; required zero events stay NOT_RUN.
Mixed measured and missing evidence aggregates to INCOMPLETE; wholly unrun evidence
aggregates to NOT_RUN. Seeds use the signed 32-bit runner range. Exclusions are descriptive and never waive
requirements. Set provenance to fresh only for a reproduced run; historical evidence
remains INCOMPLETE. audit(report, expected_identity=None) validates the contract and
optionally compares sourceSha/binarySha256 to externally trusted identities. PASS
means this declared evidence contract passed, not unspecified multiplayer coverage.
CLI: python net21_evidence.py report.json [--source-sha SHA] [--binary-sha256 SHA].
Prints an audit JSON and exits nonzero for anything except PASS.
Alternatively --campaign DIR exports run-net2 JSON artifacts as a manifest and audit.
This export preserves absent instrumentation as null and keeps legacy PASS separate.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

DENOMINATORS = ('claimsOffered', 'claimsAccepted', 'hits', 'pickups', 'grants',
                'freezeStarts', 'freezeEnds', 'touchSamplePairs', 'altContacts',
                'deathEvents', 'packetLoss', 'queueOccupancy', 'replayFacts')
STATUSES = {'PASS', 'FAIL', 'NOT_RUN', 'INCOMPLETE'}


def count(value):
    return isinstance(value, int) and not isinstance(value, bool) and value >= 0


def new_manifest(slice_name, metric_specs=None):
    """Create unmeasured evidence; specs map names to countingUnit/minimum objects."""
    specs = metric_specs or {}
    metrics = {}
    for name in dict.fromkeys((*DENOMINATORS, *specs)):
        spec = specs.get(name, {})
        metrics[name] = {'countingUnit': spec.get('countingUnit'),
                         'minimum': spec.get('minimum'), 'expected': None,
                         'observed': None, 'status': 'NOT_RUN'}
    return {'schemaVersion': 1, 'slice': slice_name, 'sourceSha': None,
            'binarySha256': None, 'config': None, 'seed': None,
            'networkProfile': None, 'provenance': None, 'metrics': metrics,
            'checks': {}, 'exclusions': []}


def audit(report, expected_identity=None):
    failures, missing, incomplete = [], [], []
    result = {'status': 'NOT_RUN', 'failures': failures, 'missing': missing,
              'incomplete': incomplete, 'metrics': {}}
    if not isinstance(report, dict):
        failures.append('report must be an object')
        result['status'] = 'FAIL'
        return result
    if not count(report.get('schemaVersion')) or report['schemaVersion'] != 1:
        failures.append('schemaVersion must be 1')
    for key in ('slice',):
        if not isinstance(report.get(key), str) or not report[key].strip():
            missing.append(key)
    for key, pattern in (('sourceSha', r'[0-9a-fA-F]{40}|[0-9a-fA-F]{64}'),
                         ('binarySha256', r'[0-9a-fA-F]{64}')):
        value = report.get(key)
        if value is None:
            missing.append(key)
        elif not isinstance(value, str) or re.fullmatch(pattern, value) is None:
            failures.append(f'{key}: invalid hash')
        if expected_identity and key in expected_identity:
            expected = expected_identity[key]
            if not isinstance(expected, str) or not isinstance(value, str) or value.lower() != expected.lower():
                failures.append(f'{key}: identity mismatch')
    for key in ('config', 'networkProfile'):
        value = report.get(key)
        if value is None:
            missing.append(key)
        elif not isinstance(value, dict) or not value:
            failures.append(f'{key}: must be a nonempty object')
    if report.get('seed') is None:
        missing.append('seed')
    elif (not isinstance(report['seed'], int) or isinstance(report['seed'], bool)
          or not -(2 ** 31) <= report['seed'] < 2 ** 31):
        failures.append('seed: must be a signed 32-bit integer')
    provenance = report.get('provenance')
    if provenance is None:
        missing.append('provenance')
    elif provenance == 'historical':
        incomplete.append('historical evidence is not a fresh reproduction')
    elif provenance != 'fresh':
        failures.append('provenance must be fresh or historical')
    if not isinstance(report.get('exclusions'), list) or any(not isinstance(x, str) or not x.strip() for x in report.get('exclusions', [])):
        failures.append('exclusions must be a list of nonempty descriptions')
    metrics = report.get('metrics', {})
    if not isinstance(metrics, dict):
        failures.append('metrics must be an object')
        metrics = {}
    for name in dict.fromkeys((*DENOMINATORS, *metrics)):
        metric = metrics.get(name)
        if metric is None:
            missing.append(f'{name}: measurement missing')
            result['metrics'][name] = {'observed': None, 'status': 'NOT_RUN'}
            continue
        if not isinstance(metric, dict):
            failures.append(f'{name}: metric must be an object')
            result['metrics'][name] = {'observed': None, 'status': 'FAIL'}
            continue
        start = len(failures)
        if not isinstance(metric.get('countingUnit'), str) or not metric['countingUnit'].strip():
            missing.append(f'{name}: countingUnit')
        for field in ('minimum', 'expected', 'observed'):
            value = metric.get(field)
            if value is None:
                missing.append(f'{name}: {field}')
            elif not count(value):
                failures.append(f'{name}: {field} must be a nonnegative integer')
        stated = metric.get('status')
        if not isinstance(stated, str) or stated not in STATUSES:
            failures.append(f'{name}: invalid status')
        if stated == 'FAIL':
            failures.append(f'{name}: explicit failure')
        minimum, expected, observed = (metric.get(f) for f in ('minimum', 'expected', 'observed'))
        status = 'NOT_RUN'
        if len(failures) > start:
            status = 'FAIL'
        elif all(count(x) for x in (minimum, expected, observed)):
            if observed == 0 and max(minimum, expected) > 0:
                missing.append(f'{name}: zero observed events')
            elif observed < max(minimum, expected) or stated == 'INCOMPLETE':
                status = 'INCOMPLETE'
                incomplete.append(f'{name}: insufficient or incomplete evidence')
            elif stated == 'PASS' and isinstance(metric.get('countingUnit'), str) and metric['countingUnit'].strip():
                status = 'PASS'
            else:
                missing.append(f'{name}: check not run')
        result['metrics'][name] = {'observed': observed if count(observed) else None, 'status': status}
    checks = report.get('checks')
    if not isinstance(checks, dict):
        failures.append('checks must be an object')
    elif not checks:
        missing.append('checks')
    else:
        for name, status in checks.items():
            if not isinstance(status, str) or status not in STATUSES or status == 'FAIL':
                failures.append(f'check {name}: {status}')
            elif status == 'NOT_RUN':
                missing.append(f'check {name}')
            elif status == 'INCOMPLETE':
                incomplete.append(f'check {name}')
    ran = any(metric['status'] in ('PASS', 'INCOMPLETE') or
              (count(metric['observed']) and metric['observed'] > 0)
              for metric in result['metrics'].values())
    ran = ran or (isinstance(checks, dict) and any(
        isinstance(status, str) and status in ('PASS', 'INCOMPLETE')
        for status in checks.values()))
    result['status'] = ('FAIL' if failures else
                        'INCOMPLETE' if incomplete or (missing and ran) else
                        'NOT_RUN' if missing else 'PASS')
    return result


def campaign_manifest(directory):
    """Export completed run-net2 artifacts without inferring absent G0 counters.

    Legacy summary PASS stays a separate check. Semantic facts deduplicate by
    (Epoch, Match, Award lane, Id), with conflicting payloads treated as failures.
    Malformed/missing campaign inputs return a fail-closed manifest with errors.
    """
    directory = Path(directory)
    units = {name: {'countingUnit': name + ' events', 'minimum': 1}
             for name in DENOMINATORS}
    units.update(offeredShots={'countingUnit': 'unique offered ShotKey', 'minimum': 1},
                 acceptedShots={'countingUnit': 'unique accepted ShotKey', 'minimum': 1})
    report = new_manifest('G0', units)
    for metric in report['metrics'].values():
        metric['expected'] = 1
    report.update(evidenceFiles=[], exportErrors=[], checks={'campaignIdentity': 'NOT_RUN',
                  'oldCampaign': 'NOT_RUN'}, exclusions=[
        'No offered/accepted claim instrumentation; carriers and shots are not claims.',
        'Pickup, grant, freeze start/end, touch sample pair, alt contact, measured packet loss and death event instrumentation unavailable.',
        'Legacy campaign PASS does not establish G0 coverage or live hit/payment coverage.',
        'Replay facts count authoritative semantic identities, not replay seek hash parity.'
    ])

    def read(name):
        path = directory / name
        raw = path.read_bytes()
        report['evidenceFiles'].append({'path': name, 'sha256': hashlib.sha256(raw).hexdigest()})
        def invalid(value):
            raise ValueError('nonfinite JSON constant: ' + value)
        data = json.loads(raw, parse_constant=invalid)
        if not isinstance(data, dict):
            raise ValueError(name + ': expected object')
        return data

    def measured(name, value, unit, threshold=1):
        if value is not None and not count(value):
            raise ValueError(name + ': malformed counter')
        report['metrics'][name] = dict(countingUnit=unit, minimum=threshold,
                                       expected=threshold, observed=value,
                                       status='NOT_RUN' if value is None or (value == 0 and threshold) else 'PASS')

    try:
        manifest, summary, server = (read(name) for name in
                                     ('manifest.json', 'summary.json', 'server.json'))
        settings = manifest['settings']
        if not isinstance(settings, dict):
            raise ValueError('settings must be an object')
        report.update(sourceSha=manifest.get('source'), binarySha256=manifest.get('assemblySha256'),
                      config={'protocol': settings.get('protocol'), 'settings': settings,
                              'commands': manifest.get('commands'), 'toolchain': manifest.get('dotnetInfo')},
                      seed=settings.get('seed'), networkProfile=manifest.get('faultProfile') or
                      {'namedProfile': settings.get('profile')}, startedUtc=manifest.get('startedUtc'))
        source, binary = report['sourceSha'], report['binarySha256']
        identity = (isinstance(source, str) and re.fullmatch(r'[0-9a-fA-F]{40}|[0-9a-fA-F]{64}', source)
                    and isinstance(binary, str) and re.fullmatch(r'[0-9a-fA-F]{64}', binary)
                    and binary == manifest.get('assemblyAfterSha256')
                    and server.get('Version', '').endswith('+' + source)
                    and count(server.get('Protocol')) and server['Protocol'] == settings.get('protocol'))
        assembly = directory / 'bin' / 'ProjectPrime.dll'
        if assembly.exists():
            actual_hash = hashlib.sha256(assembly.read_bytes()).hexdigest()
            report['evidenceFiles'].append({'path': 'bin/ProjectPrime.dll', 'sha256': actual_hash})
            identity = identity and actual_hash == binary
        report['checks']['campaignIdentity'] = 'PASS' if identity else 'FAIL'
        report['provenance'] = 'fresh' if identity else None
        legacy = summary.get('status')
        report['checks']['oldCampaign'] = legacy if isinstance(legacy, str) and legacy in STATUSES else 'FAIL'
        report['oldCampaign'] = {'status': legacy, 'coverage': summary.get('coverage'),
                                 'failures': summary.get('failures'), 'incomplete': summary.get('incomplete')}
        if manifest.get('runnerError') or summary.get('failures'):
            report['checks']['oldCampaign'] = 'FAIL'
        processes = summary['processes']
        if not isinstance(processes, list) or not processes:
            raise ValueError('missing process list')
        names = [process['name'] for process in processes]
        if 'server' not in names or len(names) != len(set(names)):
            raise ValueError('missing server or duplicate process identities')
        players = settings.get('players')
        if not count(players) or players == 0 or len(processes) != players + 1 + bool(settings.get('soak')):
            raise ValueError('process count does not match configured players/server/reconnect')
        reports = {'server': server}
        for process in processes:
            name = process['name']
            if not isinstance(name, str) or re.fullmatch(r'(server|peer-([0-9]+|late|reconnect))', name) is None:
                raise ValueError('invalid process name')
            if process.get('exit') != 0 or process.get('timedOut') or process.get('unexpectedExit'):
                report['checks']['oldCampaign'] = 'FAIL'
            if name != 'server':
                reports[name] = read(name + '.json')
        shots = {'offered': set(), 'accepted': set()}
        peer_accepted = set()
        fields = ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')
        for process_name, process_report in reports.items():
            if (process_report.get('Version') != server.get('Version') or
                    process_report.get('Protocol') != server.get('Protocol') or
                    any(not count(process_report.get(field)) or process_report[field] != 0
                        for field in ('Overflow', 'SemanticDisconnects')) or
                    any(process_report.get(field) is not False
                        for field in ('ConnectionLost', 'SemanticOverrun')) or
                    'LastError' not in process_report or process_report['LastError'] is not None):
                report['checks']['campaignIdentity'] = 'FAIL'
                report['provenance'] = None
                raise ValueError(process_name + ': identity mismatch or invalid/absent overflow/error evidence')
            rows = process_report['Shots']
            if not isinstance(rows, list):
                raise ValueError('Shots must be a list')
            for row in rows:
                stage = row['Stage']
                key = tuple(row['Key'][field] for field in fields)
                if not all(count(value) for value in key):
                    raise ValueError('malformed ShotKey')
                if stage == 'accepted' and process_name != 'server':
                    peer_accepted.add(key)
                elif stage in shots:
                    shots[stage].add(key)
        if not peer_accepted <= shots['accepted']:
            raise ValueError('peer accepted ShotKey absent from authority acceptance')
        measured('offeredShots', len(shots['offered']), 'unique offered ShotKey across all process reports')
        measured('acceptedShots', len(shots['accepted']), 'unique authority accepted ShotKey')
        measured('hits', server.get('CombatPayments', {}).get('LifetimePayments'),
                 'committed shot component health/turret reductions; excludes separate continuous/bomb paths')
        measured('queueOccupancy', server.get('Transport', {}).get('QueueHighWater'),
                 'server transport queued packets high water', 0)
        facts = server['Facts']
        if not isinstance(facts, list):
            raise ValueError('Facts must be a list')
        unique = {}
        for fact in facts:
            if not isinstance(fact.get('Baseline'), bool):
                raise ValueError('missing/malformed fact Baseline flag')
            if fact['Baseline']:
                continue
            key = tuple(fact[field] for field in ('Epoch', 'Match', 'Award', 'Id'))
            if not all(count(value) and value > 0 for value in (key[0], key[1], key[3])) or not isinstance(key[2], bool):
                raise ValueError('malformed fact identity')
            payload = (fact['Type'], fact['Payload'])
            if key in unique and unique[key] != payload:
                raise ValueError('conflicting authoritative fact identity')
            unique[key] = payload
        measured('replayFacts', len(unique), 'unique authoritative (Epoch, Match, Award lane, Id) semantic facts')
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        report['exportErrors'].append(str(error))
        report['checks']['campaignExport'] = 'FAIL'
        report['provenance'] = None
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path, nargs='?')
    parser.add_argument('--campaign', type=Path)
    parser.add_argument('--source-sha')
    parser.add_argument('--binary-sha256')
    args = parser.parse_args()
    if (args.report is None) == (args.campaign is None):
        parser.error('provide either report or --campaign DIR')
    expected = {k: v for k, v in {'sourceSha': args.source_sha, 'binarySha256': args.binary_sha256}.items() if v is not None}
    try:
        def invalid_constant(value):
            raise ValueError(f'nonfinite JSON constant: {value}')
        report = (campaign_manifest(args.campaign) if args.campaign else
                  json.loads(args.report.read_text(encoding='utf-8'), parse_constant=invalid_constant))
        result = audit(report, expected)
    except (OSError, ValueError) as error:
        result = {'status': 'FAIL', 'failures': [str(error)]}
    output = {'manifest': report, 'audit': result} if args.campaign else result
    print(json.dumps(output, indent=2, allow_nan=False))
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
