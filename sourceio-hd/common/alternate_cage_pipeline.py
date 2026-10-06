"""Build/audit/validate one isolated stateful cage alternate; no live install."""
import argparse,json,subprocess,sys,shutil
from pathlib import Path
from alternate_cage_provenance import capture,verify,digest
COMMON=Path(__file__).resolve().parent;REPO=COMMON.parents[1]
p=argparse.ArgumentParser();p.add_argument('stage',choices=['build','audit','validate']);p.add_argument('--config',required=True);p.add_argument('--source-root',default='/Users/jarrett/Downloads/mph/converted-sourceio');p.add_argument('--output',required=True);p.add_argument('--blender',default='/Applications/Blender.app/Contents/MacOS/Blender');p.add_argument('--exe');a=p.parse_args();cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();source=Path(a.source_root)/cfg['sourceRelative'];model=root/'kit/starter'/cfg['hunter'].lower()/'altform_weighted4.glb'
if a.stage=='build':
    assert cfg['skinning']=='weighted4' and digest(source)==cfg['sourceSha256'];assert not (root/'RELEASE-LOCK.json').exists();assert not (root/'build-result.json').exists(),'Choose a fresh output for an existing build';root.mkdir(parents=True,exist_ok=True);kit=REPO/cfg['nativeKit'];provenance_path=root/'prebuild-provenance.json'
    if provenance_path.exists():
        provenance=json.loads(provenance_path.read_text());verify(provenance);assert provenance['configurationSha256']==digest(cp) and provenance['sourceGlbSha256']==digest(source)
    else:provenance=capture(COMMON,REPO,cp,source,kit);provenance_path.write_text(json.dumps(provenance,indent=2)+'\n')
    shutil.copytree(kit,root/'kit',dirs_exist_ok=True)
    for f in (root/'kit/starter').rglob('*.glb'):f.unlink()
    refs={str(f.relative_to(root)):digest(f) for f in (root/'kit').rglob('*') if f.is_file() and (f.suffix in ['.dae','.py'] or f.name in ['native-reference.json','alternate-form-weighted4-entry.json'])}
    for name,h in refs.items():assert provenance['files'][str((kit/Path(name).relative_to('kit')).resolve())]==h
    (root/'generated-reference-hashes.json').write_text(json.dumps(refs,indent=2)+'\n');derived=root/'build-config';derived.mkdir(exist_ok=True)
    if (derived/'config.json').exists():assert json.loads((derived/'config.json').read_text())==cfg,'Partial output uses another configuration'
    (derived/'config.json').write_text(json.dumps(cfg,indent=2)+'\n');(derived/'material-map.json').write_text(json.dumps(cfg['materialMap'],indent=2)+'\n')
    with (root/'atlas.log').open('w') as log:subprocess.run([sys.executable,str(COMMON/'atlas.py'),'--config',str(derived/'config.json'),'--source',str(source),'--output',str(root)],stdout=log,stderr=subprocess.STDOUT,check=True)
    verify(provenance)
    with (root/'convert.log').open('w') as log:subprocess.run([a.blender,'-b','--python-exit-code','1','--python',str(COMMON/'alternate_cage_convert.py'),'--','--config',str(cp),'--source',str(source),'--output',str(root),'--repo',str(REPO)],stdout=log,stderr=subprocess.STDOUT,check=True)
    from alternate_cage_materials import companions
    companions(cfg,root,source);verify(provenance);result={'hunter':cfg['hunter'],'part':'alternateForm','skinning':'weighted4','sourceSha256':digest(source),'configSha256':digest(cp),'shippingGlbSha256':digest(model),'generatedExporterSha256':digest(root/'kit/prepare-altform-weighted4.py'),'converterSha256':digest(COMMON/'alternate_cage_convert.py'),'status':'Built; static audit and runtime/capture acceptance required','externalNativeSupplementMaterials':cfg.get('externalNativeSupplementMaterials',[])};(root/'build-result.json').write_text(json.dumps(result,indent=2)+'\n');subprocess.run([sys.executable,str(COMMON/'alternate_cage_audit.py'),'--config',str(cp),'--source',str(source),'--output',str(root)],check=True);verify(provenance)
    if a.exe:
        from install import validate
        validate(Path(a.exe),root/'kit/starter',root/'validation.log')
    print('ALTERNATE_CAGE_BUILD_STATIC_PASS',cfg['hunter'],model)
elif a.stage=='audit':subprocess.run([sys.executable,str(COMMON/'alternate_cage_audit.py'),'--config',str(cp),'--source',str(source),'--output',str(root)],check=True)
else:
    if not a.exe:p.error('--exe is required')
    from install import validate
    validate(Path(a.exe),root/'kit/starter',root/'validation.log')
