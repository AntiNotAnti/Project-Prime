"""Mac actual-asset retirement regression against an immutable compiled client."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/native-render-check'))
from campaign_identity import asset_identity, isolate_user_data

DOTNET = Path('/Users/jarrett/.dotnet/dotnet')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    checkpoint = Path(sys.argv[1]).resolve(strict=True)
    expected = sys.argv[2]
    assert sha(checkpoint / 'ProjectPrime.dll') == expected
    private = Path(tempfile.mkdtemp(prefix='prime-cpp05-retirement-')).resolve()
    durable = ROOT / 'artifacts/cpp05-actual-world-retirement' / private.name
    durable.mkdir(parents=True)
    tool = private / 'tool'
    shutil.copytree(Path(__file__).parent / 'bin/Release/net10.0', tool)
    for source in checkpoint.iterdir():
        if source.is_file() and source.suffix in ('.dll', '.dylib', '.so'):
            shutil.copy2(source, tool / source.name)
    assert sha(tool / 'ProjectPrime.dll') == expected
    userdata, before = isolate_user_data(Path.home() / 'Library/Application Support/Project Prime', private)
    export = private / 'export'
    export.mkdir()
    paths = userdata / 'paths.txt'
    lines = [line for line in paths.read_text().splitlines() if not line.startswith('Export=')]
    paths.write_text('\n'.join(lines + [f'Export={export}']) + '\n')
    env = os.environ.copy()
    for key in list(env):
        if key.startswith(('PRIME_NET', 'PROJECT_PRIME_CAREER', 'SUPABASE')) or key.endswith('_URL') and key.startswith('PROJECT_PRIME'):
            env.pop(key, None)
    env['PROJECT_PRIME_USER_DATA'] = str(userdata)
    result = private / 'result.json'
    command = [str(DOTNET), str(tool / 'nettest.dll'), str(result)]
    log = durable / 'actual-retirement.log'
    print(f'ACTUAL RETIREMENT START {private}', flush=True)
    started = time.monotonic()
    with log.open('w') as output:
        process = subprocess.Popen(command, cwd=userdata, env=env, stdout=output, stderr=subprocess.STDOUT)
        try:
            code = process.wait(timeout=180)
        except subprocess.TimeoutExpired:
            process.terminate()
            try:
                code = process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                code = process.wait()
    after = asset_identity(paths)
    receipt = {
        'scope': 'Mac actual ServerSim native-asset retirement; no UDP/GL/backend/timed campaign/device acceptance',
        'command': command, 'cwd': str(userdata), 'privateDirectory': str(private),
        'exitCode': code, 'elapsedSeconds': time.monotonic() - started,
        'productionDllSha256': sha(tool / 'ProjectPrime.dll'),
        'fixtureDllSha256': sha(tool / 'nettest.dll'),
        'log': str(log.relative_to(ROOT)), 'logSha256': sha(log),
        'assetsUnchanged': before['sha256'] == after['sha256'],
        'assetBefore': {k: v for k, v in before.items() if k != 'files'},
        'assetAfter': {k: v for k, v in after.items() if k != 'files'},
    }
    if result.exists():
        target = durable / 'result.json'
        shutil.copy2(result, target)
        receipt.update(result=str(target.relative_to(ROOT)), resultSha256=sha(target))
    (durable / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(log.read_text(), end='')
    print(f'ACTUAL RETIREMENT END trueExit={code}, assetsUnchanged={receipt["assetsUnchanged"]}, receipt={durable / "receipt.json"}', flush=True)
    return 0 if code == 0 and receipt['assetsUnchanged'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
