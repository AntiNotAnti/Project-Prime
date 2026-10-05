"""Validated directory transaction; whole-pack snapshot, test/merge, exact rollback."""
import json,shutil,os,tempfile,hashlib
from pathlib import Path
from runtime import run

def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def files(pack):
    manifest=json.loads((pack/'characters.json').read_text())
    return ['characters.json']+sorted({entry[key] for entry in manifest['models'] for key in ['model','mobileModel'] if entry.get(key)})
def validate(exe,pack,log):run(exe,['-charactermodelvalidate',pack],log)
def transaction(stage,live):
    previous=live.with_name(live.name+'.sourceio-previous')
    if previous.exists():raise RuntimeError(f'Unfinished transaction: {previous}; inspect and restore before continuing')
    had_live=live.exists()
    if had_live:live.rename(previous)
    try:stage.rename(live)
    except BaseException:
        if had_live:previous.rename(live)
        raise
    if had_live:shutil.rmtree(previous)
def install(exe,pack,live,snapshot,logdir,merge=False):
    pack=Path(pack);live=Path(live);snapshot=Path(snapshot);logdir=Path(logdir)
    validate(exe,pack,logdir/'install-validation.log')
    if snapshot.exists():raise RuntimeError(f'Snapshot already exists; use a fresh destination: {snapshot}')
    snapshot.parent.mkdir(parents=True,exist_ok=True)
    pending=Path(tempfile.mkdtemp(prefix=snapshot.name+'.pending-',dir=snapshot.parent))
    try:
        if live.exists():
            shutil.copytree(live,pending,dirs_exist_ok=True,symlinks=True)
            for p in live.rglob('*'):
                if p.is_file():assert digest(p)==digest(pending/p.relative_to(live))
        else:(pending/'characters.json').write_text(json.dumps({'format':1,'id':'empty','models':[]}))
        pending.rename(snapshot)
    finally:
        if pending.exists():shutil.rmtree(pending)
    live.parent.mkdir(parents=True,exist_ok=True);stage=Path(tempfile.mkdtemp(prefix=live.name+'.sourceio-stage-',dir=live.parent))
    try:
        if merge:
            shutil.copytree(live,stage,dirs_exist_ok=True)
            manifest=json.loads((stage/'characters.json').read_text());addition=json.loads((pack/'characters.json').read_text())
            keys={(e['hunter'].lower(),e['part'].lower(),e.get('lod',0)) for e in addition['models']}
            manifest['models']=[e for e in manifest['models'] if (e['hunter'].lower(),e['part'].lower(),e.get('lod',0)) not in keys]+addition['models']
            manifest['id']='sourceio-hd-roster-v1'
        else:manifest=json.loads((pack/'characters.json').read_text())
        for name in files(pack):
            if name=='characters.json':continue
            dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(pack/name,dest)
            assert digest(dest)==digest(pack/name)
        (stage/'characters.json').write_text(json.dumps(manifest,indent=2)+'\n')
        validate(exe,stage,logdir/'staged-validation.log')
        transaction(stage,live)
    finally:
        if stage.exists():shutil.rmtree(stage)
    (logdir/'install-receipt.json').write_text(json.dumps({'live':str(live),'rollback':str(snapshot),'merge':merge,'files':{n:digest(live/n) for n in files(live)}},indent=2)+'\n')
def rollback(exe,snapshot,live,logdir):
    snapshot=Path(snapshot);live=Path(live);validate(exe,snapshot,Path(logdir)/'rollback-validation.log')
    stage=Path(tempfile.mkdtemp(prefix=live.name+'.sourceio-restore-',dir=live.parent))
    try:
        shutil.copytree(snapshot,stage,dirs_exist_ok=True);transaction(stage,live)
    finally:
        if stage.exists():shutil.rmtree(stage)
    for p in snapshot.rglob('*'):
        if p.is_file():assert digest(p)==digest(live/p.relative_to(snapshot))
