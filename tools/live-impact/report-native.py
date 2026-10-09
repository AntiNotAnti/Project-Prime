#!/usr/bin/env python3
"""Join native diagnostic exports. Never infer rendered success from a headless run."""
import argparse, gzip, json, math
from collections import Counter
from pathlib import Path

def identity(event):
    return json.dumps(event['Identity'], sort_keys=True, separators=(',', ':'))

def shot_victim(event):
    i=event['Identity']
    return json.dumps({k:i[k] for k in ('Shot','Victim','VictimGeneration','VictimLife')}, sort_keys=True)

def read(path):
    if not path.exists(): path=path.with_suffix(path.suffix+'.gz')
    return json.loads(gzip.decompress(path.read_bytes()) if path.suffix=='.gz' else path.read_bytes())

def report(folder):
    server=read(folder/'server-impacts.json')
    authority={identity(e) for e in server['events'] if e['Stage']==1}
    manifest_path=folder.parent/'manifest.json'
    rendered=manifest_path.is_file() and json.loads(manifest_path.read_text()).get('rendered',False)
    result={'scenario':folder.name, 'rendered':rendered, 'authorityFactsRetained':len(authority),
            'authorityRingOverwritten':server['overwritten'],
            'authorityByWeapon':dict(Counter(e['Weapon'] for e in server['events'] if e['Stage']==1)), 'peers':[],
            'limitations':['Authority facts may predate peer readiness or lifecycle changes; missing facts are not a measured eligible-delivery failure rate.',
                'Latency is local-hit to live ingress within one process, only for unambiguous one-hit/one-fact shot-victim groups.',
                'Presentation classifications/draw submissions are instrumented, not a pixel visibility metric. Screenshots require separate inspection; no cross-process timestamp subtraction is used.']}
    for path in sorted(set(folder.glob('peer*-impacts.json')) | set(folder.glob('peer*-impacts.json.gz'))):
        data=read(path); ingress=[e for e in data['events'] if e['Stage']==3]
        presentation=[e for e in data['events'] if e['Stage'] in (4,5,6,10)]
        classified={identity(e) for e in presentation}
        same_shot={identity(e) for e in presentation if e['Stage'] in (4,6)}
        submitted={identity(e) for e in data['events'] if e['Stage']==14}
        stage_counts=dict(Counter(e['Stage'] for e in presentation))
        arrivals={identity(e):e['Timestamp'] for e in ingress}
        draw_delays=sorted((e['Timestamp']-arrivals[identity(e)])*1000/data['timestampFrequency']
            for e in data['events'] if e['Stage']==14 and identity(e) in arrivals and e['Timestamp']>=arrivals[identity(e)])
        draw_q=lambda p:draw_delays[min(len(draw_delays)-1,math.ceil(len(draw_delays)*p)-1)] if draw_delays else None
        keys={identity(e) for e in ingress}; local={}; received={}; latencies=[]; negative=0
        for e in data['events']:
            if e['Stage']==0: local.setdefault(shot_victim(e),[]).append(e)
        for e in ingress: received.setdefault(shot_victim(e),[]).append(e)
        for key, hits in local.items():
            confirms=received.get(key,[])
            if len(hits)!=1 or len(confirms)!=1: continue
            elapsed=(confirms[0]['Timestamp']-hits[0]['Timestamp'])*1000/data['timestampFrequency']
            if elapsed<0: negative+=1
            else: latencies.append(elapsed)
        latencies.sort()
        q=lambda p:latencies[min(len(latencies)-1,math.ceil(len(latencies)*p)-1)] if latencies else None
        result['peers'].append({'view':path.name.split('.')[0],'liveIngressUnique':len(keys),
            'authorityBacked':len(keys&authority), 'authorityAbsent':len(keys-authority),
            'liveIngressByWeapon':dict(Counter(e['Weapon'] for e in ingress)),
            'localPredictionsRetained':sum(map(len,local.values())), 'latencySamples':len(latencies),
            'negativeCandidateLatencies':negative, 'localHitToIngressP90Ms':q(.9),'localHitToIngressP99Ms':q(.99),
             'ringOverwritten':data['overwritten'], 'latenciesMs':latencies,
            'presentationClassified':len(classified), 'sameShotMatchedOrNative':len(same_shot),
            'classificationByStage':stage_counts, 'classifiedAuthorityBacked':len(classified&authority),
            'classifiedAuthorityAbsent':len(classified-authority), 'drawSubmitted':len(submitted),
            'drawSubmittedAuthorityAbsent':len(submitted-authority),
            'ingressToDrawSamples':len(draw_delays), 'ingressToDrawP90Ms':draw_q(.9), 'ingressToDrawP99Ms':draw_q(.99),
            'drawInvariants':data.get('drawInvariants'),
            'classificationByWeapon':dict(Counter(e['Weapon'] for e in presentation)),
            'predictedKills':data.get('predictedKills')})
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    folders={path.parent for pattern in ('*/server-impacts.json','*/server-impacts.json.gz') for path in a.root.glob(pattern)}
    rows=[report(path) for path in sorted(folders)]
    a.output.write_text(json.dumps({'schema':1,'scenarios':rows},indent=2)+'\n')
    print(json.dumps({'scenarios':len(rows),'output':str(a.output)}))
