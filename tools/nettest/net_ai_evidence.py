"""Strict G1 real-client activity gates, in addition to existing N0 parity checks."""
import json
import math
from pathlib import Path


def audit_campaign(directory, processes, legacy):
    directory = Path(directory)
    failures, incomplete, clients = [], [], []
    metrics = {}
    def count(value):
        if type(value) is not int or value < 0:
            raise ValueError('missing or invalid nonnegative counter')
        return value
    def read(name):
        def invalid(value):
            raise ValueError('nonfinite JSON constant: ' + value)
        return json.loads((directory / name).read_text(), parse_constant=invalid)
    def bounded(value, low, high, label):
        if type(value) is not int or not low <= value <= high:
            raise ValueError('invalid ' + label)
        return value
    def shot_key(row):
        key = row['Key']
        fields = ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')
        bounds = ((1, 2**64-1), (1, 65535), (0, 7), (1, 65535), (1, 65535), (1, 2**32-1))
        return tuple(bounded(key[field], low, high, field)
                     for field, (low, high) in zip(fields, bounds))
    try:
        settings = read('manifest.json')['settings']
        level = bounded(settings['ai_net_client'], 0, 3, 'configured AI level')
        expected = settings['players']
        if type(expected) is not int or expected not in (2, 3, 4, 8):
            raise ValueError('invalid configured players')
        seed = bounded(settings['ai_seed'], 1, 2**32-expected, 'configured AI seed')
        if type(settings['server_bot']) is not bool:
            raise ValueError('invalid server bot setting')
        if settings['server_bot'] and expected >= 8:
            raise ValueError('server bot exceeds slot capacity')
        if sum(p['name'] == 'server' for p in processes) != 1:
            raise ValueError('missing or duplicate authority process')
        peers = [p for p in processes if p['name'] != 'server']
        if len(peers) != expected or len({p['name'] for p in peers}) != expected:
            raise ValueError('missing or duplicate independent client processes')
        server = read('server.json')
        if server['Role'] != 'server':
            raise ValueError('authority report has forged role')
        masks = [bounded(window['BotMask'], 0, 255, 'roster bot mask') for window in server['Windows']]
        if not masks:
            incomplete.append('no authority roster window samples')
        bot_slots = {slot for slot in range(8) if any(mask & (1 << slot) for mask in masks)}
        slots = set()
        offered_claims = pickups = 0
        for index, process in enumerate(peers):
            report = read(process['name'] + '.json')
            if report['Role'] not in ('simulation-client', 'rendered-client'):
                raise ValueError('client report has forged role')
            ai = report['AiClient']
            slot = count(ai['LocalSlot'])
            if slot >= 8 or slot in slots:
                raise ValueError('AI client slot missing, reused or out of range')
            slots.add(slot)
            if ai['Enabled'] is not True or ai['IsBot'] is not False or ai['RosterBot'] is not False:
                failures.append(process['name'] + ': local AI acquired bot identity or was disabled')
            if (bounded(ai['Level'], 0, 3, 'AI level') != level or
                    bounded(ai['Seed'], 1, 2**32-1, 'AI seed') != seed + index):
                failures.append(process['name'] + ': AI configuration/seed mismatch')
            travel = ai['Travel']
            if type(travel) not in (int, float) or not math.isfinite(travel) or travel < 0:
                raise ValueError('invalid AI travel')
            if count(ai['InputFrames']) < 60 or count(ai['DecisionFrames']) < 60 or travel < 2:
                incomplete.append(process['name'] + ': AI idle or insufficient native movement/decisions')
            shot_rows = [(row['Stage'], shot_key(row)) for row in report['Shots']]
            shots = {key for stage, key in shot_rows if stage == 'offered' and key[2] == slot}
            if not shots:
                incomplete.append(process['name'] + ': no real offered shots')
            claims = count(report['HitClaims']['Declared'])
            offered_claims += claims
            native = [row for row in report['Pickups'] if row['Stage'] == 'owner-native-consumption']
            for row in native:
                bounded(row['Epoch'], 1, 2**64-1, 'pickup epoch')
                bounded(row['Match'], 1, 65535, 'pickup match')
                bounded(row['Generation'], 1, 65535, 'pickup generation')
                bounded(row['Life'], 1, 65535, 'pickup life')
                owner = bounded(row['Slot'], 0, 7, 'pickup slot')
                if any(type(row[axis]) not in (int, float) or not math.isfinite(row[axis]) for axis in ('X', 'Y', 'Z')):
                    raise ValueError('native pickup has nonfinite position')
                if owner != slot or row['IsBot'] is not False:
                    raise ValueError('native pickup has wrong owner/life/role')
            pickups += len(native)
            spawned_lives, alt_frames = count(ai['SpawnedLives']), count(ai['AltFrames'])
            lifecycle = {
                'spawnedLives': {'observed': spawned_lives, 'status': 'OBSERVED' if spawned_lives else 'NOT_RUN',
                                 'countingUnit': 'AI decision observations of a changed LifeId, including the initial life'},
                'altForms': {'observed': alt_frames, 'status': 'OBSERVED' if alt_frames else 'NOT_RUN',
                             'countingUnit': 'AI decision frames where the local player is in an alternative form'},
                'deathEvents': {'observed': None, 'status': 'NOT_RUN',
                                'reason': 'Sampled States Hp/Deaths are snapshots, not an exact discrete death event ledger.'},
                'respawnEvents': {'observed': None, 'status': 'NOT_RUN',
                                  'reason': 'SpawnedLives includes initial life and observed LifeId changes; it does not distinguish respawns from other lifecycle changes.'},
                'altTransitions': {'observed': None, 'status': 'NOT_RUN',
                                   'reason': 'AltFrames measures form activity frames, not distinct enter/exit transitions.'}
            }
            clients.append({'name': process['name'], 'slot': slot, 'inputFrames': ai['InputFrames'],
                            'decisionFrames': ai['DecisionFrames'], 'travel': travel,
                            'offeredShots': len(shots), 'declaredClaims': claims,
                            'nativePickupConsumptions': len(native), 'spawnedLives': spawned_lives,
                            'altFrames': alt_frames, 'weapons': ai['Weapons'], 'lifecycleEvidence': lifecycle})
        metrics = {'declaredClientClaims': offered_claims,
                   'authorityClaimReceiptsIncludingRetries': count(server['HitClaims']['Received']),
                   'authorityAppliedClaims': count(server['HitClaims']['AppliedHere']),
                   'componentPayments': count(server['CombatPayments']['LifetimePayments']),
                   'ownerNativePickupConsumptions': pickups}
        for name in ('declaredClientClaims', 'authorityClaimReceiptsIncludingRetries',
                     'componentPayments', 'ownerNativePickupConsumptions'):
            if metrics[name] == 0:
                incomplete.append(name + ': no observed events')
        client_mask = sum(1 << slot for slot in slots)
        if any(mask & client_mask for mask in masks):
            failures.append('authority roster registered an AI client as a bot')
        authority_shots = [(row['Stage'], shot_key(row)) for row in server['Shots']]
        if any(key[2] not in slots | bot_slots for _, key in authority_shots):
            raise ValueError('authority shot has unknown roster slot')
        bot_shots = {key for stage, key in authority_shots if stage in ('accepted', 'committed') and key[2] in bot_slots}
        metrics['serverBotSlots'] = sorted(bot_slots)
        metrics['serverBotShotKeys'] = len(bot_shots)
        if settings['server_bot'] and (len(bot_slots) != 1 or not bot_shots):
            incomplete.append('requested server bot has no independent identity and real shot activity')
        if not settings['server_bot'] and bot_slots:
            failures.append('unexpected authority bots')
        if legacy['status'] != 'PASS':
            failures.append('existing cross-process network parity/drain checks did not pass')
    except (OSError, ValueError, KeyError, TypeError, AttributeError) as error:
        failures.append('malformed or missing AI evidence: ' + str(error))
    return {'status': 'FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
            'failures': failures, 'incomplete': incomplete, 'clients': clients, 'metrics': metrics,
            'scope': 'Each campaign is one bounded activity phase; native pickup consumption is not an admitted grant. '
                     'Claim receipts include retries. Component payments exclude bomb/continuous paths. '
                     'Lifecycle evidence is descriptive and does not add an activity gate; OBSERVED is not lifecycle parity PASS. '
                     'Seeded gameplay RNG does not make UDP scheduling deterministic.'}
