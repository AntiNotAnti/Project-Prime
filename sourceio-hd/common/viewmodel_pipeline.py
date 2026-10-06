"""Configuration-driven Source first-person weapon conversion and acceptance."""
import argparse
import io
import json
import platform
import shutil
import subprocess
import sys
from pathlib import Path

from glb import load, image_bytes
from install import digest, install, rollback, validate
from runtime import run

COMMON = Path(__file__).resolve().parent
REPO = COMMON.parents[1]
BUILD = REPO / 'src/MphRead/bin/Release/net10.0'
if platform.system() == 'Darwin':
    BUILD /= 'osx-arm64' if platform.machine() == 'arm64' else 'osx-x64'


def audit_artwork(root):
    from PIL import Image
    audit = json.loads((root / 'audit.json').read_text())
    doc, blob = load(root / 'starter' / audit['hunter'].lower() / 'viewmodel.glb')
    sizes = {}
    for mat in doc['materials']:
        for channel in ['albedo', 'normal', 'emissive', 'material']:
            spec = (mat.get('pbrMetallicRoughness', {}).get('baseColorTexture')
                    if channel == 'albedo' else mat.get('pbrMetallicRoughness', {}).get('metallicRoughnessTexture')
                    if channel == 'material' else mat.get(channel+'Texture'))
            if not spec:
                continue
            actual = Image.open(io.BytesIO(image_bytes(doc, blob, doc['textures'][spec['index']]['source']))).convert('RGBA')
            expected = Image.open(root / 'textures' / (mat['name'] + '-' + channel + '.png')).convert('RGBA')
            assert actual.size == expected.size and actual.tobytes() == expected.tobytes(), (mat['name'], channel)
            sizes[mat['name'] + '-' + channel] = list(actual.size)
    audit['embeddedArtworkTexelsUnchanged'] = True
    audit['embeddedArtworkSizes'] = sizes
    (root / 'audit.json').write_text(json.dumps(audit, indent=2) + '\n')


def accepted(root, cfg):
    audit = json.loads((root / 'audit.json').read_text())
    check = json.loads((root / 'acceptance/acceptance.json').read_text())
    model = root / 'starter' / cfg['hunter'].lower() / 'viewmodel.glb'
    assert audit['pass'] and audit['exporterUnchanged'] and audit['independentAudit']['pass']
    assert audit['embeddedArtworkTexelsUnchanged'] and audit['teamRecolorsRequired']
    assert check['pass'] and check['hunter'] == cfg['hunter']
    assert check['testedModelSha256'] == audit['viewmodelSha256'] == digest(model)
    assert check['lateFrames'] == 990 and check['submitted'] >= 3000
    assert check['fired'] and check['zoomed'] and check['muzzleAligned']
    assert audit['nativeEffectColorAudit']['pass']
    idle = {n['name']: n['matrix'] for n in json.loads((root / 'viewmodel-native-idle.json').read_text())['nodes']}
    live = json.loads((root / 'acceptance/native-idle-frames.json').read_text())
    maximum = max(abs(n['matrix'][r][c] - idle[n['Name']][r][c])
                  for n in live for r in range(4) for c in range(4))
    assert maximum < .0001, ('Live native idle differs from authoring pose', maximum)
    cases = {c['Name']: c for c in check['cases'] if 'Name' in c}
    for name in ['idle', 'run-strafe', 'jump', 'aim-turn', 'fire', 'charge',
                 'missile', 'missile-charge', 'affinity', 'affinity-charge',
                 'zoom', 'unzoom', 'fov-60', 'fov-120', 'materials-on',
                 'materials-off', *['suit-' + str(i) for i in range(6)]]:
        assert cases[name]['submitted'] == cases[name]['Count'], name
    return check


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('stage', choices=['build', 'audit', 'validate', 'accept', 'merge', 'rollback'])
    p.add_argument('--config', required=True)
    p.add_argument('--output', required=True)
    p.add_argument('--source-root')
    p.add_argument('--kit', help='Existing native hunter authoring kit; otherwise generated at OUTPUT/kit')
    p.add_argument('--blender', default='/Applications/Blender.app/Contents/MacOS/Blender')
    p.add_argument('--exe', default=str(BUILD / 'ProjectPrime'))
    p.add_argument('--room', default='MIDSHIP')
    p.add_argument('--userdata')
    p.add_argument('--live')
    p.add_argument('--snapshot')
    a = p.parse_args()
    cp = Path(a.config).resolve()
    cfg = json.loads(cp.read_text())
    root = Path(a.output).resolve()
    root.mkdir(parents=True, exist_ok=True)
    exe = Path(a.exe).resolve()
    kit = Path(a.kit).resolve() if a.kit else root / 'kit'
    pack = root / 'starter'

    def worker(name, args):
        with (root / (name + '.log')).open('w') as log:
            subprocess.run([a.blender, '-b', '--python-exit-code', '1', '--python',
                            str(COMMON / (name + '.py')), '--', *map(str, args)],
                           stdout=log, stderr=subprocess.STDOUT, check=True)

    if a.stage in ['build', 'audit']:
        assert not (root / 'RELEASE-LOCK.json').exists(), 'Output is frozen; choose a fresh directory'
        if a.stage == 'build':
            if not a.source_root:
                p.error('--source-root is required for build')
            assert digest(Path(a.source_root) / cfg['sourceRelative']) == cfg['sourceSha256']
            if not a.kit:
                run(exe, ['-charactermodelkit', cfg['hunter'], '-output', kit], root / 'kit-generation.log')
            for name in ['prepare-viewmodel-rigid.py', 'native-reference.json', 'viewmodel-native-idle.json']:
                shutil.copy2(kit / name, root / name)
            worker('viewmodel_extract', ['--config', cp, '--output', root, '--source-root', a.source_root])
            with (root / 'atlas.log').open('w') as log:
                subprocess.run([sys.executable, str(COMMON / 'atlas.py'), '--config',
                                str(root / 'atlas-config.json'), '--source', str(root / 'source-weapon.glb'),
                                '--output', str(root)], stdout=log, stderr=subprocess.STDOUT, check=True)
            worker('viewmodel_convert', ['--config', cp, '--output', root, '--kit', kit])
            from material_maps import add_material_maps
            add_material_maps(root, pack / cfg['hunter'].lower() / 'viewmodel.glb')
            subprocess.run([sys.executable, str(COMMON / 'viewmodel_recolors.py'),
                            '--config', str(cp), '--output', str(root)], check=True)
        worker('viewmodel_audit', ['--config', cp, '--output', root, '--kit', kit])
        audit_artwork(root)
        audit = json.loads((root / 'audit.json').read_text())
        audit['pipelineInputs'] = {str(f): digest(f) for f in
                                  [cp, cp.parent / 'bone-map.json', kit / 'viewmodel-native-idle.json', *sorted(COMMON.glob('*.py'))]}
        (root / 'audit.json').write_text(json.dumps(audit, indent=2) + '\n')
        validate(exe, pack, root / 'validation.log')
    elif a.stage == 'validate':
        validate(exe, pack, root / 'validation.log')
    else:
        if not a.live:
            p.error('--live is required for accept, merge or rollback')
        live = Path(a.live).resolve()
        if a.stage == 'accept':
            if not a.userdata:
                p.error('--userdata is required for isolated acceptance')
            userdata = Path(a.userdata).resolve()
            assert live == userdata / 'character-models/default'
            snapshot = root / 'rollback-before-test'
            assert not snapshot.exists(), 'Archive previous acceptance before repeating it'
            try:
                install(exe, pack, live, snapshot, root / 'test-install')
                run(exe, ['-viewmodelacceptancecheck', a.room, '-hunter', cfg['hunter'],
                          '-output', root / 'acceptance'], root / 'acceptance.log', userdata=userdata, hd=True)
                accepted(root, cfg)
            finally:
                if snapshot.exists():
                    rollback(exe, snapshot, live, root / 'test-install')
        else:
            if not a.snapshot:
                p.error('--snapshot is required for merge or rollback')
            snapshot = Path(a.snapshot).resolve()
            if a.stage == 'merge':
                accepted(root, cfg)
                audit = json.loads((root / 'audit.json').read_text())
                for f, sha in audit['pipelineInputs'].items():
                    assert digest(f) == sha, 'Conversion input changed since audit: ' + f
                review = json.loads((root / 'visual-review.json').read_text())
                assert review['pass'] and review['modelSha256'] == audit['viewmodelSha256']
                install(exe, pack, live, snapshot, root / 'merge-install', merge=True)
            else:
                rollback(exe, snapshot, live, root / 'rollback-install')
    print(a.stage.upper() + ' PASS', cfg['hunter'], root)


if __name__ == '__main__':
    main()
