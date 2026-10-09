#!/usr/bin/env python3
"""Join opt-in NetImpactDiagnostics exports without comparing process clocks."""
import argparse, csv, json
from pathlib import Path

def join(paths):
    rows = {}
    for path in paths:
        data = json.loads(Path(path).read_text())
        for event in data['events']:
            identity = event['Identity']
            key = json.dumps(identity, sort_keys=True, separators=(',', ':'))
            row = rows.setdefault(key, {'identity': identity, 'views': {}})
            row['views'].setdefault(Path(path).stem, []).append({k: event[k] for k in ('Stage', 'Weapon', 'LocalFrame', 'Timestamp')})
    return list(rows.values())

if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('inputs', nargs='+'); p.add_argument('--output', required=True)
    a = p.parse_args(); rows = join(a.inputs)
    Path(a.output + '.json').write_text(json.dumps({'schema': 1, 'events': rows}, indent=2)+'\n')
    with open(a.output + '.csv', 'w', newline='') as output:
        w = csv.writer(output, lineterminator="\n"); w.writerow(['identity','view','stage','weapon','local_frame','process_timestamp'])
        for row in rows:
            for view, events in row['views'].items():
                for e in events: w.writerow([json.dumps(row['identity'],sort_keys=True),view,e['Stage'],e['Weapon'],e['LocalFrame'],e['Timestamp']])
