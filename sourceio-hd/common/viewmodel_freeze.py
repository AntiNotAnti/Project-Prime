"""Freeze a reviewed, accepted first-person asset with its reproducible inputs."""
import argparse
import json
import shutil
from pathlib import Path
from install import digest
from viewmodel_pipeline import accepted, COMMON

p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--config',required=True)
p.add_argument('--output',required=True)
a=p.parse_args();cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve()
accepted(root,cfg)
audit=json.loads((root/'audit.json').read_text());review=json.loads((root/'visual-review.json').read_text())
assert review['pass'] and review['modelSha256']==audit['viewmodelSha256']
for path,sha in audit['pipelineInputs'].items():assert digest(path)==sha,'Conversion input changed since audit: '+path
release=root/'releases/first-person-v1';assert not release.exists();release.mkdir(parents=True)
for name in ['starter','acceptance','roster-acceptance','textures','source-original','native-kit',
             'audit.json','selection.json','atlas-layout.json','atlas-config.json','material-map.json',
             'team-recolors.json','visual-review.json','validation.log','VIEWMODEL-COMPARISON.jpg',
             'Source_Weapon_Selection.blend','source-weapon.glb',
             cfg['hunter']+'_SourceIO_FirstPerson_Rigid.blend',
             'prepare-viewmodel-rigid.py','native-reference.json','viewmodel-native-idle.json']:
    source=root/name
    if source.is_dir():shutil.copytree(source,release/name)
    elif source.exists():shutil.copy2(source,release/name)
shutil.copytree(COMMON,release/'converter/common',ignore=shutil.ignore_patterns('__pycache__'))
folder=release/'converter'/cfg['hunter'].lower();folder.mkdir()
for name in ['viewmodel-config.json','bone-map.json']:shutil.copy2(cp.parent/name,folder/name)
lock={'hunter':cfg['hunter'],'part':'viewModel','modelSha256':audit['viewmodelSha256'],
      'files':{str(f.relative_to(release)):digest(f) for f in release.rglob('*') if f.is_file()}}
(release/'RELEASE-LOCK.json').write_text(json.dumps(lock,indent=2)+'\n')
for name,sha in lock['files'].items():assert digest(release/name)==sha
for f in release.rglob('*'):
    if f.is_file():f.chmod(0o444)
for f in sorted((f for f in release.rglob('*') if f.is_dir()),key=lambda f:len(f.parts),reverse=True):f.chmod(0o555)
release.chmod(0o555)
(root/'RELEASE-LOCK.json').write_text(json.dumps({'release':str(release),'modelSha256':audit['viewmodelSha256']},indent=2)+'\n')
print('FROZEN',cfg['hunter'],release,len(lock['files']))
