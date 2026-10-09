#!/usr/bin/env python3
"""Join native diagnostic exports. Never infer rendered success from a headless run."""
import argparse, json, math
from collections import Counter
from pathlib import Path

def identity(event):
    return json.dumps(event['Identity'], sort_keys=True, separators=(',', ':'))

def shot_victim(event):
    i=event['Identity']
    return json.dumps({k:i[k] for k in ('Shot','Victim','VictimGeneration','VictimLife')}, sort_keys=True)

def report(folder):
    server=json.loads((folder/'server-impacts.json').read_text())
    authority={identity(e) for e in server['events'] if e['Stage']==1}
    result={'scenario':folder.name, 'rendered':False, 'authorityFactsRetained':len(authority),
            'authorityRingOverwritten':server['overwritten'],
            'authorityByWeapon':dict(Counter(e['Weapon'] for e in server['events'] if e['Stage']==1)), 'peers':[],
            'limitations':['Authority facts may predate peer readiness or lifecycle changes; missing facts are not a measured eligible-delivery failure rate.',
                'Latency is local-hit to live ingress within one process, only for unambiguous one-hit/one-fact shot-victim groups.',
                'No impact-render latency, visual correlation, observer view, or predicted-kill reversal rate is measured.']}
    for path in sorted(folder.glob('peer*-impacts.json')):
        data=json.loads(path.read_text()); ingress=[e for e in data['events'] if e['Stage']==3]
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
        result['peers'].append({'view':path.stem,'liveIngressUnique':len(keys),
            'authorityBacked':len(keys&authority), 'authorityAbsent':len(keys-authority),
            'liveIngressByWeapon':dict(Counter(e['Weapon'] for e in ingress)),
            'localPredictionsRetained':sum(map(len,local.values())), 'latencySamples':len(latencies),
            'negativeCandidateLatencies':negative, 'localHitToIngressP90Ms':q(.9),'localHitToIngressP99Ms':q(.99),
            'ringOverwritten':data['overwritten'], 'latenciesMs':latencies})
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('root',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    rows=[report(path.parent) for path in sorted(a.root.glob('*/server-impacts.json'))]
    a.output.write_text(json.dumps({'schema':1,'scenarios':rows},indent=2)+'\n')
    print(json.dumps({'scenarios':len(rows),'output':str(a.output)}))
