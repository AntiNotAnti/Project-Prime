"""Separate pre-execution guard for rigid builds; weighted inputs stay frozen."""
import hashlib
from pathlib import Path
MODULES=['alternate_rigid_geometry.py','alternate_rigid_convert.py','alternate_rigid_finalize.py','alternate_rigid_materials.py','alternate_rigid_audit.py','alternate_rigid_pipeline.py','alternate_rigid_provenance.py','alternate_native.py','atlas.py','source_materials.py','material_maps.py','recolors.py','native.py','glb.py','install.py','runtime.py']
def digest(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def capture(common,repo,config,source,kit):
    paths=[Path(common)/n for n in MODULES]+[Path(config),Path(source),Path(repo)/'src/MphRead/Export/modules/mph_common.py']
    paths += [p for p in Path(kit).rglob('*') if p.is_file() and (p.suffix in ['.dae','.py'] or p.name in ['native-reference.json','alternate-form-entry.json'])]
    return {'format':1,'timing':'Captured before atlas/converter/audit execution','files':{str(p.resolve()):digest(p) for p in sorted(set(paths))},'sourceGlbSha256':digest(source),'configurationSha256':digest(config),'nativeKit':str(Path(kit).resolve())}
def verify(receipt):
    changed=[p for p,h in receipt['files'].items() if not Path(p).exists() or digest(p)!=h];assert not changed,('Inputs changed during rigid alternate build; choose a fresh build',changed);return True
