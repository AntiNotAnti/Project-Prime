"""One command per stage; hunter behavior lives entirely in JSON configuration."""
import argparse,json,subprocess,sys,hashlib,platform
from pathlib import Path
from runtime import run
from install import install,rollback,validate,digest
COMMON=Path(__file__).resolve().parent;REPO=COMMON.parents[1]
BUILD=REPO/'src/MphRead/bin/Release/net10.0'
if platform.system()=='Darwin':BUILD/= 'osx-arm64' if platform.machine()=='arm64' else 'osx-x64'
p=argparse.ArgumentParser();p.add_argument('stage',choices=['build','validate','accept','merge','rollback']);p.add_argument('--config',required=True);p.add_argument('--source-root');p.add_argument('--output',required=True);p.add_argument('--blender',default='/Applications/Blender.app/Contents/MacOS/Blender');p.add_argument('--exe',default=str(BUILD/'ProjectPrime'));p.add_argument('--room',default='MIDSHIP');p.add_argument('--live',default=str(Path.home()/'Library/Application Support/Project Prime/character-models/default'));p.add_argument('--userdata');p.add_argument('--snapshot');p.add_argument('--fidelity',action='store_true')
a=p.parse_args();cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();root.mkdir(parents=True,exist_ok=True);pack=root/'kit/starter';live=Path(a.live).resolve();exe=Path(a.exe).resolve()
def worker(name,args):
    with (root/(name+'.log')).open('w') as log:
        subprocess.run([a.blender,'-b','--python-exit-code','1','--python',str(COMMON/(name+'.py')),'--',*map(str,args)],stdout=log,stderr=subprocess.STDOUT,check=True)
def accepted():
    audit=json.loads((root/'audit.json').read_text());check=json.loads((root/'acceptance/acceptance.json').read_text())
    for name in ['config.json','bone-map.json','material-map.json']:assert digest(cp.parent/name)==audit['inputs'][name],'Configuration changed after audit'
    assert audit['pass'] and not check['failures'] and check['hunter']==cfg['hunter']
    assert check['testedModelSha256']==audit['shippingGlbSha256']==digest(pack/cfg['hunter'].lower()/'biped_weighted4.glb')
    assert check['submittedFrames']>1000
    if audit.get('inheritedNativeBindScalePreserved'):assert check['requireNativeBindIdentity'] and check['maximumNativeBindError']<.0001
    if audit.get('teamRecolorsRequired'):assert check['teamRecolorCheckedFrames']==120
    if audit.get('surfaceAudit'):
        assert check['authoredSurfaceCheckedFrames']==check['submittedFrames']
        if audit['surfaceAudit']['transparentSourceMaterials']:assert check['transparentSurfaceCheckedFrames']==180
    for label in ['launcher-before','launcher-after']:
        preview=json.loads((root/'acceptance'/(label+'.json')).read_text());assert preview['weightedFrames']==60 and preview['fallbackFrames']==0
    for key in ['airborne','falling','landed','fired','frozen','doubled','morphing','alt','unmorphing','died','respawned']:assert check[key],key
    if audit['muzzleProof']['nativeLocalPoint'] is not None:assert check['muzzleCheckedFrames']==check['submittedFrames'] and check['maximumMuzzleError']<.0001
    return check
if a.stage=='build':
    if (root/'RELEASE-LOCK.json').exists():raise RuntimeError('Output is frozen; choose another output directory')
    if not a.source_root:p.error('--source-root is required for build')
    source=Path(a.source_root).resolve()/cfg['sourceRelative']
    assert digest(source)==cfg['sourceSha256'],'Source asset differs from inspected configuration'
    run(exe,['-charactermodelkit',cfg['hunter'],'-output',root/'kit'],root/'kit-generation.log')
    exporter_hash=digest(root/'kit/prepare-biped-weighted4.py')
    references={str(p.relative_to(root)):digest(p) for p in (root/'kit').rglob('*') if p.is_file() and ('reference' in p.parts or p.name in ['prepare-biped-weighted4.py','native-reference.json','biped-weighted4-entry.json'])}
    (root/'generated-reference-hashes.json').write_text(json.dumps(references,indent=2)+'\n')
    with (root/'atlas.log').open('w') as log:subprocess.run([sys.executable,str(COMMON/'atlas.py'),'--config',str(cp),'--source',str(source),'--output',str(root)],stdout=log,stderr=subprocess.STDOUT,check=True)
    worker('convert',['--config',cp,'--source',source,'--output',root,'--repo',REPO])
    from material_maps import add_material_maps
    add_material_maps(root,pack/cfg['hunter'].lower()/'biped_weighted4.glb')
    from recolors import add_recolors
    add_recolors(cfg,root)
    from material_contract import preserve_alpha
    preserve_alpha(cfg,root,source)
    worker('audit',['--config',cp,'--output',root])
    assert digest(root/'kit/prepare-biped-weighted4.py')==exporter_hash
    validate(exe,pack,root/'validation.log')
    print('BUILD PASS',cfg['hunter'],root)
elif a.stage=='validate':validate(exe,pack,root/'validation.log')
elif a.stage=='accept':
    userdata=Path(a.userdata or __import__('os').environ.get('PROJECT_PRIME_USER_DATA') or Path.home()/'Library/Application Support/Project Prime').resolve()
    assert live==userdata/'character-models/default', 'Acceptance --live must match the selected user-data character-models/default directory'
    snapshot=root/'rollback-before-test'
    if snapshot.exists():raise RuntimeError('Acceptance snapshot already exists; use a fresh output or archive the prior attempt')
    try:
        install(exe,pack,live,snapshot,root/'test-install')
        args=['-characteracceptancecheck',a.room,'-hunter',cfg['hunter'],'-output',root/'acceptance']
        if a.fidelity:args+=['-characterfidelity']
        audit=json.loads((root/'audit.json').read_text())
        if audit['muzzleProof']['nativeLocalPoint'] is not None:args+=['-muzzleaudit',root/'audit.json']
        run(exe,args,root/'acceptance.log',userdata=userdata,hd=True);accepted()
        print('ACCEPTANCE PASS',cfg['hunter'],root/'acceptance')
    finally:
        if snapshot.exists():rollback(exe,snapshot,live,root/'test-install')
elif a.stage=='merge':
    accepted()
    review=json.loads((root/'visual-review.json').read_text());assert review['pass'], 'Visual review is required before merging'
    assert review['modelSha256']==digest(pack/cfg['hunter'].lower()/'biped_weighted4.glb'), 'Visual review is for a different GLB'
    if not a.snapshot:p.error('--snapshot is required for merge')
    install(exe,pack,live,Path(a.snapshot).resolve(),root/'merge-install',merge=True)
    print('MERGED',cfg['hunter'],live)
elif a.stage=='rollback':
    if not a.snapshot:p.error('--snapshot is required for rollback')
    rollback(exe,Path(a.snapshot).resolve(),live,root/'rollback-install');print('ROLLED BACK',live)
