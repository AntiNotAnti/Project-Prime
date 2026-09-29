#!/usr/bin/env python3
"""Generate authentic fixtures in an isolated v0.1.34 checkout, never current code."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('checkout', type=Path, help='isolated worktree at v0.1.34')
parser.add_argument('output', type=Path, help='new output directory outside tracked source')
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
checkout, output = args.checkout.resolve(), args.output.resolve()
commit = subprocess.check_output(['git', '-C', str(checkout), 'rev-parse', 'HEAD'], text=True).strip()
if commit != 'f0e01e09e08467974b8a5ea359d690ebe0a7e3a4':
    raise SystemExit('Historical producer must be exactly v0.1.34.')
# Refuse a modified serializer/schema. Diagnostic export adds only a command.
for name in ['ReplayWorldCheckpoint.cs', 'ReplayWorldSchemas.cs']:
    path = 'src/MphRead/Mods/Replay/' + name
    original = subprocess.check_output(['git', '-C', str(checkout), 'show', 'HEAD:' + path])
    if (checkout / path).read_bytes() != original:
        raise SystemExit('Modified historical source: ' + path)
if output.exists():
    raise SystemExit('Use a new output directory; fixtures are not overwritten.')
entry = checkout / 'src/MphRead/Mods/ModEntry.cs'
source = entry.read_text()
needle = '        public static bool TryHandleHeadless(string[] args)\n        {\n'
hook = '''            if (ValueAfter(args, "v134fixture") is string fixtureOutput)
            {
                Environment.ExitCode = Replay.ReplayV134FixtureExport.Run(fixtureOutput);
                return true;
            }
'''
if 'ValueAfter(args, "v134fixture")' not in source:
    if needle not in source:
        raise SystemExit('Historical command entry point differs.')
    entry.write_text(source.replace(needle, needle + hook, 1))
(checkout / 'src/MphRead/Mods/Replay/ReplayV134FixtureExport.cs').write_bytes(
    Path(__file__).with_name('ReplayV134FixtureExport.cs.txt').read_bytes())
subprocess.run([args.dotnet, 'build', 'src/MphRead/MphRead.csproj', '-c', 'Release',
                '-p:Version=0.1.34', '-p:InformationalVersion=0.1.34+f0e01e09'], cwd=checkout, check=True)
subprocess.run([args.dotnet, str(checkout / 'src/MphRead/bin/Release/net10.0/ProjectPrime.dll'),
                '-v134fixture', str(output)], cwd=checkout, check=True)
files = ['schema-v134.json', 'v134-source.ppdemo', 'v134-world.ppwc', 'v134-expected.json']
manifest = {'producerCommit': commit, 'command': 'Version=0.1.34; InformationalVersion=0.1.34+f0e01e09',
            'sha256': {name: hashlib.sha256((output/name).read_bytes()).hexdigest() for name in files}}
(output / 'provenance.json').write_text(json.dumps(manifest, indent=2) + '\n')
