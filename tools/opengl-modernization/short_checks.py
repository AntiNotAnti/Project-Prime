"""Short, explicitly scheduled remainder checks. Importing never launches native GL."""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
ON = ['VAO','DYNAMIC_STREAM','UNIFORM_REVISIONS','STATIC_CACHE','SHADOW_FAST','HUD_SAMPLERS',
      'DEPTH_BATCH','ATLAS_EVICTION','HUD_DISCARD','DEPTH_SHADER','GLES_UNIFORMS',
      'HUD_GLYPHS','HUD_SPRITES','DEPTH_PREP','PROCEDURAL_BATCH']
OFF = ['ROOM_CACHE','DEPTH_VERTEX','PROCEDURAL_STREAM']
CASES = [('solo',['-players','1']),
         ('two',['-players','2','-hunter','Spire','-outlines','Red','-outlinecheck']),
         ('shadow',['-players','1','-shadows','High']),
         ('bomb',['-players','4','-bombcheck']),
         ('impact',['-players','2','-impactcheck']),
         ('combat',['-players','8'])]

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def capture(args, enabled, repeat, case, extra, build):
    group = args.group
    OUT = args.output
    executable = args.previous_harness if build=='previous' else args.harness
    if executable.is_symlink():raise RuntimeError('Materialize harness beside intended client first: '+str(executable))
    client=executable.with_name('ProjectPrime.dll')
    binary={'harness':sha(executable),'client':sha(client)}
    identity_path=OUT/(args.prefix+'-'+build+'-identity.json')
    identity=json.loads(identity_path.read_text()) if identity_path.exists() else None
    if identity and identity['binarySha256']!=binary:raise RuntimeError('Build changed since earlier remainder capture')
    arm='candidate' if enabled else 'baseline'
    name=args.prefix+'-'+group+'-'+(case+'-' if group=='visual' else '')+arm+'-'+repeat
    paths={kind:OUT/(name+suffix) for kind,suffix in [('result','.json'),('log','.log'),('invocation','.invocation.json')]}
    shots=OUT/name
    if any(path.exists() for path in paths.values()) or shots.exists():raise RuntimeError('Refusing to overwrite '+name)
    env=dict(os.environ,PROJECT_PRIME_GL_FORCE_LEGACY='0')
    if args.user_data is not None:env['PROJECT_PRIME_USER_DATA']=str(args.user_data)
    for key in ('PROJECT_PRIME_GL_PROFILE','PROJECT_PRIME_GL_GPU_TIMER','PROJECT_PRIME_GL_FORCE_DEPTH_REPLAY','PROJECT_PRIME_GL_FORCE_STATE_REQUESTS'):
        env.pop(key,None)
    flags={flag:1 for flag in ON};flags.update({flag:0 for flag in OFF})
    if args.bundle=="remainder":
        flags.update(ROOM_CACHE=int(enabled),DEPTH_KEEP=int(enabled),SHADER_VARIANTS=int(enabled))
    elif args.bundle=="room-shader":
        flags.update(ROOM_CACHE=1,DEPTH_KEEP=1,SHADER_VARIANTS=1,
            ROOM_GROUPS=int(enabled) if args.feature in ("both","room") else 1,
            PLAIN_SHADER=int(enabled) if args.feature in ("both","shader") else 1)
    else:
        flags.update(ROOM_CACHE=1,DEPTH_KEEP=1,SHADER_VARIANTS=1,ROOM_GROUPS=1,PLAIN_SHADER=1)
        flags.update({flag:int(enabled) for flag in ('ROOM_IDENTITY','MESH_OBJECT','SHADOW_PREP','UNLIT_SHADER','SHADOW_GROUPS')})
    for flag,value in flags.items():env['PROJECT_PRIME_GL_'+flag]=str(value)
    visual=group=='visual'
    command=[args.dotnet,str(executable),'-room','MP3 PROVING GROUND' if case=='combat' else 'MP9 CRYOCHASM',
             '-seconds',str(10 if visual and case=='combat' else 8 if visual else 12),
             '-hz','144' if visual else '-1','-size','1280x720' if visual else '1920x1080',
             '-output',str(paths['result'])]+extra
    if visual:
        command+=['-shots',str(shots)]
        command+=['-stablehud'] if case=='combat' else ['-idle']
    elif group=='profile':command+=['-glprofile']
    invocation=dict(schema=1,group=group,build=build,profiled=group=='profile',separateCapture=visual,
        command=command,requestedFlags=flags,forceLegacy=0,binarySha256=binary,
        expectedAssemblyMvid=identity['assemblyMvid'] if identity else None,
        executable=str(executable),client=str(client),
        unconditionalUniformCacheChanges='Present in current build in both same-build arms; cross build controls are intentionally mixed-MVID.')
    if args.bundle!='remainder':invocation.update(bundle=args.bundle,feature=args.feature)
    paths['invocation'].write_text(json.dumps(invocation,indent=2)+'\n')
    with paths['log'].open('w') as log:completed=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT)
    if completed.returncode:raise SystemExit(completed.returncode)
    result=json.loads(paths['result'].read_text());mvid=result.get('assemblyMvid')
    if result.get('complete') is not True or not mvid or identity and identity['assemblyMvid']!=mvid:
        raise RuntimeError('Incomplete/wrong-MVID capture '+name)
    if binary!={'harness':sha(executable),'client':sha(client)}:raise RuntimeError('Binary changed during capture')
    if not identity:
        identity_path.write_text(json.dumps(dict(schema=1,assemblyMvid=mvid,binarySha256=binary,
            firstSuccessfulCapture=name),indent=2)+'\n')
    invocation.update(actualAssemblyMvid=mvid,rawResultSha256=sha(paths['result']),rawLogSha256=sha(paths['log']))
    paths['invocation'].write_text(json.dumps(invocation,indent=2)+'\n')
    print(name,'MVID',mvid,'complete',flush=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--group',choices=['same','profile','visual','cross'],required=True)
    parser.add_argument('--bundle',choices=['remainder','room-shader','completion'],default='remainder',help='Remainder retains the original controls; room-shader keeps those features enabled')
    parser.add_argument('--feature',choices=['both','room','shader'],default='both',help='Room-shader experiment: toggle selected feature(s); hold the other enabled')
    parser.add_argument('--dotnet',default='dotnet')
    parser.add_argument('--harness',type=pathlib.Path,default=ROOT/'tools/native-render-check/bin/Release/net10.0/native-render-check.dll')
    parser.add_argument('--output',type=pathlib.Path,default=ROOT/'artifacts/opengl-modernization')
    parser.add_argument('--user-data',type=pathlib.Path,help='Otherwise inherit PROJECT_PRIME_USER_DATA')
    parser.add_argument('--previous-harness',type=pathlib.Path,help='Required for the intentionally mixed-build cross group')
    parser.add_argument('--prefix',default='short-check')
    parser.add_argument('--limit',type=int,help='Run only N scheduled arms for smoke')
    parser.add_argument('--start','--start-index',dest='start_index',type=int,default=0,help='Zero-based resume index')
    args=parser.parse_args()
    if args.bundle!="room-shader" and args.feature!="both":parser.error("--feature requires --bundle room-shader")
    if not args.prefix or args.prefix in (".","..") or any(c in args.prefix for c in "/\\"):
        parser.error("prefix must be a nonempty filename component")
    if args.group=="cross" and args.previous_harness is None:parser.error("cross requires --previous-harness")
    # Keep the final component unresolved so capture can reject symlinked harnesses.
    args.harness=args.harness.absolute()
    if args.previous_harness is not None:args.previous_harness=args.previous_harness.absolute()
    args.output=args.output.resolve()
    cases=CASES+([('unlit',['-players','8','-lighting','false']),('shadow-ultra',['-players','1','-shadows','Ultra'])] if args.bundle=='completion' else [])
    if args.group=='visual':plan=[(enabled,'a',case,extra,'current') for case,extra in cases for enabled in (False,True)]
    elif args.group=='same':plan=[(enabled,repeat,'combat',['-players','8'],'current') for enabled,repeat in [(False,'a'),(True,'a'),(True,'b'),(False,'b')]]
    elif args.group=='profile':plan=[(enabled,'a','combat',['-players','8'],'current') for enabled in (False,True)]
    else:plan=[(False,'a','combat',['-players','8'],'previous'),(True,'a','combat',['-players','8'],'current')]
    if args.start_index<0 or args.start_index>=len(plan):parser.error('start-index outside plan')
    if args.limit is not None and args.limit<=0:parser.error('limit must be positive')
    selected=plan[args.start_index:]
    if args.limit is not None:selected=selected[:args.limit]
    args.output.mkdir(parents=True,exist_ok=True)
    for enabled,repeat,case,extra,build in selected:capture(args,enabled,repeat,case,extra,build)

if __name__=='__main__':main()
