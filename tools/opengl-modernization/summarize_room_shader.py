"""Summarize completed short room/shader captures; never starts native work."""
import argparse
import hashlib
import json
import math
import pathlib
import statistics

ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'artifacts/opengl-modernization';DEST=ROOT/'docs/performance'
PREFIX='room-shader';FEATURE='both'
CASES=('solo','two','shadow','bomb','impact','combat')
PLANS={'same':[('baseline','a'),('candidate','a'),('candidate','b'),('baseline','b')],
       'profile':[('baseline','a'),('candidate','a')],'cross':[('baseline','a'),('candidate','a')]}

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def relative(path):
    try:return str(path.relative_to(ROOT))
    except ValueError:return str(path)
def median(values):
    values=[v for v in values if v is not None]
    if any(isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) or v<0 for v in values):
        raise ValueError('Invalid numeric telemetry')
    return statistics.median(values) if values else None

def read(name,group,arm,case=None):
    rp,lp,ip=(OUT/(name+suffix) for suffix in ('.json','.log','.invocation.json'))
    result=json.loads(rp.read_text());inv=json.loads(ip.read_text());raw=lp.read_text()
    if result.get('complete') is not True or inv.get('group')!=group or inv.get('forceLegacy')!=0:
        raise ValueError('Incomplete/group/forceLegacy mismatch')
    mvid=result['assemblyMvid'];expected=inv.get('expectedAssemblyMvid') or inv.get('actualAssemblyMvid')
    if expected and mvid!=expected:raise ValueError('Invocation identity mismatch')
    identity_path=OUT/(PREFIX+'-'+inv['build']+'-identity.json')
    if not identity_path.exists():raise ValueError('Missing persisted build identity')
    if identity_path.exists():
        identity=json.loads(identity_path.read_text())
        if mvid!=identity['assemblyMvid'] or inv['binarySha256']!=identity['binarySha256']:
            raise ValueError('Persisted build identity mismatch')
    if inv.get('rawResultSha256') and inv['rawResultSha256']!=sha(rp):raise ValueError('Result modified after capture')
    if inv.get('rawLogSha256') and inv['rawLogSha256']!=sha(lp):raise ValueError('Log modified after capture')
    enabled=arm=='candidate';flags=inv['requestedFlags']
    if any(flags.get(key)!=1 for key in ('ROOM_CACHE','DEPTH_KEEP','SHADER_VARIANTS')):
        raise ValueError('Existing remainder features must remain enabled')
    expected_flags={'ROOM_GROUPS':int(enabled) if FEATURE in ('both','room') else 1,
                    'PLAIN_SHADER':int(enabled) if FEATURE in ('both','shader') else 1}
    if any(flags.get(key)!=value for key,value in expected_flags.items()):
        raise ValueError('Room/shader flag arm mismatch')
    if inv.get('bundle')!='room-shader' or inv.get('feature')!=FEATURE:
        raise ValueError('Experiment bundle/feature mismatch')
    if flags.get('DEPTH_VERTEX')!=0 or flags.get('PROCEDURAL_STREAM')!=0:
        raise ValueError('Disabled experiment unexpectedly active')
    visual=group=='visual'
    desired={'room':'MP3 PROVING GROUND' if case=='combat' or not visual else 'MP9 CRYOCHASM',
             'cap':144 if visual else -1,'framebuffer':[1280,720] if visual else [1920,1080],
             'renderSize':[1280,720] if visual else [1920,1080],
             'shadows':'High' if case=='shadow' else 'Off'}
    if any(result.get(key)!=value for key,value in desired.items()):raise ValueError('Actual workload/target mismatch')
    cleanup=result['cleanup']
    if cleanup.get('shadowReleased') is not True:raise ValueError('Shadow cleanup failure')
    if any(value!=0 for key,value in cleanup.items() if key.endswith('After')):raise ValueError('Live owned resource after unload')
    if not visual and (result.get('captures') or result.get('captureKillFeedLifetimeStabilized')):
        raise ValueError('Readback/stable HUD in performance run')
    if visual and len(result['captures'])!=(2 if case=='combat' else 1):raise ValueError('Unexpected simulation snapshot count')
    for key in ('bombVisibility','impactPresentation'):
        if result.get(key) and result[key].get('status')!='PASS':raise ValueError(key+' failed')
    outline=result.get('outlineOcclusion')
    if outline and (outline.get('occludedPixels')!=0 or outline.get('visiblePixels',0)<=0):raise ValueError('Outline occlusion failed')
    metrics={key:result[key] for key in ('meanCompletedFrameFps','frameIntervalP50Ms','frameIntervalP95Ms','frameIntervalP99Ms',
        'onePercentLowEstimateFps','samples','simulationHz','droppedSimulationSteps','stalls','measurementSeconds')}
    for value in metrics.values():median([value])
    if not visual and (metrics['droppedSimulationSteps'] or metrics['stalls']):raise ValueError('Performance simulation drops/stalls')
    config={key:result[key] for key in ('room','cap','effectiveCap','shadows','vendor','renderer','glVersion','softwareFrequency','vsync','framebuffer','renderSize')}
    record=dict(name=name,group=group,case=case,arm=arm,build=inv['build'],assemblyMvid=mvid,
        configuration=config,harness=metrics,cleanup=cleanup,invocation=inv,
        rawResult=relative(rp),rawResultSha256=sha(rp),rawLog=relative(lp),rawLogSha256=sha(lp),
        rawInvocation=relative(ip),rawInvocationSha256=sha(ip),
        window={key:result[key] for key in ('initialWindow','finalWindow','focusTransitions','visibilityTransitions','windowStateTransitions','windowEvents')},
        captureKillFeedLifetimeStabilized=result.get('captureKillFeedLifetimeStabilized',False),snapshotCount=len(result['captures']))
    if group=='profile':
        before,after=raw.split('NATIVERENDER measurement-start',1)
        frames=[json.loads(line.split('[glframe-json]',1)[1]) for line in after.splitlines() if '[glframe-json]' in line][2:]
        ids={f['window'] for f in frames};passes=[json.loads(line.split('[glpasses-json]',1)[1]) for line in raw.splitlines() if '[glpasses-json]' in line]
        passes=[p for p in passes if p['window'] in ids]
        if any(f.get('schema')!=1 or f.get('samples')!=240 for f in frames):raise ValueError('Incompatible profile window')
        if not frames or len(ids)!=len(frames) or len(passes)!=len(frames) or len({p['window'] for p in passes})!=len(frames):
            raise ValueError('Missing/duplicate CPU-frame alignment')
        if len({json.dumps(f['context'],sort_keys=True) for f in frames})!=1 or frames[0]['context']['assemblyMvid']!=mvid:
            raise ValueError('Profile context mismatch')
        record.update(profiled=True,context=frames[0]['context'],retainedWindows=len(frames),
            sufficientFourWindowSampling=len(frames)>=4,postMeasurementWarmupWindows=2,
            preMeasurementWindows=sum('[glframe-json]' in line for line in before.splitlines()),
            zeroSimulationTelemetryWindows=sum(f['simulationHz']==0 for f in frames),
            metrics={key:median([f[key] for f in frames]) for key in ('frameIntervalP95Ms','frameIntervalP99Ms','renderCpuP99Ms','simulationHz')},
            cpuP99Ms={key:median([p['cpu'][key]['p99Ms'] for p in passes]) for key in passes[0]['cpu']},
            counters={key:median([p['counters'][key] for p in passes]) for key in passes[0]['counters']},
            meanDepthReplayDrawsSaved=statistics.mean(p['counters']['DepthReplayDrawsSaved'] for p in passes),
            meanDepthSnapshotCopies=statistics.mean(p['counters']['DepthSnapshotCopies'] for p in passes),
            maxDroppedSteps=max(f['droppedSimulationSteps'] for f in frames),maxStalls=max(f['stalls'] for f in frames),gpuStatus='UNVERIFIED')
        if record['maxDroppedSteps'] or record['maxStalls']:raise ValueError('Retained profile simulation drops/stalls')
    else:
        record['profiled']=False
        if not visual and ('[glframe-json]' in raw or '[glpasses-json]' in raw):raise ValueError('No-profile control contains profile telemetry')
    return record

def main():
    global OUT,DEST,PREFIX,FEATURE
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prefix',default='room-shader')
    parser.add_argument('--output',type=pathlib.Path,default=OUT)
    parser.add_argument('--destination',type=pathlib.Path,default=DEST)
    parser.add_argument('--feature',choices=['both','room','shader'],default='both')
    parser.add_argument('--cross',action='store_true',help='Include optional intentionally mixed-build cross pair')
    parser.add_argument('--cross-abba',action='store_true',help='Include the optional reverse-order cross pair as well')
    args=parser.parse_args()
    if not args.prefix or args.prefix in ('.','..') or any(c in args.prefix for c in '/\\'):
        parser.error('prefix must be a filename component')
    OUT=args.output.resolve();DEST=args.destination.resolve();PREFIX=args.prefix;FEATURE=args.feature
    DEST.mkdir(parents=True,exist_ok=True)
    plans={g:p for g,p in PLANS.items() if g!='cross' or args.cross or args.cross_abba}
    if args.cross_abba:plans['cross']=PLANS['cross']+[('candidate','b'),('baseline','b')]
    runs=[];fixtures=[];pending=[];errors=[]
    for group,plan in plans.items():
        for arm,repeat in plan:
            name=PREFIX+'-'+group+'-'+arm+'-'+repeat
            if not (OUT/(name+'.json')).exists():pending.append(name);continue
            try:runs.append(read(name,group,arm,'combat'))
            except (ValueError,KeyError,OSError,json.JSONDecodeError,TypeError) as exc:errors.append(dict(name=name,error=str(exc)))
    for case in CASES:
        for arm in ('baseline','candidate'):
            name=PREFIX+'-visual-'+case+'-'+arm+'-a'
            if not (OUT/(name+'.json')).exists():pending.append(name);continue
            try:fixtures.append(read(name,'visual',arm,case))
            except (ValueError,KeyError,OSError,json.JSONDecodeError,TypeError) as exc:errors.append(dict(name=name,error=str(exc)))
    all_records=runs+fixtures;current_ids={r['assemblyMvid'] for r in all_records if r['build']=='current'}
    if len(current_ids)>1:errors.append(dict(name='identity',error='Current group mixes MVIDs'))
    for group in plans:
        selected=[r for r in runs if r['group']==group]
        if len({json.dumps(r['configuration'],sort_keys=True) for r in selected})>1:
            errors.append(dict(name=group,error='Matched group actual configuration differs'))
    comparisons=[];completed_cases=[]
    if fixtures:
        from PIL import Image
        for case in CASES:
            pair=[r for r in fixtures if r['case']==case]
            if len(pair)!=2:continue
            directories={r['arm']:OUT/r['name'] for r in pair}
            files={arm:{p.relative_to(directory) for p in directory.rglob('*') if p.is_file()
                and (p.suffix.lower()=='.png' or p.suffix.lower()=='.bin' and 'depth' in p.name.lower())}
                for arm,directory in directories.items()}
            if not files['baseline'] or files['baseline']!=files['candidate']:
                errors.append(dict(name=case,error='Visual file sets differ or are empty'));continue
            completed_cases.append(case)
            for file in sorted(files['baseline']):
                baseline=directories['baseline']/file;candidate=directories['candidate']/file
                if file.suffix.lower()=='.png':
                    with Image.open(baseline) as image:a=image.convert('RGBA');a_size=a.size;a_pixels=a.tobytes()
                    with Image.open(candidate) as image:b=image.convert('RGBA');b_size=b.size;b_pixels=b.tobytes()
                    identical=a_size==b_size and a_pixels==b_pixels;format_name='decoded-RGBA8'
                else:
                    identical=baseline.read_bytes()==candidate.read_bytes();format_name='float32-depth-bytes'
                comparisons.append(dict(case=case,file=str(file),format=format_name,identical=identical,
                    reference=relative(baseline),candidate=relative(candidate),referenceSha256=sha(baseline),candidateSha256=sha(candidate)))
    if runs:
        groups={group:{'complete':len([r for r in runs if r['group']==group])==len(plan),
                      'runs':[r['name'] for r in runs if r['group']==group]} for group,plan in plans.items()}
        report=dict(schema=1,assemblyMvids=sorted({r['assemblyMvid'] for r in runs}),groups=groups,
            method='Short 12-second ABBA aggregates; profile AB uses records after harness measurement marker with two completed windows discarded and CPU IDs aligned. Existing ROOM_CACHE/DEPTH_KEEP/SHADER_VARIANTS stay enabled. Selected ROOM_GROUPS/PLAIN_SHADER feature(s) toggle baseline off/candidate on; unselected feature stays enabled. Uniform cache changes are unconditional in both current arms. Cross group intentionally mixes previous/current builds.',
            gpuStatus='UNVERIFIED',runs=runs,pending=[n for n in pending if '-visual-' not in n],errors=errors)
        (DEST/(PREFIX+'-results.json')).write_text(json.dumps(report,indent=2)+'\n')
    if fixtures:
        complete=len(completed_cases)==len(CASES)
        report=dict(schema=1,source='native-game-on-device',separateCapture=True,
            assemblyMvid=next(iter(current_ids)) if len(current_ids)==1 else None,complete=complete,
            passed=complete and bool(comparisons) and all(c['identical'] for c in comparisons) and not errors,
            cases=completed_cases,comparisons=comparisons,fixtures=fixtures,
            notes='Combat is ten seconds/two simulation snapshots and uses capture-only stable-HUD lifetime fixture; performance never uses it. PNG equality compares decoded RGBA and dimensions; depth compares exact float32 bytes.',
            pending=[n for n in pending if '-visual-' in n],errors=errors)
        (DEST/(PREFIX+'-visual.json')).write_text(json.dumps(report,indent=2)+'\n')
    if all_records or errors or pending:
        report=dict(schema=1,scope='Present completed native results only; missing groups are pending, not passes.',
            presentValidatedRuns=len(all_records),currentAssemblyMvids=sorted(current_ids),
            pending=pending,errors=errors,passedPresentValidation=bool(all_records) and not errors,
            nativeFixtureCount=len(fixtures),comparisonCount=len(comparisons),identicalComparisons=sum(c['identical'] for c in comparisons),
            limitations='Mac-only short measurements. GPU timing unavailable. Short AB/ABBA is descriptive, not statistical confidence or full device/lifecycle acceptance. Cross-build data is not isolated feature attribution.',runs=all_records)
        (DEST/(PREFIX+'-validation.json')).write_text(json.dumps(report,indent=2)+'\n')
    print('validated',len(all_records),'comparisons',len(comparisons),'pending',len(pending),'errors',len(errors))
    return 1 if errors else 0

if __name__=='__main__':raise SystemExit(main())
