"""Audit pickup terminal conservation and granted-claim proofs separately.

Refusal-only terminal coverage can PASS while granted-claim proof coverage remains
NOT_RUN. Overall PASS requires at least one Grant with complete evidence; it is
this bounded audit contract, not full A2 acceptance or independent movement proof.
"""
import argparse
import json
import math
import struct
from pathlib import Path
import net_timed_grants

TYPES = ('DoubleDamage', 'Cloak', 'DeathAlt')
RESULTS = ('Grant', 'Taken', 'Gone', 'TooOld', 'TooFar', 'Invalid', 'Replayed', 'Capacity', 'HistoryUnavailable')
MASK = 2**32 - 1
KEY = ('Epoch', 'Match', 'Slot', 'Generation', 'Life', 'Sequence')
SPAWN = ('Epoch', 'Match', 'Room', 'SpawnId', 'Generation')
LIFE = ('AuthorityEpoch', 'MatchId', 'ShooterSlot', 'Generation', 'LifeId', 'ShotId')


def integer(v, lo, hi):
    if type(v) is not int or not lo <= v <= hi:
        raise ValueError('invalid integer')
    return v


def vector(v):
    if not isinstance(v, (list, tuple)) or len(v) != 3 or any(type(x) not in (int, float) or not math.isfinite(x) or abs(x) >= 100000 for x in v):
        raise ValueError('invalid finite position')
    return tuple(v)


def identity(row, names):
    values = tuple(row[n] for n in names)
    if any(type(v) is not int for v in values): raise ValueError('identity fields must be integers')
    return values


def payload(row, size):
    v = row['Payload']
    if not isinstance(v, str) or len(v) != size * 2:
        raise ValueError('wrong exact payload length')
    return bytes.fromhex(v)


def claim_decode(b):
    epoch, match, slot, gen, life, seq = struct.unpack_from('<QHBHHI', b)
    room, spawnid, incarnation, kind, source = struct.unpack_from('<IiIBI', b, 19)
    k = (integer(epoch, 1, 2**64-1), integer(match, 1, 65535), integer(slot, 0, 7), integer(gen, 1, 65535), integer(life, 1, 65535), integer(seq, 1, MASK))
    spawn = (epoch, match, integer(room, 1, MASK), integer(spawnid, -1, 2**31-1), integer(incarnation, 1, MASK))
    integer(kind, 0, 2)
    return k, spawn, TYPES[kind], source, vector(struct.unpack_from('<fff', b, 36))


def claim_row(r, size=48):
    b = payload(r, size)
    k, spawn, kind, source, pos = claim_decode(b)
    if identity(r['Key'], KEY) != k or identity(r['Spawn'], SPAWN) != spawn:
        raise ValueError('wire/structured identity mismatch')
    integer(r['ObservedFrame'], 0, MASK)
    if size == 48 and (r['Type'] != kind or integer(r['SourceFrame'],0,MASK) != source):
        raise ValueError('wire/structured claim mismatch')
    return b, (k, spawn, kind, source, pos)


def verdict_row(r):
    b, claim = claim_row(r, 73)
    result, decision, proof, seq, grant, expiry = struct.unpack_from('<BIIQII', b, 48)
    integer(result, 1, 9)
    vals = (RESULTS[result-1], decision, proof, seq, grant, expiry)
    if tuple(r[n] for n in ('Result', 'DecisionFrame', 'ProofFrame', 'GrantSequence', 'GrantFrame', 'ExpiresAtFrame')) != vals:
        raise ValueError('wire/structured verdict mismatch')
    if result == 1:
        if seq == 0 or expiry != (grant + 1800) & MASK:
            raise ValueError('invalid grant timing/sequence')
    elif (seq, grant, expiry) != (0, 0, 0):
        raise ValueError('refusal carries grant')
    return b, claim, vals


def delta(a, b):
    return ((a - b + 2**31) & MASK) - 2**31


def f32(value):
    """Round every native Single operation, preserving strict comparisons."""
    return struct.unpack('<f', struct.pack('<f', value))[0]


def fadd(a, b): return f32(f32(a) + f32(b))
def fsub(a, b): return f32(f32(a) - f32(b))
def fmul(a, b): return f32(f32(a) * f32(b))


def distance_squared(a, b):
    d = tuple(fsub(a[i], b[i]) for i in range(3))
    return fadd(fadd(fmul(d[0], d[0]), fmul(d[1], d[1])), fmul(d[2], d[2]))


def reaches(p, item, position):
    # Mirror PickupContactPose.Reaches exactly: position + (sphere - pose)
    # forms the center before item-center subtraction. Reordering this loses
    # native cancellation/rounding behavior at large world coordinates.
    center = tuple(fadd(position[i], fsub(p['SpherePosition'][i], p['Position'][i]))
                   if p['Alt'] else f32(position[i]) for i in range(3))
    d = tuple(fsub(item[i], center[i]) for i in range(3))
    radius = fadd(p['Radius'], .45)
    limit = fmul(radius, radius)
    if p['Alt']:
        squared = fadd(fadd(fmul(d[0], d[0]), fmul(d[1], d[1])), fmul(d[2], d[2]))
        return squared < limit
    return fadd(fmul(d[0], d[0]), fmul(d[2], d[2])) < limit and f32(p['MinHeight']) <= d[1] <= f32(p['MaxHeight'])


def audit(server_report, peer_reports):
    failures, deferred = [], []
    if not isinstance(server_report, dict) or not isinstance(peer_reports, (list,tuple)):
        return dict(status='FAIL', failures=['authority report must be object and peers must be list'], deferred=[])
    terminals, proofs, peer_seen = {}, {}, {}
    offered, sent, arrived = {}, {}, {}
    reports = [('authority', server_report)] + [(f'peer-{i}', p) for i, p in enumerate(peer_reports)]
    for name, report in reports:
        if not isinstance(report, dict):
            failures.append(name + ': report must be object'); continue
        if report.get('Overflow') != 0 or type(report.get('Overflow')) is not int:
            failures.append(name + ': evidence overflow missing/nonzero')
        for field in ('PickupClaims', 'PickupVerdicts', 'PickupItems', 'PickupProofs'):
            rows = report.get(field)
            if not isinstance(rows, list):
                deferred.append(name + ': missing ' + field); continue
            for i, r in enumerate(rows):
                try:
                    if field == 'PickupClaims':
                        b, c = claim_row(r); k = c[0]
                        allowed = ('received',) if name == 'authority' else ('offered', 'sent')
                        if r['Stage'] not in allowed: raise ValueError('invalid claim stage')
                        if name == 'authority':
                            if k in arrived and arrived[k] != b: raise ValueError('conflicting authority claim bytes')
                            arrived[k] = b
                        else:
                            target = offered if r['Stage'] == 'offered' else sent
                            if k in target and target[k] != (name, b): raise ValueError('conflicting owner claim identity')
                            target[k] = (name, b)
                    elif field == 'PickupVerdicts':
                        b, c, vals = verdict_row(r); k = c[0]
                        if r['Stage'] != ('authority' if name == 'authority' else 'received'): raise ValueError('invalid verdict stage')
                        target = terminals if name == 'authority' else peer_seen
                        if k in target and target[k][0] != b: raise ValueError('conflicting terminal bytes on retry')
                        if name != 'authority' and k in target and target[k][1] != name: raise ValueError('terminal received by multiple owners')
                        target[k] = (b, c, vals) if name == 'authority' else (b, name)
                    elif field == 'PickupItems':
                        b = payload(r, 40)
                        epoch, match, room, sid, gen, kind, frame, avail = struct.unpack_from('<QHIiIBIB', b)
                        for v, lo, hi in [(epoch,1,2**64-1),(match,1,65535),(room,1,MASK),(sid,-1,2**31-1),(gen,1,MASK),(kind,0,2),(avail,0,1)]: integer(v,lo,hi)
                        vector(struct.unpack_from('<fff', b, 28))
                        if identity(r['Spawn'], SPAWN) != (epoch,match,room,sid,gen) or r['Type'] != TYPES[kind] or r['AuthorityFrame'] != frame or type(r['Available']) is not bool or r['Available'] != bool(avail): raise ValueError('wire/structured item mismatch')
                        if r['Stage'] != ('authority' if name == 'authority' else 'received'): raise ValueError('invalid item stage')
                        integer(r['ObservedFrame'],0,MASK)
                    else:
                        if name != 'authority': raise ValueError('peer cannot supply authority contact proof')
                        k = identity(r['Key'], KEY)
                        if k in proofs: raise ValueError('duplicate proof key')
                        proofs[k] = r
                except (KeyError, TypeError, ValueError, struct.error, IndexError) as e:
                    failures.append(f'{name} {field}[{i}]: {e}')
    timed = net_timed_grants.audit(server_report)
    failures.extend('timed grants: ' + f for f in timed.get('failures', []))
    starts = {r['Sequence']:r for r in server_report.get('TimedGrants', []) if isinstance(r,dict) and r.get('EndReason') == 'Active'}
    grants = 0
    for k, (b, c, vals) in terminals.items():
        if vals[0] != 'Grant': continue
        grants += 1
        try:
            p = proofs[k]; spawn, kind, source, pos = c[1:]
            if identity(p['Spawn'], SPAWN) != spawn or p['Type'] != kind or p['SourceFrame'] != source or struct.unpack('<fff', struct.pack('<fff', *vector(p['ClaimPosition']))) != pos: raise ValueError('proof claim differs')
            life = (k[0],k[1],k[2],k[3],k[4],0)
            if identity(p['Life'], LIFE) != life: raise ValueError('proof life differs')
            for n in ('Position','SpherePosition','ItemPosition'): vector(p[n])
            for n in ('AuthorityFrame','ItemAuthorityFrame','DecisionFrame','ProofFrame','GrantFrame','ExpiresAtFrame'): integer(p[n],0,MASK)
            for n in ('Alt','Alive','CanPickup','Available'):
                if type(p[n]) is not bool: raise ValueError('invalid proof boolean')
            for n in ('Radius','MinHeight','MaxHeight'):
                if type(p[n]) not in (int,float) or not math.isfinite(p[n]): raise ValueError('invalid proof geometry')
            if not 0 < p['Radius'] <= 8 or not -128 <= p['MinHeight'] <= p['MaxHeight'] <= 128: raise ValueError('invalid native volume')
            if not p['Alive'] or not p['CanPickup'] or not p['Available'] or p['ItemAuthorityFrame'] != p['AuthorityFrame'] or p['AuthorityFrame'] != vals[2]: raise ValueError('proof lacks same-frame live available contact')
            integer(p['GrantSequence'],1,2**64-1)
            integer(p['Window'],12,120)
            if not 0 <= delta(vals[1], vals[2]) <= p['Window']: raise ValueError('expired/future contact history')
            if distance_squared(pos,p['Position']) > fmul(.75,.75) or not reaches(p,p['ItemPosition'],p['Position']) or not reaches(p,p['ItemPosition'],pos): raise ValueError('native reach or reported position tolerance failed')
            if tuple(p[n] for n in ('DecisionFrame','ProofFrame','GrantSequence','GrantFrame','ExpiresAtFrame')) != vals[1:]: raise ValueError('proof terminal differs')
            if delta(vals[4],vals[2]) < 0 or delta(vals[1],vals[4]) < 0: raise ValueError('retroactive/future grant activation')
            g = starts[vals[3]]
            if identity(g['Spawn'],SPAWN) != spawn or identity(g['Life'],LIFE) != life or g['Type'] != kind or g['ContactFrame'] != vals[4] or g['ExpiresAtFrame'] != vals[5]: raise ValueError('raw grant join differs')
        except (KeyError,TypeError,ValueError) as e: failures.append('grant '+str(k)+': '+str(e))
    for k, (name,b) in offered.items():
        if k not in sent: deferred.append(name+': offered claim not observed sent '+str(k))
    for k, (name,b) in sent.items():
        if k not in offered: failures.append(name+': sent without offered '+str(k))
        if k not in terminals or terminals[k][0][:48] != b: failures.append(name+': missing/mismatched authority terminal '+str(k))
        if k not in peer_seen or peer_seen[k] != (terminals.get(k,(b'',))[0],name): failures.append(name+': missing/mismatched owner receipt '+str(k))
    for k,(b,name) in peer_seen.items():
        if k not in sent or k not in terminals or terminals[k][0] != b: failures.append(name+': orphan/mismatched receipt '+str(k))
    for k,(b,c,v) in terminals.items():
        if k not in sent: failures.append('authority terminal lacks observed owner sent claim '+str(k))
        if arrived.get(k) != b[:48]: failures.append('authority terminal lacks exact received claim '+str(k))
    for k in proofs:
        if k not in terminals or terminals[k][2][0] != 'Grant': failures.append('orphan/non-grant contact proof '+str(k))
    observed = len(sent)
    terminal_status = 'FAIL' if failures else 'INCOMPLETE' if deferred else 'PASS' if observed and terminals else 'NOT_RUN'
    grant_status = 'FAIL' if failures and grants else 'INCOMPLETE' if deferred and grants else 'PASS' if grants else 'NOT_RUN'
    overall = terminal_status
    if terminal_status == 'PASS' and grant_status == 'NOT_RUN':
        overall = 'INCOMPLETE'
        deferred.append('No Grant verdict observed; granted-claim proof coverage was not run')
    return dict(status=overall, failures=failures, deferred=deferred, sentClaims=observed, authorityTerminals=len(terminals), grantedClaims=grants, ownerReceipts=len(peer_seen), timedGrants=timed,
                terminalCoverage=dict(status=terminal_status, observed=len(terminals)),
                grantedClaimProofCoverage=dict(status=grant_status, observed=grants),
                scope='Bounded terminal conservation and granted-claim contact-proof audit; does not establish full A2 acceptance',
                limitations=['Uncached capacity refusals are not proof of canonical terminal-cache conservation.', 'Dynamic item claims are not established by this audit.', 'No retired-life receipt exception is inferred; missing receipts fail.', 'Contact geometry uses authority-observed client movement, not independent movement proof.'])


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('campaign',type=Path);parser.add_argument('--server',type=Path);parser.add_argument('--peer',type=Path,action='append');args=parser.parse_args()
    server=args.server or args.campaign/'server.json';peers=args.peer or sorted(args.campaign.glob('peer-[0-9]*.json'))
    peers=[p for p in peers if '.drain.' not in p.name]
    result=audit(json.loads(server.read_text()),[json.loads(p.read_text()) for p in peers]);print(json.dumps(result,indent=2,allow_nan=False));return 0 if result['status'] in ('PASS','NOT_RUN') else 1

if __name__=='__main__': raise SystemExit(main())
