"""Sequential game diagnostics with an exclusive settings lock and exact restore."""
import fcntl,json,subprocess,os
from pathlib import Path

def run(exe,args,log,userdata=None,hd=False):
    userdata=Path(userdata or os.environ.get('PROJECT_PRIME_USER_DATA') or Path.home()/'Library/Application Support/Project Prime')
    log=Path(log);log.parent.mkdir(parents=True,exist_ok=True)
    with (userdata/'.sourceio-diagnostic.lock').open('w') as lock:
        fcntl.flock(lock,fcntl.LOCK_EX)
        paths=[userdata/'Savedata/settings.json',userdata/'launcher.txt']
        saved={p:p.read_bytes() if p.exists() else None for p in paths}
        backup=log.parent/'preferences-before';backup.mkdir(exist_ok=True)
        for p,b in saved.items():
            if b is not None and not (backup/p.name).exists():(backup/p.name).write_bytes(b)
        try:
            if hd:
                settings=json.loads(saved[paths[0]]);settings['MenuSettings']['CharacterModelReplacements']='on'
                paths[0].write_text(json.dumps(settings,indent=2)+'\n')
            with log.open('w') as stream:
                env=os.environ.copy();env['PROJECT_PRIME_USER_DATA']=str(userdata.resolve())
                result=subprocess.run([str(exe),*map(str,args),'-noupdate','-debuglog'],stdout=stream,stderr=subprocess.STDOUT,env=env)
        finally:
            for p,b in saved.items():
                if b is None:p.unlink(missing_ok=True)
                else:p.write_bytes(b)
        for line in log.read_text().splitlines():
            marker='standard error is being written to '
            if marker in line:
                path=Path(line.split(marker,1)[1])
                if path.exists():
                    with log.open('a') as stream:stream.write('\nNative stderr:\n'+path.read_text())
        for line in log.read_text().splitlines():
            marker='standard error is being written to '
            if marker in line:
                path=Path(line.split(marker,1)[1].replace('-native.txt','.log'))
                if path.exists():
                    with log.open('a') as stream:stream.write('\nManaged debug log:\n'+path.read_text())
        if result.returncode:raise RuntimeError(f'Game diagnostic failed ({result.returncode}): {log}')
