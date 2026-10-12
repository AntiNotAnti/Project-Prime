"""Strict G1 real-client activity gates, in addition to existing N0 parity checks."""
import json
import math
import struct
from pathlib import Path


def canonical_lifecycle(rows):
    """Decode exact protocol35 semantic facts; count events, never retries/awards.

    Respawns require an earlier canonical death and a later same-generation spawn
    with a newer life. Unjoined PlayerSpawned facts remain separate spawn events.
    """
    if not isinstance(rows, list):
        raise ValueError('Facts must be a list')
    unique = {}
    for row in rows:
        if row['Type'] == 56:
            continue
        if row['Type'] != 55 or row['Award'] is not False:
            raise ValueError('invalid semantic fact lane')
        raw = bytes.fromhex(row['Payload'])
        if len(raw) != 48:
            raise ValueError('invalid semantic fact packet length')
        (packet, match, epoch, event, tick, kind, actor, agen, alife,
         target, tgen, tlife, weapon, flags, entity, value, baseline,
         frontier) = struct.unpack('<BHQIIBBHHBHHiBiiBI', raw)
        for field, observed in (('Epoch', epoch), ('Match', match), ('Id', event), ('Tick', tick)):
            if type(row[field]) is not int or row[field] != observed:
                raise ValueError('semantic outer/header identity mismatch')
        if (packet != 55 or not epoch or not match or not 0 <= kind <= 17 or
                not -1 <= weapon <= 255 or flags > 127 or entity < -1 or baseline > 1 or
                type(row['Baseline']) is not bool or row['Baseline'] != bool(baseline) or
                type(row['AwardFrontier']) is not int or row['AwardFrontier'] != frontier):
            raise ValueError('invalid semantic packet fields')
        actor_id, target_id = (actor, agen, alife), (target, tgen, tlife)
        none = (255, 0, 0)
        player = lambda identity: identity[0] < 8 and identity[1] > 0 and identity[2] > 0
        if not all(player(identity) or identity == none for identity in (actor_id, target_id)):
            raise ValueError('invalid semantic actor identity')
        if baseline:
            if kind != 0 or actor_id != none or target_id != none or weapon != -1 or flags or entity != -1 or value:
                raise ValueError('invalid semantic baseline')
            continue
        if not event or frontier:
            raise ValueError('invalid semantic event identity/frontier')
        if (kind in (3, 7, 9, 10, 11, 12, 14) and not (player(actor_id) and target_id == none) or
                kind == 4 and not (player(target_id) and actor_id != target_id and (player(actor_id) or flags & 4)) or
                kind == 6 and not (player(actor_id) and actor_id == target_id) or
                kind in (5, 8) and not (player(actor_id) and player(target_id))):
            raise ValueError('invalid semantic event actor/target roles')
        key = (epoch, match, event)
        if key in unique and unique[key][0] != raw:
            raise ValueError('conflicting canonical semantic event identity')
        unique[key] = (raw, tick, kind, actor_id, target_id)
    deaths, spawns, respawns, pending, dead_lives = [], [], [], {}, set()
    for (epoch, match, event), (_, tick, kind, actor, target) in sorted(unique.items()):
        if kind in (4, 6):
            victim = (epoch, match, *target)
            if victim in dead_lives:
                raise ValueError('duplicate canonical death for one victim life')
            dead_lives.add(victim)
            deaths.append(victim)
            pending[victim[:-1]] = (target[2], tick, event)
        elif kind == 3:
            spawned = (epoch, match, *actor)
            spawns.append(spawned)
            previous = pending.get(spawned[:-1])
            newer_life = previous and 0 < ((actor[2] - previous[0]) & 65535) < 32768
            later_tick = previous and (0 < ((tick - previous[1]) & 0xffffffff) < 0x80000000 or
                                       tick == previous[1] and event > previous[2])
            if newer_life and later_tick:
                respawns.append(spawned)
                del pending[spawned[:-1]]
    return {'semanticEvents': len(unique), 'deaths': deaths, 'spawns': spawns, 'respawns': respawns}


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
        canonical = canonical_lifecycle(server['Facts']) if 'Facts' in server else None
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
            invalid_positions = count(ai['InvalidPositionSamples'])
            invalid_distances = count(ai['InvalidDistanceSamples'])
            if invalid_positions or invalid_distances:
                failures.append(process['name'] + ': native AI observed invalid spatial samples')
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
            clients[-1].update(invalidPositionSamples=invalid_positions,
                               invalidDistanceSamples=invalid_distances)
        metrics = {'declaredClientClaims': offered_claims,
                   'authorityClaimReceiptsIncludingRetries': count(server['CampaignClaims']['Received']),
                   'authorityAppliedClaims': count(server['CampaignClaims']['Applied']),
                   'componentPayments': count(server['CombatPayments']['LifetimePayments']),
                   'ownerNativePickupConsumptions': pickups}
        paid = server['CombatPayments'].get('PaidShotKeys')
        if 'PaidShotKeys' in server['CombatPayments']:
            if not isinstance(paid, list):
                raise ValueError('PaidShotKeys must be a list')
            paid_keys = {shot_key({'Key': key}) for key in paid}
            if any(key[2] not in slots | bot_slots for key in paid_keys):
                raise ValueError('paid shot has unknown roster slot')
            if count(server['CombatPayments']['EvidenceOverflow']):
                raise ValueError('paid shot evidence overflow')
        else:
            paid_keys = None
        metrics['paidAiSourceShotKeys'] = None if paid_keys is None else sum(key[2] in slots for key in paid_keys)
        if not metrics['paidAiSourceShotKeys']:
            incomplete.append('paidAiSourceShotKeys: missing evidence or no AI-origin paid source shots')
        metrics['canonicalLifecycle'] = {
            name: {'observed': None if canonical is None else len(canonical[source]),
                   'status': 'OBSERVED' if canonical and canonical[source] else 'NOT_RUN'}
            for name, source in (('deathEvents', 'deaths'), ('spawnEvents', 'spawns'), ('respawnEvents', 'respawns'))}
        for client in clients:
            slot = client['slot']
            client['paidSourceShotKeys'] = None if paid_keys is None else sum(key[2] == slot for key in paid_keys)
            for name, source in (('deathEvents', 'deaths'), ('respawnEvents', 'respawns')):
                observed = None if canonical is None else sum(identity[2] == slot for identity in canonical[source])
                client['lifecycleEvidence'][name] = {
                    'observed': observed, 'status': 'OBSERVED' if observed else 'NOT_RUN',
                    'countingUnit': 'unique canonical death fact' if name == 'deathEvents' else
                                    'canonical spawn joined to preceding same-generation death with newer life'}
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
                     'Paid AI source shot keys count distinct source shots, not component payments; canonical respawns require a joined preceding death. '
                     'Lifecycle evidence is descriptive and does not add an activity gate; OBSERVED is not lifecycle parity PASS. '
                     'Seeded gameplay RNG does not make UDP scheduling deterministic.'}
