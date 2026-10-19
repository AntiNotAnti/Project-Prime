"""Private direct-reference owner checks; optional owned-loopback saturation."""
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
DOTNET = Path('/Users/jarrett/.dotnet/dotnet')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    checkpoint = Path(sys.argv[1]).resolve(strict=True)
    expected = sys.argv[2]
    mode = sys.argv[3] if len(sys.argv) > 3 else 'owner'
    assert mode in ('owner', 'udp')
    assert sha(checkpoint / 'ProjectPrime.dll') == expected
    private = Path(tempfile.mkdtemp(prefix='prime-cpp05-reliable-attempt-')).resolve()
    durable = ROOT / 'artifacts/cpp05-reliable-attempt-owner' / private.name
    durable.mkdir(parents=True)
    source = Path(__file__).resolve().parent
    project = private / 'source'
    project.mkdir()
    for path in source.iterdir():
        if path.is_file() and path.suffix in ('.cs', '.csproj'):
            shutil.copy2(path, project / path.name)
    tool = private / 'tool'
    build_command = [str(DOTNET), 'build', str(project / 'reliable-attempt-owner-check.csproj'),
                     '-c', 'Release', '-p:PrimeReferenceDirectory=' + str(checkpoint), '-o', str(tool), '--nologo']
    build_log = durable / 'build.log'
    with build_log.open('w') as output:
        built = subprocess.run(build_command, cwd=private, stdout=output, stderr=subprocess.STDOUT, timeout=60)
    receipt = {'scope': 'Actual compiled immutable owner contracts; optional actual ephemeral UDP saturation; no scene/assets/GL/backend/gameplay acceptance',
               'mode': mode, 'privateDirectory': str(private), 'buildCommand': build_command,
               'buildExit': built.returncode, 'buildLogSha256': sha(build_log),
               'fixtureSourceSha256': {p.name: sha(p) for p in project.iterdir() if p.is_file()},
               'expectedProductionDllSha256': expected}
    if built.returncode == 0:
        for path in checkpoint.iterdir():
            if path.is_file() and path.suffix in ('.dll', '.dylib', '.so'):
                shutil.copy2(path, tool / path.name)
        assert sha(tool / 'ProjectPrime.dll') == expected
        userdata = private / 'user-data'
        userdata.mkdir()
        env = {k: v for k, v in os.environ.items() if not k.startswith(('PRIME_', 'MPHREAD_', 'PROJECT_PRIME_', 'SUPABASE'))}
        env['PROJECT_PRIME_USER_DATA'] = str(userdata)
        command = [str(DOTNET), str(tool / 'nettest.dll')] + (['--udp-saturation'] if mode == 'udp' else [])
        log = durable / 'actual-owner.log'
        start = time.monotonic()
        timed_out = False
        with log.open('w') as output:
            process = subprocess.Popen(command, cwd=userdata, env=env, stdout=output, stderr=subprocess.STDOUT)
            try:
                code = process.wait(timeout=60)
            except subprocess.TimeoutExpired:
                timed_out = True
                process.kill()
                code = process.wait()
        receipt.update(command=command, cwd=str(userdata), exitCode=code, timedOut=timed_out,
                       elapsedSeconds=time.monotonic() - start, productionDllSha256=sha(tool / 'ProjectPrime.dll'),
                       fixtureDllSha256=sha(tool / 'nettest.dll'), logSha256=sha(log),
                       environment='fresh explicit userdata, inherited game/service variables removed')
        print(log.read_text(), end='')
    (durable / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print('Receipt:', durable / 'receipt.json', 'trueExit:', receipt.get('exitCode', 'NOT_RUN'), flush=True)
    return 0 if built.returncode == 0 and receipt['exitCode'] == 0 and not receipt['timedOut'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
