"""Run paired LOD0/LOD1 game checks in an isolated user-data directory.

The entire existing diagnostic pack is renamed and restored byte-for-byte.
Never point this diagnostic at the user's live application-support directory.
"""
import argparse, fcntl, json, shutil
from pathlib import Path
from install import digest
from runtime import run

p=argparse.ArgumentParser()
p.add_argument('--config',required=True);p.add_argument('--output',required=True)
p.add_argument('--exe',required=True);p.add_argument('--userdata',required=True)
p.add_argument('--room',default='MIDSHIP');p.add_argument('--run-name',default='paired-acceptance-v1')
a=p.parse_args();cfg_path=Path(a.config).resolve();cfg=json.loads(cfg_path.read_text())
repo=Path(__file__).resolve().parents[2];root=Path(a.output).resolve();data=Path(a.userdata).resolve()
live=Path.home()/'Library/Application Support/Project Prime'
assert data!=live.resolve() and not data.is_relative_to(live.resolve()),'Use isolated diagnostic data'
hunter=cfg['hunter'];folder=hunter.lower();old=repo/cfg['acceptedLod0Root']
checks=root/a.run_name;assert not checks.exists(),'Use a fresh acceptance run name'
checks.mkdir(parents=True);pack=data/'character-models/default';original=pack.with_name('default.lod1-original')
def hashes(directory):return {str(f.relative_to(directory)):digest(f) for f in directory.rglob('*') if f.is_file()}
with (data/'.sourceio-acceptance.lock').open('w') as lock:
 fcntl.flock(lock,fcntl.LOCK_EX)
 assert pack.exists() and not original.exists(),'Inspect any unfinished diagnostic transaction'
 before=hashes(pack);(checks/'pack-before.json').write_text(json.dumps(before,indent=2)+'\n')
 first=json.loads((old/'kit/starter/characters.json').read_text())
 entry0=next(e for e in first['models'] if e['hunter'].lower()==folder and e['part'].lower()=='biped' and e.get('lod',0)==0)
 entry1=json.loads((root/'kit/starter/biped-lod1-weighted4-entry.json').read_text())
 audit=json.loads((root/'audit.json').read_text())
 assert audit['pass'] and audit['shippingGlbSha256']==digest(root/'kit/starter'/entry1['model'])
 assert audit['configSha256']==digest(cfg_path)
 assert all(digest(path)==sha for path,sha in audit['pipelineInputs'].items()),'Audited conversion inputs changed'
 assert digest(old/'kit/starter'/entry0['model'])==cfg['sourceGlbSha256']
 pack.rename(original)
 try:
  pack.mkdir()
  for entry,source in [(entry0,old/'kit/starter'),(entry1,root/'kit/starter')]:
   for key in ['model','mobileModel']:
    if entry.get(key):
     target=pack/entry[key];target.parent.mkdir(parents=True,exist_ok=True)
     shutil.copy2(source/entry[key],target);assert digest(target)==digest(source/entry[key])
  (pack/'characters.json').write_text(json.dumps({'format':1,'id':folder+'-lod-pair-v1','models':[entry0,entry1]},indent=2)+'\n')
  run(a.exe,['-charactermodelvalidate',pack],checks/'validation.log',userdata=data)
  run(a.exe,['-lod1acceptancecheck',a.room,'-hunter',hunter,'-muzzleaudit',old/'audit.json',
       '-lod1muzzleaudit',root/'audit.json','-output',checks],checks/'game.log',userdata=data,hd=True)
  receipt=json.loads((checks/'acceptance.json').read_text())
  assert receipt['hunter']==hunter and receipt['rosterLodSweep'] and not receipt['failures']
  assert receipt['lodModelHashes']=={'0':cfg['sourceGlbSha256'],'1':audit['shippingGlbSha256']}
  assert min(receipt['eligibleFramesByLod'])>=500 and receipt['fallbackFramesByLod']==[0,0]
  assert receipt['lodTransitions']>=12
  assert receipt['residencyBeforeSecondLod']==receipt['residencyAfterSecondLod']
  for label in ['launcher-before','launcher-after']:
   preview=json.loads((checks/(label+'.json')).read_text());assert preview['weightedFrames']==60 and preview['fallbackFrames']==0
  (checks/'SIGNED-CHECK.json').write_text(json.dumps({'pass':True,'signedExecutableSha256':digest(a.exe),
    'configSha256':digest(cfg_path),'lod0Sha256':cfg['sourceGlbSha256'],'lod1Sha256':audit['shippingGlbSha256'],
    'nativeLod0AuditSha256':digest(old/'audit.json'),'nativeLod1AuditSha256':digest(root/'audit.json')},indent=2)+'\n')
 finally:
  if pack.exists():shutil.rmtree(pack)
  original.rename(pack)
  assert hashes(pack)==before,'Diagnostic pack did not restore exactly'
  (checks/'pack-restored.json').write_text(json.dumps({'pass':True,'files':before},indent=2)+'\n')
print('LOD1_SIGNED_PAIR_ACCEPTED',hunter,checks,flush=True)
