"""Independent audit of exported NCX03 shadow rows, not a source/legal-hit proof.

Only retained paid-receipt parity and native-ordered contact parity are assessed.
Every source and collision fact remains supplied by the instrumented authority.
"""
from collections import Counter
import hashlib
import json
import math
import struct
from ncx_timeline import identity, integer

STAGES = ('ClaimArrival', 'ClaimReady', 'NativePreDamage')
STATES = ('LegacyRequired', 'PendingDependencies', 'VerifiedPaid', 'VerifiedNative', 'Reject')
REASONS = ('FamilyUnsupported', 'MissingSource', 'SourceUnavailable', 'GeometryUnavailable',
           'HeadMismatch', 'WallMismatch', 'OrderingDeferred', 'ReservationPending',
           'PaidDuplicate', 'NativeDamage', 'NativeBlocked', 'DamageUnavailable', 'InvalidIdentity')
DAMAGE_STATES = ('Unavailable', 'Available', 'Blocked')
DAMAGE_REASONS = ('Unknown', 'Incomplete', 'InvalidLife', 'LifeMismatch', 'CapturePhase',
                 'FrameMismatch', 'InactiveOrDead', 'UnsupportedPaymentFamily', 'UnsupportedContext',
                 'ContextMismatch', 'InvalidTeam', 'ProfileMismatch', 'InvalidProfile',
                 'ChannelOutOfRange', 'SpawnProtected', 'MatchSpawnProtected', 'CameraBlocked',
                 'FriendlyFireBlocked', 'ZeroChannel', 'ZeroEffectiveness', 'NativeDamage')
ACK_RESULTS = ('Applied', 'AlreadyResolved', 'RejectedDeadShooter', 'RejectedDeadVictim',
               'RejectedPlausibility', 'RejectedTooOld', 'RejectedLifecycle', 'RejectedGeometry',
               'RejectedDamage', 'RejectedLaunch', 'RejectedNoDamage', 'RejectedImpulse', 'ClaimCapacity', 'Corrected')
ACK_FLAGS = {'None': 0, 'Headshot': 1, 'Lethal': 2, 'Frozen': 4, 'Burning': 8,
             'Disrupted': 16, 'HalfturretAffected': 32, 'OutcomePresent': 64}
COUNTERS = ('fastEligible', 'fastWouldPay', 'fastWouldReject', 'missingSource', 'headMismatch',
            'wallMismatch', 'orderingDeferred', 'familyUnsupported', 'wouldDoublePay',
            'wouldAlterTrade', 'Compared', 'Disagreements', 'Uncompared', 'CorrectedExcluded')
U32 = (1 << 32) - 1


def enum(value, names):
    if type(value) is int:
        return names[integer(value, 0, len(names) - 1)]
    if type(value) is str and value in names:
        return value
    raise ValueError('unknown enum')


def boolean(value):
    if type(value) is not bool:
        raise ValueError('invalid boolean')
    return value


def flags(value):
    if type(value) is int:
        return integer(value, 0, 127)
    if type(value) is str:
        parts = value.split(', ')
        if parts and all(part in ACK_FLAGS for part in parts) and len(set(parts)) == len(parts):
            result = 0
            for part in parts:
                result |= ACK_FLAGS[part]
            return result
    raise ValueError('unknown flags')


def vector(value):
    values = []
    for field in ('X', 'Y', 'Z'):
        number = value[field]
        if type(number) not in (int, float) or not math.isfinite(number) or abs(number) > 3.4028234663852886e38:
            raise ValueError('invalid vector')
        # Runtime identity uses Vector3 float equality; normalize signed zero.
        values.append((struct.unpack('!f', struct.pack('!f', number))[0] + 0.0).hex())
    return tuple(values)


def payment(value):
    shot = identity(value['Shot'])
    tail = tuple(integer(value[name], low, high) for name, low, high in (
        ('Victim', 0, 7), ('Generation', 1, 65535), ('Life', 1, 65535),
        ('Component', 1, U32), ('Kind', 1, 6), ('Phase', 0, U32)))
    if tail[4] not in (1, 2, 5, 6):
        raise ValueError('invalid payment kind')
    return (*shot, *tail)


def shape(value):
    result = tuple(integer(value[name], 0, high) for name, high in (
        ('Id', 65535), ('Weapon', 255), ('Flags', 255), ('Damage', 65535),
        ('Frame', U32), ('AckFrame', U32), ('LaunchFrame', U32)))
    return (*result, vector(value['Point']), vector(value['Direction']))


def outcome(value):
    result = {field: integer(value[field], 0, high) for field, high in (
        ('ShotId', U32), ('ClaimId', 65535), ('VictimSlot', 255), ('VictimGeneration', 65535),
        ('VictimLife', 65535), ('DamageApplied', 65535), ('HealthAfter', 65535),
        ('HalfturretHealthAfter', 65535), ('DamageSequence', U32))}
    result['Result'] = enum(value['Result'], ACK_RESULTS)
    result['Flags'] = flags(value['Flags'])
    return result


def life(value):
    return (*identity(dict(value, ShotId=1))[:-1], integer(value['ShotId'], 0, 0))


def number(value):
    if type(value) not in (int, float) or not math.isfinite(value) or abs(value) > 3.4028234663852886e38:
        raise ValueError('invalid float')
    return struct.unpack('!f', struct.pack('!f', value))[0]


def xyz(value):
    return tuple(number(value[field]) + 0.0 for field in ('X', 'Y', 'Z'))


def native_inputs(row, value):
    """Validate detached capture constraints and re-evaluate frozen arithmetic.

    Booleans cannot prove the native collision algorithm or resource history.
    No mutable world is reconstructed from a claimant endpoint here.
    """
    errors = []
    def require(condition, message):
        if not condition: errors.append(message)
    key = row['key']; source = value['Source']; fire = source['Fire']; template = source['Damage']
    room = integer(source['Room'], 1, U32); authority_frame = integer(source['AuthorityFrame'], 0, U32)
    require(((row['legal'] - authority_frame) & U32) <= 1024, 'native source certificate observation expired')
    impact = value['Impact']; profile = value['Profile']
    require(boolean(value['SourceVerified']) and boolean(value['GeometryVerified']), 'native source/geometry capture not verified')
    require(enum(value['PriorPayment'], ('Missing', 'Reserved', 'Paid')) == 'Missing', 'native prior payment is not missing')
    boolean(value['TransientEmission'])
    require(identity(source['Key']) == key[:6], 'native source key mismatch')
    enum(source['Lane'], ('NativeInput', 'AcceptedFire'))
    require(boolean(source['TrustedSource']), 'native source not trusted')
    proof = source['Source']
    # This sparse enum is material here only in its supported Verified value.
    require(proof['Source'] in (0, 'Verified') and type(proof['Source']) is not bool
            and proof['Pose'] in (0, 'Verified') and type(proof['Pose']) is not bool, 'native source/pose proof unavailable')
    boolean(proof['DoubleHintAgrees']) if proof['DoubleHintAgrees'] is not None else None
    integer(proof['AmmoType'], -1, 1); integer(proof['Cost'], 0, (1 << 31) - 1); integer(proof['Cooldown'], 0, (1 << 31) - 1)
    require(enum(source['Unavailable'], ('None', 'ObservationMissing', 'ObservationExpired', 'OwnershipUnavailable',
                  'GrantRecordMissing', 'FundingMismatch', 'Source', 'Unsupported')) == 'None', 'native source has unavailable evidence')
    knowledge = enum(source['Knowledge'], ('Unavailable', 'NotGranted', 'Granted'))
    require(knowledge != 'Unavailable' and boolean(source['DoubleHintAgrees']), 'native funding knowledge unavailable')
    authorized = boolean(source['AuthorizedDouble'])
    require(integer(source['Components'], 0, (1 << 31) - 1) == 1, 'native source component count unsupported')
    require(integer(fire['ShotId'], 1, U32) == key[5] and integer(fire['SourceLife'], 1, 65535) == key[4], 'native fire source identity mismatch')
    source_frame = integer(fire['SourceFrame'], 0, U32)
    integer(fire['AckFrame'], 0, U32); integer(fire['AckSubFrame'], 0, 255)
    require(enum(fire['Kind'], ('PressFire', 'ReleaseFire', 'AutomaticFire', 'ContinuousTick', 'TurretFire')) in
            ('PressFire', 'ReleaseFire', 'AutomaticFire'), 'native continuous/turret source unsupported')
    require(integer(fire['Weapon'], 0, 255) == row['weapon'] == 4, 'native fire weapon mismatch')
    integer(fire['Charge'], 0, 255)
    modifiers = integer(fire['ContinuousPhase'], 0, U32)
    require(not modifiers & (1 << 28), 'native enhanced source unsupported')
    require(bool(modifiers & (1 << 29)) == (knowledge == 'Granted'), 'native Double hint/funding contradiction')
    pose_flags = integer(fire['PoseFlags'], 0, 255)
    require(bool(pose_flags & 1), 'native authored pose unavailable')
    for field in ('Origin', 'Direction', 'Aim', 'View'): xyz(fire[field])
    for field in ('X', 'Y'): number(fire['Reticle'][field])
    funding = source['Funding']
    if knowledge == 'Granted':
        require(authorized and isinstance(funding, dict), 'native Double lacks immutable funding')
        if isinstance(funding, dict):
            integer(funding['Sequence'], 1, (1 << 64) - 1)
            require(life(funding['Life']) == (*key[:5], 0), 'native funding life mismatch')
            require(enum(funding['Type'], ('DoubleDamage', 'Cloak', 'DeathAlt')) == 'DoubleDamage', 'native funding type mismatch')
            spawn = funding['Spawn']; integer(spawn['Room'], 1, U32); integer(spawn['Generation'], 1, U32)
            integer(spawn['SpawnId'], -(1 << 31), (1 << 31) - 1)
            require(spawn['Room'] == room, 'native funding room mismatch')
            require(integer(spawn['Epoch'], 1, (1 << 64) - 1) == key[0] and integer(spawn['Match'], 1, 65535) == key[1], 'native funding stream mismatch')
            contact = integer(funding['ContactFrame'], 0, U32); expires = integer(funding['ExpiresAtFrame'], 0, U32)
            observed = integer(funding['ObservationAuthorityFrame'], 0, U32); contact_source = integer(funding['SourceContactFrame'], 0, U32)
            require(((authority_frame - observed) & U32) <= 1024, 'native grant observation expired')
            require(expires == ((contact + 1800) & U32) and 0 < ((observed - contact) & U32) < 1800
                    and ((source_frame - ((contact_source + 1) & U32)) & U32) < (1 << 31), 'native funding chronology mismatch')
    else:
        require(not authorized and funding is None, 'native unfunded source carries authorization')
    gates = []
    bool_fields = ('Complete', 'Active', 'Biped', 'Bot', 'SpawnProtected', 'MatchSpawnProtected', 'DamageInvulnerable',
                   'CameraBlocked', 'SuppressDamage', 'AimTrainer', 'ExistingStatus', 'Enhanced', 'Teams', 'FriendlyFire', 'InstaGib', 'OneInChamber')
    for field, expected_life in (('SourceGates', (*key[:5], 0)), ('VictimGates', (key[0], key[1], key[6], key[7], key[8], 0))):
        raw = value[field]; gate = {name: boolean(raw[name]) for name in bool_fields}
        gate.update(Health=integer(raw['Health'], 0, (1 << 31) - 1), Frame=integer(raw['Frame'], 0, U32),
                    Team=integer(raw['Team'], -1, 7), TeamCount=integer(raw['TeamCount'], 0, 8))
        mode = raw['Mode']; modes = {0: 'None', 2: 'SinglePlayer', 3: 'Battle', 4: 'BattleTeams', 5: 'Survival', 6: 'SurvivalTeams', 7: 'Capture', 8: 'Bounty', 9: 'BountyTeams', 10: 'Nodes', 11: 'NodesTeams', 12: 'Defender', 13: 'DefenderTeams', 14: 'PrimeHunter', 15: 'InstaGib', 16: 'Relic', 17: 'Hardpoint', 18: 'HardpointTeams', 19: 'GunGame', 20: 'KillConfirmed', 21: 'KillConfirmedTeams', 22: 'Headhunter', 23: 'OneInTheChamber'}
        if type(mode) is str and mode in modes.values(): gate['Mode'] = next(code for code, name in modes.items() if name == mode)
        elif type(mode) is int and mode in modes: gate['Mode'] = mode
        else: raise ValueError('unknown game mode')
        require(life(raw['Life']) == expected_life and enum(raw['Phase'], ('Unknown', 'NativePreDamage')) == 'NativePreDamage'
                and gate['Frame'] == row['legal'], 'native life/phase/legal-frame capture mismatch')
        require(gate['Complete'] and gate['Active'] and gate['Biped'] and gate['Health'] > 0
                and not any(gate[name] for name in ('Bot', 'SuppressDamage', 'AimTrainer', 'ExistingStatus', 'Enhanced', 'InstaGib', 'OneInChamber')),
                'native actor capture incomplete or unsupported')
        require((gate['Teams'] and gate['Mode'] == 4 and 2 <= gate['TeamCount'] <= 4 and 0 <= gate['Team'] < gate['TeamCount'])
                or (not gate['Teams'] and gate['Mode'] == 3 and gate['TeamCount'] in (0, 2, 3, 4)), 'native team/mode context invalid')
        gates.append(gate)
    shooter, victim = gates
    require(key[2] != key[6] and row['pre'] == victim['Health'], 'native victim/pre-health mismatch')
    require(all(shooter[field] == victim[field] for field in ('Mode', 'Teams', 'TeamCount', 'FriendlyFire')), 'native actor contexts differ')
    require(life(profile['Key']) == (key[0], key[1], key[6], key[7], key[8], 0), 'native profile life mismatch')
    require(boolean(profile['EffectStateKnown']) and integer(profile['EffectFrame'], 0, U32) == row['legal'], 'native effect profile stamp missing')
    require(not boolean(profile['Balanced']) and not boolean(profile['Transitioning']), 'native profile context unsupported')
    integer(profile['BalancedDisruptImmunity'], 0, 65535)
    enum(profile['Hunter'], ('Samus', 'Kanden', 'Trace', 'Sylux', 'Noxus', 'Spire', 'Weavel'))
    packed = integer(profile['PackedEffectiveness'], 0, (1 << 18) - 1)
    level = number(profile['DamageLevel']); handicap = integer(profile['Handicap'], 0, 50)
    require(level in (.75, 1, 1.25) and handicap % 10 == 0, 'native profile level/handicap invalid')
    require(payment(impact['Payment']) == key and integer(impact['Weapon'], 0, 255) == row['weapon']
            and integer(impact['Frame'], 0, U32) == row['legal'] and boolean(impact['Direct']) and boolean(impact['OrdinaryDirect']),
            'native impact identity/ordering capture mismatch')
    channel = integer(impact['NativeChannel'], 0, 16_777_216); head = boolean(impact['NativeHead'])
    for field in ('Point', 'Origin', 'SegmentStart', 'SegmentEnd'): xyz(impact[field])
    require(boolean(template['Present']) and boolean(template['FirstSegmentKnown'])
            and integer(template['FirstCollisionFrame'], 0, U32) == row['geometry']
            and not boolean(template['Balanced']) and integer(template['Effects'], 0, 255) == 0
            and integer(template['DirectOnlyEffects'], 0, 255) == 0, 'native emission template incomplete/unsupported')
    for field in ('Body', 'Head', 'Splash', 'Radius', 'Speed', 'Reach'):
        require(number(template[field]) >= 0, 'native emission template has negative material values')
    integer(template['Curve'], -(1 << 31), (1 << 31) - 1)
    origin = xyz(template['Origin']); velocity = xyz(template['NativeVelocity'])
    require(origin == xyz(impact['Origin']) == xyz(impact['SegmentStart'])
            and xyz(impact['SegmentEnd']) == tuple(number(a + b) + 0.0 for a, b in zip(origin, velocity))
            and sum(a * a for a in velocity) > .000001, 'native first-segment geometry stamp mismatch')
    selected = number(template['Head'] if head else template['Body'])
    require(0 <= channel <= max(0, int(selected)), 'native impact exceeds its emitted channel')
    if number(template['Reach']) <= 0: require(channel == max(0, int(selected)), 'native constant impact channel differs from emission')
    effectiveness = (packed >> 8) & 3
    blocked = 'SpawnProtected' if victim['SpawnProtected'] else 'MatchSpawnProtected' if victim['MatchSpawnProtected'] else \
        'CameraBlocked' if victim['CameraBlocked'] else 'FriendlyFireBlocked' if victim['Teams'] and victim['Team'] == shooter['Team'] and not shooter['FriendlyFire'] else \
        'ZeroChannel' if channel == 0 else 'ZeroEffectiveness' if effectiveness == 0 else None
    if blocked:
        require(row['damage'] == dict(status='Blocked', reason=blocked, nominal=0, debit=0, lethal=False, head=False), 'native block does not match frozen gates/profile')
    elif level in (.75, 1, 1.25) and handicap % 10 == 0:
        nominal = max(1, int(number(number(channel) * (0, .5, 1, 2)[effectiveness])))
        nominal = max(1, int(number(number(nominal) * level)))
        if handicap: nominal = max(1, nominal * (100 - handicap) // 100)
        require(row['damage'] == dict(status='Available', reason='NativeDamage', nominal=nominal,
                    debit=min(nominal, victim['Health']), lethal=nominal >= victim['Health'], head=head),
                'native frozen damage formula mismatch')
    return errors


def row_value(raw):
    key = payment(raw['Key']); request = shape(raw['Request'])
    stage = enum(raw['Stage'], STAGES); decision = raw['Decision']; damage = decision['Damage']
    return dict(key=key, shape=request, stage=stage, state=enum(decision['State'], STATES),
                reason=enum(decision['Reason'], REASONS),
                damage=dict(status=enum(damage['Status'], DAMAGE_STATES), reason=enum(damage['Reason'], DAMAGE_REASONS),
                            nominal=integer(damage['NominalDamage'], 0, U32), debit=integer(damage['HealthDebit'], 0, U32),
                            lethal=boolean(damage['Lethal']), head=boolean(damage['Head'])),
                paid=outcome(decision['Paid']), actual=outcome(raw['ActualOutcome']),
                weapon=integer(raw['Weapon'], 0, 255), legal=integer(raw['LegalFrame'], 0, U32),
                geometry=integer(raw['GeometryFrame'], 0, U32), attempts=integer(raw['Attempts'], 1, (1 << 31) - 1),
                compared=boolean(raw['Compared']), agrees=boolean(raw['Agrees']), corrected=boolean(raw['Corrected']),
                debit=integer(raw['ActualDebit'], 0, (1 << 31) - 1), health=integer(raw['ActualHealth'], 0, (1 << 31) - 1),
                pre=integer(raw['PreHealth'], 0, (1 << 31) - 1), payments=integer(raw['Payments'], 0, (1 << 31) - 1),
                terminals=integer(raw['Terminals'], 0, (1 << 31) - 1),
                terminal_disagreements=integer(raw['TerminalDisagreements'], 0, (1 << 31) - 1))


def token(row):
    value = (row['key'], row['stage'], row['shape'])
    return hashlib.sha256(('ncx-fastrow-v1:' + json.dumps(value, separators=(',', ':'))).encode('ascii')).hexdigest()


def keyed(ack, row):
    key = row['key']
    return (ack['ShotId'], ack['VictimSlot'], ack['VictimGeneration'], ack['VictimLife']) == (key[5], key[6], key[7], key[8])


def inspect(row):
    """Return contradictions and independently recomputed compared agreement."""
    errors = []; state = row['state']; reason = row['reason']; stage = row['stage']; damage = row['damage']
    eligible = state in ('VerifiedPaid', 'VerifiedNative'); pay = state == 'VerifiedNative' and damage['status'] == 'Available'
    reject = state == 'Reject'
    allowed = {
        'LegacyRequired': {'FamilyUnsupported', 'SourceUnavailable', 'GeometryUnavailable', 'HeadMismatch',
                           'WallMismatch', 'OrderingDeferred', 'ReservationPending', 'DamageUnavailable', 'InvalidIdentity'},
        'PendingDependencies': {'MissingSource', 'ReservationPending'}, 'VerifiedPaid': {'PaidDuplicate'},
        'VerifiedNative': {'NativeDamage'}, 'Reject': {'NativeBlocked'}}
    if reason not in allowed[state]: errors.append('decision state/reason contradiction')
    if stage == 'ClaimArrival' and (eligible or reject): errors.append('raw arrival cannot certify a decision')
    if stage != 'NativePreDamage' and state in ('VerifiedNative', 'Reject'): errors.append('native decision on a claim stage')
    if stage == 'NativePreDamage' and state == 'VerifiedPaid': errors.append('paid receipt on a native stage')
    if stage == 'NativePreDamage' and any(row['shape'][:7]): errors.append('native stage carries a claim request shape')
    if state in ('VerifiedNative', 'Reject') and (row['weapon'] != 4 or row['key'][9:12] != (1, 1, 0)):
        errors.append('native decision outside supported direct family')
    if row['terminal_disagreements'] > row['terminals']: errors.append('terminal disagreement count exceeds terminals')
    if stage != 'NativePreDamage':
        _, weapon, claim_flags, _, frame, _, _ = row['shape'][:7]
        kind = (1 if claim_flags & 128 else 2) | (4 if claim_flags & 32 else 0)
        if weapon != row['weapon'] or kind != row['key'][10] or (frame if claim_flags & 64 else 0) != row['key'][11]:
            errors.append('request shape/payment identity contradiction')
        if row['payments']: errors.append('claim observation contains a new native payment count')
    elif row['terminals'] or row['terminal_disagreements']:
        errors.append('native observation contains claim terminals')
    if state == 'VerifiedPaid':
        if stage != 'ClaimReady' or row['weapon'] not in (0, 4) or row['key'][9:12] != (1, 1, 0):
            errors.append('paid receipt outside supported direct family')
        if not row['shape'][0] or row['shape'][2] & (32 | 64): errors.append('invalid paid request shape')
        if bool(row['shape'][2] & 1) != bool(row['paid']['Flags'] & 1): errors.append('paid head/request mismatch')
        if not keyed(row['paid'], row) or row['paid']['Result'] not in ('Applied', 'AlreadyResolved', 'Corrected') or not row['paid']['Flags'] & 64:
            errors.append('paid receipt lacks exact material outcome')
        if not row['paid']['DamageApplied'] or bool(row['paid']['Flags'] & 2) != (row['paid']['HealthAfter'] == 0):
            errors.append('paid receipt damage/lethal contradiction')
    if state == 'VerifiedNative':
        if row['weapon'] != 4 or row['key'][9:12] != (1, 1, 0): errors.append('native decision outside supported direct family')
        if damage['status'] != 'Available' or damage['reason'] != 'NativeDamage' or not 0 < damage['debit'] <= damage['nominal'] or row['pre'] <= 0:
            errors.append('native available damage contradiction')
        if damage['debit'] != min(damage['nominal'], row['pre']) or damage['lethal'] != (damage['nominal'] >= row['pre']):
            errors.append('native nominal/debit/lethal contradiction')
    elif state == 'Reject':
        if damage['status'] != 'Blocked' or damage['reason'] not in DAMAGE_REASONS[14:20] or damage['nominal'] or damage['debit'] or damage['lethal'] or damage['head']:
            errors.append('native blocked damage contradiction')
    elif damage['status'] != 'Unavailable' or damage['nominal'] or damage['debit'] or damage['lethal'] or damage['head']:
        errors.append('non-native decision carries payable damage')
    if row['corrected']:
        return errors, None
    if state not in ('VerifiedPaid', 'VerifiedNative', 'Reject'):
        if row['compared'] or row['agrees']: errors.append('fallback reported as compared parity')
        return errors, None
    if not row['compared']:
        if row['agrees']: errors.append('uncompared row claims agreement')
        if state == 'VerifiedPaid' and row['terminals']: errors.append('terminal evidence omitted its required comparison')
        return errors, None
    actual = row['actual']
    if state == 'VerifiedPaid':
        fields = ('DamageApplied', 'HealthAfter', 'Flags', 'HalfturretHealthAfter', 'DamageSequence')
        agree = row['terminals'] > 0 and row['terminal_disagreements'] == 0 and actual['Result'] in ('Applied', 'AlreadyResolved', 'Corrected') \
            and keyed(actual, row) and actual['ClaimId'] == row['shape'][0] \
            and all(actual[field] == row['paid'][field] for field in fields) \
            and row['debit'] == actual['DamageApplied'] and row['health'] == actual['HealthAfter']
    elif pay:
        agree = row['payments'] == 1 and keyed(actual, row) and actual['Result'] in ('Applied', 'AlreadyResolved') \
            and bool(actual['Flags'] & 64) and bool(actual['Flags'] & 1) == damage['head'] \
            and bool(actual['Flags'] & 2) == damage['lethal'] and actual['DamageApplied'] == damage['debit'] \
            and row['debit'] == damage['debit'] and row['health'] == row['pre'] - damage['debit'] \
            and actual['HealthAfter'] == row['health']
    else:
        agree = row['payments'] == 0 and row['debit'] == 0 and row['health'] == row['pre'] \
            and actual['DamageApplied'] == 0 and not actual['Flags'] & 64
    if bool(agree) != row['agrees']: errors.append('reported agreement contradicts material comparison')
    if not agree: errors.append('independent material parity disagreement')
    return errors, bool(agree)


def audit(reports):
    processes, failures, incomplete = [], [], []
    items = reports.items() if isinstance(reports, dict) else enumerate(reports)
    authority_count = 0
    for ordinal, (name, report) in enumerate(items):
        label = 'process-' + str(ordinal)
        authority = name in ('server', 'server.json') or isinstance(report, dict) and str(report.get('Role', '')).lower() in ('server', 'host')
        if not authority:
            continue
        authority_count += 1; computed = Counter({name: 0 for name in COUNTERS}); summaries = []; seen = set(); per_slot = Counter(); positives = 0; rejects = 0
        try:
            captured = report['CombatFastPath']
            if integer(captured['Schema'], 1, 1) != 1: raise ValueError('unsupported schema')
            mode = enum(captured['Mode'], ('Off', 'Shadow', 'Paid'))
            if integer(captured['PerShooter'], 1, 1024) != 1024: raise ValueError('unsupported capacity')
            for field in ('Overflow', 'Invalid', 'UnjoinedTerminal'):
                if integer(captured[field], 0, (1 << 63) - 1): incomplete.append(label + ': ' + field + ' evidence is incomplete')
            if mode == 'Off': incomplete.append(label + ': shadow observation disabled')
            bypasses = 0
            rows = captured['Rows']
            if not isinstance(rows, list): raise ValueError('missing rows')
            for raw in rows:
                row = row_value(raw); row_token = token(row)
                if row_token in seen: failures.append(label + ': duplicate full-key/stage/request-shape row')
                seen.add(row_token); per_slot[row['key'][2]] += 1
                errors, agreement = inspect(row)
                capture_complete = False
                failures.extend(label + ': ' + error for error in errors)
                state = row['state']; damage = row['damage']; eligible = state in ('VerifiedPaid', 'VerifiedNative')
                bypass = boolean(raw.get('PaidBypass', False))
                bypasses += int(bypass)
                if bypass and (mode != 'Paid' or state != 'VerifiedPaid' or row['stage'] != 'ClaimReady'):
                    failures.append(label + ': paid bypass outside verified paid ready seam')
                pay = state == 'VerifiedNative' and damage['status'] == 'Available'; reject = state == 'Reject'
                if row['corrected']:
                    computed['CorrectedExcluded'] += 1
                else:
                    computed.update(fastEligible=int(eligible), fastWouldPay=int(pay), fastWouldReject=int(reject),
                        missingSource=int(row['reason'] == 'MissingSource'), headMismatch=int(row['reason'] == 'HeadMismatch'),
                        wallMismatch=int(row['reason'] == 'WallMismatch'), orderingDeferred=int(row['reason'] == 'OrderingDeferred'),
                        familyUnsupported=int(row['reason'] == 'FamilyUnsupported'), wouldDoublePay=int(pay and row['payments'] > 1),
                        wouldAlterTrade=int(pay and row['compared'] and damage['lethal'] != (row['health'] == 0)),
                        Compared=int(row['compared']), Disagreements=int(row['compared'] and agreement is False),
                        Uncompared=int(not row['compared'] and (eligible or reject)))
                    capture_complete = not errors
                    if state in ('VerifiedNative', 'Reject'):
                        if not isinstance(raw.get('NativeEvidence'), dict):
                            capture_complete = False
                            incomplete.append(label + ': native authorization capture unavailable')
                        else:
                            capture_errors = native_inputs(row, raw['NativeEvidence'])
                            failures.extend(label + ': ' + message for message in capture_errors)
                            capture_complete &= not capture_errors
                    if eligible and row['compared'] and agreement and capture_complete: positives += 1
                    if reject and row['compared'] and agreement and capture_complete: rejects += 1
                    if (eligible or reject) and not row['compared']: incomplete.append(label + ': eligible/reject row lacks comparison')
                    if pay and row['payments'] > 1: failures.append(label + ': predicted duplicate payment')
                    if pay and row['compared'] and damage['lethal'] != (row['health'] == 0): failures.append(label + ': predicted lethal outcome differs')
                    if row['terminal_disagreements']: failures.append(label + ': retained terminal disagreement')
                summaries.append(dict(token=row_token, stage=row['stage'], state=state, reason=row['reason'],
                    excludedCorrection=row['corrected'], compared=row['compared'], independentlyAgrees=agreement,
                    attempts=row['attempts'], payments=row['payments'], terminals=row['terminals'], paidBypass=bypass))
            if any(count > 1024 for count in per_slot.values()): failures.append(label + ': per-shooter row capacity exceeded')
            for field in COUNTERS:
                if integer(captured[field], 0, (1 << 63) - 1) != computed[field]: failures.append(label + ': structured counter mismatch: ' + field)
            if computed['CorrectedExcluded']: incomplete.append(label + ': corrected rows excluded from certification')
            if not positives: incomplete.append(label + ': no positive eligible independently compared row')
            if integer(captured.get('PaidReceipts', 0), 0, (1 << 63) - 1) != bypasses:
                failures.append(label + ': paid receipt bypass count mismatch')
            processes.append(dict(process=label, mode=mode, rows=len(rows), positiveEligibleComparisons=positives,
                                  positiveRejectComparisons=rejects, paidReceiptBypasses=bypasses,
                                  counters=dict(computed), observations=summaries))
        except (KeyError, TypeError, ValueError, OverflowError, struct.error):
            incomplete.append(label + ': shadow report unavailable or malformed')
    if not authority_count: incomplete.append('no authority shadow report')
    return dict(schema=1, status='FAIL' if failures else 'INCOMPLETE' if incomplete else 'PASS',
                processes=processes, failures=sorted(set(failures)), incomplete=sorted(set(incomplete)),
                scope='Independent retained-row/counter/material-outcome audit only. Positive paid receipts and native-ordered first contacts; Full native capture stamps, funding constraints and frozen damage arithmetic are checked; source/resources/collision history and native geometry cannot be independently re-proved from these exports. Corrections are excluded. No fresh-claim early-settlement, whole-world/trade, transport, performance or release certification. Raw identities, claim IDs, positions and process names are not exported.')
