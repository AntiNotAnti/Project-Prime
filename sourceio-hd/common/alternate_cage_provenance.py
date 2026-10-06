"""Separate pre-execution guard for cage builds; weighted inputs stay frozen."""
import hashlib
from pathlib import Path
MODULES=['alternate_cage_geometry.py','alternate_cage_convert.py','alternate_cage_normals.py','alternate_cage_materials.py','alternate_cage_audit.py','alternate_cage_pipeline.py','alternate_cage_provenance.py','alternate_rigid_geometry.py','alternate_rigid_materials.py','alternate_native.py','atlas.py','source_materials.py','material_maps.py','recolors.py','native.py','binds.py','glb.py','install.py','runtime.py']
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def capture(common,repo,config,source,kit):
    paths=[Path(common)/n for n in MODULES]+[Path(config),Path(source),Path(repo)/'src/MphRead/Export/modules/mph_common.py']
    paths += [p for p in Path(kit).rglob('*') if p.is_file() and (p.suffix in ['.dae','.py'] or p.name in ['native-reference.json','alternate-form-weighted4-entry.json'])]
    return {'format':1,'timing':'Captured before atlas/converter/audit execution','files':{str(p.resolve()):digest(p) for p in sorted(set(paths))},'sourceGlbSha256':digest(source),'configurationSha256':digest(config),'nativeKit':str(Path(kit).resolve())}
def verify(receipt):
    changed=[p for p,h in receipt['files'].items() if not Path(p).exists() or digest(p)!=h];assert not changed,('Inputs changed during cage alternate build; choose a fresh build',changed);return True
