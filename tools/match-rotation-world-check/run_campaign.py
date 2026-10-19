"""Mac-only real server/world campaign; extracted assets are read in place."""
import argparse,hashlib,json,os,shutil,subprocess,sys,tempfile,time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/native-render-check'))
from campaign_identity import asset_identity,isolate_user_data

DOTNET=Path('/Users/jarrett/.dotnet/dotnet')
CHECKPOINT=ROOT/'artifacts/rmlui-checkpoint-rml58-final-20261017/compiled-client'
EXPECTED='b64c20891de93a6db4e9bbd0ed0b045c96029796ef030c1cdc3e02d4e1961aa3'
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--checkpoint',type=Path,default=CHECKPOINT)
    parser.add_argument('--expected-sha',default=EXPECTED)
    parser.add_argument('--require-retirement',action='store_true')
    parser.add_argument('--evidence-root',type=Path,default=ROOT/'artifacts/cpp05-actual-world-rml58')
    parser.add_argument('cases',nargs='*')
    options=parser.parse_args()
    checkpoint=options.checkpoint.resolve(strict=True)
    assert sha(checkpoint/'ProjectPrime.dll')==options.expected_sha
    private=Path(tempfile.mkdtemp(prefix='prime-cpp05-world-')).resolve()
    durable=options.evidence_root.resolve()/private.name
    durable.mkdir(parents=True)
    tool=private/'tool'
    shutil.copytree(ROOT/'tools/match-rotation-world-check/bin/Release/net10.0',tool)
    for source in checkpoint.iterdir():
        if source.is_file() and source.suffix in ('.dll','.dylib','.so'):
            shutil.copy2(source,tool/source.name)
    assert sha(tool/'ProjectPrime.dll')==options.expected_sha
    records=[]
    cases=[(0,0,'idle'),(0,2,'spectator'),(2,1,'humans2'),(4,2,'humans4'),(8,0,'humans8')]
    if options.cases:cases=[case for case in cases if case[2] in options.cases]
    assert cases,'no matching campaign cases'
    for humans,bots,label in cases:
        arm=private/label;arm.mkdir()
        userdata,identity=isolate_user_data(Path.home()/'Library/Application Support/Project Prime',arm)
        export=arm/'export';export.mkdir()
        paths=userdata/'paths.txt'
        lines=[line for line in paths.read_text().splitlines() if not line.startswith('Export=')]
        paths.write_text('\n'.join(lines+[f'Export={export}'])+'\n')
        env=os.environ.copy()
        for key in list(env):
            if key.startswith(('PRIME_NET2','PRIME_NET_AI','PROJECT_PRIME_CAREER','SUPABASE')) or key.endswith('_URL') and key.startswith('PROJECT_PRIME'):
                env.pop(key,None)
        env['PROJECT_PRIME_USER_DATA']=str(userdata)
        env['PRIME_NET2_EVIDENCE']=str(arm/'server-evidence.json')
        result=arm/'result.json';log=durable/f'{label}.log'
        command=[str(DOTNET),str(tool/'nettest.dll'),str(humans),str(bots),'idle' if label=='idle' else 'world',str(result)]
        if options.require_retirement:command.append('--require-retirement')
        print(f'WORLD CAMPAIGN START {label}: actualHumans={humans}, bots={bots}, observer={int(label=="spectator")}, private={arm}',flush=True)
        started=time.monotonic()
        with log.open('w') as output:
            process=subprocess.Popen(command,cwd=userdata,env=env,stdout=output,stderr=subprocess.STDOUT)
            try:code=process.wait(timeout=270)
            except subprocess.TimeoutExpired:
                (arm/'server-evidence.json.stop').touch()
                try:code=process.wait(timeout=25)
                except subprocess.TimeoutExpired:
                    process.terminate()
                    try:code=process.wait(timeout=10)
                    except subprocess.TimeoutExpired:process.kill();code=process.wait()
        receipt={'label':label,'command':command,'cwd':str(userdata),'exitCode':code,'elapsedSeconds':time.monotonic()-started,
            'log':str(log.relative_to(ROOT)),'logSha256':sha(log),'privateDirectory':str(arm),
            'assetIdentity':{'sha256':identity['sha256'],'fileCount':identity['fileCount'],'bytes':identity['bytes'],'roots':identity['roots']},
            'productionDllSha256':sha(tool/'ProjectPrime.dll'),'fixtureDllSha256':sha(tool/'nettest.dll'),
            'requiresRetirement':options.require_retirement}
        after=asset_identity(paths)
        receipt['assetAfter']={key:after[key] for key in ('sha256','fileCount','bytes')}
        receipt['assetsUnchanged']=identity['sha256']==after['sha256']
        if result.exists():
            target=durable/f'{label}-result.json';shutil.copy2(result,target)
            receipt['result']=str(target.relative_to(ROOT));receipt['resultSha256']=sha(target)
        records.append(receipt)
        (durable/'manifest.json').write_text(json.dumps({'scope':'Mac-only full server Run/native authority worlds; no client worlds/GL/real services/devices','privateRoot':str(private),'runs':records},indent=2)+'\n')
        print(f'WORLD CAMPAIGN END {label}: trueExit={code}, elapsed={receipt["elapsedSeconds"]:.1f}s, log={log}',flush=True)
        if code!=0 or not receipt['assetsUnchanged']:
            print('\n'.join(log.read_text().splitlines()[-35:]),flush=True)
            return 1
    print('WORLD CAMPAIGN PASS '+str(durable),flush=True)
    return 0

if __name__=='__main__':raise SystemExit(main())
