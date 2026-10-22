"""Audit sampled A3 timed-effect projection against independent native grants.

PASS requires positive native, owner, observer and prediction observations. This
is sampled conservation evidence, not every-tick audiovisual or replay proof.
"""
import argparse
import json
import struct
from pathlib import Path
from net_pickup_claims import integer, identity, payload, delta, LIFE, SPAWN, TYPES, MASK
import net_timed_grants

REASONS = ('Active','Expired','Renewed','Death','Retired','Room','Session')
CORRECTION_DEADLINE = 900  # 15-second delivery/prediction deadline at 60 native ticks

def newer16(a,b): return 0 < ((a-b)&65535) < 32768


def view_identity(v):
    """Validate a complete view, admitting only exact neutral pre-life rows.

    A native slot exists before its occupant/life is admitted. Such a sampled
    row proves no grant or prediction coverage and must not enter denominators.
    """
    life=identity(v['Life'],LIFE)
    for val,low,high in zip(life,(1,1,0,0,0,0),(2**64-1,65535,7,65535,65535,0)):integer(val,low,high)
    if v['Type'] not in TYPES:raise ValueError('unknown view type')
    for n in ('ObservedFrame','AuthorityFrame','PredictionSequence'):integer(v[n],0,MASK)
    integer(v['GrantSequence'],0,2**64-1)
    for n in ('Remaining','ConfirmedRemaining','NativeRemaining'):integer(v[n],0,1800)
    for n in ('Owner','Covered','Alive'):
        if type(v[n]) is not bool:raise ValueError('invalid view boolean')
    if (life[3]==0)!=(life[4]==0):raise ValueError('partial unadmitted life')
    unadmitted=life[3]==0
    if unadmitted and (any(v[n] for n in ('Owner','Covered','Alive'))
            or any(v[n]!=0 for n in ('Remaining','ConfirmedRemaining','NativeRemaining','GrantSequence','PredictionSequence'))):
        raise ValueError('unadmitted view must be entirely neutral')
    return life,unadmitted


def decode(row):
    b = payload(row,53)
    epoch,match,slot,generation,life = struct.unpack_from('<QHBHH',b)
    room,revision,kind,seq,sid,spawn_generation,contact,end,reason,authority = struct.unpack_from('<IIBQiIIIBI',b,15)
    for v,low,high in [(epoch,1,2**64-1),(match,1,65535),(slot,0,7),(generation,1,65535),(life,1,65535),(room,1,MASK),(revision,1,MASK),(kind,0,2)]:integer(v,low,high)
    resume=bool(reason&128);reason &=127;integer(reason,0,6)
    if seq==0:
        if (sid,spawn_generation,contact,end,reason)!=(-1,0,0,0,4):raise ValueError('invalid explicit absence')
    else:
        if sid < -1 or spawn_generation==0 or (end-contact)&MASK>1800:raise ValueError('invalid grant identity/lifetime')
        if reason in (0,1) and (end-contact)&MASK!=1800:raise ValueError('invalid active/expired duration')
        if delta(authority,contact)<0:raise ValueError('authority precedes contact')
        if reason!=0 and delta(authority,end)<0 and not(reason==2 and (end-authority)&MASK==1):raise ValueError('closure precedes authority clock')
    life_key=(epoch,match,slot,generation,life,0);spawn=(epoch,match,room,sid,spawn_generation)
    if identity(row['Life'],LIFE)!=life_key or identity(row['Spawn'],SPAWN)!=spawn:raise ValueError('wire/structured identity differs')
    names=('Room','Revision','Type','Sequence','ContactFrame','EndFrame','Reason','AuthorityFrame','Resume')
    expected=(room,revision,TYPES[kind],seq,contact,end,REASONS[reason],authority,resume)
    if tuple(row[n] for n in names)!=expected or type(row['Resume']) is not bool:raise ValueError('wire/structured state differs')
    for n in ('Room','Revision','Sequence','ContactFrame','EndFrame','AuthorityFrame','ObservedFrame'):integer(row[n],0,2**64-1 if n=='Sequence' else MASK)
    return b,dict(life=life_key,spawn=spawn,type=TYPES[kind],sequence=seq,revision=revision,contact=contact,end=end,reason=REASONS[reason],authority=authority,observed=row['ObservedFrame'])


def audit(server,clients):
    failures=[];missing=[];authority={};received=[];counts=dict(native=0,owner=0,observer=0,prediction=0);types={t:dict(native=0,owner=0,observer=0,prediction=0) for t in TYPES}
    if not isinstance(server,dict) or not isinstance(clients,(list,tuple)):return dict(status='FAIL',failures=['expected authority object and client list'])
    native=net_timed_grants.audit(server);failures.extend('native: '+f for f in native.get('failures',[]))
    starts={};closures={}
    native_rows=server.get('TimedGrants',[])
    if not isinstance(native_rows,list):native_rows=[]
    for row in native_rows:
        try:
            key=(identity(row['Life'],LIFE),row['Spawn']['Room'],row['Sequence'])
            if row['EndReason']=='Active':starts[key]=row;counts['native']+=1;types[row['Type']]['native']+=1
            else:closures[key]=row
        except (KeyError,TypeError):continue
    all_views=[]
    for name,report in [('authority',server)]+[(f'client-{i}',r) for i,r in enumerate(clients)]:
        if not isinstance(report,dict):failures.append(name+': invalid report');continue
        if type(report.get('Overflow')) is not int or report['Overflow']!=0:failures.append(name+': missing/nonzero overflow')
        for field in ('TimedGrantStates','TimedGrantViews'):
            rows=report.get(field)
            if not isinstance(rows,list):missing.append(name+': missing '+field);continue
            for i,row in enumerate(rows):
                try:
                    if field=='TimedGrantViews':
                        if name=='authority':raise ValueError('authority cannot substitute client presentation views')
                        all_views.append((name,row));continue
                    b,s=decode(row)
                    if row['Stage']!=('authority' if name=='authority' else 'received'):raise ValueError('wrong state stage')
                    if name=='authority':
                        revision_key=(s['life'],s['spawn'][2],s['type'],s['revision'])
                        if revision_key in authority and authority[revision_key][0]!=b:raise ValueError('conflicting authority revision')
                        authority[revision_key]=(b,s)
                        if s['sequence']:
                            key=(s['life'],s['spawn'][2],s['sequence']);g=starts[key]
                            candidates=[g]+([closures[key]] if key in closures else [])
                            if not any(identity(c['Spawn'],SPAWN)==s['spawn'] and c['Type']==s['type'] and c['ContactFrame']==s['contact'] and c['EndReason']==s['reason'] and c['EndFrame']==s['end'] for c in candidates):raise ValueError('state lacks exact native semantic transition')
                    else:received.append((name,b,s))
                except (KeyError,TypeError,ValueError,struct.error) as e:failures.append(f'{name} {field}[{i}]: {e}')
    published={v[0] for v in authority.values()};active={(s['life'],s['type'],s['sequence']):s for b,s in authority.values() if s['reason']=='Active' and s['sequence']}
    for name,b,s in received:
        if b not in published:failures.append(name+': received state lacks exact authority payload')
    prediction_keys=set();correction_count=0;max_correction=0;eligible_views=0;neutral_by_client={};neutral_by_type={t:0 for t in TYPES}
    for name,v in all_views:
        try:
            life,unadmitted=view_identity(v)
            kind=v['Type']
            if unadmitted:
                neutral_by_client[name]=neutral_by_client.get(name,0)+1;neutral_by_type[kind]+=1
                continue
            eligible_views+=1
            if v['Alive'] and v['Covered'] and v['NativeRemaining']!=v['Remaining']:raise ValueError('native projected timer differs')
            if v['ConfirmedRemaining']:
                if not v['Alive'] or not v['Covered'] or v['GrantSequence']==0:raise ValueError('provisional/dead view claims confirmed ownership')
                s=active[(life,kind,v['GrantSequence'])];g=starts[(life,s['spawn'][2],s['sequence'])];end=closures.get((life,s['spawn'][2],s['sequence']),g)['EndFrame']
                if delta(v['AuthorityFrame'],g['ContactFrame'])<1 or delta(g['ExpiresAtFrame'],v['AuthorityFrame'])<=0:
                    raise ValueError('confirmed view outside contact+1/natural expiry window')
                if v['ConfirmedRemaining']!=min(1800,delta(g['ExpiresAtFrame'],v['AuthorityFrame'])):raise ValueError('confirmed remaining differs from native expiry clock')
                if delta(end,v['AuthorityFrame'])<=0:
                    closure=closures.get((life,s['spawn'][2],s['sequence']))
                    corrections=[]
                    for received_name,b,notice in received:
                        if received_name!=name or notice['type']!=kind:continue
                        exact=notice['life']==life and notice['sequence']==s['sequence'] and notice['reason']==closure['EndReason'] and notice['end']==end
                        candidate=notice['life']
                        replacement=candidate[:3]==life[:3] and (newer16(candidate[3],life[3]) or candidate[3]==life[3] and newer16(candidate[4],life[4]))
                        renewal=closure['EndReason']=='Renewed' and candidate==life and notice['sequence']>s['sequence'] and notice['reason']=='Active' and delta(notice['contact'],(end-1)&MASK)>=0
                        if exact or replacement or renewal:corrections.append(notice['observed'])
                    if not corrections:raise ValueError('native closure lacks eventual client closure/new-life receipt')
                    receipt=min(corrections,key=lambda frame:delta(frame,v['ObservedFrame']))
                    after_delivery=delta(v['ObservedFrame'],receipt)
                    if after_delivery>=1:raise ValueError('active old grant view persists after correction delivery+1')
                    lag=max(delta(v['AuthorityFrame'],end),max(0,delta(receipt,v['ObservedFrame'])))
                    if lag>CORRECTION_DEADLINE:raise ValueError('closure correction exceeds finite 900-frame deadline')
                    correction_count+=1;max_correction=max(max_correction,lag)
            if v['Remaining'] and not v['ConfirmedRemaining'] and not v['PredictionSequence'] and v['GrantSequence']:
                for received_name,b,notice in received:
                    candidate=notice['life']
                    old_end=notice['life']==life and notice['sequence']==v['GrantSequence'] and notice['reason']!='Active'
                    replacement=candidate[:3]==life[:3] and (newer16(candidate[3],life[3]) or candidate[3]==life[3] and newer16(candidate[4],life[4]))
                    prior=active.get((life,kind,v['GrantSequence']))
                    closed=closures.get((life,prior['spawn'][2],v['GrantSequence'])) if prior else None
                    renewal=closed is not None and closed['EndReason']=='Renewed' and candidate==life and notice['sequence']>v['GrantSequence'] and notice['reason']=='Active' and delta(notice['contact'],(closed['EndFrame']-1)&MASK)>=0
                    if received_name==name and notice['type']==kind and (old_end or replacement or renewal) and delta(v['ObservedFrame'],notice['observed'])>=1:
                        raise ValueError('unconfirmed old visual timer persists after correction delivery+1')
            if v['Alive'] and v['Covered'] and v['ConfirmedRemaining']:
                role='owner' if v['Owner'] else 'observer';counts[role]+=1;types[kind][role]+=1
            if v['PredictionSequence']:
                if not v['Owner']:raise ValueError('observer cannot own pickup prediction')
                key=(life,kind,v['PredictionSequence'])
                if key not in prediction_keys:prediction_keys.add(key);counts['prediction']+=1;types[kind]['prediction']+=1
        except (KeyError,TypeError,ValueError) as e:failures.append(name+' view: '+str(e))
    if not counts['native'] and not received and not eligible_views and not authority:status='NOT_RUN'
    else:
        for key,n in counts.items():
            if n==0:missing.append('positive '+key+' coverage absent: NOT_RUN')
        status='INCOMPLETE' if missing else 'PASS'
    if failures:status='FAIL'
    return dict(status=status,failures=failures,missing=missing,counts=counts,coverage={k:dict(observed=n,status='OBSERVED' if n else 'NOT_RUN') for k,n in counts.items()},byType={t:{k:dict(observed=n,status='OBSERVED' if n else 'NOT_RUN') for k,n in values.items()} for t,values in types.items()},authorityStates=len(authority),receivedStates=len(received),nativeAudit=native,neutralUnadmittedViews=dict(excludedRows=sum(neutral_by_client.values()),status='NOT_RUN',byClient=neutral_by_client,byType=neutral_by_type,scope='Fully validated neutral pre-life native slots; excluded from grant/prediction denominators'),closureCorrectionLag=dict(observedSamples=correction_count,maxFrames=max_correction,deadlineFrames=CORRECTION_DEADLINE,scope='Bounded presentation correction delay only; these post-closure views are not source-authorized and do not establish A4 proof'),limitations=['Sampled view rows do not establish every-tick audiovisual agreement.','Prediction counts do not establish denial, timeout, renewal or late-join coverage.','Client receipts are checked against authority bytes; exhaustive delivery to every eligible peer is not inferred.','No replay, dynamic-item, independent movement or damage authorization acceptance.'])


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('server',type=Path);p.add_argument('clients',type=Path,nargs='*');a=p.parse_args();r=audit(json.loads(a.server.read_text()),[json.loads(path.read_text()) for path in a.clients]);print(json.dumps(r,indent=2,allow_nan=False));raise SystemExit(0 if r['status']=='PASS' else 1)
