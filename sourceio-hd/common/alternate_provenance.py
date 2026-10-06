"""Pre-execution source/code/reference fingerprint and unchanged-input guard."""
import hashlib,json
from pathlib import Path
MODULES=['alternate_native.py','alternate_components.py','alternate_convert.py','alternate_weighted_normals.py','alternate_materials.py','alternate_pipeline.py','alternate_audit.py','alternate_provenance.py','alternate_effects.py','atlas.py','source_materials.py','material_maps.py','recolors.py','binds.py','native.py','glb.py','install.py','runtime.py','shape_fit.py']
def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def capture(common,repo,config,source,kit):
    paths=[Path(common)/n for n in MODULES]+[Path(config),Path(source),Path(repo)/'src/MphRead/Export/modules/mph_common.py']
    paths += [p for p in Path(kit).rglob('*') if p.is_file() and (p.suffix in ['.dae','.py'] or p.name in ['native-reference.json','alternate-form-weighted4-entry.json'])]
    return {'format':1,'timing':'Captured before atlas/converter/audit execution','files':{str(p.resolve()):digest(p) for p in sorted(set(paths))},'sourceGlbSha256':digest(source),'configurationSha256':digest(config),'nativeKit':str(Path(kit).resolve())}
def verify(receipt):
    changed=[p for p,h in receipt['files'].items() if not Path(p).exists() or digest(p)!=h]
    assert not changed,('Inputs changed during alternate build/audit; choose a fresh build',changed)
    return True
