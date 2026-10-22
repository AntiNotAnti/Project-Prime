"""Audit the existing claim outbox's process-lifetime conservation and keyed exits."""
from collections import Counter
from ncx_timeline import integer, row_value, timeline_schema

REASONS = ('Delivered', 'TimedOut', 'CapacityEvicted', 'LifeRetired',
           'OccupantRetired', 'MatchRetired', 'SessionRetired')


def audit(reports):
    processes, failures, incomplete = [], [], []
    for ordinal, (name, report) in enumerate(sorted(reports.items())):
        if name == 'server':
            continue
        label = 'client-' + str(ordinal)
        try:
            counts = report['ClaimReceiptAccounting']
            values = {key: integer(counts[key], 0, (1 << 63) - 1) for key in (*REASONS, 'Created', 'Outstanding')}
            if any(value < 0 for value in values.values()) or values['Outstanding'] > 128:
                raise ValueError('invalid receipt counters')
            declarations, retirements, reasons = Counter(), Counter(), Counter()
            timeline = report['ShotTimeline']
            timeline_schema(timeline['Schema'])
            if timeline['Overflow'] or timeline['UnkeyedRows'] or timeline['Complete'] is not True:
                incomplete.append(label + ': incomplete timeline storage')
            for row in timeline['Rows']:
                if row.get('Stage') not in ('claim-declared', 'receipt-retired'):
                    continue
                source = row_value(row)
                identity = (*source, *(row[field] for field in ('Component', 'Phase', 'Kind',
                    'Victim', 'VictimGeneration', 'VictimLife', 'ClaimId')))
                if not row['ClaimId']:
                    raise ValueError('receipt without claim identity')
                if row['Stage'] == 'claim-declared':
                    declarations[identity] += 1
                else:
                    reason = integer(row['Result'], 0, len(REASONS) - 1)
                    if not 0 <= reason < len(REASONS):
                        raise ValueError('unknown receipt retirement')
                    retirements[identity] += 1
                    reasons[REASONS[reason]] += 1
            if values['Created'] != sum(values[key] for key in REASONS) + values['Outstanding']:
                failures.append(label + ': process-lifetime receipt conservation mismatch')
            if any(count != 1 for count in declarations.values()) or any(count != 1 for count in retirements.values()):
                failures.append(label + ': duplicate declaration or retirement identity')
            if retirements.keys() - declarations.keys():
                failures.append(label + ': retirement has no exact declaration')
            if len(declarations) != values['Created'] or any(reasons[key] != values[key] for key in REASONS):
                incomplete.append(label + ': keyed rows do not cover lifetime counters')
            unresolved = len(declarations.keys() - retirements.keys())
            if unresolved != values['Outstanding']:
                incomplete.append(label + ': unresolved keyed declarations do not match outstanding receipts')
            if values['Outstanding']:
                incomplete.append(label + ': final report still has outstanding receipts')
            processes.append(dict(process=label, counts=values, keyedDeclarations=len(declarations),
                                  keyedRetirements=len(retirements), unresolved=unresolved))
        except (KeyError, TypeError, ValueError):
            incomplete.append(label + ': receipt accounting unavailable or malformed')
    if not processes:
        incomplete.append('no client receipt accounting')
    return dict(schema=1, status='FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
                processes=processes, failures=failures, incomplete=incomplete,
                scope='Client declaration/retirement conservation and exact source/component/phase/kind/victim/claim joins. Timeline schemas 1 and 2 share these validated core fields; optional Pose/Provenance are outside this audit. Delivered includes refusals; no legal-hit, paid damage, authority ordering or gameplay activity inferred. Raw source keys and player/account identifiers are not exported.')
