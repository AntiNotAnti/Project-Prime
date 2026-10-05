"""Freeze reviewed build/evidence with per-file SHA256s. Assets remain local."""
import argparse,json,hashlib,shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--config',required=True);p.add_argument('--output',required=True);p.add_argument('--release-name',default='lod0-v1');a=p.parse_args();assert a.release_name in ['lod0-v1','lod0-v2'];cp=Path(a.config).resolve();cfg=json.loads(cp.read_text());root=Path(a.output).resolve();release=root/'releases'/a.release_name
assert not release.exists() and not (root/'RELEASE-LOCK.json').exists()
audit=json.loads((root/'audit.json').read_text());accept=json.loads((root/'acceptance/acceptance.json').read_text());review=json.loads((root/'visual-review.json').read_text())
assert audit['pass'] and not accept['failures'] and review['pass']
model=root/'kit/starter'/cfg['hunter'].lower()/'biped_weighted4.glb'
assert audit['shippingGlbSha256']==accept['testedModelSha256']==hashlib.sha256(model.read_bytes()).hexdigest()
release.mkdir(parents=True)
for name in ['kit','textures','source-original','acceptance','roster-spire-acceptance','roster-samus-acceptance','app-spire-acceptance','roster-acceptance','merge-install']:
 if (root/name).exists():shutil.copytree(root/name,release/name)
for name in ['Native_Baseline.blend',cfg['hunter']+'_SourceIO_LOD0.blend','audit.json','retarget-report.json','atlas-layout.json','native-bind-correction.json','source-alpha.json','team-recolors.json','generated-reference-hashes.json','visual-review.json',cfg['hunter'].upper()+'-RESULT.md','acceptance-review.jpg','SOURCE-GAME-COMPARISON.jpg','source-bind-solid.png','converted-bind-solid.png','FIDELITY-COMPARISON.jpg','kit-generation.log','convert.log','audit.log','validation.log','acceptance.log','roster-spire.log','roster-samus.log','roster-acceptance.log','model-contract.log','dotnet-build.log','FINAL-CHECKS.json','app-spire-acceptance.log','app-contract.log','app-publish.log','app-sign.log','app-install-receipt.json','atlas-replay.json','samus-replay-comparison.json']:
 if (root/name).exists():shutil.copy2(root/name,release/name)
shutil.copytree(cp.parent,release/'configuration');shutil.copytree(Path(__file__).resolve().parent,release/'common',ignore=shutil.ignore_patterns('__pycache__'))
files={str(p.relative_to(release)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(release.rglob('*')) if p.is_file()}
lock={'format':1,'hunter':cfg['hunter'],'triangles':audit['triangles'],'joints':audit['jointCount'],'modelSha256':audit['shippingGlbSha256'],'files':files}
(release/'RELEASE-LOCK.json').write_text(json.dumps(lock,indent=2)+'\n');(root/'RELEASE-LOCK.json').write_text(json.dumps({'release':str(release),'modelSha256':lock['modelSha256'],'hashedFiles':len(files)},indent=2)+'\n')
for p in release.rglob('*'):
 if p.is_file():p.chmod(0o444)
for name in ['Native_Baseline.blend',cfg['hunter']+'_SourceIO_LOD0.blend']:(root/name).chmod(0o444)
print('FROZEN',release,len(files),'files')
