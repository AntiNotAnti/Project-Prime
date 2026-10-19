#!/usr/bin/env python3
"""Audit bounded NCX observations; no inferred socket, collision, or pixel events.

audit accepts {process_label: report} or an iterable of report objects. Process
labels only select server/client roles and never enter output. All timestamps
are process-local Stopwatch ticks. Frame fields are metadata, not clocks.
"""
import argparse
from collections import Counter, defaultdict
from fractions import Fraction
import hashlib
import json
from pathlib import Path


KEY_FIELDS = ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')
STAGES = ('source-authored', 'source-emitted', 'intent-offered', 'intent-received', 'source-admitted',
          'source-rejected', 'source-expired', 'claim-declared', 'claim-offered', 'claim-received',
          'payment-attempt', 'payment-committed', 'payment-refused', 'terminal-decided',
          'terminal-delivered', 'predicted-feedback', 'replay-captured', 'render-submitted', 'native-contact')
BRANCHES = {'source-rejected', 'source-expired', 'payment-refused', 'source-authored', 'native-contact', 'claim-offered'}
# Coverage targets, not the events that every legal shot must produce. A miss,
# rejected source, or native hit without a claim can correctly lack many stages.
REQUIRED = set(STAGES) - BRANCHES
FIELDS = {'Component': 32, 'Phase': 32, 'Kind': 8, 'Victim': 8,
          'VictimGeneration': 16, 'VictimLife': 16, 'ClaimId': 16,
          'DamageEventId': 32, 'SourceFrame': 32, 'AckFrame': 32,
          'AckSubFrame': 8, 'LocalFrame': 32, 'AuthorityFrame': 32, 'Weapon': 8}
LOCAL_PAIRS = (('source-emitted', 'predicted-feedback'),
               ('source-emitted', 'intent-offered'),
               ('intent-received', 'source-admitted'),
               ('claim-received', 'terminal-decided'),
               ('claim-declared', 'terminal-delivered'),
               ('source-emitted', 'render-submitted'))


def integer(value, low, high):
    if type(value) is not int or not low <= value <= high:
        raise ValueError('integer outside schema range')
    return value


def identity(value):
    if not isinstance(value, dict):
        raise ValueError('missing full shot key')
    bounds = (64, 16, 3, 16, 16, 32)
    return tuple(integer(value[f], 0 if f == 'ShooterSlot' else 1,
                         (1 << bits) - 1) for f, bits in zip(KEY_FIELDS, bounds))


def token(key):
    # Versioned canonical ordered decimal tuple, preserving every lifecycle bit.
    canonical = 'ncx-shot-v1:' + ':'.join(str(v) for v in key)
    return hashlib.sha256(canonical.encode('ascii')).hexdigest()


def row_value(row):
    if not isinstance(row, dict) or not isinstance(row.get('Stage'), str) or not row['Stage']:
        raise ValueError('missing stage')
    key = identity(row['Key'])
    for field, bits in FIELDS.items():
        integer(row[field], 0, (1 << bits) - 1)
    integer(row['Timestamp'], 0, (1 << 63) - 1)
    for field in ('Value', 'Result'):
        integer(row[field], -(1 << 31), (1 << 31) - 1)
    if row['Stage'] == 'payment-committed' and (
            row['Victim'] >= 8 or not row['VictimGeneration'] or not row['VictimLife']
            or not (row['Component'] or row['Phase'])):
        raise ValueError('committed payment lacks component/phase or victim lifecycle')
    return key


def audit(reports):
    """Return privacy-safe observations and completeness, never gameplay parity."""
    items = list(reports.items()) if isinstance(reports, dict) else [
        (str(i), report) for i, report in enumerate(reports)]
    incomplete, processes, shots = [], [], defaultdict(list)
    for index, (name, report) in enumerate(items):
        process = 'process-' + str(index)
        role = 'server' if name in ('server', 'server.json') or (
            isinstance(report, dict) and str(report.get('Role', '')).lower() == 'server') else 'client'
        entry = dict(process=process, role=role, clockStatus='UNAVAILABLE', rows=0,
                     unknownStageRows=0)
        processes.append(entry)
        try:
            timeline = report['ShotTimeline']
            if type(timeline['Schema']) is not int or timeline['Schema'] != 1:
                raise ValueError('unsupported timeline schema')
            clock = timeline['Clock']
            if clock['Domain'] != 'ProcessStopwatch':
                raise ValueError('unsupported clock domain')
            frequency = integer(clock['Frequency'], 1, (1 << 63) - 1)
            overflow = integer(timeline['Overflow'], 0, (1 << 63) - 1)
            unkeyed = integer(timeline['UnkeyedRows'], 0, (1 << 63) - 1)
            if type(timeline['Complete']) is not bool:
                raise ValueError('missing producer completeness')
            if not isinstance(timeline['Rows'], list):
                raise ValueError('rows must be an array')
            entry.update(clockStatus='AVAILABLE', frequency=frequency, overflow=overflow,
                         unkeyedRows=unkeyed, complete=timeline['Complete'])
            if overflow:
                incomplete.append(process + ': timeline overflow')
            if unkeyed or not timeline['Complete']:
                incomplete.append(process + ': producer evidence incomplete/unkeyed')
            for ordinal, row in enumerate(timeline['Rows']):
                try:
                    key = row_value(row)
                    if row['Stage'] not in STAGES:
                        # Stage is the only free-text row field. Never export an
                        # untrusted label (or its row) into observations/keys.
                        entry['unknownStageRows'] += 1
                        incomplete.append(f'{process}: unrecognized stage at row {ordinal}')
                        continue
                    shots[key].append((process, role, frequency, row))
                    entry['rows'] += 1
                except (KeyError, TypeError, ValueError):
                    incomplete.append(f'{process}: invalid row {ordinal}')
        except (KeyError, TypeError, ValueError):
            incomplete.append(process + ': missing/invalid ShotTimeline clock or schema')
    if not any(p['role'] == 'server' for p in processes):
        incomplete.append('authority process report unavailable')
    if not any(p['role'] == 'client' for p in processes):
        incomplete.append('client process report unavailable')
    if not shots:
        incomplete.append('no valid shot observations')
    cases, all_stages, clock_bounds = [], Counter(), defaultdict(lambda: [[], []])
    totals = Counter()
    for key, observations in sorted(shots.items()):
        shot_token = token(key)
        stages = Counter(o[3]['Stage'] for o in observations)
        all_stages.update(stages)
        missing = sorted(REQUIRED - stages.keys())
        if missing:
            incomplete.append(shot_token + ': observation coverage absent for ' + ','.join(missing))
        claims = Counter((p, r['ClaimId']) for p, _, _, r in observations
                         if r['Stage'] == 'claim-received' and r['ClaimId'])
        claim_offers = Counter((p, r['ClaimId']) for p, _, _, r in observations
                               if r['Stage'] == 'claim-offered' and r['ClaimId'])
        payments = Counter((r['Component'], r['Phase'], r['Kind'], r['Victim'],
                            r['VictimGeneration'], r['VictimLife'])
                           for _, role, _, r in observations
                           if role == 'server' and r['Stage'] == 'payment-committed')
        terminals = {(p, r['ClaimId'], r['Victim'], r['VictimGeneration'], r['VictimLife'])
                     for p, _, _, r in observations
                     if r['Stage'] == 'terminal-delivered' and r['ClaimId']}
        duplicate_payments = sum(count - 1 for count in payments.values())
        if duplicate_payments:
            incomplete.append(shot_token + ': duplicate committed payment observations')
        latencies = []
        for process in sorted({o[0] for o in observations}):
            own = [o for o in observations if o[0] == process]
            for start, end in LOCAL_PAIRS:
                first = [o for o in own if o[3]['Stage'] == start]
                last = [o for o in own if o[3]['Stage'] == end]
                for begin in first:
                    # Receipt durations require a nonzero exact claim identity.
                    candidates = [o for o in last if start not in ('claim-received', 'claim-declared')
                                  or begin[3]['ClaimId'] != 0 and o[3]['ClaimId'] == begin[3]['ClaimId']]
                    same_starts = [o for o in first if start not in ('claim-received', 'claim-declared')
                                   or o[3]['ClaimId'] == begin[3]['ClaimId']]
                    if len(same_starts) != 1 or len(candidates) != 1:
                        continue  # Do not guess which repeated event is the start.
                    elapsed = candidates[0][3]['Timestamp'] - begin[3]['Timestamp']
                    if elapsed < 0:
                        incomplete.append(shot_token + ': inconsistent local timestamp ordering')
                        continue
                    latencies.append(dict(process=process, start=start, end=end,
                                          milliseconds=float(Fraction(elapsed * 1000, begin[2]))))
        # For offset O = server_clock - client_clock, causal nonnegative transit
        # gives O <= received - offered and O >= decided - delivered.
        offers = [o for o in observations if o[1] == 'client' and o[3]['Stage'] == 'intent-offered']
        receives = [o for o in observations if o[1] == 'server' and o[3]['Stage'] == 'intent-received']
        decisions = [o for o in observations if o[1] == 'server' and o[3]['Stage'] == 'terminal-decided']
        deliveries = [o for o in observations if o[1] == 'client' and o[3]['Stage'] == 'terminal-delivered']
        for client in sorted({o[0] for o in offers + deliveries}):
            for server in sorted({o[0] for o in receives + decisions}):
                bounds = clock_bounds[(client, server)]
                sent = [o for o in offers if o[0] == client]
                got = [o for o in receives if o[0] == server]
                # The first received carrier followed some offer, at or after
                # the first offer. This bound stays causal when earlier offers
                # were lost; it does not identify which packet was delivered.
                if sent and got:
                    offered = min(sent, key=lambda o: o[3]['Timestamp'])
                    received = min(got, key=lambda o: o[3]['Timestamp'])
                    bounds[1].append(Fraction(received[3]['Timestamp'], received[2])
                                     - Fraction(offered[3]['Timestamp'], offered[2]))
                for claim_id in sorted({o[3]['ClaimId'] for o in deliveries
                                        if o[0] == client and o[3]['ClaimId']}):
                    matched = [o for o in decisions if o[0] == server
                               and o[3]['ClaimId'] == claim_id]
                    repeated = [o for o in deliveries if o[0] == client
                                and o[3]['ClaimId'] == claim_id]
                    # The first delivery follows a decision at or after the
                    # earliest decision. No matching of retry indices is needed.
                    if matched and repeated:
                        decided = min(matched, key=lambda o: o[3]['Timestamp'])
                        delivered = min(repeated, key=lambda o: o[3]['Timestamp'])
                        bounds[0].append(Fraction(decided[3]['Timestamp'], decided[2])
                                         - Fraction(delivered[3]['Timestamp'], delivered[2]))
        metrics = dict(claimReceivedRows=stages['claim-received'], uniqueReceivedClaims=len(claims),
                       claimRetryObservations=sum(n - 1 for n in claims.values()),
                       claimOfferedRows=stages['claim-offered'], uniqueOfferedClaims=len(claim_offers),
                       claimResendOffers=sum(n - 1 for n in claim_offers.values()),
                       uniqueCommittedPayments=len(payments), committedPaymentRows=sum(payments.values()),
                       deliveredTerminals=len(terminals))
        totals.update(metrics)
        cases.append(dict(token=shot_token, stages=dict(stages), missingStages=missing,
                          observationCoverage='INCOMPLETE' if missing else 'COMPLETE',
                          paymentEvidence='OBSERVED' if payments else 'UNAVAILABLE',
                          metrics=metrics, localLatencies=latencies,
                          observations=[dict(process=p, **{f: r[f] for f in
                              (*FIELDS, 'Stage', 'Timestamp', 'Value', 'Result')})
                              for p, _, _, r in observations]))
    intervals = []
    for (client, server), (low, high) in sorted(clock_bounds.items()):
        status = 'AVAILABLE' if low and high else 'UNAVAILABLE'
        if low and high and max(low) > min(high):
            status = 'INCONSISTENT'
            incomplete.append(client + '/' + server + ': inconsistent causal clock bounds')
        interval = dict(client=client, server=server, status=status,
                        lowerPairs=len(low), upperPairs=len(high))
        if status == 'AVAILABLE':
            interval.update(lowerSeconds=float(max(low)), upperSeconds=float(min(high)),
                            uncertaintySeconds=float(min(high) - max(low)))
        intervals.append(interval)
    return dict(schema=1, status='INCOMPLETE' if incomplete else 'COMPLETE',
                scope=dict(intentOffered='transport scheduling only; not observed socket send',
                           actualPixels='UNAVAILABLE',
                           collisionObservation='AVAILABLE' if all_stages['native-contact'] else 'UNAVAILABLE',
                           nativeContact='observed contact before payment; immunity may prevent a commit',
                           sourceAuthored='provisional Begin; may abort before emission',
                           claimRetries='repeated claim-received observations; not inferred socket transmissions',
                           claimOffered='transport scheduling of a claim attempt; repeated same-claim offers distinguish resend offers, not observed socket sends',
                           completeness='missingStages describes per-shot observation coverage only; valid misses/rejections can be INCOMPLETE without a gameplay bug',
                           paymentAbsence='admitted/emitted sources alone do not prove payment; absent hit/payment events do not establish a false payment or legal-hit count',
                           legalHitDenominator='UNAVAILABLE',
                           unknownStages='rows with unrecognized stage labels are discarded; generic counts only',
                           renderSubmitted='submission only; not proof of visible pixels',
                           clockOffsets='optional causal bounds on server minus client clock; constant offset and rate assumed within capture; interval width is uncertainty, not a point one-way estimate; earliest events bound causality without matching retry packets',
                           frameMetadata='source/ACK/local/authority frames and ACK fraction are metadata; frame wrap and clock origin are not elapsed time',
                           latency='same-process Stopwatch only; no superiority claim'),
                processes=processes, counts=dict(uniqueSourceKeys=sum(any(o[3]['Stage'] == 'source-emitted'
                    for o in rows) for rows in shots.values()), uniqueObservedShotKeys=len(shots),
                    unknownStageRows=sum(p['unknownStageRows'] for p in processes),
                    **totals), stages=dict(all_stages),
                stageCoverage={stage: dict(status='OBSERVED' if all_stages[stage] else 'UNAVAILABLE',
                                           required=stage in REQUIRED) for stage in STAGES}, shots=cases,
                causalClockOffsetIntervals=intervals, incomplete=incomplete)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('campaign', type=Path)
    args = parser.parse_args(argv)
    try:
        manifest = json.loads((args.campaign / 'manifest.json').read_text())
        if not isinstance(manifest, dict):
            raise ValueError('manifest must be an object')
        labels = {'server'} | {p.stem for p in args.campaign.glob('client*.json') if '.' not in p.stem}
        # run-net2.py currently uses peer-N labels in manifest.commands; other
        # campaign callers use client-N. Resolve only sibling report filenames.
        commands = manifest.get('commands', [])
        if not isinstance(commands, list):
            raise ValueError('manifest commands must be an array')
        for command in commands:
            name = command.get('name') if isinstance(command, dict) else None
            if isinstance(name, str) and name.startswith(('peer-', 'client-')) and Path(name).name == name:
                labels.add(name)
        paths = [args.campaign / 'server.json', *(args.campaign / (name + '.json') for name in sorted(labels - {'server'}))]
        reports, issues = {}, []
        for index, path in enumerate(paths):
            try:
                reports['server' if index == 0 else 'client-' + str(index)] = json.loads(path.read_text())
            except (OSError, ValueError):
                issues.append(('server' if index == 0 else 'client-' + str(index)) + ': unreadable report')
        result = audit(reports)
        if issues:
            result['incomplete'].extend(issues)
            result['status'] = 'INCOMPLETE'
        result['manifest'] = 'present; content not copied'
        with (args.campaign / 'ncx-timeline.json').open('x') as output:
            json.dump(result, output, indent=2, allow_nan=False)
            output.write('\n')
    except (OSError, ValueError) as error:
        parser.exit(2, 'ncx timeline export failed: ' + str(error) + '\n')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
