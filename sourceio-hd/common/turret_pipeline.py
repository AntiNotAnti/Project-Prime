"""Build one isolated alternate pack from frozen Source and native contracts.

This module deliberately does not install or launch GPU acceptance. Root owns
those stages after the static audit and manual capture review.
"""
import argparse,json,subprocess,sys,hashlib,shutil,platform
from pathlib import Path
from turret_provenance import capture,verify
COMMON=Path(__file__).resolve().parent;REPO=COMMON.parents[1]
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
p=argparse.ArgumentParser();p.add_argument('stage',choices=['build','audit','validate']);p.add_argument('--config',required=True);p.add_argument('--source-root',default='/Users/jarrett/Downloads/mph/converted-sourceio');p.add_argument('--output',required=True);p.add_argument('--blender',default='/Applications/Blender.app/Contents/MacOS/Blender');p.add_argument('--exe');a=p.parse_args()
cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();source=Path(a.source_root).resolve()/cfg['sourceRelative'];model=root/'kit/starter'/cfg['hunter'].lower()/'halfturret_weighted4.glb'
if a.stage=='build':
    assert cfg['skinning']=='weighted4','Rigid alternate conversion is a separate state-aware stage'
    assert digest(source)==cfg['sourceSha256'];assert not (root/'RELEASE-LOCK.json').exists()
    if (root/'build-result.json').exists():raise RuntimeError('Choose a fresh output for an existing build')
    root.mkdir(parents=True,exist_ok=True);kit=REPO/cfg['nativeKit'];shutil.copytree(kit,root/'kit',dirs_exist_ok=True)
    provenance_path=root/'prebuild-provenance.json'
    if provenance_path.exists():
        provenance=json.loads(provenance_path.read_text());verify(provenance)
        assert provenance['configurationSha256']==digest(cp) and provenance['sourceGlbSha256']==digest(source)
    else:
        provenance=capture(COMMON,REPO,cp,source,kit);provenance_path.write_text(json.dumps(provenance,indent=2)+'\n')
    # Reuse only native contracts; accepted biped models are not part of this pack.
    for f in (root/'kit/starter').rglob('*.glb'):f.unlink()
    refs={str(f.relative_to(root)):digest(f) for f in (root/'kit').rglob('*') if f.is_file() and (f.suffix in ['.dae','.py'] or f.name=='native-reference.json' or f.name=='halfturret-weighted4-entry.json')}
    for name,h in refs.items():assert provenance['files'][str((kit/Path(name).relative_to('kit')).resolve())]==h,('Copied native reference differs from prebuild source',name)
    (root/'generated-reference-hashes.json').write_text(json.dumps(refs,indent=2)+'\n')
    derived=root/'build-config';derived.mkdir(exist_ok=True)
    if (derived/'config.json').exists():assert json.loads((derived/'config.json').read_text())==cfg,'Partial build was created with another configuration; choose a fresh output'
    (derived/'config.json').write_text(json.dumps(cfg,indent=2)+'\n');(derived/'material-map.json').write_text(json.dumps(cfg['materialMap'],indent=2)+'\n')
    from turret_select import curate
    atlas_source=curate(cfg,source,root/'atlas-source.glb')
    with (root/'atlas.log').open('w') as log:subprocess.run([sys.executable,str(COMMON/'atlas.py'),'--config',str(derived/'config.json'),'--source',str(atlas_source),'--output',str(root)],stdout=log,stderr=subprocess.STDOUT,check=True)
    verify(provenance)
    with (root/'convert.log').open('w') as log:subprocess.run([a.blender,'-b','--python-exit-code','1','--python',str(COMMON/'turret_convert.py'),'--','--config',str(cp),'--source',str(source),'--output',str(root),'--repo',str(REPO)],stdout=log,stderr=subprocess.STDOUT,check=True)
    from turret_materials import companions
    companions(cfg,root,source);verify(provenance)
    result={'hunter':cfg['hunter'],'part':'halfturret','sourceSha256':digest(source),'configSha256':digest(cp),'shippingGlbSha256':digest(model),'generatedExporterSha256':digest(root/'kit/prepare-halfturret-weighted4.py'),'status':'Built; static audit and runtime acceptance required','nativeKit':str(kit),'converterSha256':digest(COMMON/'turret_convert.py')}
    (root/'build-result.json').write_text(json.dumps(result,indent=2)+'\n')
    subprocess.run([sys.executable,str(COMMON/'turret_audit.py'),'--config',str(cp),'--source',str(source),'--output',str(root)],check=True)
    verify(provenance)
    if a.exe:
        from install import validate
        validate(Path(a.exe),root/'kit/starter',root/'validation.log')
    print('TURRET_BUILD_STATIC_PASS',cfg['hunter'],model)
elif a.stage=='audit':subprocess.run([sys.executable,str(COMMON/'turret_audit.py'),'--config',str(cp),'--source',str(source),'--output',str(root)],check=True)
else:
    if not a.exe:p.error('--exe is required for validate')
    from install import validate
    validate(Path(a.exe),root/'kit/starter',root/'validation.log')
